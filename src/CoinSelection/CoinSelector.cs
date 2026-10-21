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
	/// Candidate and underlying-input limits apply independently; a grouped candidate consumes one
	/// candidate slot and its declared number of inputs. Neither limit changes the ranking rules.
	/// Forced omissions of candidates that exceed the value or input capacity are contracted within
	/// the current node; scanning those candidates does not consume additional search nodes.
	/// Suffix bounds account for the remaining candidate and input capacity, and each incumbent tightens the
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

		IndexedCandidate<T>[] sorted = SnapshotCandidates(candidates, out long valueDivisor);
		if (cancellationToken.IsCancellationRequested)
		{
			return EmptyResult<T>(SelectionStatus.Cancelled);
		}

		long[] remainingBounds = PrepareCandidates(sorted, useUniformCostBounds: true, out long uniformValue);
		long maximumShortfall = Math.Min(options.MaximumDifference, options.MaximumShortfall);
		long maximumExcess = Math.Min(options.MaximumDifference, options.MaximumExcess);
		long lowerValue = options.Mode == SelectionMode.AtLeastTarget
			? target
			: target > maximumShortfall ? target - maximumShortfall : 1;
		long upperValue = options.Mode == SelectionMode.AtMostTarget
			? target
			: maximumExcess > long.MaxValue - target ? long.MaxValue : target + maximumExcess;
		lowerValue = Math.Max(lowerValue, options.MinimumValue);
		AlignValueBounds(ref lowerValue, ref upperValue, valueDivisor);
		long minimumDifference = MinimumPossibleDifference(target, lowerValue, upperValue, valueDivisor);
		return options.MaximumCandidateCount < Math.Min(sorted.Length, options.MaximumInputCount)
			? Search<T, LimitedCandidateCount>(sorted, remainingBounds, uniformValue, minimumDifference, target, options, lowerValue, upperValue, cancellationToken)
			: Search<T, UnlimitedCandidateCount>(sorted, remainingBounds, uniformValue, minimumDifference, target, options, lowerValue, upperValue, cancellationToken);
	}

	/// <summary>Finds separate best selections at or below and at or above the target in one search.</summary>
	/// <typeparam name="T">The caller's item or group identifier.</typeparam>
	/// <param name="candidates">Candidates to snapshot. Each candidate can be selected at most once per selection.</param>
	/// <param name="target">The strictly positive desired value, in the candidates' integer units.</param>
	/// <param name="options">Shared constraints and work limit. Mode is ignored; both directions are searched.</param>
	/// <param name="cancellationToken">Cancels the shared search, retaining the best selection found on each side.</param>
	/// <returns>Two independently ranked results with one shared visited-node count.</returns>
	/// <remarks>
	/// Both directions include the target, so an exact selection can appear in both results.
	/// Each side minimizes target difference, then cost, then underlying input count; remaining ties
	/// retain the first selection in the same deterministic order as <see cref="Select{T}"/>.
	/// Candidates are validated, snapshotted and sorted once. MaximumSearchNodes applies to the
	/// combined traversal, not separately to each side. Each result reports the shared node count.
	/// MaximumCandidateCount and MaximumInputCount constrain each side's selection independently.
	/// Forced omissions are contracted within the current node, as in Select.
	/// Cancellation and node exhaustion may leave either side without a selection or with an
	/// unproven partial selection. Once the search begins, an empty allowed interval reports NoSolution.
	/// Validation and snapshotting precede cancellation, as in Select. A token already cancelled at that
	/// point returns Cancelled for both sides before their allowed intervals are examined.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The candidate list is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The target or an option is invalid.</exception>
	/// <exception cref="ArgumentException">A candidate has invalid value, cost, or input count.</exception>
	/// <exception cref="OverflowException">Aggregate candidate values or costs exceed signed 64-bit values.</exception>
	public static TwoSidedSelectionResult<T> SelectBoth<T>(
		IReadOnlyList<SelectionCandidate<T>> candidates,
		long target,
		SelectionOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(candidates);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(target);
		options ??= new SelectionOptions();
		ValidateOptions(options);
		IndexedCandidate<T>[] sorted = SnapshotCandidates(candidates, out long valueDivisor);
		if (cancellationToken.IsCancellationRequested)
		{
			SelectionResult<T> cancelled = EmptyResult<T>(SelectionStatus.Cancelled);
			return new TwoSidedSelectionResult<T>(cancelled, cancelled);
		}

		long[] remainingValues = PrepareCandidates(sorted, useUniformCostBounds: false, out _);
		long shortfall = Math.Min(options.MaximumDifference, options.MaximumShortfall);
		long excess = Math.Min(options.MaximumDifference, options.MaximumExcess);
		long lowerValue = Math.Max(options.MinimumValue, target > shortfall ? target - shortfall : 1);
		long upperValue = SaturatingAdd(target, excess);
		AlignValueBounds(ref lowerValue, ref upperValue, valueDivisor);
		long atMostLower = lowerValue;
		long atMostUpper = Math.Min(target, upperValue);
		AlignValueBounds(ref atMostLower, ref atMostUpper, valueDivisor);
		long atLeastLower = Math.Max(target, lowerValue);
		long atLeastUpper = upperValue;
		AlignValueBounds(ref atLeastLower, ref atLeastUpper, valueDivisor);
		long atMostMaximum = atMostLower <= atMostUpper ? atMostUpper : 0;
		long atLeastMinimum = atLeastLower <= atLeastUpper ? atLeastLower : 0;
		return options.MaximumCandidateCount < Math.Min(sorted.Length, options.MaximumInputCount)
			? SearchBoth<T, LimitedCandidateCount>(sorted, remainingValues, target, options, lowerValue, upperValue, atMostMaximum, atLeastMinimum, cancellationToken)
			: SearchBoth<T, UnlimitedCandidateCount>(sorted, remainingValues, target, options, lowerValue, upperValue, atMostMaximum, atLeastMinimum, cancellationToken);
	}

	private static IndexedCandidate<T>[] SnapshotCandidates<T>(IReadOnlyList<SelectionCandidate<T>> candidates, out long valueDivisor)
	{
		var sorted = new IndexedCandidate<T>[candidates.Count];
		long aggregateValue = 0;
		long aggregateCost = 0;
		long divisor = 0;
		for (int i = 0; i < sorted.Length; i++)
		{
			SelectionCandidate<T>? candidate = candidates[i];
			if (candidate is null || candidate.Value <= 0 || candidate.Cost < 0 || candidate.InputCount <= 0)
			{
				throw new ArgumentException($"Candidate at index {i} must have positive value and input count, and nonnegative cost.", nameof(candidates));
			}

			aggregateValue = checked(aggregateValue + candidate.Value);
			aggregateCost = checked(aggregateCost + candidate.Cost);
			if (divisor != 1)
			{
				long value = candidate.Value;
				while (divisor != 0)
				{
					long remainder = value % divisor;
					value = divisor;
					divisor = remainder;
				}

				divisor = value;
			}

			sorted[i] = new IndexedCandidate<T>(candidate, i);
		}

		valueDivisor = divisor;
		return sorted;
	}

	private static void AlignValueBounds(ref long lowerValue, ref long upperValue, long divisor)
	{
		if (divisor <= 1 || lowerValue > upperValue)
		{
			return;
		}

		// Every subset sum is a multiple of the candidates' greatest common divisor.
		upperValue -= upperValue % divisor;
		long remainder = lowerValue % divisor;
		if (remainder == 0)
		{
			return;
		}

		long adjustment = divisor - remainder;
		if (lowerValue > upperValue || adjustment > upperValue - lowerValue)
		{
			// No multiple fits, including when rounding up would exceed long.MaxValue.
			upperValue = 0;
			return;
		}

		lowerValue += adjustment;
	}

	private static long MinimumPossibleDifference(long target, long lowerValue, long upperValue, long divisor)
	{
		if (lowerValue > upperValue)
		{
			return long.MaxValue;
		}

		if (target < lowerValue)
		{
			return lowerValue - target;
		}

		if (target > upperValue)
		{
			return target - upperValue;
		}

		// Aligned bounds contain both neighboring multiples when the target lies inside.
		// This is only a distance lower bound: neither multiple need be a subset sum.
		if (divisor <= 1)
		{
			return 0;
		}

		long remainder = target % divisor;
		return Math.Min(remainder, divisor - remainder);
	}

	private static long[] PrepareCandidates<T>(IndexedCandidate<T>[] sorted, bool useUniformCostBounds, out long uniformValue)
	{
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

		uniformValue = useUniformCostBounds && sorted.Length > 0 && sorted[0].Candidate.Value == sorted[^1].Candidate.Value
			? sorted[0].Candidate.Value : 0;
		// Equal values need only an arithmetic value bound. Reuse the same suffix
		// buffer for cost bounds in that case; two-sided searches retain value bounds.
		var remainingBounds = new long[sorted.Length + 1];
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
			remainingBounds[i] = remainingBounds[i + 1] + (uniformValue != 0 ? candidate.Cost : candidate.Value);
		}

		return remainingBounds;
	}

	// Compile this loop fully on its first call, independent of tiered profile history.
	// Keep incumbent copying and result mapping outside its register allocation.
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining |
		System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
	private static SelectionResult<T> Search<T, TCandidateLimit>(
		IndexedCandidate<T>[] sorted,
		long[] remainingBounds,
		long uniformValue,
		long minimumDifference,
		long target,
		SelectionOptions options,
		long lowerValue,
		long upperValue,
		CancellationToken cancellationToken)
		where TCandidateLimit : struct
	{
		int pathCapacity = Math.Min(sorted.Length, options.MaximumInputCount);
		if (typeof(TCandidateLimit) == typeof(LimitedCandidateCount))
		{
			pathCapacity = Math.Min(pathCapacity, options.MaximumCandidateCount);
		}
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
		while (true)
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
					distanceIsFinal = difference == minimumDifference;
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
				(typeof(TCandidateLimit) == typeof(LimitedCandidateCount) && selectedCount == options.MaximumCandidateCount) ||
				lowerValue > upperValue || currentValue >= searchUpper)
			{
				goto Backtrack;
			}

			if (distanceIsFinal &&
				(currentCost > bestCost || (currentCost == bestCost && currentInputCount >= bestInputCount)))
			{
				// The request's minimum possible distance cannot improve any further.
				goto Backtrack;
			}

			int remainingCapacity = options.MaximumInputCount - currentInputCount;
			if (distanceIsFinal && bestCost == 0)
			{
				// Cost cannot improve on zero. Reserve only inputs that could improve
				// the count, allowing Closest's otherwise tied above-target value.
				int countImprovement = options.Mode == SelectionMode.Closest && bestValue < target ? 0 : 1;
				remainingCapacity = Math.Min(remainingCapacity, bestInputCount - currentInputCount - countImprovement);
				if (remainingCapacity <= 0)
				{
					goto Backtrack;
				}
			}

		TryCandidate:
			SelectionCandidate<T> candidate = sorted[depth].Candidate;
			long availableValue = searchUpper - currentValue;
			if (candidate.Value <= availableValue && candidate.InputCount <= remainingCapacity)
			{
				int remainingCandidates = remainingCapacity;
				if (typeof(TCandidateLimit) == typeof(LimitedCandidateCount))
				{
					remainingCandidates = Math.Min(remainingCandidates, options.MaximumCandidateCount - selectedCount);
				}
				// Every candidate consumes one candidate slot and at least one input. The
				// largest remaining k values therefore bound both capacities optimistically.
				long maximumAddition;
				if (uniformValue != 0)
				{
					maximumAddition = Math.Min(remainingCandidates, sorted.Length - depth) * uniformValue;
				}
				else
				{
					maximumAddition = remainingBounds[depth];
					if (remainingCandidates < sorted.Length - depth)
					{
						maximumAddition -= remainingBounds[depth + remainingCandidates];
					}
				}
				long maximumValue = currentValue + maximumAddition;
				if (maximumValue < searchLower)
				{
					goto Backtrack;
				}

				if (uniformValue != 0 && (bestValue == target ||
					(lowerValue > target && bestValue == lowerValue) ||
					(upperValue < target && bestValue == upperValue)))
				{
					// Equal values are cost sorted: the first k remaining candidates
					// give an optimistic cost for reaching the proven best value.
					int neededCandidates = (int)((bestValue - currentValue) / uniformValue);
					long minimumCost = currentCost + (remainingBounds[depth] - remainingBounds[depth + neededCandidates]);
					if (minimumCost > bestCost ||
						(minimumCost == bestCost && neededCandidates >= bestInputCount - currentInputCount))
					{
						goto Backtrack;
					}
				}

				if (!distanceIsFinal && maximumValue < target && target - maximumValue == bestDifference && bestIndices is not null &&
					(currentCost > bestCost || (currentCost == bestCost && currentInputCount >= bestInputCount)))
				{
					// An equal-distance upper boundary is already terminal, and a fixed
					// above-target lower boundary is covered by the distance floor.
					// Only an equal-distance reachable maximum below target remains.
					goto Backtrack;
				}

				if (distanceIsFinal && (candidate.Cost > bestCost - currentCost ||
					(candidate.Cost == bestCost - currentCost && candidate.InputCount > bestInputCount - currentInputCount)))
				{
					goto ChargePrunedChild;
				}

				selectedIndices[selectedCount++] = depth;
				currentValue += candidate.Value;
				currentCost += candidate.Cost;
				currentInputCount += candidate.InputCount;
				depth++;
				continue;
			}

			// Forced omissions keep the same prefix and have no inclusion branch.
			// Contract them within this node, still observing cancellation while scanning.
			depth++;
			while (depth < sorted.Length &&
				(sorted[depth].Candidate.Value > availableValue || sorted[depth].Candidate.InputCount > remainingCapacity) &&
				!cancellationToken.IsCancellationRequested)
			{
				depth++;
			}
			if (cancellationToken.IsCancellationRequested)
			{
				continue;
			}

			if (depth < sorted.Length)
			{
				goto TryCandidate;
			}
			goto Backtrack;

		ChargePrunedChild:
			// The inclusion child cannot improve the proven distance or its secondary
			// rank. Charge its node without pushing, evaluating and popping the path.
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
			depth = sorted[depth].NextDistinct;
			if (depth < sorted.Length)
			{
				// The omission child has the same already-ranked prefix. Enter it
				// normally, then reuse the unchanged prefix bounds and capacities.
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
				goto TryCandidate;
			}
			if (cancellationToken.IsCancellationRequested)
			{
				stoppedStatus = SelectionStatus.Cancelled;
				break;
			}

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
			if (depth == sorted.Length)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					stoppedStatus = SelectionStatus.Cancelled;
					break;
				}

				goto Backtrack;
			}
		}

		SelectionStatus status = stoppedStatus ?? (bestIndices is null ? SelectionStatus.NoSolution : SelectionStatus.Optimal);
		return CreateResult(sorted, bestIndices, bestSelectedCount, bestValue, bestCost, bestInputCount, visitedNodes, status);
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining |
		System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
	private static TwoSidedSelectionResult<T> SearchBoth<T, TCandidateLimit>(
		IndexedCandidate<T>[] sorted,
		long[] remainingValues,
		long target,
		SelectionOptions options,
		long lowerValue,
		long upperValue,
		long atMostMaximum,
		long atLeastMinimum,
		CancellationToken cancellationToken)
		where TCandidateLimit : struct
	{
		int pathCapacity = Math.Min(sorted.Length, options.MaximumInputCount);
		if (typeof(TCandidateLimit) == typeof(LimitedCandidateCount))
		{
			pathCapacity = Math.Min(pathCapacity, options.MaximumCandidateCount);
		}
		var selectedIndices = new int[pathCapacity];
		int selectedCount = 0;
		long currentValue = 0;
		long currentCost = 0;
		int currentInputCount = 0;
		int depth = 0;
		long visitedNodes = 0;
		BestSelection atMost = default;
		BestSelection atLeast = default;
		bool bothDistancesAreFinal = false;
		long pruningCost = 0;
		int pruningInputCount = 0;
		long searchLower = lowerValue;
		long searchUpper = upperValue;
		SelectionStatus? stoppedStatus = null;

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
				bool updated = false;
				// Tighten each side independently: a near-target result on one side
				// must not discard a more distant result needed on the other side.
				if (currentValue <= target && atMost.TryUpdate(currentValue, currentCost, currentInputCount,
					selectedIndices, selectedCount, atMostTarget: true))
				{
					searchLower = currentValue;
					updated = true;
				}

				if (currentValue >= target && atLeast.TryUpdate(currentValue, currentCost, currentInputCount,
					selectedIndices, selectedCount, atMostTarget: false))
				{
					searchUpper = currentValue;
					updated = true;
				}

				if (updated)
				{
					// Zero denotes an empty side. Each nonempty side must independently
					// reach its own nearest lattice value before distance cannot improve.
					bothDistancesAreFinal = atMost.Value == atMostMaximum && atLeast.Value == atLeastMinimum;
					if (bothDistancesAreFinal)
					{
						// A branch remains eligible if it can improve either side. The
						// lexicographically worse secondary rank is the permissive bound.
						bool useAtLeast = atMost.Indices is null || (atLeast.Indices is not null &&
							(atLeast.Cost > atMost.Cost || (atLeast.Cost == atMost.Cost && atLeast.InputCount > atMost.InputCount)));
						pruningCost = useAtLeast ? atLeast.Cost : atMost.Cost;
						pruningInputCount = useAtLeast ? atLeast.InputCount : atMost.InputCount;
					}
				}

				if (currentValue == target && currentCost == 0 && currentInputCount == 1)
				{
					// This exact selection is unbeatable in both directions.
					break;
				}
			}

			if (depth == sorted.Length || currentInputCount == options.MaximumInputCount ||
				(typeof(TCandidateLimit) == typeof(LimitedCandidateCount) && selectedCount == options.MaximumCandidateCount) ||
				lowerValue > upperValue || currentValue >= searchUpper)
			{
				goto Backtrack;
			}

			if (bothDistancesAreFinal &&
				(currentCost > pruningCost || (currentCost == pruningCost && currentInputCount >= pruningInputCount)))
			{
				goto Backtrack;
			}

			int remainingCapacity = options.MaximumInputCount - currentInputCount;
			if (bothDistancesAreFinal && pruningCost == 0)
			{
				remainingCapacity = Math.Min(remainingCapacity, pruningInputCount - currentInputCount - 1);
				if (remainingCapacity <= 0)
				{
					goto Backtrack;
				}
			}

		TryCandidate:
			SelectionCandidate<T> candidate = sorted[depth].Candidate;
			long availableValue = searchUpper - currentValue;
			if (candidate.Value <= availableValue && candidate.InputCount <= remainingCapacity)
			{
				int remainingCandidates = remainingCapacity;
				if (typeof(TCandidateLimit) == typeof(LimitedCandidateCount))
				{
					remainingCandidates = Math.Min(remainingCandidates, options.MaximumCandidateCount - selectedCount);
				}
				long maximumAddition = remainingValues[depth];
				if (remainingCandidates < sorted.Length - depth)
				{
					maximumAddition -= remainingValues[depth + remainingCandidates];
				}

				long maximumValue = currentValue + maximumAddition;
				if (maximumValue < searchLower ||
					(maximumValue < target && maximumValue == atMost.Value &&
					 atMost.SecondaryCannotImprove(currentCost, currentInputCount)))
				{
					goto Backtrack;
				}

				if (bothDistancesAreFinal && (candidate.Cost > pruningCost - currentCost ||
					(candidate.Cost == pruningCost - currentCost && candidate.InputCount > pruningInputCount - currentInputCount)))
				{
					goto ChargePrunedChild;
				}

				selectedIndices[selectedCount++] = depth;
				currentValue += candidate.Value;
				currentCost += candidate.Cost;
				currentInputCount += candidate.InputCount;
				depth++;
				continue;
			}

			depth++;
			while (depth < sorted.Length &&
				(sorted[depth].Candidate.Value > availableValue || sorted[depth].Candidate.InputCount > remainingCapacity) &&
				!cancellationToken.IsCancellationRequested)
			{
				depth++;
			}
			if (cancellationToken.IsCancellationRequested)
			{
				continue;
			}

			if (depth < sorted.Length)
			{
				goto TryCandidate;
			}
			goto Backtrack;

		ChargePrunedChild:
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
			depth = sorted[depth].NextDistinct;
			if (depth < sorted.Length)
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
				goto TryCandidate;
			}
			if (cancellationToken.IsCancellationRequested)
			{
				stoppedStatus = SelectionStatus.Cancelled;
				break;
			}

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
			depth = sorted[includedDepth].NextDistinct;
			if (depth == sorted.Length)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					stoppedStatus = SelectionStatus.Cancelled;
					break;
				}

				goto Backtrack;
			}
		}

		SelectionStatus atMostStatus = lowerValue > Math.Min(target, upperValue)
			? SelectionStatus.NoSolution
			: stoppedStatus ?? (atMost.Indices is null ? SelectionStatus.NoSolution : SelectionStatus.Optimal);
		SelectionStatus atLeastStatus = Math.Max(lowerValue, target) > upperValue
			? SelectionStatus.NoSolution
			: stoppedStatus ?? (atLeast.Indices is null ? SelectionStatus.NoSolution : SelectionStatus.Optimal);
		return new TwoSidedSelectionResult<T>(
			CreateResult(sorted, atMost.Indices, atMost.SelectedCount, atMost.Value, atMost.Cost,
				atMost.InputCount, visitedNodes, atMostStatus),
			CreateResult(sorted, atLeast.Indices, atLeast.SelectedCount, atLeast.Value, atLeast.Cost,
				atLeast.InputCount, visitedNodes, atLeastStatus));
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
			options.MaximumInputCount <= 0 || options.MaximumCandidateCount <= 0 || options.MaximumSearchNodes <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(options), "Value tolerances must be nonnegative; minimum value, candidate, input and search limits must be positive.");
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

	private struct BestSelection
	{
		public int[]? Indices;
		public int SelectedCount;
		public long Value;
		public long Cost;
		public int InputCount;

		public readonly bool SecondaryCannotImprove(long cost, int inputCount) =>
			Indices is not null && (cost > Cost || (cost == Cost && inputCount >= InputCount));

		public bool TryUpdate(long value, long cost, int inputCount, int[] path, int selectedCount, bool atMostTarget)
		{
			if (Indices is not null)
			{
				bool better;
				if (value != Value)
				{
					better = atMostTarget ? value > Value : value < Value;
				}
				else if (cost != Cost)
				{
					better = cost < Cost;
				}
				else
				{
					better = inputCount < InputCount;
				}

				if (!better)
				{
					return false;
				}
			}

			Indices ??= new int[path.Length];
			SelectedCount = selectedCount;
			Value = value;
			Cost = cost;
			InputCount = inputCount;
			CopySelection(path, Indices, selectedCount);
			return true;
		}
	}

	// Value-type instantiations let the JIT remove candidate-limit checks entirely
	// when that limit cannot bind. Neither tag is instantiated or adds a buffer.
	private readonly struct UnlimitedCandidateCount { }
	private readonly struct LimitedCandidateCount { }

	private readonly record struct IndexedCandidate<T>(SelectionCandidate<T> Candidate, int OriginalIndex, int NextDistinct = 0);
}
