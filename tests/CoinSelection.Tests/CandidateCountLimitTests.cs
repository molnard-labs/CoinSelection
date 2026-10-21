using Xunit;

namespace CoinSelection.Tests;

public class CandidateCountLimitTests
{
	[Fact]
	public void TheCandidateLimitDefaultsToUnlimited()
	{
		Assert.Equal(int.MaxValue, new SelectionOptions().MaximumCandidateCount);
	}

	[Theory]
	[InlineData(1, 4, 1)]
	[InlineData(1, 3, 8)]
	[InlineData(2, 3, 6)]
	[InlineData(2, 4, 1)]
	public void AtomicAndUnderlyingLimitsHaveIndependentEffects(int candidateLimit, int inputLimit, int expectedMask)
	{
		SelectionCandidate<int>[] candidates =
		[
			new(0, 10, 0, 4), new(1, 6, 1), new(2, 4, 1), new(3, 9, 3)
		];
		SelectionOptions options = new()
		{
			Mode = SelectionMode.Closest, MaximumCandidateCount = candidateLimit, MaximumInputCount = inputLimit
		};

		SelectionResult<int> closest = CoinSelector.Select(candidates, 10, options, TestContext.Current.CancellationToken);
		AssertReferences(closest, candidates.Where((_, index) => (expectedMask & (1 << index)) != 0).ToArray());
		AssertAllModesAndBoth(candidates, 10, options);
	}

