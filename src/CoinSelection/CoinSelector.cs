namespace CoinSelection;

/// <summary>Finds bounded subsets of positive integer values with deterministic branch and bound.</summary>
public static class CoinSelector
{
	/// <summary>Finds the best valid nonempty selection within the supplied search limits.</summary>
	/// <typeparam name="T">The caller's item or group identifier.</typeparam>
	/// <param name="candidates">Candidates to snapshot. Each candidate can be selected at most once.</param>
	/// <param name="target">The strictly positive desired value, in the candidates' integer units.</param>
	/// <param name="options">Selection constraints, or <see langword="null"/> for defaults.</param>
	/// <param name="cancellationToken">Cancellation returns the best selection found, with cancelled status.</param>
	/// <returns>An immutable result, including the reason the search stopped.</returns>
	/// <remarks>
	/// Ranking minimizes absolute target difference, then cost, then underlying input count.
	/// For <see cref="SelectionMode.Closest"/>, an otherwise tied above-target selection wins.
	/// Remaining ties retain the first selection found. Search order is descending value,
	/// ascending cost, ascending input count, then original position, with inclusion visited first.
	/// Equivalent candidates retain the earliest original references while skipping repeated combinations.
	/// The node budget counts the root and each entered child, including nodes immediately pruned.
	/// Suffix bounds account for the remaining input capacity, and each incumbent tightens the
	/// searched value interval inclusively so equal-distance cost/count ties remain eligible.
	/// Additional pruning can improve the result obtained within the same node budget.
	/// Validation and input snapshotting occur before cancellation is observed. Concurrent mutation
	/// while the input is being snapshotted is unsupported. Worst-case search time is exponential;
	/// memory use is linear in the number of candidates, with no recursive call stack.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The candidate list is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The target or an option is invalid.</exception>
	/// <exception cref="ArgumentException">A candidate is null or has invalid value, cost, or input count.</exception>
	/// <exception cref="OverflowException">The sum of all candidate values or all candidate costs exceeds <see cref="long.MaxValue"/>.</exception>
	public static SelectionResult<T> Select<T>(
		IReadOnlyList<SelectionCandidate<T>> candidates,
		long target,
		SelectionOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(candidates);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(target);
		options ??= new SelectionOptions();
		ValidateOptions(options);

		var sorted = new IndexedCandidate<T>[candidates.Count];
		long aggregateValue = 0;
		long aggregateCost = 0;
		for (int i = 0; i < sorted.Length; i++)
		{
			SelectionCandidate<T>? candidate = candidates[i];
			if (candidate is null || candidate.Value <= 0 || candidate.Cost < 0 || candidate.InputCount <= 0)
			{
				throw new ArgumentException($"Candidate at index {i} must have positive value and input count, and nonnegative cost.", nameof(candidates));
			}

			aggregateValue = checked(aggregateValue + candidate.Value);
			aggregateCost = checked(aggregateCost + candidate.Cost);
			sorted[i] = new IndexedCandidate<T>(candidate, i);
		}

		if (cancellationToken.IsCancellationRequested)
		{
			return EmptyResult<T>(SelectionStatus.Cancelled);
		}

		Array.Sort(sorted, static (left, right) =>
		{
			int comparison = right.Candidate.Value.CompareTo(left.Candidate.Value);
			if (comparison == 0)
			{
				comparison = left.Candidate.Cost.CompareTo(right.Candidate.Cost);
			}

			if (comparison == 0)
			{
				comparison = left.Candidate.InputCount.CompareTo(right.Candidate.InputCount);
			}

			return comparison != 0 ? comparison : left.OriginalIndex.CompareTo(right.OriginalIndex);
		});

		long maximumShortfall = Math.Min(options.MaximumDifference, options.MaximumShortfall);
		long maximumExcess = Math.Min(options.MaximumDifference, options.MaximumExcess);
		long lowerValue = options.Mode == SelectionMode.AtLeastTarget
			? target
			: target > maximumShortfall ? target - maximumShortfall : 1;
		long upperValue = options.Mode == SelectionMode.AtMostTarget
			? target
			: maximumExcess > long.MaxValue - target ? long.MaxValue : target + maximumExcess;
		lowerValue = Math.Max(lowerValue, options.MinimumValue);

		var remainingValues = new long[sorted.Length + 1];
		int nextDistinct = sorted.Length;
		for (int i = sorted.Length - 1; i >= 0; i--)
		{
			SelectionCandidate<T> candidate = sorted[i].Candidate;
			if (i + 1 < sorted.Length)
			{
				SelectionCandidate<T> next = sorted[i + 1].Candidate;
				if (candidate.Value != next.Value || candidate.Cost != next.Cost || candidate.InputCount != next.InputCount)
				{
					nextDistinct = i + 1;
				}
			}

			sorted[i] = new IndexedCandidate<T>(candidate, sorted[i].OriginalIndex, nextDistinct);
			remainingValues[i] = remainingValues[i + 1] + candidate.Value;
		}

		return Search(sorted, remainingValues, target, options, lowerValue, upperValue, cancellationToken);
	}

