using Xunit;

namespace CoinSelection.Tests;

public class IncumbentCapacityPruningTests
{
	[Theory]
	[InlineData(3, 1, SelectionStatus.SearchLimitReached)]
	[InlineData(4, 1, SelectionStatus.SearchLimitReached)]
	[InlineData(5, 1, SelectionStatus.SearchLimitReached)]
	[InlineData(6, 1, SelectionStatus.SearchLimitReached)]
	[InlineData(7, 0, SelectionStatus.SearchLimitReached)]
	[InlineData(8, 0, SelectionStatus.Optimal)]
	public void AnImmediatelyPrunedInclusionStillConsumesItsChildNode(long budget, long expectedCost, SelectionStatus expectedStatus)
	{
		SelectionCandidate<int>[] candidates = [new(0, 10, 1, 2), new(1, 9, 2), new(2, 6), new(3, 4)];
		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			SelectionOptions options = new() { Mode = mode, MaximumDifference = 0, MaximumSearchNodes = budget };
			SelectionResult<int> single = CoinSelector.Select(candidates, 10, options, TestContext.Current.CancellationToken);
			TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 10, options, TestContext.Current.CancellationToken);
			foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
			{
				Assert.Equal(expectedStatus, result.Status);
				Assert.Equal(budget, result.VisitedNodes);
				Assert.Equal(10, result.TotalValue);
				Assert.Equal(expectedCost, result.TotalCost);
				AssertReferences(result, expectedCost == 0 ? [candidates[2], candidates[3]] : [candidates[0]]);
			}
		}
	}

	[Theory]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.Closest)]
	public void ALowerCostExactSelectionMayUseMoreUnderlyingInputs(SelectionMode mode)
	{
		SelectionCandidate<int>[] candidates = [new(0, 100, 8), new(1, 60, 2, 4), new(2, 40, 1, 3)];
		SelectionResult<int> result = CoinSelector.Select(candidates, 100,
			new() { Mode = mode, MaximumInputCount = 7 }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(100, result.TotalValue);
		Assert.Equal(3, result.TotalCost);
		Assert.Equal(7, result.InputCount);
		AssertReferences(result, candidates[1], candidates[2]);
	}

	[Theory]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.Closest)]
	public void EqualCostRankingUsesUnderlyingInputsRatherThanTheNumberOfGroups(SelectionMode mode)
	{
		SelectionCandidate<int>[] candidates = [new(0, 1_000, 0, 7), new(1, 600, 0, 2), new(2, 500, 0, 6), new(3, 400, 0, 3)];
		SelectionResult<int> result = CoinSelector.Select(candidates, 1_000,
			new() { Mode = mode, MaximumInputCount = 20 }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(1_000, result.TotalValue);
		Assert.Equal(0, result.TotalCost);
		Assert.Equal(5, result.InputCount);
		AssertReferences(result, candidates[1], candidates[3]);
	}

	[Theory]
	[InlineData(0, 4, 2, 2, 1_000)]
	[InlineData(0, 3, 2, 2, 900)]
	[InlineData(1, 1, 4, 4, 1_000)]
	public void AMidpointAboveTargetTieMayUseTheSameInputCountAsTheEarlierBelowResult(
		long belowCost, int belowInputs, int firstInputs, int secondInputs, long expectedValue)
	{
		SelectionCandidate<int>[] candidates = [new(0, 900, belowCost, belowInputs), new(1, 600, 0, firstInputs), new(2, 400, 0, secondInputs)];
		SelectionResult<int> result = CoinSelector.Select(candidates, 950,
			new() { Mode = SelectionMode.Closest, MaximumDifference = 50 }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(expectedValue, result.TotalValue);
		Assert.Equal(expectedValue == 900 ? belowInputs : firstInputs + secondInputs, result.InputCount);
		if (expectedValue == 900)
		{
			AssertReferences(result, candidates[0]);
		}
		else
		{
			AssertReferences(result, candidates[1], candidates[2]);
		}
	}

	[Theory]
	[InlineData(1_000, 1, true)]
	[InlineData(950, 1_000, false)]
	public void BothUsesWeightedZeroCostCountBoundsWithAnExactOrOnlyAboveInterval(long target, long minimumValue, bool belowIsPossible)
	{
		SelectionCandidate<int>[] candidates = [new(0, 1_000, 0, 7), new(1, 600, 0, 2), new(2, 400, 0, 3)];
		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, target,
			new() { MinimumValue = minimumValue }, TestContext.Current.CancellationToken);
		Assert.Equal(belowIsPossible ? SelectionStatus.Optimal : SelectionStatus.NoSolution, result.AtMostTarget.Status);
		Assert.Equal(belowIsPossible, result.AtMostTarget.HasSelection);
		foreach (SelectionResult<int> side in belowIsPossible ? new[] { result.AtMostTarget, result.AtLeastTarget } : [result.AtLeastTarget])
		{
			Assert.Equal(SelectionStatus.Optimal, side.Status);
			Assert.Equal(1_000, side.TotalValue);
			Assert.Equal(0, side.TotalCost);
			Assert.Equal(5, side.InputCount);
			AssertReferences(side, candidates[1], candidates[2]);
		}
	}

	[Fact]
	public void AnAlreadyAboveMidpointResultRetainsItsOtherwiseEqualRank()
	{
		SelectionCandidate<int>[] candidates = [new(0, 1_100, 0, 4), new(1, 600, 0, 2), new(2, 400, 0, 2)];
		SelectionResult<int> result = CoinSelector.Select(candidates, 1_050,
			new() { Mode = SelectionMode.Closest, MaximumDifference = 50 }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(1_100, result.TotalValue);
		Assert.Equal(4, result.InputCount);
		AssertReferences(result, candidates[0]);
	}

	[Theory]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.Closest)]
	public void AFinalToleranceBoundaryStillAllowsALowerCostWithMoreInputs(SelectionMode mode)
	{
		SelectionCandidate<int>[] candidates = [new(0, 900, 7), new(1, 600, 0, 3), new(2, 300, 0, 2)];
		SelectionResult<int> result = CoinSelector.Select(candidates, 950, new()
		{
			Mode = mode,
			MinimumValue = 900,
			MaximumShortfall = 50,
			MaximumExcess = 0
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(900, result.TotalValue);
		Assert.Equal(0, result.TotalCost);
		Assert.Equal(5, result.InputCount);
		AssertReferences(result, candidates[1], candidates[2]);
	}

	[Theory]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.Closest)]
	public void AnOverBudgetCandidateDoesNotHideALaterZeroCostExactSelection(SelectionMode mode)
	{
		SelectionCandidate<int>[] candidates = [new(0, 1_000, 1, 4), new(1, 900, 2), new(2, 600), new(3, 400)];
		SelectionResult<int> result = CoinSelector.Select(candidates, 1_000,
			new() { Mode = mode }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(1_000, result.TotalValue);
		Assert.Equal(0, result.TotalCost);
		Assert.Equal(2, result.InputCount);
		AssertReferences(result, candidates[2], candidates[3]);
	}

	[Fact]
	public void NearMaximumAggregateValuesAndCostsRetainTheCheaperSelection()
	{
		long target = long.MaxValue / 2;
		SelectionCandidate<int>[] candidates = [new(0, target, long.MaxValue - 3), new(1, target - 7, 1, 3), new(2, 7, 2, 2)];
		SelectionResult<int> result = CoinSelector.Select(candidates, target,
			new() { MaximumDifference = 0 }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(target, result.TotalValue);
		Assert.Equal(3, result.TotalCost);
		Assert.Equal(5, result.InputCount);
		AssertReferences(result, candidates[1], candidates[2]);
	}

	[Fact]
	public void NearMaximumInputCountsCanImproveWithoutOverflowingRemainingCapacity()
	{
		SelectionCandidate<int>[] candidates = [new(0, 100, 0, int.MaxValue), new(1, 60, 0, int.MaxValue - 2), new(2, 40)];
		SelectionResult<int> result = CoinSelector.Select(candidates, 100,
			new() { MaximumDifference = 0 }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(100, result.TotalValue);
		Assert.Equal(0, result.TotalCost);
		Assert.Equal(int.MaxValue - 1, result.InputCount);
		AssertReferences(result, candidates[1], candidates[2]);
	}

	[Theory]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.Closest)]
	public void LongImpossibleRunsRetainTheEarliestEligibleReferencesWithinASmallBudget(SelectionMode mode)
	{
		SelectionCandidate<int>[] candidates = [.. Enumerable.Range(0, 128).Select(index => new SelectionCandidate<int>(index, 10_000 + index)),
			.. Enumerable.Range(128, 64).Select(index => new SelectionCandidate<int>(index, 900, 0, 3)),
			new(192, 600), new(193, 600), new(194, 400), new(195, 400)];
		SelectionOptions options = new() { Mode = mode, MaximumDifference = 0, MaximumInputCount = 2, MaximumSearchNodes = 3 };
		SelectionResult<int> single = CoinSelector.Select(candidates, 1_000, options, TestContext.Current.CancellationToken);
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 1_000, options, TestContext.Current.CancellationToken);

		foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			Assert.True(result.HasSelection);
			Assert.Equal(1_000, result.TotalValue);
			Assert.Equal(0, result.TotalCost);
			Assert.Equal(2, result.InputCount);
			Assert.InRange(result.VisitedNodes, 1, options.MaximumSearchNodes);
			AssertReferences(result, candidates[192], candidates[194]);
		}
	}

	[Fact]
	public void TwoSidedNonexactDistanceFloorsRetainIndependentCostAndInputCountImprovements()
	{
		SelectionCandidate<int>[] candidates = [new(0, 14_700, 9), new(1, 14_600, 7), new(2, 10_000, 2, 4), new(3, 9_900, 1, 3), new(4, 4_700, 0, 2)];
		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 14_617,
			cancellationToken: TestContext.Current.CancellationToken);

		AssertRankAndReferences(result.AtMostTarget, 14_600, 1, 5, candidates[3], candidates[4]);
		AssertRankAndReferences(result.AtLeastTarget, 14_700, 2, 6, candidates[2], candidates[4]);
	}

	[Fact]
	public void ReachingTheBelowFloorDoesNotProveTheUnreachableNearestAboveValue()
	{
		SelectionCandidate<int>[] candidates = [new(0, 14_600, 9), new(1, 9_900, 1, 3), new(2, 4_700, 1, 2)];
		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 14_617,
			cancellationToken: TestContext.Current.CancellationToken);

		// 14700 is the nearest allowed above-target GCD multiple, but no subset reaches it.
		AssertRankAndReferences(result.AtMostTarget, 14_600, 2, 5, candidates[1], candidates[2]);
		AssertRankAndReferences(result.AtLeastTarget, 19_300, 10, 3, candidates[0], candidates[2]);
	}

	[Fact]
	public void TwoSidedZeroCostCountPruningUsesTheLargerOfTheIndependentInputCounts()
	{
		SelectionCandidate<int>[] candidates = [new(0, 1_000, 0, 7), new(1, 900, 0, 2), new(2, 600, 0, 2), new(3, 400, 0, 3)];
		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 950,
			cancellationToken: TestContext.Current.CancellationToken);

		AssertRankAndReferences(result.AtMostTarget, 900, 0, 2, candidates[1]);
		AssertRankAndReferences(result.AtLeastTarget, 1_000, 0, 5, candidates[2], candidates[3]);
	}

	[Fact]
	public void TwoSidedLowerCostImprovementMayUseMoreInputsThanEitherEarlyIncumbent()
	{
		SelectionCandidate<int>[] candidates = [new(0, 1_000, 0, 2), new(1, 900, 5), new(2, 600, 1, 4), new(3, 300, 0, 4)];
		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 950,
			cancellationToken: TestContext.Current.CancellationToken);

		AssertRankAndReferences(result.AtMostTarget, 900, 1, 8, candidates[2], candidates[3]);
		AssertRankAndReferences(result.AtLeastTarget, 1_000, 0, 2, candidates[0]);
	}

	[Fact]
	public void TwoSidedMidpointResultsKeepTheirOwnReferencesAtEqualCostsAndCounts()
	{
		SelectionCandidate<int>[] candidates = [new(0, 900, 0, 4), new(1, 600, 0, 2), new(2, 400, 0, 2)];
		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 950,
			new() { MaximumDifference = 50 }, TestContext.Current.CancellationToken);

		AssertRankAndReferences(result.AtMostTarget, 900, 0, 4, candidates[0]);
		AssertRankAndReferences(result.AtLeastTarget, 1_000, 0, 4, candidates[1], candidates[2]);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void AnEmptySideDoesNotPreventCheaperInputCountsOnTheRemainingSide(bool aboveOnly)
	{
		SelectionCandidate<int>[] candidates = aboveOnly
			? [new(0, 1_000, 0, 7), new(1, 600, 0, 2), new(2, 400, 0, 3)]
			: [new(0, 900, 0, 7), new(1, 600, 0, 2), new(2, 300, 0, 3)];
		SelectionOptions options = aboveOnly
			? new() { MinimumValue = 1_000, MaximumExcess = 50 }
			: new() { MaximumShortfall = 50, MaximumExcess = 0 };
		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 950, options, TestContext.Current.CancellationToken);
		SelectionResult<int> feasible = aboveOnly ? result.AtLeastTarget : result.AtMostTarget;
		SelectionResult<int> empty = aboveOnly ? result.AtMostTarget : result.AtLeastTarget;

		Assert.Equal(SelectionStatus.NoSolution, empty.Status);
		Assert.False(empty.HasSelection);
		AssertRankAndReferences(feasible, aboveOnly ? 1_000 : 900, 0, 5, candidates[1], candidates[2]);
	}

	[Fact]
	public void SmallWeightedRequestsMatchIndependentExhaustiveRanksAndCanonicalReferences()
	{
		Random random = new(93_287);
		for (int sample = 0; sample < 96; sample++)
		{
			int unit = random.Next(1, 8);
			SelectionCandidate<int>[] candidates = Enumerable.Range(0, random.Next(1, 11))
				.Select(index => new SelectionCandidate<int>(index, unit * random.Next(1, 17), sample % 3 == 0 ? 0 : random.Next(0, 6), random.Next(1, 6)))
				.ToArray();
			long target = unit * random.Next(1, 25) + (sample % 3 == 0 ? 0 : unit / 2);
			SelectionOptions policy = new()
			{
				MinimumValue = sample % 11 == 0 ? target + 1 : 1,
				MaximumShortfall = sample % 4 == 0 ? 0 : random.Next(0, unit * 5),
				MaximumExcess = sample % 5 == 0 ? 0 : random.Next(0, unit * 5),
				MaximumDifference = sample % 7 == 0 ? unit / 2 : long.MaxValue,
				MaximumInputCount = sample % 8 == 0 ? int.MaxValue : random.Next(1, 20),
				MaximumSearchNodes = 65_536
			};
			foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
			{
				SelectionOptions options = policy with { Mode = mode };
				AssertMatchesOracle(candidates, target, options,
					CoinSelector.Select(candidates, target, options, TestContext.Current.CancellationToken));
			}
			TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, target, policy, TestContext.Current.CancellationToken);
			AssertMatchesOracle(candidates, target, policy with { Mode = SelectionMode.AtMostTarget }, both.AtMostTarget);
			AssertMatchesOracle(candidates, target, policy with { Mode = SelectionMode.AtLeastTarget }, both.AtLeastTarget);
		}
	}

	private static void AssertRankAndReferences(SelectionResult<int> actual, long expectedValue, long expectedCost,
		int expectedInputs, params SelectionCandidate<int>[] expected)
	{
		Assert.Equal(SelectionStatus.Optimal, actual.Status);
		Assert.Equal(expectedValue, actual.TotalValue);
		Assert.Equal(expectedCost, actual.TotalCost);
		Assert.Equal(expectedInputs, actual.InputCount);
		AssertReferences(actual, expected);
	}

	private static void AssertReferences(SelectionResult<int> actual, params SelectionCandidate<int>[] expected)
	{
		Assert.Equal(expected.Length, actual.SelectedCandidates.Count);
		for (int index = 0; index < expected.Length; index++)
		{
			Assert.Same(expected[index], actual.SelectedCandidates[index]);
		}
	}

	private static void AssertMatchesOracle(SelectionCandidate<int>[] candidates, long target, SelectionOptions options, SelectionResult<int> actual)
	{
		SelectionCandidate<int>[] ordered = candidates.OrderByDescending(candidate => candidate.Value).ThenBy(candidate => candidate.Cost)
			.ThenBy(candidate => candidate.InputCount).ThenBy(candidate => candidate.Item).ToArray();
		(long Difference, long Cost, long Inputs, int Side)? bestRank = null;
		SelectionCandidate<int>[] best = [];
		for (int mask = (1 << ordered.Length) - 1; mask > 0; mask--)
		{
			SelectionCandidate<int>[] selected = ordered.Where((candidate, index) => (mask & (1 << (ordered.Length - index - 1))) != 0).ToArray();
			long value = selected.Sum(candidate => candidate.Value);
			long cost = selected.Sum(candidate => candidate.Cost);
			long inputs = selected.Sum(candidate => (long)candidate.InputCount);
			long shortfall = value < target ? target - value : 0;
			long excess = value > target ? value - target : 0;
			if (value < options.MinimumValue || inputs > options.MaximumInputCount || shortfall > options.MaximumShortfall ||
				excess > options.MaximumExcess || shortfall + excess > options.MaximumDifference ||
				(options.Mode == SelectionMode.AtMostTarget && value > target) || (options.Mode == SelectionMode.AtLeastTarget && value < target))
			{
				continue;
			}
			var rank = (shortfall + excess, cost, inputs, options.Mode == SelectionMode.Closest && value < target ? 1 : 0);
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
		Assert.Equal(best.Sum(candidate => (long)candidate.InputCount), actual.InputCount);
		AssertReferences(actual, best);
	}
}