	[Theory]
	[InlineData(0, 4)]
	[InlineData(5, 1)]
	public void FewerAtomicCandidatesDoNotOutrankLowerCostOrFewerUnderlyingInputs(long singleCost, int singleInputs)
	{
		SelectionCandidate<int>[] candidates = [new(0, 10, singleCost, singleInputs), new(1, 6), new(2, 4)];
		SelectionOptions options = new() { MaximumCandidateCount = 2, MaximumDifference = 0 };

		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			SelectionResult<int> result = CoinSelector.Select(candidates, 10, options with { Mode = mode },
				TestContext.Current.CancellationToken);
			AssertReferences(result, candidates[1], candidates[2]);
			AssertComplete(candidates, 10, options with { Mode = mode }, result);
		}
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 10, options, TestContext.Current.CancellationToken);
		AssertReferences(both.AtMostTarget, candidates[1], candidates[2]);
		AssertReferences(both.AtLeastTarget, candidates[1], candidates[2]);
	}

	[Theory]
	[InlineData(3, 22)]
	[InlineData(2, 9)]
	public void CandidateCountIsAConstraintAndNotAnAdditionalTieBreaker(int candidateLimit, int expectedMask)
	{
		// 8+1+1 is encountered first. Its value, cost and actual input count tie 6+4.
		SelectionCandidate<int>[] candidates = [new(7, 6, 0, 2), new(7, 1), new(7, 8), new(7, 4), new(7, 1)];
		SelectionCandidate<int>[] expected = candidates.Where((_, index) => (expectedMask & (1 << index)) != 0).ToArray();
		SelectionOptions options = new()
		{
			MaximumCandidateCount = candidateLimit, MaximumInputCount = 3, MaximumDifference = 0
		};

		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			SelectionResult<int> result = CoinSelector.Select(candidates, 10, options with { Mode = mode },
				TestContext.Current.CancellationToken);
			AssertReferences(result, expected);
			Assert.Equal(3, result.InputCount);
		}
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 10, options, TestContext.Current.CancellationToken);
		AssertReferences(both.AtMostTarget, expected);
		AssertReferences(both.AtLeastTarget, expected);
		AssertAllModesAndBoth(candidates, 10, options);
	}

	[Fact]
	public void SmallWeightedSelectionsMatchIndependentCompleteSubsetEnumeration()
	{
		Random random = new(62_987);
		for (int sample = 0; sample < 96; sample++)
		{
			int unit = sample % 4 == 0 ? 100 : 1;
			SelectionCandidate<int>[] candidates = Enumerable.Range(0, sample % 9)
				.Select(index => new SelectionCandidate<int>(index % 3, unit * random.Next(1, 16),
					random.Next(0, 5), random.Next(1, 6))).ToArray();
			long target = unit * random.Next(1, 31) + (sample % 4 == 0 ? 17 : 0);
			SelectionOptions options = new()
			{
				Mode = (SelectionMode)(sample % 3),
				MaximumCandidateCount = sample % 6 == 0 ? int.MaxValue : random.Next(1, 6),
				MaximumInputCount = sample % 7 == 0 ? int.MaxValue : random.Next(1, 16),
				MinimumValue = sample % 8 == 0 ? target + 1 : 1,
				MaximumDifference = sample % 5 == 0 ? random.Next(0, 9) * unit : long.MaxValue,
				MaximumShortfall = sample % 3 == 0 ? random.Next(0, 11) * unit : long.MaxValue,
				MaximumExcess = sample % 4 == 0 ? random.Next(0, 11) * unit : long.MaxValue,
				MaximumSearchNodes = long.MaxValue
			};
			AssertAllModesAndBoth(candidates, target, options);
		}
	}

	[Theory]
	[InlineData(35, 2)]
	[InlineData(35, 3)]
	[InlineData(30, 3)]
	public void UniformValuesKeepCorrectCostBoundsWithBothCountLimits(long target, int candidateLimit)
	{
		SelectionCandidate<int>[] candidates =
		[
			new(0, 10, 4, 3), new(1, 10, 0, 2), new(2, 10),
			new(3, 10, 1), new(4, 10, 2, 2), new(5, 10, 3)
		];
		foreach (int inputLimit in new[] { 3, 6 })
		{
			AssertAllModesAndBoth(candidates, target, new()
			{
				MaximumCandidateCount = candidateLimit, MaximumInputCount = inputLimit,
				MaximumSearchNodes = long.MaxValue
			});
		}
	}

	[Fact]
	public void TheCandidateLimitAloneCanProveAnExactUniformTargetImpossibleAtTheRoot()
	{
		SelectionCandidate<int>[] candidates = [new(0, 10), new(1, 10, 1, 2), new(2, 10, 2), new(3, 10, 3, 2)];
		SelectionOptions options = new()
		{
			MaximumCandidateCount = 2, MaximumInputCount = 6, MaximumDifference = 0
		};
		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			SelectionOptions singleOptions = options with { Mode = mode };
			SelectionResult<int> single = CoinSelector.Select(candidates, 30, singleOptions, TestContext.Current.CancellationToken);
			AssertComplete(candidates, 30, singleOptions, single);
			Assert.InRange(single.VisitedNodes, 0, 1);
		}
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 30, options, TestContext.Current.CancellationToken);
		AssertComplete(candidates, 30, options with { Mode = SelectionMode.AtMostTarget }, both.AtMostTarget);
		AssertComplete(candidates, 30, options with { Mode = SelectionMode.AtLeastTarget }, both.AtLeastTarget);
		Assert.InRange(both.VisitedNodes, 0, 1);
	}

	[Fact]
	public void MaximumIntegerWeightsAndLongAmountsDoNotChangeTheMeaningOfOneCandidate()
	{
		SelectionCandidate<int> maximum = new(0, long.MaxValue, long.MaxValue, int.MaxValue);
		SelectionOptions one = new()
		{
			MaximumCandidateCount = 1, MaximumInputCount = int.MaxValue,
			MaximumDifference = 0, MaximumSearchNodes = long.MaxValue
		};
		AssertAllModesAndBoth([maximum], long.MaxValue, one);

		SelectionCandidate<int>[] candidates = [new(0, long.MaxValue - 1, 7, int.MaxValue), new(1, 1)];
		AssertAllModesAndBoth(candidates, long.MaxValue - 1, one);
		// Both atomic candidates fit the group limit, but their actual input sum is int.MaxValue + 1.
		AssertAllModesAndBoth(candidates, long.MaxValue, one with { MaximumCandidateCount = 2 });
		AssertAllModesAndBoth([new(0, 6, 0, 2), new(1, 4)], 10,
			one with { MaximumCandidateCount = int.MaxValue });
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(5)]
	[InlineData(32)]
	public void ExplicitUnlimitedCandidateCountKeepsDefaultBoundedResultsAndNodeAccounting(long budget)
	{
		SelectionCandidate<int>[] candidates = [new(0, 19, 2, 3), new(1, 11, 0, 2), new(2, 7), new(3, 3, 0, 2), new(4, 2, 1)];
		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			SelectionOptions defaults = new()
			{
				Mode = mode, MaximumInputCount = 5, MaximumShortfall = 3,
				MaximumExcess = 4, MaximumSearchNodes = budget
			};
			SelectionOptions explicitLimit = defaults with { MaximumCandidateCount = int.MaxValue };
			AssertSameResult(CoinSelector.Select(candidates, 23, defaults, TestContext.Current.CancellationToken),
				CoinSelector.Select(candidates, 23, explicitLimit, TestContext.Current.CancellationToken));
			TwoSidedSelectionResult<int> oldBoth = CoinSelector.SelectBoth(candidates, 23, defaults, TestContext.Current.CancellationToken);
			TwoSidedSelectionResult<int> explicitBoth = CoinSelector.SelectBoth(candidates, 23, explicitLimit, TestContext.Current.CancellationToken);
			AssertSameResult(oldBoth.AtMostTarget, explicitBoth.AtMostTarget);
			AssertSameResult(oldBoth.AtLeastTarget, explicitBoth.AtLeastTarget);
			Assert.Equal(oldBoth.VisitedNodes, explicitBoth.VisitedNodes);
		}
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(5)]
	[InlineData(8)]
	[InlineData(16)]
	public void BoundedSelectionsRespectBothCountsAndTheSharedNodeBudget(long budget)
	{
		SelectionCandidate<int>[] candidates =
		[
			new(0, 17, 3, 4), new(1, 13, 2), new(2, 11, 0, 3), new(3, 7, 1, 2),
			new(4, 5), new(5, 3, 1), new(6, 2, 0, 2)
		];
		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			SelectionOptions options = new()
			{
				Mode = mode, MaximumCandidateCount = 2, MaximumInputCount = 4,
				MaximumShortfall = 6, MaximumExcess = 5, MaximumDifference = 6, MaximumSearchNodes = budget
			};
			AssertBounded(candidates, 24, options,
				CoinSelector.Select(candidates, 24, options, TestContext.Current.CancellationToken));
			TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 24, options, TestContext.Current.CancellationToken);
			AssertBounded(candidates, 24, options with { Mode = SelectionMode.AtMostTarget }, both.AtMostTarget);
			AssertBounded(candidates, 24, options with { Mode = SelectionMode.AtLeastTarget }, both.AtLeastTarget);
			Assert.Equal(both.VisitedNodes, both.AtMostTarget.VisitedNodes);
			Assert.Equal(both.VisitedNodes, both.AtLeastTarget.VisitedNodes);
		}
	}

	[Fact]
	public void PreCancellationPreservesBothCountConstraintsWithoutEnteringNodes()
	{
		SelectionCandidate<int>[] candidates = [new(0, 9, 0, 2), new(1, 6), new(2, 3)];
		SelectionOptions options = new() { MaximumCandidateCount = 1, MaximumInputCount = 2 };
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		SelectionResult<int> single = CoinSelector.Select(candidates, 9, options, cancellation.Token);
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 9, options, cancellation.Token);
		foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.Cancelled, result.Status);
			Assert.Equal(0, result.VisitedNodes);
			AssertSnapshot(candidates, 9, options, result);
			Assert.False(result.HasSelection);
		}
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(int.MinValue)]
	public void InvalidCandidateLimitsAreRejectedEvenForEmptyInput(int limit)
	{
		SelectionOptions options = new() { MaximumCandidateCount = limit };
		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.Select<int>([], 1, options,
			TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.SelectBoth<int>([], 1, options,
			TestContext.Current.CancellationToken));
	}

	private static void AssertAllModesAndBoth(SelectionCandidate<int>[] candidates, long target, SelectionOptions options)
	{
		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			SelectionOptions singleOptions = options with { Mode = mode };
			AssertComplete(candidates, target, singleOptions,
				CoinSelector.Select(candidates, target, singleOptions, TestContext.Current.CancellationToken));
		}
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, target, options, TestContext.Current.CancellationToken);
		AssertComplete(candidates, target, options with { Mode = SelectionMode.AtMostTarget }, both.AtMostTarget);
		AssertComplete(candidates, target, options with { Mode = SelectionMode.AtLeastTarget }, both.AtLeastTarget);
		Assert.Equal(both.VisitedNodes, both.AtMostTarget.VisitedNodes);
		Assert.Equal(both.VisitedNodes, both.AtLeastTarget.VisitedNodes);
	}

	private static void AssertComplete(SelectionCandidate<int>[] candidates, long target, SelectionOptions options,
		SelectionResult<int> actual)
	{
		SelectionCandidate<int>[] expected = Enumerate(candidates, target, options);
		Assert.Equal(expected.Length == 0 ? SelectionStatus.NoSolution : SelectionStatus.Optimal, actual.Status);
		AssertSnapshot(candidates, target, options, actual);
		AssertReferences(actual, expected);
	}

	private static void AssertBounded(SelectionCandidate<int>[] candidates, long target, SelectionOptions options,
		SelectionResult<int> actual)
	{
		AssertSnapshot(candidates, target, options, actual);
		if (actual.Status is SelectionStatus.Optimal or SelectionStatus.NoSolution)
		{
			AssertComplete(candidates, target, options, actual);
		}
		else
		{
			Assert.Equal(SelectionStatus.SearchLimitReached, actual.Status);
		}
	}

	private static void AssertSnapshot(SelectionCandidate<int>[] candidates, long target, SelectionOptions options,
		SelectionResult<int> result)
	{
		Assert.InRange(result.VisitedNodes, 0, options.MaximumSearchNodes);
		Assert.InRange(result.SelectedCandidates.Count, 0, options.MaximumCandidateCount);
		Assert.Equal(result.SelectedCandidates.Sum(candidate => candidate.Value), result.TotalValue);
		Assert.Equal(result.SelectedCandidates.Sum(candidate => candidate.Cost), result.TotalCost);
		Assert.Equal(result.SelectedCandidates.Sum(candidate => (long)candidate.InputCount), result.InputCount);
		int previous = -1;
		foreach (SelectionCandidate<int> candidate in result.SelectedCandidates)
		{
			int index = Array.FindIndex(candidates, previous + 1, item => ReferenceEquals(item, candidate));
			Assert.True(index > previous);
			previous = index;
		}
		if (result.HasSelection)
		{
			Assert.True(Valid(result.TotalValue, result.InputCount, result.SelectedCandidates.Count, target, options));
		}
	}

	private static SelectionCandidate<int>[] Enumerate(SelectionCandidate<int>[] candidates, long target, SelectionOptions options)
	{
		int[] order = Enumerable.Range(0, candidates.Length).OrderByDescending(index => candidates[index].Value)
			.ThenBy(index => candidates[index].Cost).ThenBy(index => candidates[index].InputCount).ThenBy(index => index).ToArray();
		(long Difference, long Cost, long Inputs, int Side)? bestRank = null;
		int[] best = [];
		// Visit every subset, without search pruning; descending masks retain canonical include-first ties.
		for (int mask = (1 << candidates.Length) - 1; mask > 0; mask--)
		{
			int[] indices = order.Where((_, position) => (mask & (1 << (order.Length - position - 1))) != 0).ToArray();
			long value = indices.Sum(index => candidates[index].Value);
			long cost = indices.Sum(index => candidates[index].Cost);
			long inputs = indices.Sum(index => (long)candidates[index].InputCount);
			if (!Valid(value, inputs, indices.Length, target, options)) continue;
			var rank = (Math.Abs(value - target), cost, inputs, options.Mode == SelectionMode.Closest && value < target ? 1 : 0);
			if (bestRank is null || rank.CompareTo(bestRank.Value) < 0)
			{
				bestRank = rank;
				best = indices.Order().ToArray();
			}
		}
		return best.Select(index => candidates[index]).ToArray();
	}

	private static bool Valid(long value, long inputs, int count, long target, SelectionOptions options)
	{
		long shortfall = Math.Max(0, target - value), excess = Math.Max(0, value - target);
		return value >= options.MinimumValue && inputs <= options.MaximumInputCount && count <= options.MaximumCandidateCount &&
			shortfall <= options.MaximumShortfall && excess <= options.MaximumExcess && shortfall + excess <= options.MaximumDifference &&
			(options.Mode != SelectionMode.AtMostTarget || value <= target) &&
			(options.Mode != SelectionMode.AtLeastTarget || value >= target);
	}

	private static void AssertSameResult(SelectionResult<int> expected, SelectionResult<int> actual)
	{
		Assert.Equal(expected.Status, actual.Status);
		Assert.Equal(expected.VisitedNodes, actual.VisitedNodes);
		Assert.Equal(expected.TotalValue, actual.TotalValue);
		Assert.Equal(expected.TotalCost, actual.TotalCost);
		Assert.Equal(expected.InputCount, actual.InputCount);
		AssertReferences(actual, expected.SelectedCandidates.ToArray());
	}

	private static void AssertReferences(SelectionResult<int> actual, params SelectionCandidate<int>[] expected)
	{
		Assert.Equal(expected.Length, actual.SelectedCandidates.Count);
		for (int index = 0; index < expected.Length; index++)
		{
			Assert.Same(expected[index], actual.SelectedCandidates[index]);
		}
	}
}
