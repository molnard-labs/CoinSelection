using Xunit;

namespace CoinSelection.Tests;

public class CandidateCountBoundaryTests
{
	[Fact]
	public void ACandidateLimitEqualToTheInputLimitPreservesEverySearchPrefix()
	{
		SelectionCandidate<int>[] candidates =
		[
			new(7, 8, 2), new(7, 20, 0, 3), new(7, 4, 1), new(7, 3), new(7, 14, 6, 2)
		];
		AssertRedundantLimit(candidates, inputLimit: 3, candidateLimit: 3,
			exact: [candidates[0], candidates[2], candidates[3]],
			limitedBelow: [candidates[4]], limitedAbove: [candidates[3], candidates[4]]);
	}

	[Fact]
	public void ACandidateLimitEqualToTheAvailableCountPreservesEverySearchPrefix()
	{
		SelectionCandidate<int>[] candidates = [new(7, 4, 1), new(7, 8, 2, 2), new(7, 3, 0, 2)];
		AssertRedundantLimit(candidates, inputLimit: 6, candidateLimit: 3,
			exact: candidates, limitedBelow: [candidates[0], candidates[1]], limitedAbove: []);
	}

	private static void AssertRedundantLimit(SelectionCandidate<int>[] candidates, int inputLimit, int candidateLimit,
		SelectionCandidate<int>[] exact, SelectionCandidate<int>[] limitedBelow, SelectionCandidate<int>[] limitedAbove)
	{
		const long target = 15;
		SelectionOptions unlimited = new()
		{
			MaximumInputCount = inputLimit, MinimumValue = 11,
			MaximumShortfall = 4, MaximumExcess = 4, MaximumDifference = 4,
			MaximumSearchNodes = long.MaxValue
		};
		SelectionOptions redundant = unlimited with { MaximumCandidateCount = candidateLimit };
		SelectionOptions limited = unlimited with { MaximumCandidateCount = candidateLimit - 1 };

		// The full three-candidate selection is exact. Removing one slot changes
		// the public result, whereas a redundant finite limit must preserve it.
		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			AssertComplete(CoinSelector.Select(candidates, target, redundant with { Mode = mode },
				TestContext.Current.CancellationToken), exact);
			AssertComplete(CoinSelector.Select(candidates, target, limited with { Mode = mode },
				TestContext.Current.CancellationToken), mode == SelectionMode.AtLeastTarget ? limitedAbove : limitedBelow);
		}
		TwoSidedSelectionResult<int> fullBoth = CoinSelector.SelectBoth(candidates, target, redundant,
			TestContext.Current.CancellationToken);
		AssertComplete(fullBoth.AtMostTarget, exact);
		AssertComplete(fullBoth.AtLeastTarget, exact);
		TwoSidedSelectionResult<int> limitedBoth = CoinSelector.SelectBoth(candidates, target, limited,
			TestContext.Current.CancellationToken);
		AssertComplete(limitedBoth.AtMostTarget, limitedBelow);
		AssertComplete(limitedBoth.AtLeastTarget, limitedAbove);

		// A constraint that is implied by another public constraint must preserve
		// not only the final answer but every deterministic bounded-search prefix.
		foreach (long budget in new long[] { 1, 2, 3, 4, 5, 8, 13, 32, long.MaxValue })
		{
			foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
			{
				SelectionOptions baseline = unlimited with { Mode = mode, MaximumSearchNodes = budget };
				SelectionOptions finite = baseline with { MaximumCandidateCount = candidateLimit };
				SelectionResult<int> expected = CoinSelector.Select(candidates, target, baseline, TestContext.Current.CancellationToken);
				SelectionResult<int> actual = CoinSelector.Select(candidates, target, finite, TestContext.Current.CancellationToken);
				AssertSamePrefix(expected, actual, candidateLimit, inputLimit, budget);
			}
			SelectionOptions bothBaseline = unlimited with { MaximumSearchNodes = budget };
			TwoSidedSelectionResult<int> expectedBoth = CoinSelector.SelectBoth(candidates, target, bothBaseline,
				TestContext.Current.CancellationToken);
			TwoSidedSelectionResult<int> actualBoth = CoinSelector.SelectBoth(candidates, target,
				bothBaseline with { MaximumCandidateCount = candidateLimit }, TestContext.Current.CancellationToken);
			AssertSamePrefix(expectedBoth.AtMostTarget, actualBoth.AtMostTarget, candidateLimit, inputLimit, budget);
			AssertSamePrefix(expectedBoth.AtLeastTarget, actualBoth.AtLeastTarget, candidateLimit, inputLimit, budget);
			Assert.Equal(actualBoth.AtMostTarget.VisitedNodes, actualBoth.AtLeastTarget.VisitedNodes);
		}
	}

	private static void AssertComplete(SelectionResult<int> result, SelectionCandidate<int>[] expected)
	{
		Assert.Equal(expected.Length == 0 ? SelectionStatus.NoSolution : SelectionStatus.Optimal, result.Status);
		Assert.Equal(expected.Length != 0, result.HasSelection);
		Assert.Equal(expected.Sum(candidate => candidate.Value), result.TotalValue);
		Assert.Equal(expected.Sum(candidate => candidate.Cost), result.TotalCost);
		Assert.Equal(expected.Sum(candidate => candidate.InputCount), result.InputCount);
		AssertReferences(result, expected);
	}

	private static void AssertSamePrefix(SelectionResult<int> expected, SelectionResult<int> actual,
		int candidateLimit, int inputLimit, long budget)
	{
		Assert.Equal(expected.Status, actual.Status);
		Assert.Equal(expected.HasSelection, actual.HasSelection);
		Assert.Equal(expected.VisitedNodes, actual.VisitedNodes);
		Assert.Equal(expected.TotalValue, actual.TotalValue);
		Assert.Equal(expected.TotalCost, actual.TotalCost);
		Assert.Equal(expected.InputCount, actual.InputCount);
		Assert.InRange(actual.VisitedNodes, 1, budget);
		Assert.InRange(actual.SelectedCandidates.Count, 0, candidateLimit);
		Assert.InRange(actual.InputCount, 0, inputLimit);
		AssertReferences(actual, expected.SelectedCandidates);
	}

	private static void AssertReferences(SelectionResult<int> actual, IReadOnlyList<SelectionCandidate<int>> expected)
	{
		Assert.Equal(expected.Count, actual.SelectedCandidates.Count);
		for (int index = 0; index < expected.Count; index++)
		{
			Assert.Same(expected[index], actual.SelectedCandidates[index]);
		}
	}
}
