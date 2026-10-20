using Xunit;

namespace CoinSelection.Tests;

public class SymmetryPruningTests
{
	[Theory]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.Closest)]
	public void FullyTiedDuplicatesRetainEarliestCandidateReferencesInOriginalOrder(SelectionMode mode)
	{
		SelectionCandidate<string>[] candidates =
		[
			new("small", 3, 1),
			new("first eight", 8, 2),
			new("second eight", 8, 2),
			new("five", 5, 1),
			new("third eight", 8, 2)
		];

		SelectionResult<string> result = CoinSelector.Select(candidates, 19, new()
		{
			Mode = mode,
			MaximumDifference = 0,
			MaximumSearchNodes = 4_096
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(19, result.TotalValue);
		Assert.Equal(5, result.TotalCost);
		Assert.Equal(3, result.InputCount);
		Assert.Collection(result.SelectedCandidates,
			candidate => Assert.Same(candidates[0], candidate),
			candidate => Assert.Same(candidates[1], candidate),
			candidate => Assert.Same(candidates[2], candidate));
	}

	[Fact]
	public void RepeatedReferenceStillRepresentsTwoSelectablePositions()
	{
		SelectionCandidate<object> repeated = new(new object(), 6, 2);
		SelectionCandidate<object>[] candidates = [repeated, new(new object(), 5), repeated];

		SelectionResult<object> result = CoinSelector.Select(candidates, 12,
			new() { MaximumDifference = 0 }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(12, result.TotalValue);
		Assert.Equal(4, result.TotalCost);
		Assert.Equal(2, result.InputCount);
		Assert.Collection(result.SelectedCandidates,
			candidate => Assert.Same(repeated, candidate),
			candidate => Assert.Same(repeated, candidate));
	}

	[Fact]
	public void EqualValuesWithDifferentCostsAndGroupSizesAreNotInterchangeable()
	{
		SelectionCandidate<string>[] candidates =
		[
			new("cheap large group", 8, 0, 2),
			new("costly single", 8, 1),
			new("four", 4)
		];

		SelectionResult<string> result = CoinSelector.Select(candidates, 12, new()
		{
			MaximumDifference = 0,
			MaximumInputCount = 2
		}, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(12, result.TotalValue);
		Assert.Equal(1, result.TotalCost);
		Assert.Equal(2, result.InputCount);
		Assert.Collection(result.SelectedCandidates,
			candidate => Assert.Same(candidates[1], candidate),
			candidate => Assert.Same(candidates[2], candidate));
	}

	[Theory]
	[InlineData(3, false)]
	[InlineData(4, true)]
	public void EqualValuesAndCostsRetainEachCandidatesUnderlyingInputCount(int inputLimit, bool permitted)
	{
		SelectionCandidate<string>[] candidates = [new("single", 8, 2), new("group", 8, 2, 3)];

		SelectionResult<string> result = CoinSelector.Select(candidates, 16,
			new() { MaximumDifference = 0, MaximumInputCount = inputLimit }, TestContext.Current.CancellationToken);

		Assert.Equal(permitted ? SelectionStatus.Optimal : SelectionStatus.NoSolution, result.Status);
		Assert.Equal(permitted, result.HasSelection);
		Assert.Equal(permitted ? 4 : 0, result.InputCount);
		Assert.Equal(permitted ? 4 : 0, result.TotalCost);
	}

	[Fact]
	public void EqualValuesAndCountsRetainEachCandidatesCost()
	{
		SelectionCandidate<int>[] candidates = [new(0, 8, 1), new(1, 8, 5)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 16,
			new() { MaximumDifference = 0 }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(16, result.TotalValue);
		Assert.Equal(6, result.TotalCost);
		Assert.Equal(2, result.InputCount);
	}

	[Theory]
	[InlineData(SelectionMode.AtLeastTarget, 6, 21)]
	[InlineData(SelectionMode.AtMostTarget, 6, 14)]
	[InlineData(SelectionMode.Closest, 6, 21)]
	[InlineData(SelectionMode.AtLeastTarget, 5, 0)]
	[InlineData(SelectionMode.AtMostTarget, 5, 14)]
	[InlineData(SelectionMode.Closest, 5, 14)]
	public void DuplicateGroupsRespectBothInclusiveAsymmetricBoundsAndTheInputLimit(
		SelectionMode mode, int inputLimit, long expectedValue)
	{
		SelectionCandidate<int>[] candidates = Enumerable.Range(0, 6)
			.Select(index => new SelectionCandidate<int>(index, 7, 2, 2)).ToArray();

		SelectionResult<int> result = CoinSelector.Select(candidates, 20, new()
		{
			Mode = mode,
			MinimumValue = 14,
			MaximumShortfall = 6,
			MaximumExcess = 1,
			MaximumInputCount = inputLimit,
			MaximumSearchNodes = 4_096
		}, TestContext.Current.CancellationToken);

		Assert.Equal(expectedValue == 0 ? SelectionStatus.NoSolution : SelectionStatus.Optimal, result.Status);
		Assert.Equal(expectedValue, result.TotalValue);
		int selectedCount = (int)(expectedValue / 7);
		Assert.Equal(selectedCount * 2, result.TotalCost);
		Assert.Equal(selectedCount * 2, result.InputCount);
		Assert.Equal(Enumerable.Range(0, selectedCount), result.SelectedCandidates.Select(candidate => candidate.Item));
	}

	[Theory]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.Closest)]
	public void DuplicateHeavyUnreachableIntervalIsProvedWithinASmallWorkBudget(SelectionMode mode)
	{
		SelectionCandidate<int>[] candidates = Enumerable.Range(0, 48)
			.Select(index => new SelectionCandidate<int>(index, 10, 1)).ToArray();

		SelectionResult<int> result = CoinSelector.Select(candidates, 235, new()
		{
			Mode = mode,
			MaximumShortfall = 2,
			MaximumExcess = 3,
			MaximumInputCount = 24,
			MaximumSearchNodes = 4_096
		}, TestContext.Current.CancellationToken);

		// Every possible sum is a multiple of ten; none lies in [233, 238].
		// Enumerating the billions of interchangeable subsets would exhaust this budget.
		Assert.Equal(SelectionStatus.NoSolution, result.Status);
		Assert.False(result.HasSelection);
		Assert.False(result.IsOptimal);
		Assert.InRange(result.VisitedNodes, 1, 1_000);
	}

	[Fact]
	public void BudgetedDuplicateSearchReturnsAValidDeterministicPartialSelection()
	{
		SelectionCandidate<int>[] candidates = Enumerable.Range(0, 48)
			.Select(index => new SelectionCandidate<int>(index, 10, 1)).ToArray();
		SelectionOptions options = new()
		{
			Mode = SelectionMode.Closest,
			MaximumShortfall = 15,
			MaximumExcess = 15,
			MaximumInputCount = 8,
			MaximumSearchNodes = 8
		};

		SelectionResult<int> first = CoinSelector.Select(candidates, 65, options, TestContext.Current.CancellationToken);
		SelectionResult<int> second = CoinSelector.Select(candidates, 65, options, TestContext.Current.CancellationToken);
		SelectionResult<int> complete = CoinSelector.Select(candidates, 65,
			options with { MaximumSearchNodes = 4_096 }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.SearchLimitReached, first.Status);
		Assert.True(first.HasSelection);
		Assert.False(first.IsOptimal);
		Assert.InRange(first.VisitedNodes, 1, options.MaximumSearchNodes);
		Assert.InRange(first.TotalValue, 50, 80);
		Assert.InRange(first.InputCount, 1, options.MaximumInputCount);
		Assert.Equal(first.SelectedCandidates.Sum(candidate => candidate.Value), first.TotalValue);
		Assert.Equal(first.SelectedCandidates.Sum(candidate => candidate.Cost), first.TotalCost);
		Assert.Equal(first.SelectedCandidates.Sum(candidate => candidate.InputCount), first.InputCount);
		Assert.Equal(first.Status, second.Status);
		Assert.Equal(first.VisitedNodes, second.VisitedNodes);
		Assert.Equal(first.SelectedCandidates, second.SelectedCandidates);
		Assert.Equal(SelectionStatus.Optimal, complete.Status);
		Assert.Equal(60, complete.TotalValue);
		Assert.Equal(6, complete.TotalCost);
		Assert.True(Rank(first.TotalValue, first.TotalCost, first.InputCount, 65, options.Mode)
			.CompareTo(Rank(complete.TotalValue, complete.TotalCost, complete.InputCount, 65, options.Mode)) >= 0);
	}

	[Fact]
	public void DuplicateHeavySmallInputsMatchIndependentExhaustiveRankingAndIdentity()
	{
		Random random = new(77_021);
		for (int sample = 0; sample < 80; sample++)
		{
			var templates = Enumerable.Range(0, random.Next(1, 5))
				.Select(index => (Value: random.Next(1, 15), Cost: random.Next(0, 5), Inputs: random.Next(1, 4)))
				.ToArray();
			var candidates = new SelectionCandidate<object>[random.Next(0, 11)];
			for (int index = 0; index < candidates.Length; index++)
			{
				var template = templates[random.Next(templates.Length)];
				candidates[index] = index > 0 && index % 5 == 0
					? candidates[index - 1]
					: new(new object(), template.Value, template.Cost, template.Inputs);
			}

			long target = random.Next(1, 55);
			foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
			{
				SelectionOptions options = new()
				{
					Mode = mode,
					MinimumValue = sample % 5 == 0 ? target + 1 : random.Next(1, 15),
					MaximumShortfall = sample % 7 == 0 ? long.MaxValue : random.Next(0, 20),
					MaximumExcess = sample % 8 == 0 ? long.MaxValue : random.Next(0, 20),
					MaximumDifference = sample % 3 == 0 ? random.Next(0, 15) : long.MaxValue,
					MaximumInputCount = sample % 6 == 0 ? int.MaxValue : random.Next(1, 15),
					MaximumSearchNodes = 4_096
				};
				OracleSelection? expected = EnumerateSubsets(candidates, target, options);
				SelectionResult<object> actual = CoinSelector.Select(candidates, target, options, TestContext.Current.CancellationToken);

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
		}
	}

	private static OracleSelection? EnumerateSubsets(SelectionCandidate<object>[] candidates, long target, SelectionOptions options)
	{
		var ordered = candidates.Select((candidate, index) => (Candidate: candidate, Index: index))
			.OrderByDescending(item => item.Candidate.Value).ThenBy(item => item.Candidate.Cost)
			.ThenBy(item => item.Candidate.InputCount).ThenBy(item => item.Index).ToArray();
		OracleSelection? best = null;
		// In descending masks, the most significant bit visits each sorted inclusion first.
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
				(options.Mode == SelectionMode.AtLeastTarget && value < target) ||
				(options.Mode == SelectionMode.AtMostTarget && value > target))
			{
				continue;
			}

			var rank = Rank(value, cost, inputCount, target, options.Mode);
			if (best is null || rank.CompareTo(best.Rank) < 0)
			{
				originalIndices.Sort();
				best = new(value, cost, inputCount, rank, originalIndices.ToArray());
			}
		}

		return best;
	}

	private static (long Difference, long Cost, int InputCount, int Side) Rank(
		long value, long cost, int inputCount, long target, SelectionMode mode) =>
		(Math.Abs(value - target), cost, inputCount, mode == SelectionMode.Closest && value < target ? 1 : 0);

	private sealed record OracleSelection(long Value, long Cost, int InputCount,
		(long Difference, long Cost, int InputCount, int Side) Rank, int[] OriginalIndices);
}
