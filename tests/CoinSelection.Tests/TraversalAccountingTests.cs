using Xunit;

namespace CoinSelection.Tests;

public class TraversalAccountingTests
{
	[Theory]
	[InlineData(3, false, SelectionStatus.SearchLimitReached)]
	[InlineData(5, true, SelectionStatus.SearchLimitReached)]
	[InlineData(6, true, SelectionStatus.Optimal)]
	public void AnInsufficientWeightedInclusionAndItsOmissionConsumeSeparateNodes(
		long budget, bool hasSelection, SelectionStatus expectedStatus)
	{
		SelectionCandidate<int>[] candidates = [new(0, 6, 0, 2), new(1, 5), new(2, 4)];
		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			SelectionOptions options = new() { Mode = mode, MaximumInputCount = 2, MaximumDifference = 0, MaximumSearchNodes = budget };
			SelectionResult<int> single = CoinSelector.Select(candidates, 9, options, TestContext.Current.CancellationToken);
			TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 9, options, TestContext.Current.CancellationToken);
			foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
			{
				Assert.Equal(expectedStatus, result.Status);
				Assert.Equal(budget, result.VisitedNodes);
				Assert.Equal(hasSelection, result.HasSelection);
				Assert.Equal(hasSelection ? 9 : 0, result.TotalValue);
				Assert.Equal(hasSelection ? 2 : 0, result.InputCount);
				AssertReferences(result, hasSelection ? [candidates[1], candidates[2]] : []);
			}
		}
	}

	[Fact]
	public void AnInsufficientWeightedChildCanProveNoSolutionWithoutChangingNodeAccounting()
	{
		SelectionCandidate<int>[] candidates = [new(0, 6, 0, 2), new(1, 5), new(2, 4)];
		SelectionOptions options = new() { MaximumInputCount = 3, MaximumDifference = 0, MaximumSearchNodes = 3 };
		SelectionResult<int> single = CoinSelector.Select(candidates, 12, options, TestContext.Current.CancellationToken);
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 12, options, TestContext.Current.CancellationToken);
		foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.NoSolution, result.Status);
			Assert.False(result.HasSelection);
			Assert.Equal(3, result.VisitedNodes);
		}
	}

	[Fact]
	public void ReturningToAnOmissionUsesTheInputLimitTightenedByItsDescendant()
	{
		SelectionCandidate<int>[] candidates = [new(0, 10, 0, 4), new(1, 7, 0, 2), new(2, 3), new(3, 2)];
		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			SelectionOptions options = new() { Mode = mode, MaximumDifference = 0, MaximumSearchNodes = 7 };
			SelectionResult<int> single = CoinSelector.Select(candidates, 10, options, TestContext.Current.CancellationToken);
			TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 10, options, TestContext.Current.CancellationToken);
			foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
			{
				Assert.Equal(SelectionStatus.Optimal, result.Status);
				Assert.Equal(7, result.VisitedNodes);
				Assert.Equal(10, result.TotalValue);
				Assert.Equal(0, result.TotalCost);
				Assert.Equal(3, result.InputCount);
				AssertReferences(result, candidates[1], candidates[2]);
			}
		}
	}

	[Fact]
	public void OmissionTraversalKeepsLaterMidpointCostImprovementsOnEitherSide()
	{
		SelectionCandidate<int>[] candidates = [new(0, 900, 2), new(1, 600, 0, 2), new(2, 400, 1, 2), new(3, 300)];
		SelectionOptions options = new() { Mode = SelectionMode.Closest, MaximumDifference = 50 };
		SelectionResult<int> single = CoinSelector.Select(candidates, 950, options, TestContext.Current.CancellationToken);
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth(candidates, 950, options, TestContext.Current.CancellationToken);

		foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget })
		{
			Assert.Equal(SelectionStatus.Optimal, result.Status);
			Assert.Equal(900, result.TotalValue);
			Assert.Equal(0, result.TotalCost);
			Assert.Equal(3, result.InputCount);
			AssertReferences(result, candidates[1], candidates[3]);
		}
		Assert.Equal(SelectionStatus.Optimal, both.AtLeastTarget.Status);
		Assert.Equal(1_000, both.AtLeastTarget.TotalValue);
		Assert.Equal(1, both.AtLeastTarget.TotalCost);
		Assert.Equal(4, both.AtLeastTarget.InputCount);
		AssertReferences(both.AtLeastTarget, candidates[1], candidates[2]);
	}

	[Fact]
	public void AVeryLargeNodeBudgetStillReportsTheSmallNumberOfEnteredNodes()
	{
		SelectionCandidate<int> candidate = new(0, 5, 1, 2);
		SelectionOptions options = new() { MaximumSearchNodes = long.MaxValue };
		SelectionResult<int> single = CoinSelector.Select([candidate], 5, options, TestContext.Current.CancellationToken);
		TwoSidedSelectionResult<int> both = CoinSelector.SelectBoth([candidate], 5, options, TestContext.Current.CancellationToken);
		foreach (SelectionResult<int> result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.Optimal, result.Status);
			Assert.Equal(2, result.VisitedNodes);
			Assert.Equal(5, result.TotalValue);
			Assert.Equal(1, result.TotalCost);
			Assert.Equal(2, result.InputCount);
			AssertReferences(result, candidate);
		}
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
