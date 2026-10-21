using NBitcoin;
using Xunit;

namespace CoinSelection.NBitcoin.Tests;

public class GroupCountLimitTests
{
	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp, 11)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 7)]
	public void OneScriptGroupMaySpendThreeInputsWithinIndependentLimits(BitcoinFeePolicy policy, long fee)
	{
		BitcoinInput[] inputs = [Input(1, 101, 1, 9), Input(2, 201, 2, 9), Input(3, 301, 3, 9)];
		SelectionOptions options = new() { MaximumCandidateCount = 1, MaximumInputCount = 3, MaximumDifference = 0 };
		foreach (BitcoinSelectionResult result in EveryDirection(inputs, 603 - fee, policy, options))
		{
			AssertSelection(result, 603 - fee, fee, inputs);
		}

		foreach (BitcoinSelectionResult result in EveryDirection(inputs, 603 - fee, policy, options with { MaximumInputCount = 2 }))
		{
			AssertNoSelection(result);
		}
	}

	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp, 11)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 7)]
	public void AnExactPaymentUsingTwoScriptsNeedsTwoCandidatesEvenWithThreeInputSlots(BitcoinFeePolicy policy, long fee)
	{
		BitcoinInput[] inputs = [Input(1, 101, 1, 9), Input(2, 201, 2, 8), Input(3, 301, 3, 9)];
		SelectionOptions options = new() { MaximumCandidateCount = 1, MaximumInputCount = 3, MaximumDifference = 0 };
		foreach (BitcoinSelectionResult result in EveryDirection(inputs, 603 - fee, policy, options))
		{
			AssertNoSelection(result);
		}

		foreach (BitcoinSelectionResult result in EveryDirection(inputs, 603 - fee, policy, options with { MaximumCandidateCount = 2 }))
		{
			AssertSelection(result, 603 - fee, fee, inputs);
		}
	}

	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp, 11)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 7)]
	public void DisablingGroupingCountsEachUtxoAsOneCandidate(BitcoinFeePolicy policy, long fee)
	{
		BitcoinInput[] inputs = [Input(1, 101, 1, 9), Input(2, 201, 2, 9), Input(3, 301, 3, 9)];
		SelectionOptions options = new() { MaximumCandidateCount = 1, MaximumInputCount = 3, MaximumDifference = 0 };
		foreach (BitcoinSelectionResult result in EveryDirection(inputs, 603 - fee, policy, options, groupByScript: false))
		{
			AssertNoSelection(result);
		}
		long oneInputFee = policy == BitcoinFeePolicy.RoundUp ? 5 : 3;
		foreach (BitcoinSelectionResult result in EveryDirection(inputs, 201 - oneInputFee, policy, options, groupByScript: false))
		{
			AssertSelection(result, 201 - oneInputFee, oneInputFee, inputs[1]);
		}

		foreach (BitcoinSelectionResult result in EveryDirection(inputs, 603 - fee, policy,
			options with { MaximumCandidateCount = 3 }, groupByScript: false))
		{
			AssertSelection(result, 603 - fee, fee, inputs);
		}
	}

	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp, 592, 11, 611, 4)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 596, 7, 613, 2)]
	public void ClosestAndBothApplyInclusivePaymentBoundsAfterBothIndependentCountLimits(
		BitcoinFeePolicy policy, long belowPayment, long belowFee, long abovePayment, long aboveFee)
	{
		BitcoinInput first = Input(1, 101, 1, 9);
		BitcoinInput alternative = Input(2, 615, 1, 8);
		BitcoinInput second = Input(3, 201, 2, 9);
		BitcoinInput third = Input(4, 301, 3, 9);
		BitcoinInput[] inputs = [first, alternative, second, third];
		SelectionOptions options = new()
		{
			Mode = SelectionMode.Closest,
			MaximumCandidateCount = 1,
			MaximumInputCount = 3,
			MaximumShortfall = 600 - belowPayment,
			MaximumExcess = abovePayment - 600
		};
		BitcoinSelectionResult closest = Select(inputs, 600, policy, options);
		BitcoinTwoSidedSelectionResult both = SelectBoth(inputs, 600, policy, options);
		AssertSelection(closest, belowPayment, belowFee, first, second, third);
		AssertSelection(both.AtMostTarget, belowPayment, belowFee, first, second, third);
		AssertSelection(both.AtLeastTarget, abovePayment, aboveFee, alternative);
		Assert.Equal(both.AtMostTarget.VisitedNodes, both.AtLeastTarget.VisitedNodes);

		SelectionOptions onlySmallerInputSet = options with { MaximumInputCount = 2 };
		AssertSelection(Select(inputs, 600, policy, onlySmallerInputSet), abovePayment, aboveFee, alternative);
		BitcoinTwoSidedSelectionResult smallerBoth = SelectBoth(inputs, 600, policy, onlySmallerInputSet);
		AssertNoSelection(smallerBoth.AtMostTarget);
		AssertSelection(smallerBoth.AtLeastTarget, abovePayment, aboveFee, alternative);

		foreach (BitcoinSelectionResult result in EveryDirection(inputs, 600, policy,
			onlySmallerInputSet with { MaximumExcess = options.MaximumExcess - 1 }))
		{
			AssertNoSelection(result);
		}
	}

	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp, 11)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 7)]
	public void AnUneconomicalMemberStillUsesAnInputSlotInsideOnePositiveScriptGroup(BitcoinFeePolicy policy, long fee)
	{
		BitcoinInput uneconomical = Input(1, 1, 3, 9);
		BitcoinInput[] inputs = [uneconomical, Input(2, 101, 1, 9), Input(3, 201, 2, 9)];
		SelectionOptions options = new() { MaximumCandidateCount = 1, MaximumInputCount = 3, MaximumDifference = 0 };
		foreach (BitcoinSelectionResult result in EveryDirection(inputs, 303 - fee, policy, options))
		{
			AssertSelection(result, 303 - fee, fee, inputs);
		}
		foreach (BitcoinSelectionResult result in EveryDirection(inputs, 303 - fee, policy, options with { MaximumInputCount = 2 }))
		{
			AssertNoSelection(result);
		}
	}

	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp)]
	[InlineData(BitcoinFeePolicy.NBitcoin)]
	public void SmallWalletsMatchIndependentUtxoSubsetEnumeration(BitcoinFeePolicy policy)
	{
		Random random = new(74_619);
		for (int sample = 0; sample < 24; sample++)
		{
			BitcoinInput[] inputs = Enumerable.Range(0, 2 + sample % 6)
				.Select(index => Input(sample * 10 + index + 1, random.Next(0, 25), random.Next(1, 5), random.Next(1, 4))).ToArray();
			long target = 8 + sample % 35;
			SelectionOptions options = new()
			{
				MaximumCandidateCount = 1 + sample % 3,
				MaximumInputCount = 1 + sample % inputs.Length,
				MinimumValue = sample % 7 == 0 ? 7 : 1,
				MaximumShortfall = sample % 5 == 0 ? 0 : 1 + sample % 11,
				MaximumExcess = sample % 4 == 0 ? 0 : 2 + sample % 13,
				MaximumDifference = sample % 6 == 0 ? 0 : long.MaxValue
			};
			foreach (bool grouping in new[] { false, true })
			{
				foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
				{
					SelectionOptions direction = options with { Mode = mode };
					AssertMatchesOracle(inputs, target, policy, direction, grouping,
						Select(inputs, target, policy, direction, grouping));
				}
				BitcoinTwoSidedSelectionResult both = SelectBoth(inputs, target, policy, options, grouping);
				AssertMatchesOracle(inputs, target, policy, options with { Mode = SelectionMode.AtMostTarget }, grouping, both.AtMostTarget);
				AssertMatchesOracle(inputs, target, policy, options with { Mode = SelectionMode.AtLeastTarget }, grouping, both.AtLeastTarget);
			}
		}
	}

	private static IEnumerable<BitcoinSelectionResult> EveryDirection(
		BitcoinInput[] inputs, long target, BitcoinFeePolicy policy, SelectionOptions options, bool groupByScript = true)
	{
		foreach (SelectionMode mode in Enum.GetValues<SelectionMode>())
		{
			yield return Select(inputs, target, policy, options with { Mode = mode }, groupByScript);
		}
		BitcoinTwoSidedSelectionResult both = SelectBoth(inputs, target, policy, options, groupByScript);
		yield return both.AtMostTarget;
		yield return both.AtLeastTarget;
	}

	private static BitcoinSelectionResult Select(BitcoinInput[] inputs, long target, BitcoinFeePolicy policy,
		SelectionOptions options, bool grouping = true) => BitcoinCoinSelector.Select(inputs, Money.Satoshis(target),
			new FeeRate(1.2m), 1, policy, options, grouping, TestContext.Current.CancellationToken);

	private static BitcoinTwoSidedSelectionResult SelectBoth(BitcoinInput[] inputs, long target, BitcoinFeePolicy policy,
		SelectionOptions options, bool grouping = true) => BitcoinCoinSelector.SelectBoth(inputs, Money.Satoshis(target),
			new FeeRate(1.2m), 1, policy, options, grouping, TestContext.Current.CancellationToken);

	private static void AssertSelection(BitcoinSelectionResult result, long payment, long fee, params BitcoinInput[] expected)
	{
		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.True(result.HasSelection);
		Assert.True(result.IsOptimal);
		Assert.Equal(payment, result.PaymentAmount.Satoshi);
		Assert.Equal(fee, result.EstimatedFee.Satoshi);
		Assert.Equal(payment + fee, result.TotalAmount.Satoshi);
		Assert.Equal(expected.Length, result.InputCount);
		Assert.Equal(expected.Length, result.SelectedInputs.Count);
		for (int index = 0; index < expected.Length; index++)
		{
			Assert.Same(expected[index], result.SelectedInputs[index]);
			Assert.Same(expected[index].Coin, result.SelectedInputs[index].Coin);
		}
	}

	private static void AssertNoSelection(BitcoinSelectionResult result)
	{
		Assert.Equal(SelectionStatus.NoSolution, result.Status);
		Assert.False(result.HasSelection);
		Assert.Empty(result.SelectedInputs);
		Assert.Equal(0, result.InputCount);
		Assert.Equal(0, result.TotalAmount.Satoshi);
		Assert.Equal(0, result.EstimatedFee.Satoshi);
		Assert.Equal(0, result.PaymentAmount.Satoshi);
	}

	private static void AssertMatchesOracle(BitcoinInput[] inputs, long target, BitcoinFeePolicy policy,
		SelectionOptions options, bool grouping, BitcoinSelectionResult actual)
	{
		string[] scripts = inputs.Select(input => ScriptKey(input)).ToArray();
		long[] amounts = inputs.Select(input => ((Money)input.Coin.Amount).Satoshi).ToArray();
		long[] inputFees = inputs.Select(input => Fee(input.InputVirtualSize, policy)).ToArray();
		long fixedFee = Fee(1, policy);
		(long Difference, long Fee, int Inputs, int Side)? bestRank = null;
		long bestPayment = 0;
		for (int mask = 1; mask < 1 << inputs.Length; mask++)
		{
			int[] selected = Enumerable.Range(0, inputs.Length).Where(index => (mask & (1 << index)) != 0).ToArray();
			if (selected.Length > options.MaximumInputCount || (grouping
				? selected.Select(index => scripts[index]).Distinct().Count()
				: selected.Length) > options.MaximumCandidateCount)
			{
				continue;
			}
			if (grouping && selected.Any(index => Enumerable.Range(0, inputs.Length)
				.Any(other => scripts[other] == scripts[index] && (mask & (1 << other)) == 0)))
			{
				continue;
			}
			bool nonpositiveCandidate = grouping
				? selected.GroupBy(index => scripts[index]).Any(group => group.Sum(index => amounts[index] - inputFees[index]) <= 0)
				: selected.Any(index => amounts[index] - inputFees[index] <= 0);
			if (nonpositiveCandidate)
			{
				continue;
			}
			long effective = selected.Sum(index => amounts[index] - inputFees[index]);
			long payment = effective - fixedFee;
			long shortfall = Math.Max(0, target - payment);
			long excess = Math.Max(0, payment - target);
			if (effective < options.MinimumValue || payment <= 0 || shortfall > options.MaximumShortfall || excess > options.MaximumExcess ||
				shortfall + excess > options.MaximumDifference || (options.Mode == SelectionMode.AtMostTarget && excess > 0) ||
				(options.Mode == SelectionMode.AtLeastTarget && shortfall > 0))
			{
				continue;
			}
			var rank = (shortfall + excess, selected.Sum(index => inputFees[index]) + fixedFee, selected.Length,
				options.Mode == SelectionMode.Closest && shortfall > 0 ? 1 : 0);
			if (bestRank is null || rank.CompareTo(bestRank.Value) < 0)
			{
				bestRank = rank;
				bestPayment = payment;
			}
		}
		if (bestRank is null)
		{
			AssertNoSelection(actual);
			return;
		}
		Assert.Equal(SelectionStatus.Optimal, actual.Status);
		Assert.True(actual.HasSelection);
		Assert.Equal(bestPayment, actual.PaymentAmount.Satoshi);
		Assert.Equal(bestRank.Value.Fee, actual.EstimatedFee.Satoshi);
		Assert.Equal(bestRank.Value.Inputs, actual.InputCount);
		Assert.Equal(bestPayment - target, actual.PaymentDifference.Satoshi);
		Assert.Equal(bestPayment + bestRank.Value.Fee, actual.TotalAmount.Satoshi);
		int[] selectedPositions = actual.SelectedInputs.Select(input => Array.FindIndex(inputs, original => ReferenceEquals(original, input))).ToArray();
		Assert.All(selectedPositions, index => Assert.InRange(index, 0, inputs.Length - 1));
		Assert.Equal(selectedPositions.Length, actual.InputCount);
		Assert.Equal(selectedPositions.Sum(index => amounts[index]), actual.TotalAmount.Satoshi);
		Assert.Equal(fixedFee + selectedPositions.Sum(index => inputFees[index]), actual.EstimatedFee.Satoshi);
		Assert.Equal(selectedPositions.OrderBy(index => index), selectedPositions);
		Assert.Equal(selectedPositions.Length, selectedPositions.Distinct().Count());
		Assert.InRange(grouping ? actual.SelectedInputs.Select(ScriptKey).Distinct().Count() : selectedPositions.Length,
			1, options.MaximumCandidateCount);
		Assert.InRange(selectedPositions.Length, 1, options.MaximumInputCount);
		if (grouping)
		{
			foreach (string script in actual.SelectedInputs.Select(ScriptKey).Distinct())
			{
				Assert.Equal(scripts.Count(original => original == script), actual.SelectedInputs.Count(input => ScriptKey(input) == script));
			}
		}
	}

	private static long Fee(int size, BitcoinFeePolicy policy) => policy == BitcoinFeePolicy.NBitcoin
		? new FeeRate(1.2m).GetFee(size).Satoshi
		: (long)decimal.Ceiling(1.2m * size);

	private static string ScriptKey(BitcoinInput input) => Convert.ToHexString(input.Coin.TxOut.ScriptPubKey.ToBytes());

	private static BitcoinInput Input(int id, long amount, int size, int scriptId)
	{
		byte[] script = new byte[22];
		script[1] = 20;
		BitConverter.GetBytes(scriptId).CopyTo(script, 2);
		return new BitcoinInput(new Coin(uint256.Parse(id.ToString("x64")), 0, Money.Satoshis(amount), new Script(script)), size);
	}
}
