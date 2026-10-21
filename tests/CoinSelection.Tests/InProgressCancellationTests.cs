using Xunit;

namespace CoinSelection.Tests;

public class InProgressCancellationTests
{
	private const long Target = 25_001;
	private const long WarmupNodes = 256;
	[Theory]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.Closest)]
	public async Task CancellingARunningSingleSearchPreservesItsIncumbent(SelectionMode mode)
	{
		SelectionCandidate<int>[] candidates = HardCandidates();
		SelectionOptions options = Options() with { Mode = mode };
		SelectionResult<int>[] results = await CancelRunningSearch(candidates, options, both: false);

		AssertRetainedSelection(candidates, options, mode, results[0]);
		AssertSameSelection(results[0], Run(candidates,
			options with { MaximumSearchNodes = results[0].VisitedNodes }, both: false, TestContext.Current.CancellationToken)[0]);
	}

	[Fact]
	public async Task CancellingARunningSharedSearchPreservesBothIndependentIncumbents()
	{
		SelectionCandidate<int>[] candidates = HardCandidates();
		SelectionOptions options = Options();
		SelectionResult<int>[] results = await CancelRunningSearch(candidates, options, both: true);

		Assert.Equal(results[0].VisitedNodes, results[1].VisitedNodes);
		AssertRetainedSelection(candidates, options, SelectionMode.AtMostTarget, results[0]);
		AssertRetainedSelection(candidates, options, SelectionMode.AtLeastTarget, results[1]);
		SelectionResult<int>[] replay = Run(candidates,
			options with { MaximumSearchNodes = results[0].VisitedNodes }, both: true, TestContext.Current.CancellationToken);
		AssertSameSelection(results[0], replay[0]);
		AssertSameSelection(results[1], replay[1]);
	}

	private static SelectionOptions Options() => new()
	{
		MinimumValue = Target - 15,
		MaximumDifference = 23,
		MaximumShortfall = 17,
		MaximumExcess = 29,
		MaximumCandidateCount = 15,
		MaximumInputCount = 36,
		// A second failsafe in case cancellation itself regresses.
		MaximumSearchNodes = 100_000_000
	};

	private static SelectionCandidate<int>[] HardCandidates()
	{
		List<SelectionCandidate<int>> candidates = [new(0, Target - 1, 1_000, 3)];
		for (int index = 0; index < 80; index++)
		{
			candidates.Add(new(index % 7, 2 * (101 + index * 73 % 997), 1 + index * 17 % 29, 2 + index % 3));
		}
		candidates.Insert(37, new(0, Target + 1, 1_000, 3));
		// Eligible values are even, so neither side can reach the odd target. The
		// excluded odd value makes the global GCD one and prevents an early proof.
		candidates.Add(new(0, 1_000_001));
		return candidates.ToArray();
	}

	private static async Task<SelectionResult<int>[]> CancelRunningSearch(
		SelectionCandidate<int>[] candidates, SelectionOptions options, bool both)
	{
		List<string> attempts = [];
		foreach (int delayMilliseconds in new[] { 1, 4, 16, 64, 256 })
		{
			using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
			TaskCompletionSource<SelectionResult<int>[]> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
			Task<SelectionResult<int>[]> worker = Task.Factory.StartNew(() =>
			{
				try
				{
					SelectionResult<int>[] warmup = Run(candidates, options with { MaximumSearchNodes = WarmupNodes }, both,
						cancellation.Token);
					started.SetResult(warmup);
					return Run(candidates, options, both, cancellation.Token);
				}
				catch (Exception error)
				{
					started.TrySetException(error);
					throw;
				}
			}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

			SelectionResult<int>[] warmup;
			SelectionResult<int>[] results;
			try
			{
				// The dedicated thread has already JIT-warmed the same search before
				// signalling. Retrying only compensates for cancellation before enough
				// actual search work; no elapsed-time requirement determines success.
				warmup = await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
				await Task.Delay(delayMilliseconds, TestContext.Current.CancellationToken);
				cancellation.Cancel();
				results = await worker.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
			}
			finally
			{
				cancellation.Cancel();
			}

			for (int side = 0; side < warmup.Length; side++)
			{
				Assert.Equal(SelectionStatus.SearchLimitReached, warmup[side].Status);
				Assert.Equal(WarmupNodes, warmup[side].VisitedNodes);
				AssertSeedRank(warmup[side], both ? (side == 0 ? SelectionMode.AtMostTarget : SelectionMode.AtLeastTarget) : options.Mode);
			}

			attempts.Add($"{delayMilliseconds} ms: {results[0].Status}, {results[0].VisitedNodes} nodes");
			Assert.All(results, result => Assert.Equal(SelectionStatus.Cancelled, result.Status));
			if (results[0].VisitedNodes >= WarmupNodes)
			{
				// Once this prefix has been visited, missing incumbents are a failure,
				// not a reason to retry until a favourable cancellation happens.
				Assert.All(results, result => Assert.True(result.HasSelection));
				return results;
			}
		}

		Assert.Fail("Cancellation never reached the warmed search prefix: " + string.Join("; ", attempts));
		return [];
	}

	private static SelectionResult<int>[] Run(SelectionCandidate<int>[] candidates, SelectionOptions options,
		bool both, CancellationToken cancellationToken)
	{
		if (!both)
		{
			return [CoinSelector.Select(candidates, Target, options, cancellationToken)];
		}
		TwoSidedSelectionResult<int> result = CoinSelector.SelectBoth(candidates, Target, options, cancellationToken);
		Assert.Equal(result.VisitedNodes, result.AtLeastTarget.VisitedNodes);
		return [result.AtMostTarget, result.AtLeastTarget];
	}

	private static void AssertRetainedSelection(SelectionCandidate<int>[] candidates, SelectionOptions options,
		SelectionMode mode, SelectionResult<int> result)
	{
		Assert.Equal(SelectionStatus.Cancelled, result.Status);
		Assert.False(result.IsOptimal);
		Assert.True(result.HasSelection);
		Assert.InRange(result.VisitedNodes, WarmupNodes, options.MaximumSearchNodes - 1);
		Assert.InRange(result.SelectedCandidates.Count, 1, options.MaximumCandidateCount);

		long value = 0, cost = 0, inputs = 0;
		int previousIndex = -1;
		foreach (SelectionCandidate<int> selected in result.SelectedCandidates)
		{
			int originalIndex = Array.FindIndex(candidates, previousIndex + 1, candidate => ReferenceEquals(candidate, selected));
			Assert.True(originalIndex > previousIndex, "Every selected candidate must retain its original reference and input order.");
			previousIndex = originalIndex;
			value += selected.Value;
			cost += selected.Cost;
			inputs += selected.InputCount;
		}
		Assert.Equal(value, result.TotalValue);
		Assert.Equal(cost, result.TotalCost);
		Assert.Equal(inputs, result.InputCount);
		Assert.InRange(inputs, 1, options.MaximumInputCount);
		Assert.True(inputs > result.SelectedCandidates.Count, "The fixture must exercise grouped underlying inputs.");
		Assert.True(value >= options.MinimumValue);
		Assert.InRange(Math.Abs(value - Target), 0, options.MaximumDifference);
		if (value < Target)
		{
			Assert.NotEqual(SelectionMode.AtLeastTarget, mode);
			Assert.InRange(Target - value, 0, options.MaximumShortfall);
		}
		else
		{
			Assert.NotEqual(SelectionMode.AtMostTarget, mode);
			Assert.InRange(value - Target, 0, options.MaximumExcess);
		}
		AssertSeedRank(result, mode);
	}

	private static void AssertSeedRank(SelectionResult<int> result, SelectionMode mode)
	{
		// The first feasible large candidate on each permitted side is 25,000 or
		// 25,002, cost 1,000 and three inputs. A retained incumbent cannot rank worse.
		(long distance, long cost, int inputs, int side) rank =
			(Math.Abs(result.TotalValue - Target), result.TotalCost, result.InputCount,
			 mode == SelectionMode.Closest && result.TotalValue < Target ? 1 : 0);
		Assert.True(result.HasSelection);
		Assert.True(rank.CompareTo((1L, 1_000L, 3, 0)) <= 0, $"The retained rank {rank} is worse than the known early selection.");
	}

	private static void AssertSameSelection(SelectionResult<int> cancelled, SelectionResult<int> replay)
	{
		// Node-limited replay cannot observe cancellation. Identical references at
		// the same deterministic prefix verify that cancellation preserved its best.
		Assert.Equal(cancelled.VisitedNodes, replay.VisitedNodes);
		Assert.Equal(cancelled.TotalValue, replay.TotalValue);
		Assert.Equal(cancelled.TotalCost, replay.TotalCost);
		Assert.Equal(cancelled.InputCount, replay.InputCount);
		Assert.Equal(cancelled.SelectedCandidates.Count, replay.SelectedCandidates.Count);
		for (int index = 0; index < cancelled.SelectedCandidates.Count; index++)
		{
			Assert.Same(cancelled.SelectedCandidates[index], replay.SelectedCandidates[index]);
		}
	}
}
