using Xunit;

namespace CoinSelection.Tests;

public class CoinSelectorTests
{
	[Fact]
	public void DefaultsRequireTargetToBeCovered()
	{
		SelectionCandidate<string>[] candidates = [new("small", 8), new("large", 13)];

		SelectionResult<string> result = CoinSelector.Select(candidates, 10, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.True(result.HasSelection);
		Assert.True(result.IsOptimal);
		Assert.Equal("large", Assert.Single(result.SelectedCandidates).Item);
		Assert.Equal(13, result.TotalValue);
	}

	[Theory]
	[InlineData(SelectionMode.AtLeastTarget, 11)]
	[InlineData(SelectionMode.AtMostTarget, 9)]
	[InlineData(SelectionMode.Closest, 11)]
	public void SelectsOnPermittedSide(SelectionMode mode, long expectedValue)
	{
		SelectionCandidate<string>[] candidates = [new("below", 9), new("above", 11)];

		SelectionResult<string> result = CoinSelector.Select(candidates, 10, new() { Mode = mode }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(expectedValue, result.TotalValue);
		Assert.Equal(SelectionStatus.Optimal, result.Status);
	}

	[Fact]
	public void ExactAmountOutranksCheaperOvershoot()
	{
		SelectionCandidate<string>[] candidates = [new("overshoot", 11), new("exact", 10, 100)];

		SelectionResult<string> result = CoinSelector.Select(candidates, 10, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal("exact", Assert.Single(result.SelectedCandidates).Item);
		Assert.Equal(100, result.TotalCost);
	}

	[Fact]
	public void EqualAmountsPreferLowerCostBeforeInputCount()
	{
		SelectionCandidate<string>[] candidates = [new("expensive", 10, 5), new("cheap", 10, 1, 3)];

		SelectionResult<string> result = CoinSelector.Select(candidates, 10, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal("cheap", Assert.Single(result.SelectedCandidates).Item);
		Assert.Equal(3, result.InputCount);
	}

	[Fact]
	public void EqualAmountsAndCostsPreferFewerUnderlyingInputs()
	{
		SelectionCandidate<string>[] candidates = [new("group", 10, 1, 3), new("single", 10, 1)];

		SelectionResult<string> result = CoinSelector.Select(candidates, 10, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal("single", Assert.Single(result.SelectedCandidates).Item);
		Assert.Equal(1, result.InputCount);
	}

	[Fact]
	public void ClosestReplacesEarlierBelowTargetSelectionWhenOtherwiseTied()
	{
		SelectionCandidate<string>[] candidates = [new("below", 9, InputCount: 2), new("six", 6), new("five", 5)];

		SelectionResult<string> result = CoinSelector.Select(candidates, 10, new()
		{
			Mode = SelectionMode.Closest,
			MaximumDifference = 1
		}, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(11, result.TotalValue);
		Assert.Equal(["six", "five"], result.SelectedCandidates.Select(x => x.Item));
	}

	[Fact]
	public void CostOutranksClosestSidePreference()
	{
		SelectionCandidate<string>[] candidates = [new("below", 9), new("above", 11, 1)];

		SelectionResult<string> result = CoinSelector.Select(candidates, 10, new() { Mode = SelectionMode.Closest }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(9, result.TotalValue);
	}

	[Theory]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.Closest)]
	public void MinimumValueCanRequireAFartherAboveTargetSelection(SelectionMode mode)
	{
		SelectionCandidate<int>[] candidates = [new(0, 10), new(1, 30)];

		var result = CoinSelector.Select(candidates, 11, new()
		{
			Mode = mode,
			MinimumValue = 12
		}, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(30, result.TotalValue);
	}

	[Fact]
	public void MinimumValueIsInclusiveAndAppliesToTheWholeSubset()
	{
		SelectionCandidate<int>[] candidates = [new(0, 5), new(1, 4)];

		var result = CoinSelector.Select(candidates, 10, new()
		{
			Mode = SelectionMode.AtMostTarget,
			MinimumValue = 9
		}, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(9, result.TotalValue);
		Assert.Equal(2, result.InputCount);
	}

	[Theory]
	[InlineData(SelectionMode.AtMostTarget, 100)]
	[InlineData(SelectionMode.Closest, 11)]
	public void IncompatibleMinimumAndTargetToleranceProveNoSolution(SelectionMode mode, long minimum)
	{
		SelectionCandidate<int>[] candidates = [new(0, 10), new(1, 100)];

		var result = CoinSelector.Select(candidates, 10, new()
		{
			Mode = mode,
			MinimumValue = minimum,
			MaximumDifference = 0
		}, cancellationToken: TestContext.Current.CancellationToken);

		Assert.False(result.HasSelection);
		Assert.Equal(SelectionStatus.NoSolution, result.Status);
		Assert.Equal(1, result.VisitedNodes);
	}

	[Fact]
	public void EqualValuesRetainTheSelectedItemIdentity()
	{
		object expensiveItem = new();
		object cheapItem = new();
		SelectionCandidate<object>[] candidates = [new(expensiveItem, 10, 5), new(cheapItem, 10, 1)];

		SelectionResult<object> result = CoinSelector.Select(candidates, 10, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Same(cheapItem, Assert.Single(result.SelectedCandidates).Item);
		Assert.Same(candidates[1], result.SelectedCandidates[0]);
		Assert.Equal(1, result.TotalCost);
	}

	[Fact]
	public void FullyTiedCandidatesPreferOriginalPosition()
	{
		SelectionCandidate<string>[] candidates = [new("first", 10, 1), new("second", 10, 1)];

		SelectionResult<string> result = CoinSelector.Select(candidates, 10, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal("first", Assert.Single(result.SelectedCandidates).Item);
	}

	[Fact]
	public void ResultsPreserveOriginalOrderAndDoNotModifyInput()
	{
		SelectionCandidate<string>[] candidates = [new("small", 3), new("large", 9), new("middle", 5)];
		SelectionCandidate<string>[] original = (SelectionCandidate<string>[])candidates.Clone();

		SelectionResult<string> result = CoinSelector.Select(candidates, 12, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(["small", "large"], result.SelectedCandidates.Select(x => x.Item));
		Assert.Equal(original, candidates);
	}

	[Fact]
	public void ResultCollectionIsImmutableAndIndependentOfCallerList()
	{
		var candidates = new List<SelectionCandidate<string>> { new("selected", 10) };

		SelectionResult<string> result = CoinSelector.Select(candidates, 10, cancellationToken: TestContext.Current.CancellationToken);
		candidates.Clear();

		Assert.Equal("selected", Assert.Single(result.SelectedCandidates).Item);
		var collection = Assert.IsAssignableFrom<IList<SelectionCandidate<string>>>(result.SelectedCandidates);
		Assert.Throws<NotSupportedException>(() => collection[0] = new("replacement", 1));
	}

	[Theory]
	[InlineData(SelectionMode.AtLeastTarget, 11)]
	[InlineData(SelectionMode.AtMostTarget, 9)]
	[InlineData(SelectionMode.Closest, 11)]
	public void ToleranceIsInclusiveAndUsesAbsoluteUnits(SelectionMode mode, long value)
	{
		SelectionCandidate<int>[] candidates = [new(0, value)];

		Assert.True(CoinSelector.Select(candidates, 10, new() { Mode = mode, MaximumDifference = 1 }, cancellationToken: TestContext.Current.CancellationToken).HasSelection);
		SelectionResult<int> excluded = CoinSelector.Select(candidates, 10, new() { Mode = mode, MaximumDifference = 0 }, cancellationToken: TestContext.Current.CancellationToken);
		Assert.Equal(SelectionStatus.NoSolution, excluded.Status);
		Assert.False(excluded.HasSelection);
	}

	[Fact]
	public void GroupCountsCannotExceedUnderlyingInputLimit()
	{
		SelectionCandidate<string>[] candidates = [new("oversized group", 10, InputCount: 3), new("single", 11)];

		SelectionResult<string> result = CoinSelector.Select(candidates, 10, new() { MaximumInputCount = 2 }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal("single", Assert.Single(result.SelectedCandidates).Item);
		Assert.Equal(1, result.InputCount);
	}

	[Fact]
	public void SumOfUnderlyingInputCountsCannotExceedLimit()
	{
		SelectionCandidate<int>[] candidates = [new(0, 6, InputCount: 2), new(1, 4, InputCount: 2)];

		Assert.Equal(SelectionStatus.NoSolution, CoinSelector.Select(candidates, 10, new() { MaximumInputCount = 3 }, cancellationToken: TestContext.Current.CancellationToken).Status);
		Assert.Equal(4, CoinSelector.Select(candidates, 10, new() { MaximumInputCount = 4 }, cancellationToken: TestContext.Current.CancellationToken).InputCount);
	}

	[Fact]
	public void LargeInputCountsDoNotOverflow()
	{
		SelectionCandidate<int>[] candidates = [new(0, 6, InputCount: int.MaxValue), new(1, 4)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 10, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.NoSolution, result.Status);
	}

	[Fact]
	public void BudgetCanEndBeforeAnySelectionIsFound()
	{
		SelectionCandidate<int>[] candidates = [new(0, 10)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 10, new() { MaximumSearchNodes = 1 }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.SearchLimitReached, result.Status);
		Assert.Equal(1, result.VisitedNodes);
		Assert.False(result.HasSelection);
		Assert.False(result.IsOptimal);
	}

	[Fact]
	public void InsufficientValueCanProveNoSolutionWithinSingleNodeBudget()
	{
		SelectionCandidate<int>[] candidates = [new(0, 9)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 10, new() { MaximumSearchNodes = 1 }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.NoSolution, result.Status);
		Assert.Equal(1, result.VisitedNodes);
		Assert.False(result.HasSelection);
	}

	[Fact]
	public void BudgetReturnsBestValidSelectionWithoutClaimingOptimality()
	{
		SelectionCandidate<int>[] candidates = [new(0, 11, 1), new(1, 10, 2)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 10, new() { MaximumSearchNodes = 2 }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.SearchLimitReached, result.Status);
		Assert.Equal(2, result.VisitedNodes);
		Assert.True(result.HasSelection);
		Assert.False(result.IsOptimal);
		Assert.Equal(11, result.TotalValue);
		Assert.Equal(1, result.TotalCost);
	}

	[Fact]
	public void UnbeatableSelectionProvesOptimalityAtTheBudgetBoundary()
	{
		SelectionCandidate<int>[] candidates = [new(0, 10)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 10, new() { MaximumSearchNodes = 2 }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(2, result.VisitedNodes);
	}

	[Fact]
	public void PreCancellationReturnsNoSelectionWithoutVisitingNodes()
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		SelectionCandidate<int>[] candidates = [new(0, 10)];

		SelectionResult<int> result = CoinSelector.Select(candidates, 10, cancellationToken: cancellation.Token);

		Assert.Equal(SelectionStatus.Cancelled, result.Status);
		Assert.Equal(0, result.VisitedNodes);
		Assert.False(result.HasSelection);
		Assert.False(result.IsOptimal);
		Assert.Equal(0, result.TotalValue);
		Assert.Equal(0, result.TotalCost);
		Assert.Equal(0, result.InputCount);
	}

	[Fact]
	public void CancellationDoesNotBypassValidation()
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		SelectionCandidate<int>[] candidates = [new(0, 0)];

		Assert.Throws<ArgumentException>(() => CoinSelector.Select(candidates, 10, cancellationToken: cancellation.Token));
	}

	[Fact]
	public void EmptyInputProvesNoSolutionAfterVisitingRoot()
	{
		SelectionResult<int> result = CoinSelector.Select<int>([], 1, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.NoSolution, result.Status);
		Assert.Equal(1, result.VisitedNodes);
		Assert.False(result.HasSelection);
	}

	[Fact]
	public void DeepSearchDoesNotUseTheCallStack()
	{
		SelectionCandidate<int>[] candidates = Enumerable.Range(0, 30_000).Select(i => new SelectionCandidate<int>(i, 2)).ToArray();

		SelectionResult<int> result = CoinSelector.Select(candidates, 1, new() { MaximumDifference = 0 }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.NoSolution, result.Status);
		Assert.Equal(30_001, result.VisitedNodes);
	}

	[Fact]
	public void HugeToleranceAndTargetDoNotOverflowBounds()
	{
		SelectionCandidate<int>[] candidates = [new(0, long.MaxValue, long.MaxValue)];

		SelectionResult<int> result = CoinSelector.Select(candidates, long.MaxValue, new() { Mode = SelectionMode.Closest }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(long.MaxValue, result.TotalValue);
		Assert.Equal(long.MaxValue, result.TotalCost);
	}

	[Fact]
	public void RejectsAggregateValueOverflow()
	{
		SelectionCandidate<int>[] candidates = [new(0, long.MaxValue), new(1, 1)];

		Assert.Throws<OverflowException>(() => CoinSelector.Select(candidates, 1, cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void RejectsAggregateCostOverflow()
	{
		SelectionCandidate<int>[] candidates = [new(0, 1, long.MaxValue), new(1, 1, 1)];

		Assert.Throws<OverflowException>(() => CoinSelector.Select(candidates, 1, cancellationToken: TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void RejectsNonpositiveTarget(long target)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.Select<int>([], target, cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void RejectsNullListAndNullCandidate()
	{
		Assert.Throws<ArgumentNullException>(() => CoinSelector.Select<int>(null!, 1, cancellationToken: TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentException>(() => CoinSelector.Select<int>([null!], 1, cancellationToken: TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData(0, 0, 1)]
	[InlineData(-1, 0, 1)]
	[InlineData(1, -1, 1)]
	[InlineData(1, 0, 0)]
	[InlineData(1, 0, -1)]
	public void RejectsInvalidCandidate(long value, long cost, int inputCount)
	{
		SelectionCandidate<int>[] candidates = [new(0, value, cost, inputCount)];

		Assert.Throws<ArgumentException>(() => CoinSelector.Select(candidates, 1, cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void RejectsInvalidOptions()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.Select<int>([], 1, new() { MinimumValue = 0 }, cancellationToken: TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.Select<int>([], 1, new() { MinimumValue = -1 }, cancellationToken: TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.Select<int>([], 1, new() { Mode = (SelectionMode)99 }, cancellationToken: TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.Select<int>([], 1, new() { MaximumDifference = -1 }, cancellationToken: TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.Select<int>([], 1, new() { MaximumInputCount = 0 }, cancellationToken: TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.Select<int>([], 1, new() { MaximumInputCount = -1 }, cancellationToken: TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.Select<int>([], 1, new() { MaximumSearchNodes = 0 }, cancellationToken: TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentOutOfRangeException>(() => CoinSelector.Select<int>([], 1, new() { MaximumSearchNodes = -1 }, cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void BudgetedSearchIsDeterministic()
	{
		SelectionCandidate<int>[] candidates = Enumerable.Range(0, 30)
			.Select(i => new SelectionCandidate<int>(i, 19 + i % 7, i % 3, 1 + i % 2)).ToArray();
		SelectionOptions options = new() { Mode = SelectionMode.Closest, MaximumSearchNodes = 300 };

		SelectionResult<int> first = CoinSelector.Select(candidates, 153, options, cancellationToken: TestContext.Current.CancellationToken);
		SelectionResult<int> second = CoinSelector.Select(candidates, 153, options, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(first.Status, second.Status);
		Assert.Equal(first.VisitedNodes, second.VisitedNodes);
		Assert.Equal(first.SelectedCandidates, second.SelectedCandidates);
	}

	[Fact]
	public void SmallRandomInputsMatchExhaustiveOracleInAllModes()
	{
		Random random = new(84_201);
		for (int sample = 0; sample < 180; sample++)
		{
			int length = random.Next(0, 11);
			SelectionCandidate<int>[] candidates = Enumerable.Range(0, length)
				.Select(i => new SelectionCandidate<int>(i, random.Next(1, 25), random.Next(0, 8), random.Next(1, 4))).ToArray();
			long target = random.Next(1, 100);
			foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
			{
				SelectionOptions options = new()
				{
					Mode = mode,
					MinimumValue = sample % 2 == 0 ? 1 : random.Next(1, 110),
					MaximumDifference = sample % 3 == 0 ? long.MaxValue : random.Next(0, 30),
					MaximumInputCount = random.Next(1, 15),
					MaximumSearchNodes = 100_000
				};

				SelectionResult<int> actual = CoinSelector.Select(candidates, target, options, cancellationToken: TestContext.Current.CancellationToken);
				OracleResult? expected = ExhaustiveOracle(candidates, target, options);
				if (expected is null)
				{
					Assert.Equal(SelectionStatus.NoSolution, actual.Status);
					Assert.False(actual.HasSelection);
					continue;
				}

				Assert.Equal(SelectionStatus.Optimal, actual.Status);
				Assert.Equal(expected.Value, actual.TotalValue);
				Assert.Equal(expected.Cost, actual.TotalCost);
				Assert.Equal(expected.InputCount, actual.InputCount);
				Assert.Equal(expected.OriginalIndices, actual.SelectedCandidates.Select(x => x.Item));
				Assert.Equal(actual.TotalValue, actual.SelectedCandidates.Sum(x => x.Value));
				Assert.Equal(actual.TotalCost, actual.SelectedCandidates.Sum(x => x.Cost));
				Assert.Equal(actual.InputCount, actual.SelectedCandidates.Sum(x => x.InputCount));
			}
		}
	}

	private static OracleResult? ExhaustiveOracle(SelectionCandidate<int>[] candidates, long target, SelectionOptions options)
	{
		var ordered = candidates.Select((candidate, index) => (Candidate: candidate, Index: index))
			.OrderByDescending(x => x.Candidate.Value).ThenBy(x => x.Candidate.Cost)
			.ThenBy(x => x.Candidate.InputCount).ThenBy(x => x.Index).ToArray();
		OracleResult? best = null;
		for (int mask = (1 << ordered.Length) - 1; mask >= 1; mask--)
		{
			long value = 0;
			long cost = 0;
			int count = 0;
			var indices = new List<int>();
			for (int i = 0; i < ordered.Length; i++)
			{
				if ((mask & (1 << (ordered.Length - 1 - i))) != 0)
				{
					value += ordered[i].Candidate.Value;
					cost += ordered[i].Candidate.Cost;
					count += ordered[i].Candidate.InputCount;
					indices.Add(ordered[i].Index);
				}
			}

			long difference = Math.Abs(value - target);
			if (value < options.MinimumValue || count > options.MaximumInputCount || difference > options.MaximumDifference ||
				(options.Mode == SelectionMode.AtLeastTarget && value < target) ||
				(options.Mode == SelectionMode.AtMostTarget && value > target))
			{
				continue;
			}

			var rank = (difference, cost, count, options.Mode == SelectionMode.Closest && value < target ? 1 : 0);
			if (best is null || rank.CompareTo(best.Rank) < 0)
			{
				indices.Sort();
				best = new OracleResult(value, cost, count, rank, indices.ToArray());
			}
		}

		return best;
	}

	private sealed record OracleResult(long Value, long Cost, int InputCount,
		(long Difference, long Cost, int InputCount, int Side) Rank, int[] OriginalIndices);
}
