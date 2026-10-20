using Xunit;

namespace CoinSelection.Tests;

public class AsymmetricToleranceTests
{
	[Fact]
	public void DirectionalTolerancesDefaultToUnbounded()
	{
		SelectionOptions options = new();

		Assert.Equal(long.MaxValue, options.MaximumShortfall);
		Assert.Equal(long.MaxValue, options.MaximumExcess);
	}

	[Theory]
	[InlineData(7, false)]
	[InlineData(8, true)]
	[InlineData(13, true)]
	[InlineData(14, false)]
	public void ClosestAppliesBothInclusiveDirectionalBoundaries(long value, bool permitted)
	{
		SelectionResult<int> result = CoinSelector.Select([new SelectionCandidate<int>(0, value)], 10, new()
		{
			Mode = SelectionMode.Closest,
			MaximumShortfall = 2,
			MaximumExcess = 3
		}, TestContext.Current.CancellationToken);

		Assert.Equal(permitted ? SelectionStatus.Optimal : SelectionStatus.NoSolution, result.Status);
		Assert.Equal(permitted, result.HasSelection);
		Assert.Equal(permitted ? value : 0, result.TotalValue);
	}

	[Fact]
	public void OneClosestRequestConsidersBothSidesAndKeepsDistanceFirstRanking()
	{
		SelectionCandidate<string>[] candidates = [new("below", 8), new("above", 11, 100)];
		SelectionOptions options = new()
		{
			Mode = SelectionMode.Closest,
			MaximumShortfall = 2,
			MaximumExcess = 1
		};

		SelectionResult<string> result = CoinSelector.Select(candidates, 10, options, TestContext.Current.CancellationToken);
		SelectionResult<string> belowOnly = CoinSelector.Select(candidates, 10, options with { MaximumExcess = 0 }, TestContext.Current.CancellationToken);

		Assert.Same(candidates[1], Assert.Single(result.SelectedCandidates));
		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Same(candidates[0], Assert.Single(belowOnly.SelectedCandidates));
		Assert.Equal(SelectionStatus.Optimal, belowOnly.Status);
	}

