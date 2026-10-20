using Xunit;

namespace CoinSelection.Tests;

public class ProductionOptimizationTests
{
	[Fact]
	public void RemainingInputCapacityCanProveInsufficiencyAtTheRoot()
	{
		SelectionCandidate<int>[] candidates = Enumerable.Range(0, 1_000)
			.Select(i => new SelectionCandidate<int>(i, 100)).ToArray();

		SelectionResult<int> result = CoinSelector.Select(candidates, 801,
			new() { MaximumInputCount = 8, MaximumSearchNodes = 1 }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.NoSolution, result.Status);
		Assert.False(result.HasSelection);
		Assert.Equal(1, result.VisitedNodes);
	}

	[Fact]
	public void CapacityBoundRemainsOptimisticForUnequalGroupWeights()
	{
		SelectionCandidate<int>[] candidates = [new(0, 12, InputCount: 3), new(1, 8, InputCount: 2), new(2, 7)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 15,
			new() { MaximumInputCount = 3, MaximumDifference = 0 }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(15, result.TotalValue);
		Assert.Equal(3, result.InputCount);
		Assert.Equal([1, 2], result.SelectedCandidates.Select(x => x.Item));
	}

	[Fact]
	public void IncumbentLowerBoundaryRetainsEqualDistanceCheaperSelections()
	{
		SelectionCandidate<string>[] candidates = [new("expensive", 9, 5), new("five", 5, 1), new("four", 4, 1)];

		SelectionResult<string> result = CoinSelector.Select(candidates, 10,
			new() { Mode = SelectionMode.AtMostTarget }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(9, result.TotalValue);
		Assert.Equal(2, result.TotalCost);
		Assert.Equal(["five", "four"], result.SelectedCandidates.Select(x => x.Item));
	}

	[Fact]
	public void IncumbentUpperBoundaryRetainsOtherwiseTiedAboveTargetSelection()
	{
		SelectionCandidate<int>[] candidates = [new(0, 9, InputCount: 2), new(1, 6), new(2, 5)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 10,
			new() { Mode = SelectionMode.Closest }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(11, result.TotalValue);
		Assert.Equal(2, result.InputCount);
		Assert.Equal([1, 2], result.SelectedCandidates.Select(x => x.Item));
	}

	[Fact]
	public void TightenedUpperBoundStillFindsAnExactSelectionAfterAnOvershoot()
	{
		SelectionCandidate<int>[] candidates = [new(0, 11, 1), new(1, 10, 2), new(2, 9, 0)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 10,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(10, result.TotalValue);
		Assert.Equal(1, Assert.Single(result.SelectedCandidates).Item);
	}

	[Fact]
	public void ReusedBestPathBufferDiscardsIndicesFromEarlierLongerSelections()
	{
		SelectionCandidate<int>[] candidates = [new(0, 1), new(1, 6), new(2, 1), new(3, 4), new(4, 8)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 10,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(10, result.TotalValue);
		Assert.Equal(2, result.InputCount);
		Assert.Equal([1, 3], result.SelectedCandidates.Select(x => x.Item));
		Assert.Same(candidates[1], result.SelectedCandidates[0]);
		Assert.Same(candidates[3], result.SelectedCandidates[1]);
	}

	[Fact]
	public void CapacityBoundDoesNotOverflowAtInt64Limit()
	{
		SelectionCandidate<int>[] candidates = [new(0, long.MaxValue - 1), new(1, 1)];

		SelectionResult<int> insufficient = CoinSelector.Select(candidates, long.MaxValue,
			new() { MaximumInputCount = 1, MaximumSearchNodes = 1 }, TestContext.Current.CancellationToken);
		SelectionResult<int> exact = CoinSelector.Select(candidates, long.MaxValue,
			new() { MaximumInputCount = 2 }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.NoSolution, insufficient.Status);
		Assert.Equal(1, insufficient.VisitedNodes);
		Assert.Equal(SelectionStatus.Optimal, exact.Status);
		Assert.Equal(long.MaxValue, exact.TotalValue);
		Assert.Equal(2, exact.InputCount);
	}

	[Fact]
	public void LongSelectedPathIsCopiedWithoutRecursionOrHardCodedLengthLimit()
	{
		SelectionCandidate<int>[] candidates = Enumerable.Range(0, 30_000)
			.Select(i => new SelectionCandidate<int>(i, 2)).ToArray();

		SelectionResult<int> result = CoinSelector.Select(candidates, 60_000,
			new() { MaximumDifference = 0 }, TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(30_000, result.InputCount);
		Assert.Equal(Enumerable.Range(0, 30_000), result.SelectedCandidates.Select(x => x.Item));
	}
}
