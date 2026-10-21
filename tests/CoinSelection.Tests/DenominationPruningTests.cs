using Xunit;

namespace CoinSelection.Tests;

public class DenominationPruningTests
{
	[Theory]
	[InlineData(SelectionMode.AtLeastTarget, 0, 0)]
	[InlineData(SelectionMode.AtMostTarget, 0, 0)]
	[InlineData(SelectionMode.Closest, 0, 0)]
	[InlineData(SelectionMode.AtLeastTarget, 10, 20)]
	[InlineData(SelectionMode.AtMostTarget, 10, 20)]
	[InlineData(SelectionMode.Closest, 10, 20)]
	public void AnIntervalBetweenDenominationsIsProvedImpossibleWithinOneNode(
		SelectionMode mode, long shortfall, long excess)
	{
		SelectionCandidate<int>[] candidates = Enumerable.Range(0, 12)
			.Select(index => new SelectionCandidate<int>(index, 100 * (1 + index % 3), 1)).ToArray();
		SelectionOptions options = new()
		{
			Mode = mode,
			MaximumShortfall = shortfall,
			MaximumExcess = excess,
			MaximumSearchNodes = 1
		};

		SelectionResult<int> single = CoinSelector.Select(candidates, 1_050, options, TestContext.Current.CancellationToken);
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 1_050, options, TestContext.Current.CancellationToken);