	[Theory]
	[InlineData(1, 0, 1, 2, true)]
	[InlineData(0, 0, 1, 2, false)]
	[InlineData(0, 0, 1, 1, true)]
	public void EqualDistancesRetainCostCountAndAboveTargetTieBreaks(
		long belowCost, long aboveCost, int belowInputs, int aboveInputs, bool preferAbove)
	{
		SelectionCandidate<int>[] candidates = [new(0, 9, belowCost, belowInputs), new(1, 11, aboveCost, aboveInputs)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 10, new()
		{
			Mode = SelectionMode.Closest,
			MaximumShortfall = 1,
			MaximumExcess = 3
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Same(candidates[preferAbove ? 1 : 0], Assert.Single(result.SelectedCandidates));
	}

	[Theory]
	[InlineData(true, 9, true)]
	[InlineData(true, 8, false)]
	[InlineData(true, 30, true)]
	[InlineData(false, 11, true)]
	[InlineData(false, 12, false)]
	[InlineData(false, 1, true)]
	public void SettingOnlyOneToleranceLeavesTheOtherDirectionUnbounded(bool limitShortfall, long value, bool permitted)
	{
		SelectionOptions options = limitShortfall
			? new() { Mode = SelectionMode.Closest, MaximumShortfall = 1 }
			: new() { Mode = SelectionMode.Closest, MaximumExcess = 1 };

		SelectionResult<int> result = CoinSelector.Select([new SelectionCandidate<int>(0, value)], 10, options, TestContext.Current.CancellationToken);

		Assert.Equal(permitted ? SelectionStatus.Optimal : SelectionStatus.NoSolution, result.Status);
		Assert.Equal(permitted, result.HasSelection);
	}

	[Theory]
	[InlineData(0, 2, 11)]
	[InlineData(2, 0, 9)]
	[InlineData(0, 0, 0)]
	public void ZeroToleranceForbidsTheCorrespondingSide(long shortfall, long excess, long expectedValue)
	{
		SelectionCandidate<int>[] candidates = [new(0, 9), new(1, 11)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 10, new()
		{
			Mode = SelectionMode.Closest,
			MaximumShortfall = shortfall,
			MaximumExcess = excess
		}, TestContext.Current.CancellationToken);

		Assert.Equal(expectedValue, result.TotalValue);
		Assert.Equal(expectedValue == 0 ? SelectionStatus.NoSolution : SelectionStatus.Optimal, result.Status);
	}

	[Fact]
	public void TwoZeroTolerancesStillPermitAnExactSubset()
	{
		SelectionCandidate<int>[] candidates = [new(0, 6), new(1, 4), new(2, 11)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 10, new()
		{
			Mode = SelectionMode.Closest,
			MaximumShortfall = 0,
			MaximumExcess = 0
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(10, result.TotalValue);
		Assert.Equal([0, 1], result.SelectedCandidates.Select(candidate => candidate.Item));
	}

	[Theory]
	[InlineData(8, false)]
	[InlineData(9, true)]
	[InlineData(11, true)]
	[InlineData(12, false)]
	public void LegacyMaximumDifferenceIntersectsBothDirectionalTolerances(long value, bool permitted)
	{
		SelectionResult<int> result = CoinSelector.Select([new SelectionCandidate<int>(0, value)], 10, new()
		{
			Mode = SelectionMode.Closest,
			MaximumDifference = 1,
			MaximumShortfall = 2,
			MaximumExcess = 4
		}, TestContext.Current.CancellationToken);

		Assert.Equal(permitted ? SelectionStatus.Optimal : SelectionStatus.NoSolution, result.Status);
	}

	[Theory]
	[InlineData(SelectionMode.AtLeastTarget, 9)]
	[InlineData(SelectionMode.AtMostTarget, 11)]
	public void DirectionalTolerancesDoNotOverrideModeConstraints(SelectionMode mode, long value)
	{
		SelectionResult<int> result = CoinSelector.Select([new SelectionCandidate<int>(0, value)], 10, new()
		{
			Mode = mode,
			MaximumShortfall = 3,
			MaximumExcess = 3
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.NoSolution, result.Status);
		Assert.False(result.HasSelection);
	}

	[Theory]
	[InlineData(SelectionMode.AtLeastTarget, 13, true)]
	[InlineData(SelectionMode.AtLeastTarget, 14, false)]
	[InlineData(SelectionMode.AtMostTarget, 8, true)]
	[InlineData(SelectionMode.AtMostTarget, 7, false)]
	public void OneSidedModesAlsoRespectTheirDirectionalTolerance(SelectionMode mode, long value, bool permitted)
	{
		SelectionResult<int> result = CoinSelector.Select([new SelectionCandidate<int>(0, value)], 10, new()
		{
			Mode = mode,
			MaximumShortfall = 2,
			MaximumExcess = 3
		}, TestContext.Current.CancellationToken);

		Assert.Equal(permitted ? SelectionStatus.Optimal : SelectionStatus.NoSolution, result.Status);
	}

	[Fact]
	public void MinimumValueIntersectsTheAsymmetricIntervalInclusively()
	{
		SelectionCandidate<int>[] candidates = [new(0, 9), new(1, 12), new(2, 14)];
		SelectionOptions options = new()
		{
			Mode = SelectionMode.Closest,
			MaximumShortfall = 4,
			MaximumExcess = 4,
			MinimumValue = 12
		};

		SelectionResult<int> result = CoinSelector.Select(candidates, 10, options, TestContext.Current.CancellationToken);
		SelectionResult<int> impossible = CoinSelector.Select(candidates, 10, options with { MinimumValue = 15 }, TestContext.Current.CancellationToken);
		SelectionResult<int> atMost = CoinSelector.Select(candidates, 10, options with { Mode = SelectionMode.AtMostTarget }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Same(candidates[1], Assert.Single(result.SelectedCandidates));
		Assert.Equal(SelectionStatus.NoSolution, impossible.Status);
		Assert.Equal(1, impossible.VisitedNodes);
		Assert.Equal(SelectionStatus.NoSolution, atMost.Status);
	}

	[Fact]
	public void AsymmetricBoundsSaturateNearLongMaximumAndAtThePositiveMinimum()
	{
		SelectionResult<int> above = CoinSelector.Select([new SelectionCandidate<int>(0, long.MaxValue)], long.MaxValue - 1, new()
		{
			Mode = SelectionMode.Closest,
			MaximumShortfall = 0,
			MaximumExcess = 2
		}, TestContext.Current.CancellationToken);
		SelectionResult<int> below = CoinSelector.Select([new SelectionCandidate<int>(0, 1)], long.MaxValue, new()
		{
			Mode = SelectionMode.Closest,
			MaximumShortfall = long.MaxValue,
			MaximumExcess = 0
		}, TestContext.Current.CancellationToken);
		SelectionResult<int> excluded = CoinSelector.Select([new SelectionCandidate<int>(0, long.MaxValue - 4)], long.MaxValue - 1, new()
		{
			Mode = SelectionMode.Closest,
			MaximumShortfall = 2,
			MaximumExcess = long.MaxValue
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, above.Status);
		Assert.Equal(long.MaxValue, above.TotalValue);
		Assert.Equal(SelectionStatus.Optimal, below.Status);
		Assert.Equal(1, below.TotalValue);
		Assert.Equal(SelectionStatus.NoSolution, excluded.Status);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void NegativeDirectionalToleranceIsRejectedBeforeCancellation(bool negativeShortfall)
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		SelectionOptions options = negativeShortfall
			? new() { MaximumShortfall = -1 }
			: new() { MaximumExcess = -1 };

		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.Select<int>([], 10, options, cancellation.Token));
	}

	[Fact]
	public void SeededAsymmetricRequestsMatchIndependentExhaustiveSubsetRanking()
	{
		Random random = new(61_407);
		for (int sample = 0; sample < 96; sample++)
		{
			var candidates = new SelectionCandidate<object>[random.Next(0, 11)];
			for (int index = 0; index < candidates.Length; index++)
			{
				candidates[index] = index > 0 && index % 4 == 0
					? candidates[index - 1]
					: new(new object(), random.Next(1, 25), random.Next(0, 7), random.Next(1, 5));
			}

			long target = random.Next(1, 90);
			foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
			{
				SelectionOptions options = new()
				{
					Mode = mode,
					MinimumValue = sample % 4 == 0 ? target + random.Next(0, 15) : random.Next(1, 25),
					MaximumShortfall = sample % 6 == 0 ? long.MaxValue : random.Next(0, 35),
					MaximumExcess = sample % 7 == 0 ? long.MaxValue : random.Next(0, 35),
					MaximumDifference = sample % 3 == 0 ? random.Next(0, 20) : long.MaxValue,
					MaximumInputCount = sample % 8 == 0 ? int.MaxValue : random.Next(1, 16),
					MaximumSearchNodes = long.MaxValue
				};

				OracleSelection? expected = EnumerateSubsets(candidates, target, options);
				SelectionResult<object> actual = CoinSelector.Select(candidates, target, options, TestContext.Current.CancellationToken);

				Assert.Equal(expected is null ? SelectionStatus.NoSolution : SelectionStatus.Optimal, actual.Status);
				Assert.Equal(expected is not null, actual.HasSelection);
				Assert.Equal(expected is not null, actual.IsOptimal);
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
		}
	}

	private static OracleSelection? EnumerateSubsets(SelectionCandidate<object>[] candidates, long target, SelectionOptions options)
	{
		var ordered = candidates.Select((candidate, index) => (Candidate: candidate, Index: index))
			.OrderByDescending(item => item.Candidate.Value).ThenBy(item => item.Candidate.Cost)
			.ThenBy(item => item.Candidate.InputCount).ThenBy(item => item.Index).ToArray();
		OracleSelection? best = null;
		// Descending masks in this bit order independently enumerate inclusion before omission.
		for (int mask = (1 << ordered.Length) - 1; mask > 0; mask--)
		{
			long value = 0;
			long cost = 0;
			long inputCount = 0;
			List<int> originalIndices = [];
			for (int index = 0; index < ordered.Length; index++)
			{
				if ((mask & (1 << (ordered.Length - index - 1))) == 0)
				{
					continue;
				}

				value += ordered[index].Candidate.Value;
				cost += ordered[index].Candidate.Cost;
				inputCount += ordered[index].Candidate.InputCount;
				originalIndices.Add(ordered[index].Index);
			}

			long shortfall = value < target ? target - value : 0;
			long excess = value > target ? value - target : 0;
			long difference = shortfall + excess;
			if (value < options.MinimumValue || inputCount > options.MaximumInputCount ||
				shortfall > options.MaximumShortfall || excess > options.MaximumExcess || difference > options.MaximumDifference ||
				(options.Mode == SelectionMode.AtLeastTarget && value < target) ||
				(options.Mode == SelectionMode.AtMostTarget && value > target))
			{
				continue;
			}

			var rank = (difference, cost, inputCount, options.Mode == SelectionMode.Closest && value < target ? 1 : 0);
			if (best is null || rank.CompareTo(best.Rank) < 0)
			{
				originalIndices.Sort();
				best = new(value, cost, checked((int)inputCount), rank, originalIndices.ToArray());
			}
		}

		return best;
	}

	private sealed record OracleSelection(long Value, long Cost, int InputCount,
		(long Difference, long Cost, long InputCount, int Side) Rank, int[] OriginalIndices);
}
