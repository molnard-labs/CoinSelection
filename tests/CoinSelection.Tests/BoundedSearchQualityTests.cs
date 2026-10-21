using Xunit;

namespace CoinSelection.Tests;

public class BoundedSearchQualityTests
{
	private static readonly long[] Budgets = [1, 2, 8, 32];

	[Fact]
	public void RootOnlyBudgetDoesNotEnterAnEligibleChildAfterForcedOmissions()
	{
		SelectionCandidate<int>[] candidates = [new(0, 1_001), new(1, 1_000), new(2, 10, 2)];
		SelectionOptions options = new() { MaximumDifference = 0, MaximumSearchNodes = 1 };
		SelectionResult<int> single = CoinSelector.Select(candidates, 10, options, TestContext.Current.CancellationToken);
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 10, options, TestContext.Current.CancellationToken);

		foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.SearchLimitReached, result.Status);
			Assert.False(result.HasSelection);
			Assert.Equal(1, result.VisitedNodes);
		}
	}

	[Theory]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.Closest)]
	public void OversizedValuePrefixDoesNotExhaustTheBudgetBeforeAnExactCandidate(SelectionMode mode)
	{
		SelectionCandidate<int>[] candidates = Enumerable.Range(0, 48)
			.Select(index => new SelectionCandidate<int>(index, 1_000 + index)).Append(new(48, 10, 2)).ToArray();
		SelectionOptions options = new() { Mode = mode, MaximumDifference = 0, MaximumSearchNodes = 2 };

		SelectionResult<int> single = CoinSelector.Select(candidates, 10, options, TestContext.Current.CancellationToken);
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 10, options, TestContext.Current.CancellationToken);

		foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.Optimal, result.Status);
			Assert.True(result.HasSelection);
			Assert.Equal(10, result.TotalValue);
			Assert.Equal(2, result.TotalCost);
			Assert.Equal(1, result.InputCount);
			Assert.Same(candidates[^1], Assert.Single(result.SelectedCandidates));
			Assert.Equal(2, result.VisitedNodes);
		}
		Assert.InRange(both.VisitedNodes, 1, options.MaximumSearchNodes);
	}

	[Theory]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.Closest)]
	public void HeavyGroupPrefixDoesNotHideAnEligibleSingleInput(SelectionMode mode)
	{
		SelectionCandidate<int>[] candidates = Enumerable.Range(0, 48)
			.Select(index => new SelectionCandidate<int>(index, 100, 0, 2)).Append(new(48, 100, 1)).ToArray();
		SelectionOptions options = new() { Mode = mode, MaximumDifference = 0, MaximumInputCount = 1, MaximumSearchNodes = 2 };

		SelectionResult<int> single = CoinSelector.Select(candidates, 100, options, TestContext.Current.CancellationToken);
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 100, options, TestContext.Current.CancellationToken);

		foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			Assert.True(result.HasSelection);
			Assert.Equal(100, result.TotalValue);
			Assert.Equal(1, result.TotalCost);
			Assert.Equal(1, result.InputCount);
			Assert.Same(candidates[^1], Assert.Single(result.SelectedCandidates));
			Assert.InRange(result.VisitedNodes, 1, options.MaximumSearchNodes);
		}
	}

	[Theory]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.Closest)]
	public void ForcedGroupOmissionsUseTheCapacityRemainingAfterTheSelectedPrefix(SelectionMode mode)
	{
		SelectionCandidate<int>[] candidates = [new(0, 7), .. Enumerable.Range(1, 32)
			.Select(index => new SelectionCandidate<int>(index, 3, 0, 2)), new(33, 3, 1)];
		SelectionOptions options = new() { Mode = mode, MaximumDifference = 0, MaximumInputCount = 2, MaximumSearchNodes = 3 };

		SelectionResult<int> single = CoinSelector.Select(candidates, 10, options, TestContext.Current.CancellationToken);
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 10, options, TestContext.Current.CancellationToken);

		foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			Assert.True(result.HasSelection);
			Assert.Equal(10, result.TotalValue);
			Assert.Equal(1, result.TotalCost);
			Assert.Equal(2, result.InputCount);
			Assert.Collection(result.SelectedCandidates,
				candidate => Assert.Same(candidates[0], candidate),
				candidate => Assert.Same(candidates[^1], candidate));
			Assert.InRange(result.VisitedNodes, 1, options.MaximumSearchNodes);
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void NoSolutionCanBeProvedWithoutEnteringAnImpossibleInclusion(bool oversizedGroups)
	{
		SelectionCandidate<int>[] candidates = oversizedGroups
			? [new(0, 100, 0, 3), new(1, 99, 0, 3)]
			: [new(0, 1_000), new(1, 1_001)];
		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			SelectionOptions options = new() { Mode = mode, MaximumDifference = 1, MaximumInputCount = 2, MaximumSearchNodes = 1 };
			SelectionResult<int> single = CoinSelector.Select(candidates, 100, options, TestContext.Current.CancellationToken);
			TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 100, options, TestContext.Current.CancellationToken);

			AssertNoSolution(single);
			AssertNoSolution(both.AtMostTarget);
			AssertNoSolution(both.AtLeastTarget);
			Assert.InRange(single.VisitedNodes, 1, options.MaximumSearchNodes);
			Assert.InRange(both.VisitedNodes, 1, options.MaximumSearchNodes);
		}
	}

	[Fact]
	public void BothSidesReceiveTheirOwnSelectionsAfterAnOversizedPrefix()
	{
		SelectionCandidate<int>[] candidates = Enumerable.Range(0, 32)
			.Select(index => new SelectionCandidate<int>(index, 1_000 + index)).Concat([new(32, 11, 1), new(33, 9, 1)]).ToArray();
		SelectionOptions options = new() { MaximumShortfall = 1, MaximumExcess = 1, MaximumInputCount = 1, MaximumSearchNodes = 4 };

		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 10, options, TestContext.Current.CancellationToken);

		Assert.True(result.AtMostTarget.HasSelection);
		Assert.True(result.AtLeastTarget.HasSelection);
		Assert.Same(candidates[^1], Assert.Single(result.AtMostTarget.SelectedCandidates));
		Assert.Same(candidates[^2], Assert.Single(result.AtLeastTarget.SelectedCandidates));
		Assert.Equal(9, result.AtMostTarget.TotalValue);
		Assert.Equal(11, result.AtLeastTarget.TotalValue);
		Assert.InRange(result.VisitedNodes, 1, options.MaximumSearchNodes);
		Assert.Equal(result.VisitedNodes, result.AtMostTarget.VisitedNodes);
		Assert.Equal(result.VisitedNodes, result.AtLeastTarget.VisitedNodes);
	}

	[Fact]
	public void ALocallyClosestPrefixDoesNotReplaceTheNeedToSearchItsOmission()
	{
		SelectionCandidate<int>[] candidates = [new(0, 9), new(1, 6, 1), new(2, 4, 1)];
		SelectionOptions options = new() { Mode = SelectionMode.AtMostTarget, MaximumSearchNodes = 2 };

		SelectionResult<int> early = CoinSelector.Select(candidates, 10, options, TestContext.Current.CancellationToken);
		SelectionResult<int> complete = CoinSelector.Select(candidates, 10,
			options with { MaximumSearchNodes = 32 }, TestContext.Current.CancellationToken);

		Assert.True(early.HasSelection);
		Assert.Equal(9, early.TotalValue);
		Assert.InRange(early.VisitedNodes, 1, options.MaximumSearchNodes);
		Assert.Equal(SelectionStatus.Optimal, complete.Status);
		Assert.Equal(10, complete.TotalValue);
		Assert.Equal(2, complete.TotalCost);
		Assert.Equal([1, 2], complete.SelectedCandidates.Select(candidate => candidate.Item));
	}

	[Fact]
	public void IncreasingTheNodeBudgetNeverWorsensTheRankOnEitherSide()
	{
		SelectionCandidate<int>[][] fixtures =
		[
			[new(0, 60), new(1, 19), new(2, 18), new(3, 11, 2), new(4, 10, 3), new(5, 9)],
			[new(0, 7), new(1, 3, 0, 2), new(2, 3, 0, 2), new(3, 3, 1)],
			[new(0, 9, 0, 2), new(1, 6, 1), new(2, 4, 1), new(3, 1), new(4, 60, 0, 3)],
			[new(0, 12, 0, 3), new(1, 11, 1), new(2, 9, 1), new(3, 8, 0, 2), new(4, 1)]
		];
		foreach (SelectionCandidate<int>[] candidates in fixtures)
		{
			foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
			{
				SelectionResult<int>? previous = null;
				TwoSidedSelectionResult<int>? previousBoth = null;
				foreach (long budget in Budgets)
				{
					SelectionOptions options = new() { Mode = mode, MaximumShortfall = 2, MaximumExcess = 1, MaximumInputCount = 2, MaximumSearchNodes = budget };
					SelectionResult<int> current = CoinSelector.Select(candidates, 10, options, TestContext.Current.CancellationToken);
					SelectionResult<int> repeated = CoinSelector.Select(candidates, 10, options, TestContext.Current.CancellationToken);
					TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 10, options, TestContext.Current.CancellationToken);
					AssertValid(current, candidates, 10, options, mode);
					AssertValid(both.AtMostTarget, candidates, 10, options, SelectionMode.AtMostTarget);
					AssertValid(both.AtLeastTarget, candidates, 10, options, SelectionMode.AtLeastTarget);
					Assert.Equal(current.Status, repeated.Status);
					Assert.Equal(current.VisitedNodes, repeated.VisitedNodes);
					Assert.Equal(current.SelectedCandidates, repeated.SelectedCandidates);
					Assert.InRange(both.VisitedNodes, 1, budget);
					if (previous is not null && previousBoth is not null)
					{
						AssertDoesNotWorsen(previous, current, 10, mode);
						AssertDoesNotWorsen(previousBoth.AtMostTarget, both.AtMostTarget, 10, SelectionMode.AtMostTarget);
						AssertDoesNotWorsen(previousBoth.AtLeastTarget, both.AtLeastTarget, 10, SelectionMode.AtLeastTarget);
					}
					previous = current;
					previousBoth = both;
				}
			}
		}
	}

	[Fact]
	public void NearMaximumAmountsAndCostsDoNotOverflowWhileOmittingAnImpossiblePrefix()
	{
		SelectionCandidate<int>[] candidates = [new(0, long.MaxValue - 8, long.MaxValue - 1, int.MaxValue), new(1, 8, 1)];
		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			SelectionOptions options = new() { Mode = mode, MaximumDifference = 0, MaximumInputCount = 1, MaximumSearchNodes = 2 };
			SelectionResult<int> single = CoinSelector.Select(candidates, 8, options, TestContext.Current.CancellationToken);
			TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 8, options, TestContext.Current.CancellationToken);

			foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
			{
				Assert.True(result.HasSelection);
				Assert.Equal(8, result.TotalValue);
				Assert.Equal(1, result.TotalCost);
				Assert.Equal(1, result.InputCount);
				Assert.Same(candidates[1], Assert.Single(result.SelectedCandidates));
				Assert.InRange(result.VisitedNodes, 1, options.MaximumSearchNodes);
			}
		}
	}

	[Fact]
	public void PreCancellationReturnsBeforeProcessingAnImpossiblePrefix()
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		SelectionCandidate<int>[] candidates = [new(0, 1_001), new(1, 1_000), new(2, 10, 1)];
		SelectionOptions options = new() { MaximumDifference = 0, MaximumSearchNodes = 2 };
		SelectionResult<int> single = CoinSelector.Select(candidates, 10, options, cancellation.Token);
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 10, options, cancellation.Token);

		foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.Cancelled, result.Status);
			Assert.False(result.HasSelection);
			Assert.Equal(0, result.VisitedNodes);
		}
	}

	[Fact]
	public void CompletedSmallSearchesKeepTheExhaustiveRankAndCanonicalCandidateReferences()
	{
		Random random = new(73_941);
		for (int sample = 0; sample < 64; sample++)
		{
			SelectionCandidate<int>[] candidates = Enumerable.Range(0, random.Next(0, 11))
				.Select(index => new SelectionCandidate<int>(index, random.Next(1, 41), random.Next(0, 6), random.Next(1, 6))).ToArray();
			long target = random.Next(1, 65);
			SelectionOptions policy = new()
			{
				MinimumValue = sample % 6 == 0 ? target + 1 : 1,
				MaximumShortfall = sample % 7 == 0 ? long.MaxValue : random.Next(0, 20),
				MaximumExcess = sample % 8 == 0 ? long.MaxValue : random.Next(0, 20),
				MaximumDifference = sample % 3 == 0 ? random.Next(0, 15) : long.MaxValue,
				MaximumInputCount = random.Next(1, 13),
				MaximumSearchNodes = 65_536
			};
			foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
			{
				SelectionOptions options = policy with { Mode = mode };
				AssertMatchesOracle(candidates, target, options, mode,
					CoinSelector.Select(candidates, target, options, TestContext.Current.CancellationToken));
				TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, target, options, TestContext.Current.CancellationToken);
				AssertMatchesOracle(candidates, target, options, SelectionMode.AtMostTarget, both.AtMostTarget);
				AssertMatchesOracle(candidates, target, options, SelectionMode.AtLeastTarget, both.AtLeastTarget);
			}
		}
	}

	private static void AssertNoSolution<T>(SelectionResult<T> result)
	{
		Assert.Equal(SelectionStatus.NoSolution, result.Status);
		Assert.False(result.HasSelection);
		Assert.False(result.IsOptimal);
		Assert.Equal(0, result.TotalValue);
		Assert.Equal(0, result.TotalCost);
		Assert.Equal(0, result.InputCount);
	}

	private static void AssertValid(SelectionResult<int> result, SelectionCandidate<int>[] candidates,
		long target, SelectionOptions options, SelectionMode mode)
	{
		Assert.InRange(result.VisitedNodes, 1, options.MaximumSearchNodes);
		Assert.Equal(result.SelectedCandidates.Sum(candidate => candidate.Value), result.TotalValue);
		Assert.Equal(result.SelectedCandidates.Sum(candidate => candidate.Cost), result.TotalCost);
		Assert.Equal(result.SelectedCandidates.Sum(candidate => candidate.InputCount), result.InputCount);
		if (!result.HasSelection)
		{
			return;
		}
		Assert.InRange(result.InputCount, 1, options.MaximumInputCount);
		Assert.True(result.TotalValue >= options.MinimumValue);
		long shortfall = Math.Max(0, target - result.TotalValue);
		long excess = Math.Max(0, result.TotalValue - target);
		Assert.InRange(shortfall, 0, options.MaximumShortfall);
		Assert.InRange(excess, 0, options.MaximumExcess);
		Assert.InRange(shortfall + excess, 0, options.MaximumDifference);
		Assert.True(mode != SelectionMode.AtMostTarget || result.TotalValue <= target);
		Assert.True(mode != SelectionMode.AtLeastTarget || result.TotalValue >= target);
		Assert.Equal(result.SelectedCandidates.OrderBy(candidate => candidate.Item), result.SelectedCandidates);
		foreach (SelectionCandidate<int> selected in result.SelectedCandidates)
		{
			Assert.Same(candidates[selected.Item], selected);
		}
	}

	private static void AssertDoesNotWorsen(SelectionResult<int> before, SelectionResult<int> after, long target, SelectionMode mode)
	{
		if (before.HasSelection)
		{
			Assert.True(after.HasSelection);
			Assert.True(Rank(after.TotalValue, after.TotalCost, after.InputCount, target, mode)
				.CompareTo(Rank(before.TotalValue, before.TotalCost, before.InputCount, target, mode)) <= 0);
		}
	}

	private static void AssertMatchesOracle(SelectionCandidate<int>[] candidates, long target, SelectionOptions options,
		SelectionMode mode, SelectionResult<int> actual)
	{
		var ordered = candidates.OrderByDescending(candidate => candidate.Value).ThenBy(candidate => candidate.Cost)
			.ThenBy(candidate => candidate.InputCount).ThenBy(candidate => candidate.Item).ToArray();
		(long Difference, long Cost, int Inputs, int Side)? bestRank = null;
		SelectionCandidate<int>[] best = [];
		for (int mask = (1 << ordered.Length) - 1; mask > 0; mask--)
		{
			SelectionCandidate<int>[] selected = ordered.Where((candidate, index) =>
				(mask & (1 << (ordered.Length - index - 1))) != 0).ToArray();
			long value = selected.Sum(candidate => candidate.Value);
			long cost = selected.Sum(candidate => candidate.Cost);
			int inputs = selected.Sum(candidate => candidate.InputCount);
			long shortfall = Math.Max(0, target - value);
			long excess = Math.Max(0, value - target);
			if (value < options.MinimumValue || inputs > options.MaximumInputCount ||
				shortfall > options.MaximumShortfall || excess > options.MaximumExcess || shortfall + excess > options.MaximumDifference ||
				(mode == SelectionMode.AtLeastTarget && value < target) || (mode == SelectionMode.AtMostTarget && value > target))
			{
				continue;
			}

			var rank = Rank(value, cost, inputs, target, mode);
			if (bestRank is null || rank.CompareTo(bestRank.Value) < 0)
			{
				bestRank = rank;
				best = selected.OrderBy(candidate => candidate.Item).ToArray();
			}
		}

		Assert.Equal(bestRank is null ? SelectionStatus.NoSolution : SelectionStatus.Optimal, actual.Status);
		Assert.Equal(bestRank is not null, actual.HasSelection);
		Assert.Equal(best.Sum(candidate => candidate.Value), actual.TotalValue);
		Assert.Equal(best.Sum(candidate => candidate.Cost), actual.TotalCost);
		Assert.Equal(best.Sum(candidate => candidate.InputCount), actual.InputCount);
		Assert.Equal(best.Length, actual.SelectedCandidates.Count);
		for (int index = 0; index < best.Length; index++)
		{
			Assert.Same(best[index], actual.SelectedCandidates[index]);
		}
	}

	private static (long Difference, long Cost, int Inputs, int Side) Rank(long value, long cost, int inputs, long target, SelectionMode mode) =>
		(Math.Abs(value - target), cost, inputs, mode == SelectionMode.Closest && value < target ? 1 : 0);
}
