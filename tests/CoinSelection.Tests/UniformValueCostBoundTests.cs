using Xunit;

namespace CoinSelection.Tests;

public class UniformValueCostBoundTests
{
	[Fact]
	public void UniqueCostsProveTheCheapestAtMostSelectionWithinTheExistingBudget()
	{
		SelectionCandidate<int>[] candidates = Enumerable.Range(1, 64)
			.Select(index => new SelectionCandidate<int>(index, 100, index)).ToArray();

		SelectionResult<int> result = CoinSelector.Select(candidates, 3_150, new()
		{
			Mode = SelectionMode.AtMostTarget,
			MaximumSearchNodes = 100_000
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.True(result.IsOptimal);
		Assert.Equal(3_100, result.TotalValue);
		Assert.Equal(496, result.TotalCost);
		Assert.Equal(31, result.InputCount);
		Assert.Equal(Enumerable.Range(1, 31), result.SelectedCandidates.Select(candidate => candidate.Item));
		Assert.InRange(result.VisitedNodes, 1, 100_000);
	}

	[Theory]
	[InlineData(2, 1, 2)]
	[InlineData(4, 0, 4)]
	public void CheapHeavyGroupsDoNotHideTheMoreExpensiveFeasibleCombination(
		int inputLimit, long expectedCost, int expectedInputs)
	{
		SelectionCandidate<int>[] candidates = [new(0, 100, 0, 3), new(1, 100), new(2, 100, 1), new(3, 100, 1)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 250, new()
		{
			Mode = SelectionMode.AtMostTarget,
			MaximumInputCount = inputLimit
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(200, result.TotalValue);
		Assert.Equal(expectedCost, result.TotalCost);
		Assert.Equal(expectedInputs, result.InputCount);
		int[] expectedIndices = inputLimit == 2 ? [1, 2] : [0, 1];
		Assert.Equal(expectedIndices, result.SelectedCandidates.Select(candidate => candidate.Item));
		Assert.Same(candidates[expectedIndices[0]], result.SelectedCandidates[0]);
		Assert.Same(candidates[expectedIndices[1]], result.SelectedCandidates[1]);
	}

	[Theory]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.Closest)]
	public void ZeroCostTiesPreferActualInputCountsAndKeepOriginalReferenceOrder(SelectionMode mode)
	{
		SelectionCandidate<int>[] candidates = [new(0, 100, 0, 3), new(1, 100), new(2, 100, 0, 2), new(3, 100), new(4, 100)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 250, new()
		{
			Mode = mode,
			MaximumExcess = 0
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(200, result.TotalValue);
		Assert.Equal(0, result.TotalCost);
		Assert.Equal(2, result.InputCount);
		Assert.Collection(result.SelectedCandidates,
			candidate => Assert.Same(candidates[1], candidate),
			candidate => Assert.Same(candidates[3], candidate));
	}

	[Fact]
	public void VeryLargeGroupCountsDoNotOverflowTheOptimisticCountComparison()
	{
		SelectionCandidate<int>[] candidates = [new(0, 10, 0, int.MaxValue), new(1, 10), new(2, 10)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 25,
			new() { Mode = SelectionMode.AtMostTarget }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(20, result.TotalValue);
		Assert.Equal(0, result.TotalCost);
		Assert.Equal(2, result.InputCount);
		Assert.Equal([1, 2], result.SelectedCandidates.Select(candidate => candidate.Item));
	}

	[Theory]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.Closest)]
	public void MaximumAggregateCostDoesNotOverflowTheRemainingCostBound(SelectionMode mode)
	{
		SelectionCandidate<int>[] candidates = [new(0, 3, long.MaxValue - 3), new(1, 3, 1), new(2, 3, 2)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 7,
			new() { Mode = mode }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		bool above = mode == SelectionMode.AtLeastTarget;
		Assert.Equal(above ? 9 : 6, result.TotalValue);
		Assert.Equal(above ? long.MaxValue : 3, result.TotalCost);
		Assert.Equal(above ? 3 : 2, result.InputCount);
		Assert.Equal(above ? new[] { 0, 1, 2 } : new[] { 1, 2 }, result.SelectedCandidates.Select(candidate => candidate.Item));
	}

	[Theory]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.Closest)]
	public void NearMaximumValuesAndCostsKeepTheCorrectDirectionalRank(SelectionMode mode)
	{
		long unit = long.MaxValue / 3;
		SelectionCandidate<int>[] candidates = [new(0, unit, unit), new(1, unit, unit), new(2, unit, unit + 1)];

		SelectionResult<int> result = CoinSelector.Select(candidates, long.MaxValue - 2,
			new() { Mode = mode }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		bool below = mode == SelectionMode.AtMostTarget;
		Assert.Equal(below ? 2 * unit : long.MaxValue - 1, result.TotalValue);
		Assert.Equal(below ? 2 * unit : long.MaxValue, result.TotalCost);
		Assert.Equal(below ? 2 : 3, result.InputCount);
		Assert.Equal(Enumerable.Range(0, below ? 2 : 3), result.SelectedCandidates.Select(candidate => candidate.Item));
	}

	[Fact]
	public void WeightedUniformInputsMatchIndependentExhaustiveRankingInAllModes()
	{
		Random random = new(28_307);
		for (int sample = 0; sample < 96; sample++)
		{
			int unit = random.Next(1, 21);
			SelectionCandidate<int>[] candidates = Enumerable.Range(0, random.Next(0, 10))
				.Select(index => new SelectionCandidate<int>(index, unit,
					sample % 7 == 0 ? 0 : random.Next(0, 9), random.Next(1, 5))).ToArray();
			long target = unit * random.Next(1, 12) + random.Next(unit);
			SelectionOptions policy = new()
			{
				MinimumValue = sample % 6 == 0 ? target + 1 : 1,
				MaximumShortfall = sample % 4 == 0 ? 0 : random.Next(0, unit * 4),
				MaximumExcess = sample % 5 == 0 ? 0 : random.Next(0, unit * 4),
				MaximumDifference = sample % 3 == 0 ? random.Next(0, unit * 2) : long.MaxValue,
				MaximumInputCount = sample % 8 == 0 ? int.MaxValue : random.Next(1, 20),
				MaximumSearchNodes = 65_536
			};
			foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
			{
				SelectionOptions options = policy with { Mode = mode };
				SelectionResult<int> actual = CoinSelector.Select(candidates, target, options, TestContext.Current.CancellationToken);
				AssertMatchesOracle(candidates, target, options, actual);
			}
		}
	}

	private static void AssertMatchesOracle(SelectionCandidate<int>[] candidates, long target,
		SelectionOptions options, SelectionResult<int> actual)
	{
		var ordered = candidates.OrderBy(candidate => candidate.Cost).ThenBy(candidate => candidate.InputCount)
			.ThenBy(candidate => candidate.Item).ToArray();
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
				(options.Mode == SelectionMode.AtLeastTarget && value < target) || (options.Mode == SelectionMode.AtMostTarget && value > target))
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
		Assert.Equal(bestRank is not null, actual.IsOptimal);
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