	// Compile this loop fully on its first call, independent of tiered profile history.
	// Keep incumbent copying and result mapping outside its register allocation.
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining |
		System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
	private static SelectionResult<T> Search<T>(
		IndexedCandidate<T>[] sorted,
		long[] remainingValues,
		long target,
		SelectionOptions options,
		long lowerValue,
		long upperValue,
		CancellationToken cancellationToken)
	{
		int pathCapacity = Math.Min(sorted.Length, options.MaximumInputCount);
		var selectedIndices = new int[pathCapacity];
		int selectedCount = 0;
		long currentValue = 0;
		long currentCost = 0;
		int currentInputCount = 0;
		int depth = 0;
		long visitedNodes = 0;
		long bestValue = 0;
		long bestCost = 0;
		int bestInputCount = 0;
		long bestDifference = long.MaxValue;
		bool distanceIsFinal = false;
		int[]? bestIndices = null;
		int bestSelectedCount = 0;
		long searchLower = lowerValue;
		long searchUpper = upperValue;
		SelectionStatus? stoppedStatus = null;

		// The selected path also holds the pending inclusion ancestors. Popping
		// its last index returns directly to the next unexplored omission child.
		while (depth >= 0)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				stoppedStatus = SelectionStatus.Cancelled;
				break;
			}

			if (visitedNodes >= options.MaximumSearchNodes)
			{
				stoppedStatus = SelectionStatus.SearchLimitReached;
				break;
			}

			visitedNodes++;
			if (currentValue >= searchLower && currentValue <= searchUpper)
			{
				long difference = Distance(currentValue, target);
				if (IsBetter(currentValue, currentCost, currentInputCount, difference, target, options.Mode, bestIndices is not null, bestValue, bestDifference, bestCost, bestInputCount))
				{
					bestValue = currentValue;
					bestCost = currentCost;
					bestInputCount = currentInputCount;
					bestDifference = difference;
					// The request's distance floor needs checking only when the incumbent changes.
					distanceIsFinal = difference == 0 || (lowerValue > target && currentValue == lowerValue);
					bestIndices ??= new int[pathCapacity];
					bestSelectedCount = selectedCount;
					CopySelection(selectedIndices, bestIndices, selectedCount);
					searchLower = Math.Max(lowerValue, target > bestDifference ? target - bestDifference : 1);
					searchUpper = Math.Min(upperValue, SaturatingAdd(target, bestDifference));
					if (difference == 0 && bestCost == 0 && bestInputCount == 1)
					{
						// Positive values/counts and nonnegative costs make this rank unbeatable.
						break;
					}
				}
			}

			if (depth == sorted.Length || currentInputCount == options.MaximumInputCount ||
				lowerValue > upperValue || currentValue >= searchUpper)
			{
				goto Backtrack;
			}

			bool secondaryCannotImprove = bestIndices is not null &&
				(currentCost > bestCost || (currentCost == bestCost && currentInputCount >= bestInputCount));
			if (distanceIsFinal && secondaryCannotImprove)
			{
				// The request's minimum possible distance cannot improve any further.
				goto Backtrack;
			}

			SelectionCandidate<T> candidate = sorted[depth].Candidate;
			long availableValue = searchUpper - currentValue;
			if (candidate.Value <= availableValue)
			{
				// Every candidate consumes at least one input, so the largest remaining k values
				// are an optimistic upper bound even when candidates represent larger groups.
				int remainingCapacity = options.MaximumInputCount - currentInputCount;
				long maximumAddition = remainingValues[depth];
				if (remainingCapacity < sorted.Length - depth)
				{
					maximumAddition -= remainingValues[depth + remainingCapacity];
				}
				long maximumValue = currentValue + maximumAddition;
				if (maximumValue < searchLower)
				{
					goto Backtrack;
				}

				if (secondaryCannotImprove && maximumValue < target && target - maximumValue == bestDifference)
				{
					// An equal-distance upper boundary is already terminal, and a fixed
					// above-target lower boundary is covered by distanceIsFinal.
					// Only an equal-distance reachable maximum below target remains.
					goto Backtrack;
				}

				if (candidate.InputCount <= remainingCapacity)
				{
					selectedIndices[selectedCount++] = depth;
					currentValue += candidate.Value;
					currentCost += candidate.Cost;
					currentInputCount += candidate.InputCount;
					depth++;
					continue;
				}
			}

