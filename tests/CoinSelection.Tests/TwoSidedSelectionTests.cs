using Xunit;

namespace CoinSelection.Tests;

public class TwoSidedSelectionTests
{
	[Fact]
	public void ACheaperCloserLowerSelectionCannotPruneTheRequiredUpperSelection()
	{
		SelectionCandidate<int>[] candidates = [new(0, 9), new(1, 7, 1), new(2, 5)];
		var result = CoinSelector.SelectBoth(candidates, 10, new()
		{
			MaximumShortfall = 1,
			MaximumExcess = 5,
			MaximumInputCount = 2
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.AtMostTarget.Status);
		Assert.Equal(9, result.AtMostTarget.TotalValue);
		Assert.Equal(0, result.AtMostTarget.TotalCost);
		Assert.Equal(SelectionStatus.Optimal, result.AtLeastTarget.Status);
		Assert.Equal(12, result.AtLeastTarget.TotalValue);
		Assert.Equal(1, result.AtLeastTarget.TotalCost);
		Assert.Equal([1, 2], result.AtLeastTarget.SelectedCandidates.Select(candidate => candidate.Item));
	}

	[Fact]
	public void AnEmptyLowerIntervalIsProvedEvenWhenTheOtherSideExhaustsTheBudget()
	{
		var result = CoinSelector.SelectBoth([new SelectionCandidate<int>(0, 12)], 10,
			new() { MinimumValue = 11, MaximumSearchNodes = 1 }, TestContext.Current.CancellationToken);

		Assert.Equal(1, result.VisitedNodes);
		Assert.Equal(result.VisitedNodes, result.AtMostTarget.VisitedNodes);
		Assert.Equal(result.VisitedNodes, result.AtLeastTarget.VisitedNodes);
		Assert.Equal(SelectionStatus.NoSolution, result.AtMostTarget.Status);
		Assert.False(result.AtMostTarget.HasSelection);
		Assert.Equal(SelectionStatus.SearchLimitReached, result.AtLeastTarget.Status);
		Assert.False(result.AtLeastTarget.HasSelection);
	}

	[Theory]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.Closest)]
	public void OneRequestReturnsBothSidesRegardlessOfTheModeOption(SelectionMode mode)
	{
		SelectionCandidate<string>[] candidates = [new("below", 99), new("above", 103)];

		TwoSidedSelectionResult<string> result = CoinSelector.SelectBoth(candidates, 100, new()
		{
			Mode = mode,
			MaximumShortfall = 1,
			MaximumExcess = 3
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.AtMostTarget.Status);
		Assert.Equal(SelectionStatus.Optimal, result.AtLeastTarget.Status);
		Assert.Equal(99, result.AtMostTarget.TotalValue);
		Assert.Equal(103, result.AtLeastTarget.TotalValue);
		Assert.Same(candidates[0], Assert.Single(result.AtMostTarget.SelectedCandidates));
		Assert.Same(candidates[1], Assert.Single(result.AtLeastTarget.SelectedCandidates));
	}

	[Fact]
	public void EachSideIndependentlyRanksCostThenInputCountThenFirstIdentity()
	{
		SelectionCandidate<string>[] candidates =
		[
			new("below expensive", 9, 2),
			new("below cheap large group", 9, 1, 3),
			new("above expensive", 11, 3),
			new("above cheap group", 11, 2, 2),
			new("above best", 11, 2),
			new("below best", 9, 1, 2),
			new("below tied later", 9, 1, 2),
			new("above tied later", 11, 2)
		];

		TwoSidedSelectionResult<string> result = CoinSelector.SelectBoth(candidates, 10, new()
		{
			MaximumShortfall = 1,
			MaximumExcess = 1
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.AtMostTarget.Status);
		Assert.Equal(SelectionStatus.Optimal, result.AtLeastTarget.Status);
		Assert.Same(candidates[5], Assert.Single(result.AtMostTarget.SelectedCandidates));
		Assert.Equal(1, result.AtMostTarget.TotalCost);
		Assert.Equal(2, result.AtMostTarget.InputCount);
		Assert.Same(candidates[4], Assert.Single(result.AtLeastTarget.SelectedCandidates));
		Assert.Equal(2, result.AtLeastTarget.TotalCost);
		Assert.Equal(1, result.AtLeastTarget.InputCount);
	}

	[Fact]
	public void AnExactSubsetBelongsToBothInclusiveSidesAndPreservesOriginalOrder()
	{
		SelectionCandidate<int>[] candidates = [new(0, 5, 1), new(1, 3), new(2, 5, 1), new(3, 5, 1)];

		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 13, new()
		{
			MaximumShortfall = 0,
			MaximumExcess = 0
		}, TestContext.Current.CancellationToken);

		foreach (SelectionResult<int> side in new[] { result.AtMostTarget, result.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.Optimal, side.Status);
			Assert.True(side.IsOptimal);
			Assert.Equal(13, side.TotalValue);
			Assert.Equal(2, side.TotalCost);
			Assert.Equal(3, side.InputCount);
			Assert.Collection(side.SelectedCandidates,
				candidate => Assert.Same(candidates[0], candidate),
				candidate => Assert.Same(candidates[1], candidate),
				candidate => Assert.Same(candidates[2], candidate));
		}
	}

	[Fact]
	public void UnbeatableExactSingleInputCanCompleteBothSides()
	{
		SelectionCandidate<int>[] candidates = [new(0, 99, 2), new(1, 100), new(2, 103, 1)];

		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 100,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.AtMostTarget.Status);
		Assert.Equal(SelectionStatus.Optimal, result.AtLeastTarget.Status);
		Assert.Same(candidates[1], Assert.Single(result.AtMostTarget.SelectedCandidates));
		Assert.Same(candidates[1], Assert.Single(result.AtLeastTarget.SelectedCandidates));
	}

	[Theory]
	[InlineData(98, 0, 0)]
	[InlineData(99, 99, 0)]
	[InlineData(103, 0, 103)]
	[InlineData(104, 0, 0)]
	public void BothSidesRespectTheirOwnInclusiveTolerance(long value, long expectedBelow, long expectedAbove)
	{
		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth([new SelectionCandidate<int>(0, value)], 100, new()
		{
			MaximumShortfall = 1,
			MaximumExcess = 3
		}, TestContext.Current.CancellationToken);

		AssertCompletedValue(result.AtMostTarget, expectedBelow);
		AssertCompletedValue(result.AtLeastTarget, expectedAbove);
	}

	[Theory]
	[InlineData(0, 3, 10, 0, 103)]
	[InlineData(1, 0, 10, 99, 0)]
	[InlineData(1, 3, 2, 99, 0)]
	[InlineData(0, 0, 10, 0, 0)]
	public void DirectionalAndLegacyLimitsIntersectIndependently(
		long shortfall, long excess, long difference, long expectedBelow, long expectedAbove)
	{
		SelectionCandidate<int>[] candidates = [new(0, 99), new(1, 103)];

		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 100, new()
		{
			MaximumShortfall = shortfall,
			MaximumExcess = excess,
			MaximumDifference = difference
		}, TestContext.Current.CancellationToken);

		AssertCompletedValue(result.AtMostTarget, expectedBelow);
		AssertCompletedValue(result.AtLeastTarget, expectedAbove);
	}

	[Theory]
	[InlineData(99, 99, 103)]
	[InlineData(100, 0, 103)]
	[InlineData(103, 0, 103)]
	[InlineData(104, 0, 0)]
	public void MinimumValueCanEliminateOneSideWithoutEliminatingTheOther(long minimum, long expectedBelow, long expectedAbove)
	{
		SelectionCandidate<int>[] candidates = [new(0, 99), new(1, 103)];

		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 100, new()
		{
			MinimumValue = minimum,
			MaximumShortfall = 1,
			MaximumExcess = 3
		}, TestContext.Current.CancellationToken);

		AssertCompletedValue(result.AtMostTarget, expectedBelow);
		AssertCompletedValue(result.AtLeastTarget, expectedAbove);
	}

	[Theory]
	[InlineData(2, 0, 0)]
	[InlineData(3, 9, 11)]
	[InlineData(4, 10, 10)]
	public void BothSidesEnforceUnderlyingGroupCountsRatherThanCandidateCounts(int inputLimit, long expectedBelow, long expectedAbove)
	{
		SelectionCandidate<int>[] candidates = [new(0, 9, 0, 3), new(1, 6, 1, 2), new(2, 5), new(3, 4, 0, 2)];

		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 10, new()
		{
			MaximumShortfall = 1,
			MaximumExcess = 1,
			MaximumInputCount = inputLimit
		}, TestContext.Current.CancellationToken);

		AssertCompletedValue(result.AtMostTarget, expectedBelow);
		AssertCompletedValue(result.AtLeastTarget, expectedAbove);
		Assert.InRange(result.AtMostTarget.InputCount, 0, inputLimit);
		Assert.InRange(result.AtLeastTarget.InputCount, 0, inputLimit);
		if (inputLimit == 3)
		{
			Assert.Same(candidates[0], Assert.Single(result.AtMostTarget.SelectedCandidates));
			Assert.Equal([1, 2], result.AtLeastTarget.SelectedCandidates.Select(candidate => candidate.Item));
		}
	}

	[Fact]
	public void EmptyInputProvesNoSolutionOnBothSides()
	{
		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth<int>([], 10,
			cancellationToken: TestContext.Current.CancellationToken);

		AssertCompletedValue(result.AtMostTarget, 0);
		AssertCompletedValue(result.AtLeastTarget, 0);
		Assert.InRange(result.VisitedNodes, 0, 1);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	public void TheNodeBudgetIsSharedAcrossBothSidesAndRetainsValidPartialSelections(long budget)
	{
		SelectionCandidate<int>[] candidates = [new(0, 9, 1), new(1, 11, 1), new(2, 9, 1), new(3, 11, 1)];
		SelectionOptions options = new()
		{
			MaximumShortfall = 1,
			MaximumExcess = 1,
			MaximumInputCount = 2,
			MaximumSearchNodes = budget
		};

		TwoSidedSelectionResult<int> first = CoinSelector.SelectBoth(candidates, 10, options, TestContext.Current.CancellationToken);
		TwoSidedSelectionResult<int> second = CoinSelector.SelectBoth(candidates, 10, options, TestContext.Current.CancellationToken);

		Assert.InRange(first.VisitedNodes, 1, budget);
		Assert.Equal(first.VisitedNodes, second.VisitedNodes);
		Assert.Equal(SelectionStatus.SearchLimitReached, first.AtMostTarget.Status);
		Assert.Equal(SelectionStatus.SearchLimitReached, first.AtLeastTarget.Status);
		Assert.False(first.AtMostTarget.IsOptimal);
		Assert.False(first.AtLeastTarget.IsOptimal);
		AssertEquivalent(first.AtMostTarget, second.AtMostTarget);
		AssertEquivalent(first.AtLeastTarget, second.AtLeastTarget);
		AssertValidPartial(first.AtMostTarget, 10, options, SelectionMode.AtMostTarget);
		AssertValidPartial(first.AtLeastTarget, 10, options, SelectionMode.AtLeastTarget);
		if (budget == 1)
		{
			Assert.False(first.AtMostTarget.HasSelection);
			Assert.False(first.AtLeastTarget.HasSelection);
		}
		else
		{
			Assert.True(first.AtMostTarget.HasSelection || first.AtLeastTarget.HasSelection);
		}
	}

	[Theory]
	[InlineData(1, long.MaxValue)]
	[InlineData(104, 3)]
	public void PreCancellationReturnsNoSelectionsAndVisitsNoNodesEvenWithEmptyIntervals(long minimumValue, long maximumExcess)
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		SelectionCandidate<int>[] candidates = [new(0, 99), new(1, 103)];

		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 100,
			new() { MinimumValue = minimumValue, MaximumExcess = maximumExcess }, cancellation.Token);

		Assert.Equal(0, result.VisitedNodes);
		foreach (SelectionResult<int> side in new[] { result.AtMostTarget, result.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.Cancelled, side.Status);
			Assert.False(side.HasSelection);
			Assert.False(side.IsOptimal);
			Assert.Equal(0, side.VisitedNodes);
			Assert.Equal(0, side.TotalValue);
			Assert.Equal(0, side.TotalCost);
			Assert.Equal(0, side.InputCount);
		}
	}

	[Fact]
	public void BothBoundsAndCandidateTotalsRemainValidNearLongMaximum()
	{
		SelectionCandidate<int>[] candidates =
		[
			new(0, long.MaxValue - 6, long.MaxValue - 10, 2),
			new(1, 4, 4),
			new(2, 2, 6)
		];

		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, long.MaxValue - 3, new()
		{
			MaximumShortfall = 4,
			MaximumExcess = 4,
			MaximumInputCount = 3
		}, TestContext.Current.CancellationToken);

		AssertCompletedValue(result.AtMostTarget, long.MaxValue - 4);
		AssertCompletedValue(result.AtLeastTarget, long.MaxValue - 2);
		Assert.Equal(long.MaxValue - 4, result.AtMostTarget.TotalCost);
		Assert.Equal(long.MaxValue - 6, result.AtLeastTarget.TotalCost);
		Assert.Equal([0, 2], result.AtMostTarget.SelectedCandidates.Select(candidate => candidate.Item));
		Assert.Equal([0, 1], result.AtLeastTarget.SelectedCandidates.Select(candidate => candidate.Item));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CheckedValueAndCostAggregationPrecedeCancellation(bool overflowCost)
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		SelectionCandidate<int>[] candidates = overflowCost
			? [new(0, 1, long.MaxValue), new(1, 1, 1)]
			: [new(0, long.MaxValue), new(1, 1)];

		Assert.Throws<OverflowException>(() => CoinSelector.SelectBoth(candidates, 1, cancellationToken: cancellation.Token));
	}

	[Fact]
	public void InvalidInputsAndOptionsAreValidatedEvenWhenPreCancelled()
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		Assert.Throws<ArgumentNullException>(() => CoinSelector.SelectBoth<int>(null!, 1, cancellationToken: cancellation.Token));
		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.SelectBoth<int>([], 0, cancellationToken: cancellation.Token));
		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.SelectBoth<int>([], -1, cancellationToken: cancellation.Token));
		Assert.Throws<ArgumentException>(() => CoinSelector.SelectBoth<int>([null!], 1, cancellationToken: cancellation.Token));
		Assert.Throws<ArgumentException>(() => CoinSelector.SelectBoth([new SelectionCandidate<int>(0, 0)], 1, cancellationToken: cancellation.Token));
		Assert.Throws<ArgumentException>(() => CoinSelector.SelectBoth([new SelectionCandidate<int>(0, 1, -1)], 1, cancellationToken: cancellation.Token));
		Assert.Throws<ArgumentException>(() => CoinSelector.SelectBoth([new SelectionCandidate<int>(0, 1, InputCount: 0)], 1, cancellationToken: cancellation.Token));
		SelectionOptions[] invalidOptions =
		[
			new() { Mode = (SelectionMode)99 },
			new() { MinimumValue = 0 },
			new() { MaximumDifference = -1 },
			new() { MaximumShortfall = -1 },
			new() { MaximumExcess = -1 },
			new() { MaximumInputCount = 0 },
			new() { MaximumSearchNodes = 0 }
		];
		foreach (SelectionOptions options in invalidOptions)
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.SelectBoth<int>([], 1, options, cancellation.Token));
		}
	}

	[Fact]
	public void ResultsAreImmutableSnapshotsOfTheOriginalCandidateReferences()
	{
		SelectionCandidate<int> below = new(0, 99);
		SelectionCandidate<int> above = new(1, 103);
		List<SelectionCandidate<int>> candidates = [below, above];

		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 100,
			cancellationToken: TestContext.Current.CancellationToken);
		candidates.Clear();

		Assert.Same(below, Assert.Single(result.AtMostTarget.SelectedCandidates));
		Assert.Same(above, Assert.Single(result.AtLeastTarget.SelectedCandidates));
		foreach (SelectionResult<int> side in new[] { result.AtMostTarget, result.AtLeastTarget })
		{
			IList<SelectionCandidate<int>> selected = Assert.IsAssignableFrom<IList<SelectionCandidate<int>>>(side.SelectedCandidates);
			Assert.Throws<NotSupportedException>(() => selected[0] = new(2, 100));
		}
	}

	[Fact]
	public void DuplicateHeavyGroupedRequestsMatchAnExhaustiveOracleAndSeparateCompleteSearches()
	{
		Random random = new(23_519);
		for (int sample = 0; sample < 80; sample++)
		{
			var templates = Enumerable.Range(0, random.Next(1, 5))
				.Select(index => (Value: random.Next(1, 20), Cost: random.Next(0, 6), Inputs: random.Next(1, 5)))
				.ToArray();
			var candidates = new SelectionCandidate<object>[random.Next(0, 11)];
			for (int index = 0; index < candidates.Length; index++)
			{
				var template = templates[random.Next(templates.Length)];
				candidates[index] = index > 0 && index % 5 == 0
					? candidates[index - 1]
					: new(new object(), template.Value, template.Cost, template.Inputs);
			}

			long target = random.Next(1, 70);
			SelectionOptions policy = new()
			{
				MinimumValue = sample % 6 == 0 ? target + 1 : random.Next(1, 20),
				MaximumShortfall = sample % 7 == 0 ? long.MaxValue : random.Next(0, 25),
				MaximumExcess = sample % 8 == 0 ? long.MaxValue : random.Next(0, 25),
				MaximumDifference = sample % 3 == 0 ? random.Next(0, 15) : long.MaxValue,
				MaximumInputCount = sample % 5 == 0 ? int.MaxValue : random.Next(1, 17),
				MaximumSearchNodes = 65_536
			};
			OracleSelection? expectedBelow = EnumerateSubsets(candidates, target, policy, SelectionMode.AtMostTarget);
			OracleSelection? expectedAbove = EnumerateSubsets(candidates, target, policy, SelectionMode.AtLeastTarget);
			SelectionResult<object> separateBelow = CoinSelector.Select(candidates, target,
				policy with { Mode = SelectionMode.AtMostTarget }, TestContext.Current.CancellationToken);
			SelectionResult<object> separateAbove = CoinSelector.Select(candidates, target,
				policy with { Mode = SelectionMode.AtLeastTarget }, TestContext.Current.CancellationToken);

			foreach (SelectionMode ignoredMode in Enum.GetValues<SelectionMode>())
			{
				TwoSidedSelectionResult<object> actual = CoinSelector.SelectBoth(candidates, target,
					policy with { Mode = ignoredMode }, TestContext.Current.CancellationToken);

				AssertMatchesOracle(candidates, expectedBelow, actual.AtMostTarget);
				AssertMatchesOracle(candidates, expectedAbove, actual.AtLeastTarget);
				AssertEquivalent(separateBelow, actual.AtMostTarget);
				AssertEquivalent(separateAbove, actual.AtLeastTarget);
				Assert.InRange(actual.VisitedNodes, 0, policy.MaximumSearchNodes);
			}
		}
	}

	private static void AssertCompletedValue<T>(SelectionResult<T> result, long expectedValue)
	{
		Assert.Equal(expectedValue == 0 ? SelectionStatus.NoSolution : SelectionStatus.Optimal, result.Status);
		Assert.Equal(expectedValue != 0, result.HasSelection);
		Assert.Equal(expectedValue != 0, result.IsOptimal);
		Assert.Equal(expectedValue, result.TotalValue);
		if (expectedValue == 0)
		{
			Assert.Empty(result.SelectedCandidates);
			Assert.Equal(0, result.TotalCost);
			Assert.Equal(0, result.InputCount);
		}
	}

	private static void AssertEquivalent<T>(SelectionResult<T> expected, SelectionResult<T> actual)
	{
		Assert.Equal(expected.Status, actual.Status);
		Assert.Equal(expected.HasSelection, actual.HasSelection);
		Assert.Equal(expected.IsOptimal, actual.IsOptimal);
		Assert.Equal(expected.TotalValue, actual.TotalValue);
		Assert.Equal(expected.TotalCost, actual.TotalCost);
		Assert.Equal(expected.InputCount, actual.InputCount);
		Assert.Equal(expected.SelectedCandidates.Count, actual.SelectedCandidates.Count);
		for (int index = 0; index < expected.SelectedCandidates.Count; index++)
		{
			Assert.Same(expected.SelectedCandidates[index], actual.SelectedCandidates[index]);
		}
	}

	private static void AssertValidPartial<T>(SelectionResult<T> result, long target, SelectionOptions options, SelectionMode mode)
	{
		if (!result.HasSelection)
		{
			Assert.Equal(0, result.TotalValue);
			Assert.Equal(0, result.TotalCost);
			Assert.Equal(0, result.InputCount);
			return;
		}

		Assert.Equal(result.SelectedCandidates.Sum(candidate => candidate.Value), result.TotalValue);
		Assert.Equal(result.SelectedCandidates.Sum(candidate => candidate.Cost), result.TotalCost);
		Assert.Equal(result.SelectedCandidates.Sum(candidate => candidate.InputCount), result.InputCount);
		Assert.InRange(result.InputCount, 1, options.MaximumInputCount);
		Assert.True(result.TotalValue >= options.MinimumValue);
		long shortfall = result.TotalValue < target ? target - result.TotalValue : 0;
		long excess = result.TotalValue > target ? result.TotalValue - target : 0;
		Assert.InRange(shortfall, 0, options.MaximumShortfall);
		Assert.InRange(excess, 0, options.MaximumExcess);
		Assert.InRange(shortfall + excess, 0, options.MaximumDifference);
		Assert.True(mode == SelectionMode.AtMostTarget ? result.TotalValue <= target : result.TotalValue >= target);
	}

	private static void AssertMatchesOracle(
		SelectionCandidate<object>[] candidates, OracleSelection? expected, SelectionResult<object> actual)
	{
		Assert.Equal(expected is null ? SelectionStatus.NoSolution : SelectionStatus.Optimal, actual.Status);
		Assert.Equal(expected is not null, actual.HasSelection);
		Assert.Equal(expected?.Value ?? 0, actual.TotalValue);
		Assert.Equal(expected?.Cost ?? 0, actual.TotalCost);
		Assert.Equal(expected?.InputCount ?? 0, actual.InputCount);
		int[] indices = expected?.OriginalIndices ?? [];
		Assert.Equal(indices.Length, actual.SelectedCandidates.Count);
		for (int index = 0; index < indices.Length; index++)
		{
			Assert.Same(candidates[indices[index]], actual.SelectedCandidates[index]);
		}
	}

	private static OracleSelection? EnumerateSubsets(
		SelectionCandidate<object>[] candidates, long target, SelectionOptions options, SelectionMode mode)
	{
		var ordered = candidates.Select((candidate, index) => (Candidate: candidate, Index: index))
			.OrderByDescending(item => item.Candidate.Value).ThenBy(item => item.Candidate.Cost)
			.ThenBy(item => item.Candidate.InputCount).ThenBy(item => item.Index).ToArray();
		OracleSelection? best = null;
		// Descending masks independently enumerate the documented inclusion-first tie order.
		for (int mask = (1 << ordered.Length) - 1; mask > 0; mask--)
		{
			long value = 0;
			long cost = 0;
			int inputCount = 0;
			List<int> originalIndices = [];
			for (int index = 0; index < ordered.Length; index++)
			{
				if ((mask & (1 << (ordered.Length - index - 1))) != 0)
				{
					value += ordered[index].Candidate.Value;
					cost += ordered[index].Candidate.Cost;
					inputCount += ordered[index].Candidate.InputCount;
					originalIndices.Add(ordered[index].Index);
				}
			}

			long shortfall = value < target ? target - value : 0;
			long excess = value > target ? value - target : 0;
			if (value < options.MinimumValue || inputCount > options.MaximumInputCount ||
				shortfall > options.MaximumShortfall || excess > options.MaximumExcess ||
				shortfall + excess > options.MaximumDifference ||
				(mode == SelectionMode.AtLeastTarget && value < target) ||
				(mode == SelectionMode.AtMostTarget && value > target))
			{
				continue;
			}

			var rank = (Difference: shortfall + excess, Cost: cost, InputCount: inputCount);
			if (best is null || rank.CompareTo(best.Rank) < 0)
			{
				originalIndices.Sort();
				best = new(value, cost, inputCount, rank, originalIndices.ToArray());
			}
		}

		return best;
	}

	private sealed record OracleSelection(long Value, long Cost, int InputCount,
		(long Difference, long Cost, int InputCount) Rank, int[] OriginalIndices);
}
