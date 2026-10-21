using NBitcoin;
using Xunit;

namespace CoinSelection.NBitcoin.Tests;

public class FeeCalculationPolicyTests
{
	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp, 1_200, 68, 19, 105)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 1_200, 68, 19, 103)]
	[InlineData(BitcoinFeePolicy.RoundUp, 100, 19, 19, 4)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 100, 19, 19, 2)]
	[InlineData(BitcoinFeePolicy.RoundUp, 100, 1, 0, 1)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 100, 1, 0, 2)]
	[InlineData(BitcoinFeePolicy.RoundUp, 0, 68, 0, 0)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 0, 68, 0, 2)]
	[InlineData(BitcoinFeePolicy.RoundUp, 0, 68, 200, 0)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 0, 68, 200, 2)]
	public void KnownFeesIncludeThePoliciesDistinctRoundingAndMinimums(
		BitcoinFeePolicy policy, long feePerK, int inputSize, int fixedSize, long expectedFee)
	{
		BitcoinInput input = Input(1, 1_000 + expectedFee, inputSize);
		FeeRate rate = new(Money.Satoshis(feePerK));
		SelectionOptions options = new() { MaximumDifference = 0 };

		BitcoinSelectionResult single = Select(policy, [input], 1_000, rate, fixedSize, options);
		BitcoinTwoSidedSelectionResult both = SelectBoth(policy, [input], 1_000, rate, fixedSize, options);

		foreach (BitcoinSelectionResult result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			AssertPayment(result, 1_000, expectedFee, input);
		}
	}

	[Fact]
	public void ExistingCallsKeepRoundUpAndTheirOriginalOptionalArguments()
	{
		BitcoinInput[] inputs = [Input(1, 51, 19), Input(2, 54, 1)];
		Money payment = Money.Satoshis(100);
		FeeRate rate = new(0.1m);
		SelectionOptions options = new() { MaximumDifference = 0 };
		CancellationToken token = TestContext.Current.CancellationToken;

		// Both the old complete positional call and its default-literal options remain valid.
		BitcoinSelectionResult legacy = BitcoinCoinSelector.Select(inputs, payment, rate, 19, options, true, token);
		BitcoinSelectionResult defaults = BitcoinCoinSelector.Select(inputs, payment, rate, 19, default, cancellationToken: token);
		BitcoinSelectionResult explicitPolicy = Select(BitcoinFeePolicy.RoundUp, inputs, 100, rate, 19, options);
		BitcoinTwoSidedSelectionResult legacyBoth = BitcoinCoinSelector.SelectBoth(inputs, payment, rate, 19, options, true, token);
		BitcoinTwoSidedSelectionResult defaultsBoth = BitcoinCoinSelector.SelectBoth(inputs, payment, rate, 19, default, cancellationToken: token);
		BitcoinTwoSidedSelectionResult explicitBoth = SelectBoth(BitcoinFeePolicy.RoundUp, inputs, 100, rate, 19, options);

		foreach (BitcoinSelectionResult result in new[]
		{
			legacy, defaults, explicitPolicy, legacyBoth.AtMostTarget, legacyBoth.AtLeastTarget,
			defaultsBoth.AtMostTarget, defaultsBoth.AtLeastTarget, explicitBoth.AtMostTarget, explicitBoth.AtLeastTarget
		})
		{
			AssertPayment(result, 100, 5, inputs);
		}
		Assert.Equal(legacy.VisitedNodes, explicitPolicy.VisitedNodes);
		Assert.Equal(legacyBoth.VisitedNodes, explicitBoth.VisitedNodes);
	}

	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp, 0, 105)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 1, 103)]
	public void ThePolicyCanChangeWhichOriginalCoinMakesAnExactPayment(
		BitcoinFeePolicy policy, int expectedIndex, long expectedFee)
	{
		BitcoinInput[] inputs = [Input(1, 1_105, 68), Input(2, 1_103, 68)];
		SelectionOptions options = new() { MaximumDifference = 0, MaximumInputCount = 1 };

		BitcoinSelectionResult single = Select(policy, inputs, 1_000, new FeeRate(1.2m), 19, options);
		BitcoinTwoSidedSelectionResult both = SelectBoth(policy, inputs, 1_000, new FeeRate(1.2m), 19, options);

		foreach (BitcoinSelectionResult result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			AssertPayment(result, 1_000, expectedFee, inputs[expectedIndex]);
		}
	}

	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp, 1, 6)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 1, 3)]
	[InlineData(BitcoinFeePolicy.RoundUp, 3, 10)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 3, 7)]
	public void GroupingDoesNotReplacePerInputFeesWithARoundedGroupFee(
		BitcoinFeePolicy policy, int inputSize, long expectedFee)
	{
		BitcoinInput[] inputs = [Input(1, 51, inputSize, 9), Input(2, 52, inputSize, 9)];
		long expectedPayment = 103 - expectedFee;
		SelectionOptions options = new() { MaximumDifference = 0 };

		foreach (bool group in new[] { true, false })
		{
			BitcoinSelectionResult single = Select(policy, inputs, expectedPayment, new FeeRate(1.2m), 1, options, group);
			BitcoinTwoSidedSelectionResult both = SelectBoth(policy, inputs, expectedPayment, new FeeRate(1.2m), 1, options, group);
			foreach (BitcoinSelectionResult result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
			{
				AssertPayment(result, expectedPayment, expectedFee, inputs);
			}
		}
	}

	[Fact]
	public void APolicyCanMakeAnOtherwiseZeroEffectiveScriptGroupSpendable()
	{
		BitcoinInput[] inputs = [Input(1, 2, 19, 9), Input(2, 2, 19, 9)];
		SelectionOptions options = new() { MaximumDifference = 0 };
		BitcoinSelectionResult excluded = Select(BitcoinFeePolicy.RoundUp, inputs, 1, new FeeRate(0.1m), 0, options);
		BitcoinTwoSidedSelectionResult excludedBoth = SelectBoth(BitcoinFeePolicy.RoundUp, inputs, 1, new FeeRate(0.1m), 0, options);

		foreach (BitcoinSelectionResult result in new[] { excluded, excludedBoth.AtMostTarget, excludedBoth.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.NoSolution, result.Status);
			Assert.False(result.HasSelection);
			Assert.Empty(result.SelectedInputs);
			Assert.Equal(0, result.EstimatedFee.Satoshi);
		}

		BitcoinSelectionResult admitted = Select(BitcoinFeePolicy.NBitcoin, inputs, 1, new FeeRate(0.1m), 0, options);
		BitcoinTwoSidedSelectionResult admittedBoth = SelectBoth(BitcoinFeePolicy.NBitcoin, inputs, 1, new FeeRate(0.1m), 0, options);
		foreach (BitcoinSelectionResult result in new[] { admitted, admittedBoth.AtMostTarget, admittedBoth.AtLeastTarget })
		{
			AssertPayment(result, 1, 3, inputs);
		}
	}

	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp, 100, 4)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 101, 3)]
	public void APositiveGroupKeepsUneconomicalMembersAndCountsEveryInput(
		BitcoinFeePolicy policy, long groupedPayment, long groupedFee)
	{
		BitcoinInput[] inputs = [Input(1, 1, 19, 9), Input(2, 103, 19, 9)];
		FeeRate rate = new(0.1m);
		SelectionOptions exact = new() { MaximumDifference = 0 };
		AssertPayment(Select(policy, inputs, groupedPayment, rate, 0, exact), groupedPayment, groupedFee, inputs);
		BitcoinTwoSidedSelectionResult both = SelectBoth(policy, inputs, groupedPayment, rate, 0, exact);
		AssertPayment(both.AtMostTarget, groupedPayment, groupedFee, inputs);
		AssertPayment(both.AtLeastTarget, groupedPayment, groupedFee, inputs);

		SelectionOptions limited = new() { Mode = SelectionMode.Closest, MaximumDifference = 1, MaximumInputCount = 1 };
		Assert.False(Select(policy, inputs, 101, rate, 0, limited).HasSelection);
		BitcoinTwoSidedSelectionResult limitedBoth = SelectBoth(policy, inputs, 101, rate, 0, limited);
		Assert.False(limitedBoth.AtMostTarget.HasSelection);
		Assert.False(limitedBoth.AtLeastTarget.HasSelection);
		AssertPayment(Select(policy, inputs, 101, rate, 0, limited, false), 101, 2, inputs[1]);
	}

	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp, true, 974, 1_047, 44, 153)]
	[InlineData(BitcoinFeePolicy.RoundUp, false, 974, 1_047, 44, 153)]
	[InlineData(BitcoinFeePolicy.NBitcoin, true, 976, 1_049, 42, 151)]
	[InlineData(BitcoinFeePolicy.NBitcoin, false, 976, 1_049, 42, 151)]
	public void BothSidesMatchSeparateCompletedSearchesWithAsymmetricPaymentBounds(
		BitcoinFeePolicy policy, bool group, long belowPayment, long abovePayment, long belowFee, long aboveFee)
	{
		BitcoinInput[] inputs =
		[
			Input(1, 700, 68, 9), Input(2, 399, 69, 8), Input(3, 500, 40, 9),
			Input(4, 1_100, 1, 7), Input(5, 1_018, 17, 6)
		];
		SelectionOptions options = new()
		{
			Mode = SelectionMode.Closest, MaximumShortfall = 30, MaximumExcess = 60,
			MaximumInputCount = 3, MaximumSearchNodes = long.MaxValue
		};
		FeeRate rate = new(1.2m);
		BitcoinTwoSidedSelectionResult both = SelectBoth(policy, inputs, 1_000, rate, 19, options, group);
		BitcoinSelectionResult below = Select(policy, inputs, 1_000, rate, 19,
			options with { Mode = SelectionMode.AtMostTarget }, group);
		BitcoinSelectionResult above = Select(policy, inputs, 1_000, rate, 19,
			options with { Mode = SelectionMode.AtLeastTarget }, group);

		AssertPayment(below, belowPayment, belowFee, inputs[4]);
		AssertPayment(both.AtMostTarget, belowPayment, belowFee, inputs[4]);
		AssertPayment(above, abovePayment, aboveFee, inputs[0], inputs[2]);
		AssertPayment(both.AtLeastTarget, abovePayment, aboveFee, inputs[0], inputs[2]);
		Assert.Equal(belowPayment - 1_000, both.AtMostTarget.PaymentDifference.Satoshi);
		Assert.Equal(abovePayment - 1_000, both.AtLeastTarget.PaymentDifference.Satoshi);
		Assert.Equal(both.VisitedNodes, both.AtMostTarget.VisitedNodes);
		Assert.Equal(both.VisitedNodes, both.AtLeastTarget.VisitedNodes);
	}

	[Fact]
	public void NBitcoinPolicyUsesAWideProductInsteadOfInheritingNativeMultiplicationOverflow()
	{
		BitcoinInput input = Input(1, long.MaxValue, 999);
		FeeRate rate = new(Money.Satoshis(long.MaxValue));
		SelectionOptions options = new() { MaximumDifference = 0 };
		// floor(M * 999 / 1000) + floor(M / 1000) = M - 1 for M = long.MaxValue.
		BitcoinSelectionResult single = Select(BitcoinFeePolicy.NBitcoin, [input], 1, rate, 1, options);
		BitcoinTwoSidedSelectionResult both = SelectBoth(BitcoinFeePolicy.NBitcoin, [input], 1, rate, 1, options);
		foreach (BitcoinSelectionResult result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			AssertPayment(result, 1, long.MaxValue - 1, input);
		}
	}

	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp, false)]
	[InlineData(BitcoinFeePolicy.NBitcoin, false)]
	[InlineData(BitcoinFeePolicy.RoundUp, true)]
	[InlineData(BitcoinFeePolicy.NBitcoin, true)]
	public void AnUnrepresentableInputOrFixedFeeIsRejected(BitcoinFeePolicy policy, bool fixedPart)
	{
		BitcoinInput[] inputs = fixedPart ? [] : [Input(1, 100, 1_001)];
		int fixedSize = fixedPart ? 1_001 : 0;
		FeeRate rate = new(Money.Satoshis(long.MaxValue));
		Assert.Throws<OverflowException>(() => Select(policy, inputs, 1, rate, fixedSize));
		Assert.Throws<OverflowException>(() => SelectBoth(policy, inputs, 1, rate, fixedSize));
	}

	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp)]
	[InlineData(BitcoinFeePolicy.NBitcoin)]
	public void SeparatelyRepresentableInputFeesCannotOverflowTheirScriptGroupSilently(BitcoinFeePolicy policy)
	{
		BitcoinInput[] inputs = [Input(1, 0, 501, 9), Input(2, 0, 501, 9)];
		FeeRate rate = new(Money.Satoshis(long.MaxValue));
		Assert.Throws<OverflowException>(() => Select(policy, inputs, 1, rate, 0));
		Assert.Throws<OverflowException>(() => SelectBoth(policy, inputs, 1, rate, 0));
	}

	[Fact]
	public void NBitcoinsMinimumFixedFeeStillParticipatesInTargetOverflowChecks()
	{
		Assert.False(Select(BitcoinFeePolicy.RoundUp, [], long.MaxValue, FeeRate.Zero, 0).HasSelection);
		Assert.Throws<OverflowException>(() => Select(BitcoinFeePolicy.NBitcoin, [], long.MaxValue, FeeRate.Zero, 0));
		Assert.Throws<OverflowException>(() => SelectBoth(BitcoinFeePolicy.NBitcoin, [], long.MaxValue, FeeRate.Zero, 0));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(2)]
	[InlineData(int.MaxValue)]
	public void InvalidPoliciesAreRejectedEvenWithNoInputsAndNoNominalFee(int value)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Select((BitcoinFeePolicy)value, [], 1, FeeRate.Zero, 0));
		Assert.Throws<ArgumentOutOfRangeException>(() => SelectBoth((BitcoinFeePolicy)value, [], 1, FeeRate.Zero, 0));
	}

	private static BitcoinSelectionResult Select(BitcoinFeePolicy policy, BitcoinInput[] inputs, long payment,
		FeeRate rate, int fixedSize, SelectionOptions? options = null, bool group = true)
		=> BitcoinCoinSelector.Select(inputs, Money.Satoshis(payment), rate, fixedSize, policy, options, group,
			TestContext.Current.CancellationToken);

	private static BitcoinTwoSidedSelectionResult SelectBoth(BitcoinFeePolicy policy, BitcoinInput[] inputs, long payment,
		FeeRate rate, int fixedSize, SelectionOptions? options = null, bool group = true)
		=> BitcoinCoinSelector.SelectBoth(inputs, Money.Satoshis(payment), rate, fixedSize, policy, options, group,
			TestContext.Current.CancellationToken);

	private static void AssertPayment(BitcoinSelectionResult result, long payment, long fee, params BitcoinInput[] inputs)
	{
		Assert.True(result.HasSelection);
		Assert.True(result.IsOptimal);
		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Equal(inputs.Length, result.InputCount);
		Assert.Equal(payment + fee, result.TotalAmount.Satoshi);
		Assert.Equal(fee, result.EstimatedFee.Satoshi);
		Assert.Equal(payment, result.PaymentAmount.Satoshi);
		Assert.Equal(inputs.Length, result.SelectedInputs.Count);
		for (int i = 0; i < inputs.Length; i++)
		{
			Assert.Same(inputs[i], result.SelectedInputs[i]);
			Assert.Same(inputs[i].Coin, result.SelectedInputs[i].Coin);
		}
	}

	private static BitcoinInput Input(int id, long amount, int inputSize, int? scriptId = null)
		=> new(new Coin(uint256.Parse(id.ToString("x64")), 0, Money.Satoshis(amount),
			new Script(BitConverter.GetBytes(scriptId ?? id))), inputSize);
}
