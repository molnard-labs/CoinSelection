using NBitcoin;
using Xunit;

namespace CoinSelection.NBitcoin.Tests;

public class BitcoinCoinSelectorTests
{
	[Fact]
	public void ExplicitSizesProduceAnExactPaymentAfterAllFees()
	{
		BitcoinInput input = Input(1, 1_110, 50);

		var result = BitcoinCoinSelector.Select([input], Money.Satoshis(1_000), new FeeRate(2m), 5,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.HasSelection);
		Assert.True(result.IsOptimal);
		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Same(input, Assert.Single(result.SelectedInputs));
		Assert.Equal(1, result.InputCount);
		Assert.Equal(1_110, result.TotalAmount.Satoshi);
		Assert.Equal(110, result.EstimatedFee.Satoshi);
		Assert.Equal(1_000, result.PaymentAmount.Satoshi);
		Assert.Equal(0, result.PaymentDifference.Satoshi);
	}

	[Theory]
	[InlineData(SelectionMode.AtLeastTarget, 1_015, 15)]
	[InlineData(SelectionMode.AtMostTarget, 990, -10)]
	[InlineData(SelectionMode.Closest, 990, -10)]
	public void SearchModesApplyToThePaymentAfterFees(SelectionMode mode, long expected, long difference)
	{
		var result = BitcoinCoinSelector.Select(
			[Input(1, 990), Input(2, 1_015)], Money.Satoshis(1_000), FeeRate.Zero, 0,
			new SelectionOptions { Mode = mode }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.HasSelection);
		Assert.Equal(expected, result.PaymentAmount.Satoshi);
		Assert.Equal(difference, result.PaymentDifference.Satoshi);
	}