		AssertNoSolution(single);
		AssertNoSolution(both.AtMostTarget);
		AssertNoSolution(both.AtLeastTarget);
		Assert.InRange(single.VisitedNodes, 0, 1);
		Assert.InRange(both.VisitedNodes, 0, 1);
	}

	[Theory]
	[InlineData(1, 0, 1_100)]
	[InlineData(2, 10, 1_000)]
	[InlineData(2, 0, 1_000)]
	public void PercentageIntervalRetainsBothReachableSidesAndClosestRanking(
		long fiveHundredCost, long hundredCost, long expectedClosest)
	{
		SelectionCandidate<int>[] candidates = [new(0, 600, 1), new(1, 500, fiveHundredCost), new(2, 400, 1), new(3, 100, hundredCost)];
		// Whole-unit 75%-125% bounds for 1050 are [787, 1312].
		SelectionOptions options = new() { MinimumValue = 787, MaximumShortfall = 263, MaximumExcess = 262 };

		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 1_050, options, TestContext.Current.CancellationToken);
		SelectionResult<int> closest = CoinSelector.Select(candidates, 1_050,
			options with { Mode = SelectionMode.Closest }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, both.AtMostTarget.Status);
		Assert.Equal(SelectionStatus.Optimal, both.AtLeastTarget.Status);
		Assert.Equal(1_000, both.AtMostTarget.TotalValue);
		Assert.Equal(1_100, both.AtLeastTarget.TotalValue);
		Assert.Equal(SelectionStatus.Optimal, closest.Status);
		Assert.Equal(expectedClosest, closest.TotalValue);
	}

	[Theory]
	[InlineData(40, 10, 10)]
	[InlineData(41, 11, 9)]
	public void ReachableInclusiveEndpointsSurviveIntervalAlignment(long target, long shortfall, long excess)
	{
		SelectionCandidate<int>[] candidates = [new(0, 30), new(1, 50)];
		SelectionOptions options = new() { MaximumShortfall = shortfall, MaximumExcess = excess, MaximumInputCount = 1 };

		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, target, options, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.AtMostTarget.Status);
		Assert.Equal(SelectionStatus.Optimal, result.AtLeastTarget.Status);
		Assert.Same(candidates[0], Assert.Single(result.AtMostTarget.SelectedCandidates));
		Assert.Same(candidates[1], Assert.Single(result.AtLeastTarget.SelectedCandidates));
	}

	[Fact]
	public void ReachableExactSelectionsStillPreferCostAndEarliestOriginalReferences()
	{
		SelectionCandidate<int>[] candidates = [new(0, 70, 9), new(1, 40, 1), new(2, 30, 1), new(3, 30, 1)];
		SelectionOptions options = new() { MaximumDifference = 0, MaximumInputCount = 2 };

		SelectionResult<int> single = CoinSelector.Select(candidates, 70, options, TestContext.Current.CancellationToken);
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 70, options, TestContext.Current.CancellationToken);

		foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.Optimal, result.Status);
			Assert.Equal(70, result.TotalValue);
			Assert.Equal(2, result.TotalCost);
			Assert.Equal(2, result.InputCount);
			Assert.Collection(result.SelectedCandidates,
				candidate => Assert.Same(candidates[1], candidate),
				candidate => Assert.Same(candidates[2], candidate));
		}
	}

	[Theory]
	[InlineData(1, 0, 0)]
	[InlineData(2, 2, 2)]
	[InlineData(3, 3, 0)]
	public void ReachableDenominationsStillRespectWeightedInputCountsAndCosts(int inputLimit, int expectedInputs, long expectedCost)
	{
		SelectionCandidate<int>[] candidates = [new(0, 70, 0, 3), new(1, 40, 1), new(2, 30, 1)];

		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 70,
			new() { MaximumDifference = 0, MaximumInputCount = inputLimit }, TestContext.Current.CancellationToken);

		foreach (SelectionResult<int> result in new[] { both.AtMostTarget, both.AtLeastTarget })
		{
			if (expectedInputs == 0)
			{
				AssertNoSolution(result);
			}
			else
			{
				Assert.Equal(SelectionStatus.Optimal, result.Status);
				Assert.Equal(70, result.TotalValue);
				Assert.Equal(expectedInputs, result.InputCount);
				Assert.Equal(expectedCost, result.TotalCost);
			}
		}
	}

	[Fact]
	public void NearestLowerDenominationStillAllowsCheaperAndFewerInputTies()
	{
		foreach (SelectionMode mode in new[] { SelectionMode.AtMostTarget, SelectionMode.Closest })
		{
			foreach ((long cost, int inputs) in new[] { (3L, 1), (2L, 3) })
			{
				SelectionCandidate<int>[] candidates = [new(0, 30, cost, inputs), new(1, 20, 1), new(2, 10, 1), new(3, 10, 1)];
				SelectionResult<int> result = CoinSelector.Select(candidates, 35, new()
				{
					Mode = mode,
					MaximumShortfall = 5,
					MaximumExcess = 0
				}, TestContext.Current.CancellationToken);

				Assert.Equal(SelectionStatus.Optimal, result.Status);
				Assert.Equal(30, result.TotalValue);
				Assert.Equal(2, result.TotalCost);
				Assert.Equal(2, result.InputCount);
				Assert.Collection(result.SelectedCandidates,
					candidate => Assert.Same(candidates[1], candidate),
					candidate => Assert.Same(candidates[2], candidate));
			}
		}
	}

	[Fact]
	public void MinimumValueAboveTargetDoesNotAdmitTheLowerAlignedAmount()
	{
		SelectionCandidate<int>[] candidates = [new(0, 40), new(1, 30), new(2, 20)];
		SelectionOptions options = new() { MinimumValue = 66, MaximumShortfall = 15, MaximumExcess = 15 };

		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 65, options, TestContext.Current.CancellationToken);
		SelectionResult<int> closest = CoinSelector.Select(candidates, 65,
			options with { Mode = SelectionMode.Closest }, TestContext.Current.CancellationToken);

		AssertNoSolution(both.AtMostTarget);
		Assert.Equal(SelectionStatus.Optimal, both.AtLeastTarget.Status);
		Assert.Equal(70, both.AtLeastTarget.TotalValue);
		Assert.Equal(SelectionStatus.Optimal, closest.Status);
		Assert.Equal(70, closest.TotalValue);
	}

	[Theory]
	[InlineData(2, 1, SelectionStatus.SearchLimitReached, SelectionStatus.NoSolution)]
	[InlineData(1, 3, SelectionStatus.NoSolution, SelectionStatus.SearchLimitReached)]
	[InlineData(1, 1, SelectionStatus.NoSolution, SelectionStatus.NoSolution)]
	public void AlignmentCanProveOneSideEmptyWhileTheOtherSideExhaustsTheSharedBudget(
		long shortfall, long excess, SelectionStatus belowStatus, SelectionStatus aboveStatus)
	{
		SelectionCandidate<int>[] candidates = [new(0, 8), new(1, 12)];

		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 10, new()
		{
			MaximumShortfall = shortfall,
			MaximumExcess = excess,
			MaximumSearchNodes = 1
		}, TestContext.Current.CancellationToken);

		Assert.Equal(belowStatus, result.AtMostTarget.Status);
		Assert.Equal(aboveStatus, result.AtLeastTarget.Status);
		Assert.False(result.AtMostTarget.HasSelection);
		Assert.False(result.AtLeastTarget.HasSelection);
		Assert.InRange(result.VisitedNodes, 0, 1);
		Assert.Equal(result.VisitedNodes, result.AtMostTarget.VisitedNodes);
		Assert.Equal(result.VisitedNodes, result.AtLeastTarget.VisitedNodes);
	}

	[Fact]
	public void RelativelyPrimeValuesPreserveTheCompleteRankingAndIdentity()
	{
		SelectionCandidate<int>[] candidates = [new(0, 10, 3), new(1, 10, 1, 2), new(2, 11, 1), new(3, 9, 2)];
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 20,
			new() { MaximumDifference = 1, MaximumInputCount = 2 }, TestContext.Current.CancellationToken);

		foreach (SelectionResult<int> result in new[] { both.AtMostTarget, both.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.Optimal, result.Status);
			Assert.Equal(20, result.TotalValue);
			Assert.Equal(3, result.TotalCost);
			Assert.Equal(2, result.InputCount);
			Assert.Collection(result.SelectedCandidates,
				candidate => Assert.Same(candidates[2], candidate),
				candidate => Assert.Same(candidates[3], candidate));
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CeilingPastLongMaximumProvesNoSolutionWhileTheValidFloorIsRetained(bool singleLargeDenomination)
	{
		SelectionCandidate<int>[] candidates = singleLargeDenomination
			? [new(0, long.MaxValue - 1, 1)]
			: [new(0, long.MaxValue - 3, 1), new(1, 2, 1)];
		SelectionOptions exact = new() { MaximumDifference = 0, MaximumSearchNodes = 1 };
		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			SelectionResult<int> result = CoinSelector.Select(candidates, long.MaxValue,
				exact with { Mode = mode }, TestContext.Current.CancellationToken);
			AssertNoSolution(result);
			Assert.InRange(result.VisitedNodes, 0, 1);
		}

		TwoSidedSelectionResult<int> impossible = CoinSelector.SelectBoth(candidates, long.MaxValue, exact, TestContext.Current.CancellationToken);
		TwoSidedSelectionResult<int> floor = CoinSelector.SelectBoth(candidates, long.MaxValue,
			new() { MaximumShortfall = 1, MaximumExcess = 0 }, TestContext.Current.CancellationToken);

		AssertNoSolution(impossible.AtMostTarget);
		AssertNoSolution(impossible.AtLeastTarget);
		Assert.InRange(impossible.VisitedNodes, 0, 1);
		Assert.Equal(SelectionStatus.Optimal, floor.AtMostTarget.Status);
		Assert.Equal(long.MaxValue - 1, floor.AtMostTarget.TotalValue);
		Assert.Equal(candidates.Length, floor.AtMostTarget.InputCount);
		AssertNoSolution(floor.AtLeastTarget);
	}

	[Fact]
	public void PreCancellationPrecedesTheDenominationImpossibilityProof()
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		SelectionCandidate<int>[] candidates = [new(0, 1_000), new(1, 100)];
		SelectionOptions options = new() { MaximumDifference = 0, MaximumSearchNodes = 1 };
		SelectionResult<int> single = CoinSelector.Select(candidates, 1_050, options, cancellation.Token);
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 1_050, options, cancellation.Token);

		foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.Cancelled, result.Status);
			Assert.False(result.HasSelection);
			Assert.Equal(0, result.VisitedNodes);
		}
		Assert.Equal(0, both.VisitedNodes);
	}

	[Fact]
	public void SmallDenominationRequestsMatchIndependentExhaustiveRankingOnEverySide()
	{
		Random random = new(91_033);
		for (int sample = 0; sample < 48; sample++)
		{
			int unit = random.Next(2, 13);
			var candidates = Enumerable.Range(0, random.Next(0, 10))
				.Select(index => new SelectionCandidate<int>(index,
					sample % 5 == 0 && index == 0 ? 1 : unit * random.Next(1, 9), random.Next(0, 5), random.Next(1, 4)))
				.ToArray();
			long target = unit * random.Next(1, 30) + random.Next(unit);
			SelectionOptions policy = new()
			{
				MinimumValue = sample % 7 == 0 ? target + 1 : 1,
				MaximumShortfall = sample % 4 == 0 ? long.MaxValue : unit * random.Next(0, 8),
				MaximumExcess = sample % 6 == 0 ? long.MaxValue : unit * random.Next(0, 8),
				MaximumDifference = sample % 3 == 0 ? random.Next(0, unit * 3) : long.MaxValue,
				MaximumInputCount = sample % 8 == 0 ? int.MaxValue : random.Next(1, 12),
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
		Assert.Empty(result.SelectedCandidates);
		Assert.Equal(0, result.TotalValue);
		Assert.Equal(0, result.TotalCost);
		Assert.Equal(0, result.InputCount);
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

			var rank = (shortfall + excess, cost, inputs, mode == SelectionMode.Closest && value < target ? 1 : 0);
			if (bestRank is null || rank.CompareTo(bestRank.Value) < 0)
			{
				bestRank = rank;
				best = selected.OrderBy(candidate => candidate.Item).ToArray();
			}
		}

		if (bestRank is null)
		{
			AssertNoSolution(actual);
			return;
		}

		Assert.Equal(SelectionStatus.Optimal, actual.Status);
		Assert.Equal(best.Sum(candidate => candidate.Value), actual.TotalValue);
		Assert.Equal(best.Sum(candidate => candidate.Cost), actual.TotalCost);
		Assert.Equal(best.Sum(candidate => candidate.InputCount), actual.InputCount);
		Assert.Equal(best.Length, actual.SelectedCandidates.Count);
		for (int index = 0; index < best.Length; index++)
		{
			Assert.Same(best[index], actual.SelectedCandidates[index]);
		}
	}
}
