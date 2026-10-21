using Xunit;

namespace CoinSelection.Tests;

public class MinimumDistancePruningTests
{
	[Theory]
	[InlineData(0, 2, 0, 1_000)]
	[InlineData(0, 1, 0, 900)]
	[InlineData(0, 2, 1, 900)]
	[InlineData(2, 2, 1, 1_000)]
	public void AProvenMidpointDistanceRetainsCostCountAndLaterAboveTargetTies(
		long belowCost, int belowInputs, long pairCost, long expectedValue)
	{
		SelectionCandidate<int>[] candidates = [new(0, 600, pairCost), new(1, 900, belowCost, belowInputs), new(2, 400)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 950,
			new() { Mode = SelectionMode.Closest }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(expectedValue, result.TotalValue);
		Assert.Equal(expectedValue == 900 ? belowCost : pairCost, result.TotalCost);
		Assert.Equal(expectedValue == 900 ? belowInputs : 2, result.InputCount);
		if (expectedValue == 900)
		{
			Assert.Same(candidates[1], Assert.Single(result.SelectedCandidates));
		}
		else
		{
			Assert.Collection(result.SelectedCandidates,
				candidate => Assert.Same(candidates[0], candidate),
				candidate => Assert.Same(candidates[2], candidate));
		}
	}

	[Theory]
	[InlineData(9, 1, 1, 1, 2)]
	[InlineData(2, 3, 2, 0, 2)]
	[InlineData(0, 3, 0, 0, 0)]
	public void ANearTargetDistanceFloorStillAllowsLaterCostOrInputCountImprovements(
		long earlyCost, int earlyInputs, long firstCost, long secondCost, long expectedCost)
	{
		SelectionCandidate<int>[] candidates = [new(0, 9_900, firstCost), new(1, 14_600, earlyCost, earlyInputs), new(2, 4_700, secondCost)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 14_617,
			new() { Mode = SelectionMode.Closest }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(14_600, result.TotalValue);
		Assert.Equal(expectedCost, result.TotalCost);
		Assert.Equal(2, result.InputCount);
		Assert.Collection(result.SelectedCandidates,
			candidate => Assert.Same(candidates[0], candidate),
			candidate => Assert.Same(candidates[2], candidate));
	}

	[Theory]
	[InlineData(950, 50, 49, 1, 900)]
	[InlineData(950, 49, 50, 1, 1_000)]
	[InlineData(950, 50, 50, 951, 1_000)]
	[InlineData(951, 51, 0, 1, 900)]
	[InlineData(949, 0, 51, 1, 1_000)]
	[InlineData(950, 50, 200, 1_001, 1_100)]
	[InlineData(950, 50, 50, 1_001, 0)]
	public void TheDistanceFloorUsesOnlyThePermittedAlignedInterval(
		long target, long shortfall, long excess, long minimum, long expectedValue)
	{
		SelectionCandidate<int>[] candidates = [new(0, 900), new(1, 600), new(2, 400), new(3, 1_100)];

		SelectionResult<int> result = CoinSelector.Select(candidates, target, new()
		{
			Mode = SelectionMode.Closest,
			MaximumShortfall = shortfall,
			MaximumExcess = excess,
			MinimumValue = minimum
		}, TestContext.Current.CancellationToken);

		Assert.Equal(expectedValue == 0 ? SelectionStatus.NoSolution : SelectionStatus.Optimal, result.Status);
		Assert.Equal(expectedValue != 0, result.HasSelection);
		Assert.Equal(expectedValue, result.TotalValue);
	}

	[Fact]
	public void LegacyDifferenceCanExcludeBothMidpointNeighbors()
	{
		SelectionCandidate<int>[] candidates = [new(0, 900), new(1, 600), new(2, 400)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 950, new()
		{
			Mode = SelectionMode.Closest,
			MaximumShortfall = 50,
			MaximumExcess = 50,
			MaximumDifference = 49
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.NoSolution, result.Status);
		Assert.False(result.HasSelection);
	}

	[Fact]
	public void AnOptimisticGcdDistanceDoesNotAssumeEitherNeighborIsReachable()
	{
		SelectionCandidate<int>[] candidates = [new(0, 900, 1), new(1, 600, 1), new(2, 400)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 750,
			new() { Mode = SelectionMode.Closest, MaximumDifference = 200 }, TestContext.Current.CancellationToken);

		// The neighboring multiples 700 and 800 cannot be made from these candidates.
		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(900, result.TotalValue);
		Assert.Same(candidates[0], Assert.Single(result.SelectedCandidates));
	}

	[Theory]
	[InlineData(true, 5, 0, 2)]
	[InlineData(false, 4, 1, 4)]
	[InlineData(false, 2, 2, 2)]
	public void UniformMidpointsKeepTheCostAndActualInputCountOptimum(
		bool zeroCosts, int inputLimit, long expectedCost, int expectedInputs)
	{
		long otherCost = zeroCosts ? 0 : 1;
		SelectionCandidate<int>[] candidates = [new(0, 100, 0, 3), new(1, 100, otherCost), new(2, 100, otherCost), new(3, 100, otherCost)];
		SelectionOptions options = new() { Mode = SelectionMode.Closest, MaximumDifference = 50, MaximumInputCount = inputLimit };

		SelectionResult<int> result = CoinSelector.Select(candidates, 250, options, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(200, result.TotalValue);
		Assert.Equal(expectedCost, result.TotalCost);
		Assert.Equal(expectedInputs, result.InputCount);
		AssertMatchesOracle(candidates, 250, options, result);
	}

	[Fact]
	public void AClosestFloorDoesNotDiscardTheFartherRequiredResultFromSelectBoth()
	{
		SelectionCandidate<int>[] candidates = [new(0, 14_600, 9), new(1, 9_900, 1), new(2, 4_700, 1)];

		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, 14_617,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.AtMostTarget.Status);
		Assert.Equal(SelectionStatus.Optimal, result.AtLeastTarget.Status);
		Assert.Equal(14_600, result.AtMostTarget.TotalValue);
		Assert.Equal(2, result.AtMostTarget.TotalCost);
		Assert.Equal(19_300, result.AtLeastTarget.TotalValue);
		Assert.Equal(10, result.AtLeastTarget.TotalCost);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(2)]
	public void NearLongMaximumDistanceCalculationsDoNotConstructAnOverflowingNeighbor(long targetReduction)
	{
		SelectionCandidate<int>[] candidates = [new(0, long.MaxValue - 3, 1), new(1, 2)];

		SelectionResult<int> result = CoinSelector.Select(candidates, long.MaxValue - targetReduction,
			new() { Mode = SelectionMode.Closest }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(targetReduction == 0 ? long.MaxValue - 1 : long.MaxValue - 3, result.TotalValue);
		Assert.Equal(1, result.TotalCost);
		Assert.Equal(targetReduction == 0 ? 2 : 1, result.InputCount);
	}

	[Fact]
	public void ASingleMaximumDenominationHasASafeFarAboveTargetDistance()
	{
		SelectionCandidate<int> candidate = new(0, long.MaxValue, long.MaxValue);

		SelectionResult<int> result = CoinSelector.Select([candidate], 1,
			new() { Mode = SelectionMode.Closest }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(long.MaxValue, result.TotalValue);
		Assert.Equal(long.MaxValue, result.TotalCost);
		Assert.Same(candidate, Assert.Single(result.SelectedCandidates));
	}

	[Fact]
	public void EmptyInputAndPreCancellationKeepTheirCompletionStates()
	{
		SelectionResult<int> empty = CoinSelector.Select<int>([], 950,
			new() { Mode = SelectionMode.Closest }, TestContext.Current.CancellationToken);
		Assert.Equal(SelectionStatus.NoSolution, empty.Status);
		Assert.False(empty.HasSelection);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		SelectionResult<int> cancelled = CoinSelector.Select([new SelectionCandidate<int>(0, 900)], 950,
			new() { Mode = SelectionMode.Closest }, cancellation.Token);
		Assert.Equal(SelectionStatus.Cancelled, cancelled.Status);
		Assert.False(cancelled.HasSelection);
		Assert.Equal(0, cancelled.VisitedNodes);
	}

	[Fact]
	public void GcdRemaindersBeforeAtAndAfterMidpointsMatchIndependentExhaustiveRanking()
	{
		Random random = new(46_229);
		for (int sample = 0; sample < 72; sample++)
		{
			int unit = 2 * random.Next(2, 10);
			SelectionCandidate<int>[] candidates = Enumerable.Range(0, random.Next(1, 10))
				.Select(index => new SelectionCandidate<int>(index, unit * (index == 0 ? 1 : random.Next(2, 9)),
					random.Next(0, 5), random.Next(1, 5))).ToArray();
			int remainder = (sample % 3) switch { 0 => 1, 1 => unit / 2, _ => unit - 1 };
			long target = unit * random.Next(1, 12) + remainder;
			SelectionOptions policy = new()
			{
				MinimumValue = sample % 7 == 0 ? target + 1 : 1,
				MaximumShortfall = sample % 5 == 0 ? long.MaxValue : random.Next(0, unit * 3),
				MaximumExcess = sample % 6 == 0 ? long.MaxValue : random.Next(0, unit * 3),
				MaximumDifference = sample % 4 == 0 ? random.Next(0, unit * 2) : long.MaxValue,
				MaximumInputCount = sample % 8 == 0 ? int.MaxValue : random.Next(1, 15),
				MaximumSearchNodes = 65_536
			};
			foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
			{
				SelectionOptions options = policy with { Mode = mode };
				AssertMatchesOracle(candidates, target, options,
					CoinSelector.Select(candidates, target, options, TestContext.Current.CancellationToken));
			}
		}
	}

	private static void AssertMatchesOracle(SelectionCandidate<int>[] candidates, long target,
		SelectionOptions options, SelectionResult<int> actual)
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