			// An impossible inclusion enters the omit child directly.
			depth++;
			// Consecutive oversized candidates only enter omission children with
			// unchanged path totals and incumbent. Count and cancel every node,
			// while avoiding repeated rank and bound checks for the same path.
			while (depth < sorted.Length && sorted[depth].Candidate.Value > availableValue &&
				visitedNodes < options.MaximumSearchNodes && !cancellationToken.IsCancellationRequested)
			{
				visitedNodes++;
				depth++;
			}
			continue;

		Backtrack:
			if (selectedCount == 0)
			{
				break;
			}

			int includedDepth = selectedIndices[--selectedCount];
			SelectionCandidate<T> includedCandidate = sorted[includedDepth].Candidate;
			currentValue -= includedCandidate.Value;
			currentCost -= includedCandidate.Cost;
			currentInputCount -= includedCandidate.InputCount;
			// Once this candidate is omitted, choosing a later equivalent candidate
			// repeats a combination already visited with the earlier reference.
			depth = sorted[includedDepth].NextDistinct;
		}

		SelectionStatus status = stoppedStatus ?? (bestIndices is null ? SelectionStatus.NoSolution : SelectionStatus.Optimal);
		return CreateResult(sorted, bestIndices, bestSelectedCount, bestValue, bestCost, bestInputCount, visitedNodes, status);
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static SelectionResult<T> CreateResult<T>(
		IndexedCandidate<T>[] sorted,
		int[]? bestIndices,
		int bestSelectedCount,
		long bestValue,
		long bestCost,
		int bestInputCount,
		long visitedNodes,
		SelectionStatus status)
	{
		if (bestIndices is null)
		{
			return new SelectionResult<T>([], 0, 0, 0, visitedNodes, status);
		}

		var selectedInOriginalOrder = new IndexedCandidate<T>[bestSelectedCount];
		for (int i = 0; i < bestSelectedCount; i++)
		{
			selectedInOriginalOrder[i] = sorted[bestIndices[i]];
		}

		Array.Sort(selectedInOriginalOrder, static (left, right) => left.OriginalIndex.CompareTo(right.OriginalIndex));
		var resultCandidates = new SelectionCandidate<T>[bestSelectedCount];
		for (int i = 0; i < bestSelectedCount; i++)
		{
			resultCandidates[i] = selectedInOriginalOrder[i].Candidate;
		}

		return new SelectionResult<T>(resultCandidates, bestValue, bestCost, bestInputCount, visitedNodes, status);
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static void CopySelection(int[] source, int[] destination, int count) =>
		source.AsSpan(0, count).CopyTo(destination);

	private static void ValidateOptions(SelectionOptions options)
	{
		if (!Enum.IsDefined(options.Mode))
		{
			throw new ArgumentOutOfRangeException(nameof(options), "The selection mode is invalid.");
		}

		if (options.MinimumValue <= 0 || options.MaximumDifference < 0 || options.MaximumShortfall < 0 || options.MaximumExcess < 0 ||
			options.MaximumInputCount <= 0 || options.MaximumSearchNodes <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(options), "Value tolerances must be nonnegative; minimum value, input and search limits must be positive.");
		}
	}

	private static bool IsBetter(long currentValue, long currentCost, int currentInputCount, long difference, long target, SelectionMode mode,
		bool hasBest, long bestValue, long bestDifference, long bestCost, int bestInputCount)
	{
		if (!hasBest || difference != bestDifference)
		{
			return !hasBest || difference < bestDifference;
		}

		if (currentCost != bestCost)
		{
			return currentCost < bestCost;
		}

		if (currentInputCount != bestInputCount)
		{
			return currentInputCount < bestInputCount;
		}

		return mode == SelectionMode.Closest && currentValue > target && bestValue < target;
	}

	private static long Distance(long value, long target) => value >= target ? value - target : target - value;

	private static long SaturatingAdd(long value, long addition) => addition > long.MaxValue - value ? long.MaxValue : value + addition;

	private static SelectionResult<T> EmptyResult<T>(SelectionStatus status) => new([], 0, 0, 0, 0, status);

	private readonly record struct IndexedCandidate<T>(SelectionCandidate<T> Candidate, int OriginalIndex, int NextDistinct = 0);
}