	[Theory]
	[InlineData(SelectionMode.AtMostTarget, 0)]
	[InlineData(SelectionMode.AtMostTarget, -100)]
	[InlineData(SelectionMode.Closest, 0)]
	[InlineData(SelectionMode.Closest, -100)]
	public void WideToleranceNeverSelectsAZeroOrNegativePayment(SelectionMode mode, long payment)
	{
		var result = BitcoinCoinSelector.Select(
			[Input(1, 1_001 + payment, 1)], Money.Satoshis(100), new FeeRate(1m), 1_000,
			new SelectionOptions { Mode = mode }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.False(result.HasSelection);
		Assert.Equal(SelectionStatus.NoSolution, result.Status);
		Assert.Empty(result.SelectedInputs);
		Assert.Equal(0, result.PaymentAmount.Satoshi);
	}

	[Fact]
	public void ClosestSelectsADistantPositivePaymentInsteadOfANearerZeroPayment()
	{
		BitcoinInput zeroPayment = Input(1, 11, 1);
		BitcoinInput positivePayment = Input(2, 31, 1);

		var result = BitcoinCoinSelector.Select(
			[zeroPayment, positivePayment], Money.Satoshis(1), new FeeRate(1m), 10,
			new SelectionOptions { Mode = SelectionMode.Closest }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.HasSelection);
		Assert.True(result.IsOptimal);
		Assert.Same(positivePayment, Assert.Single(result.SelectedInputs));
		Assert.Equal(20, result.PaymentAmount.Satoshi);
		Assert.Equal(19, result.PaymentDifference.Satoshi);
	}

	[Fact]
	public void ASearchLimitCannotExposeANonpositivePayment()
	{
		var result = BitcoinCoinSelector.Select(
			[Input(1, 901, 1), Input(2, 201, 1)], Money.Satoshis(100), new FeeRate(1m), 1_000,
			new SelectionOptions { Mode = SelectionMode.Closest, MaximumSearchNodes = 2 },
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.False(result.HasSelection);
		Assert.Equal(SelectionStatus.SearchLimitReached, result.Status);
		Assert.Equal(2, result.VisitedNodes);
		Assert.Empty(result.SelectedInputs);
		Assert.Equal(0, result.PaymentAmount.Satoshi);
	}

	[Fact]
	public void AnExplicitMinimumValueIsPreservedBeforeTheFixedFee()
	{
		BitcoinInput belowMinimum = Input(1, 1_041, 1);
		BitcoinInput aboveMinimum = Input(2, 1_201, 1);

		var result = BitcoinCoinSelector.Select(
			[belowMinimum, aboveMinimum], Money.Satoshis(100), new FeeRate(1m), 1_000,
			new SelectionOptions { Mode = SelectionMode.Closest, MinimumValue = 1_050 },
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Same(aboveMinimum, Assert.Single(result.SelectedInputs));
		Assert.Equal(200, result.PaymentAmount.Satoshi);
	}

	[Fact]
	public void AnInvalidMinimumValueIsRejectedBeforePositivePaymentClamping()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => BitcoinCoinSelector.Select(
			[Input(1, 1_001, 1)], Money.Satoshis(100), new FeeRate(1m), 1_000,
			new SelectionOptions { MinimumValue = 0 }, cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void DuplicateEffectiveValuesPreserveTheCheaperCoinsIdentity()
	{
		BitcoinInput expensive = Input(1, 1_100, 100);
		BitcoinInput cheap = Input(2, 1_050, 50);

		var result = BitcoinCoinSelector.Select([expensive, cheap], Money.Satoshis(1_000), new FeeRate(1m), 0,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Same(cheap, Assert.Single(result.SelectedInputs));
		Assert.Equal(50, result.EstimatedFee.Satoshi);
		Assert.Equal(1_000, result.PaymentAmount.Satoshi);
	}

	[Fact]
	public void SameScriptCoinsAreSelectedTogetherByDefault()
	{
		BitcoinInput first = Input(1, 400, scriptId: 9);
		BitcoinInput second = Input(2, 600, scriptId: 9);

		var result = BitcoinCoinSelector.Select([first, second], Money.Satoshis(600), FeeRate.Zero, 0,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(2, result.InputCount);
		Assert.Equal(2, result.SelectedInputs.Count);
		Assert.Contains(first, result.SelectedInputs);
		Assert.Contains(second, result.SelectedInputs);
		Assert.Equal(1_000, result.PaymentAmount.Satoshi);
	}

	[Fact]
	public void ScriptGroupingCanBeDisabledExplicitly()
	{
		BitcoinInput first = Input(1, 400, scriptId: 9);
		BitcoinInput second = Input(2, 600, scriptId: 9);

		var result = BitcoinCoinSelector.Select(
			[first, second], Money.Satoshis(600), FeeRate.Zero, 0, groupByScriptPubKey: false,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Same(second, Assert.Single(result.SelectedInputs));
		Assert.Equal(600, result.PaymentAmount.Satoshi);
	}

	[Fact]
	public void InterleavedScriptGroupsReturnInputsInOriginalOrder()
	{
		BitcoinInput first = Input(1, 300, scriptId: 9);
		BitcoinInput second = Input(2, 200, scriptId: 8);
		BitcoinInput third = Input(3, 500, scriptId: 9);

		var result = BitcoinCoinSelector.Select([first, second, third], Money.Satoshis(1_000), FeeRate.Zero, 0,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal([first, second, third], result.SelectedInputs);
	}

	[Fact]
	public void ScriptGroupsCountEveryActualInputAgainstTheLimit()
	{
		var result = BitcoinCoinSelector.Select(
			[Input(1, 400, scriptId: 9), Input(2, 600, scriptId: 9)],
			Money.Satoshis(1_000), FeeRate.Zero, 0,
			new SelectionOptions { MaximumInputCount = 1 }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.False(result.HasSelection);
		Assert.Equal(SelectionStatus.NoSolution, result.Status);
	}

	[Fact]
	public void APositiveGroupRetainsItsUneconomicalMembers()
	{
		BitcoinInput uneconomical = Input(1, 10, 20, scriptId: 9);
		BitcoinInput economical = Input(2, 1_010, 20, scriptId: 9);

		var result = BitcoinCoinSelector.Select(
			[uneconomical, economical], Money.Satoshis(980), new FeeRate(1m), 0,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(2, result.InputCount);
		Assert.Contains(uneconomical, result.SelectedInputs);
		Assert.Equal(980, result.PaymentAmount.Satoshi);
	}

	[Theory]
	[InlineData(10)]
	[InlineData(20)]
	public void NonpositiveEffectiveGroupsAreExcluded(long amount)
	{
		var result = BitcoinCoinSelector.Select([Input(1, amount, 20)], Money.Satoshis(1), new FeeRate(1m), 0,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.False(result.HasSelection);
		Assert.Empty(result.SelectedInputs);
		Assert.Equal(0, result.TotalAmount.Satoshi);
		Assert.Equal(0, result.EstimatedFee.Satoshi);
		Assert.Equal(0, result.PaymentAmount.Satoshi);
		Assert.Equal(-1, result.PaymentDifference.Satoshi);
	}

	[Fact]
	public void FeesAreRoundedPerInputAndFixedPart()
	{
		var result = BitcoinCoinSelector.Select(
			[Input(1, 51, 1), Input(2, 52, 1)], Money.Satoshis(100), new FeeRate(0.1m), 1,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.HasSelection);
		Assert.Equal(3, result.EstimatedFee.Satoshi);
		Assert.Equal(100, result.PaymentAmount.Satoshi);
	}

	[Fact]
	public void FractionalInputFeesAreRoundedUpBeyondNBitcoinsMinimum()
	{
		var result = BitcoinCoinSelector.Select(
			[Input(1, 102, 19)], Money.Satoshis(100), new FeeRate(0.1m), 0,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.HasSelection);
		Assert.Equal(2, result.EstimatedFee.Satoshi);
		Assert.Equal(100, result.PaymentAmount.Satoshi);
	}

	[Fact]
	public void FractionalFixedFeesAreRoundedUp()
	{
		var result = BitcoinCoinSelector.Select(
			[Input(1, 103, 1)], Money.Satoshis(100), new FeeRate(0.1m), 19,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.HasSelection);
		Assert.Equal(3, result.EstimatedFee.Satoshi);
		Assert.Equal(100, result.PaymentAmount.Satoshi);
	}

	[Fact]
	public void FeeMultiplicationMayExceedLongWhenTheRoundedFeeFits()
	{
		long expectedFee = checked((long)decimal.Ceiling((decimal)long.MaxValue * 999 / 1_000m));
		var result = BitcoinCoinSelector.Select(
			[Input(1, long.MaxValue, 999)], Money.Satoshis(1), new FeeRate(Money.Satoshis(long.MaxValue)), 0,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.HasSelection);
		Assert.Equal(expectedFee, result.EstimatedFee.Satoshi);
		Assert.Equal(long.MaxValue - expectedFee, result.PaymentAmount.Satoshi);
	}

	[Fact]
	public void AFeeBeyondLongMaxValueIsRejected()
	{
		Assert.Throws<OverflowException>(() => BitcoinCoinSelector.Select(
			[Input(1, 100, 1_001)], Money.Satoshis(1), new FeeRate(Money.Satoshis(long.MaxValue)), 0,
			cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void ZeroFeeRateDoesNotInvokeNBitcoinsOneSatoshiMinimum()
	{
		var result = BitcoinCoinSelector.Select([Input(1, 100, 68)], Money.Satoshis(100), FeeRate.Zero, 200,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.HasSelection);
		Assert.Equal(0, result.EstimatedFee.Satoshi);
		Assert.Equal(100, result.PaymentAmount.Satoshi);
	}

	[Fact]
	public void ZeroFixedSizeHasNoFee()
	{
		var result = BitcoinCoinSelector.Select([Input(1, 101, 1)], Money.Satoshis(100), new FeeRate(1m), 0,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.HasSelection);
		Assert.Equal(1, result.EstimatedFee.Satoshi);
		Assert.Equal(100, result.PaymentAmount.Satoshi);
	}

	[Fact]
	public void MaximumDifferenceIsMeasuredAgainstTheRequestedPayment()
	{
		var result = BitcoinCoinSelector.Select(
			[Input(1, 1_025, 10)], Money.Satoshis(1_000), new FeeRate(1m), 5,
			new SelectionOptions { MaximumDifference = 9 }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.False(result.HasSelection);
	}

	[Fact]
	public void ResultCollectionAndAmountsAreSnapshots()
	{
		BitcoinInput original = Input(1, 100);
		List<BitcoinInput> inputs = [original];
		var result = BitcoinCoinSelector.Select(inputs, Money.Satoshis(100), FeeRate.Zero, 0,
			cancellationToken: TestContext.Current.CancellationToken);

		inputs.Clear();
		((Coin)original.Coin).Amount = Money.Satoshis(200);

		Assert.Same(original, Assert.Single(result.SelectedInputs));
		Assert.Equal(100, result.TotalAmount.Satoshi);
		Assert.Equal(100, result.PaymentAmount.Satoshi);
		Assert.Throws<NotSupportedException>(() => ((IList<BitcoinInput>)result.SelectedInputs).Clear());
	}

	[Fact]
	public void APreCancelledSearchReturnsTheCoreCancellationStatus()
	{
		using CancellationTokenSource source = new();
		source.Cancel();

		var result = BitcoinCoinSelector.Select(
			[Input(1, 100)], Money.Satoshis(100), FeeRate.Zero, 0, cancellationToken: source.Token);

		Assert.Equal(SelectionStatus.Cancelled, result.Status);
		Assert.False(result.HasSelection);
		Assert.False(result.IsOptimal);
	}

	[Fact]
	public void ABoundedSearchPreservesItsBestPaymentAndStatus()
	{
		BitcoinInput first = Input(1, 111, 1);
		BitcoinInput exact = Input(2, 110, 10);

		var result = BitcoinCoinSelector.Select(
			[first, exact], Money.Satoshis(100), new FeeRate(1m), 0,
			new SelectionOptions { MaximumSearchNodes = 2 }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.HasSelection);
		Assert.False(result.IsOptimal);
		Assert.Equal(SelectionStatus.SearchLimitReached, result.Status);
		Assert.Equal(2, result.VisitedNodes);
		Assert.Same(first, Assert.Single(result.SelectedInputs));
		Assert.Equal(110, result.PaymentAmount.Satoshi);
		Assert.Equal(10, result.PaymentDifference.Satoshi);
	}

	[Fact]
	public void DuplicateOutpointsAreRejectedEvenWithDifferentScripts()
	{
		BitcoinInput first = Input(1, 100, scriptId: 1);
		BitcoinInput duplicate = Input(1, 200, scriptId: 2);

		Assert.Throws<ArgumentException>(() => BitcoinCoinSelector.Select(
			[first, duplicate], Money.Satoshis(100), FeeRate.Zero, 0,
			cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void NullBitcoinOutpointsAreRejected()
	{
		BitcoinInput input = new(new Coin(new OutPoint(), new TxOut(Money.Satoshis(100), new Script())), 1);

		Assert.Throws<ArgumentException>(() => BitcoinCoinSelector.Select([input], Money.Satoshis(100), FeeRate.Zero, 0,
			cancellationToken: TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData(0, 0, 1)]
	[InlineData(-1, 0, 1)]
	[InlineData(1, -1, 1)]
	[InlineData(1, 0, 0)]
	[InlineData(1, 0, -1)]
	public void InvalidAmountsAndSizesAreRejected(long payment, int fixedSize, int inputSize)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => BitcoinCoinSelector.Select(
			[Input(1, 100, inputSize)], Money.Satoshis(payment), FeeRate.Zero, fixedSize,
			cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void NegativeCoinAmountsAreRejected()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => BitcoinCoinSelector.Select(
			[Input(1, -1)], Money.Satoshis(1), FeeRate.Zero, 0,
			cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void NullArgumentsAndInputsAreRejected()
	{
		Assert.Throws<ArgumentNullException>(() => BitcoinCoinSelector.Select(null!, Money.Satoshis(1), FeeRate.Zero, 0,
			cancellationToken: TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() => BitcoinCoinSelector.Select([], null!, FeeRate.Zero, 0,
			cancellationToken: TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() => BitcoinCoinSelector.Select([], Money.Satoshis(1), null!, 0,
			cancellationToken: TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() => BitcoinCoinSelector.Select([null!], Money.Satoshis(1), FeeRate.Zero, 0,
			cancellationToken: TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() => BitcoinCoinSelector.Select([new BitcoinInput(null!, 1)], Money.Satoshis(1), FeeRate.Zero, 0,
			cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void PaymentPlusFixedFeeOverflowIsRejected()
	{
		Assert.Throws<OverflowException>(() => BitcoinCoinSelector.Select(
			[], Money.Satoshis(long.MaxValue), new FeeRate(1m), 1,
			cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void AGroupAmountOverflowIsRejected()
	{
		Assert.Throws<OverflowException>(() => BitcoinCoinSelector.Select(
			[Input(1, long.MaxValue, scriptId: 9), Input(2, 1, scriptId: 9)],
			Money.Satoshis(1), FeeRate.Zero, 0,
			cancellationToken: TestContext.Current.CancellationToken));
	}

	private static BitcoinInput Input(int id, long amount, int inputSize = 1, int? scriptId = null)
		=> new(new Coin(uint256.Parse(id.ToString("x64")), 0, Money.Satoshis(amount),
			new Script(BitConverter.GetBytes(scriptId ?? id))), inputSize);
}
