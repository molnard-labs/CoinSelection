using NBitcoin;
using Xunit;

namespace CoinSelection.NBitcoin.Tests;

public class AsymmetricPaymentTests
{
	[Theory]
	[InlineData(980, 20, 35, true)]
	[InlineData(979, 20, 35, false)]
	[InlineData(1_035, 20, 35, true)]
	[InlineData(1_036, 20, 35, false)]
	[InlineData(1_000, 0, 35, true)]
	[InlineData(999, 0, 35, false)]
	[InlineData(1_035, 0, 35, true)]
	[InlineData(980, 20, 0, true)]
	[InlineData(1_001, 20, 0, false)]
	[InlineData(1_000, 20, 0, true)]
	public void DirectionalLimitsApplyInclusivelyToThePaymentAfterBothFees(
		long payment, long maximumShortfall, long maximumExcess, bool expectedSelection)
	{
		// Two satoshis per virtual byte: 100 input satoshis and 10 fixed satoshis.
		BitcoinInput input = Input(1, payment + 110, 50, scriptId: 1);

		var result = BitcoinCoinSelector.Select(
			[input], Money.Satoshis(1_000), new FeeRate(2m), 5,
			new SelectionOptions
			{
				Mode = SelectionMode.Closest,
				MaximumShortfall = maximumShortfall,
				MaximumExcess = maximumExcess
			}, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(expectedSelection, result.HasSelection);
		if (expectedSelection)
		{
			Assert.Equal(SelectionStatus.Optimal, result.Status);
			Assert.True(result.IsOptimal);
			Assert.Same(input, Assert.Single(result.SelectedInputs));
			Assert.Same(input.Coin, result.SelectedInputs[0].Coin);
			Assert.Equal(1, result.InputCount);
			Assert.Equal(payment + 110, result.TotalAmount.Satoshi);
			Assert.Equal(110, result.EstimatedFee.Satoshi);
			Assert.Equal(payment, result.PaymentAmount.Satoshi);
			Assert.Equal(payment - 1_000, result.PaymentDifference.Satoshi);
		}
		else
		{
			Assert.Equal(SelectionStatus.NoSolution, result.Status);
			Assert.Empty(result.SelectedInputs);
			Assert.Equal(0, result.TotalAmount.Satoshi);
			Assert.Equal(0, result.EstimatedFee.Satoshi);
			Assert.Equal(0, result.PaymentAmount.Satoshi);
		}
	}

	[Fact]
	public void ACloserPaymentOutsideTheUpperLimitCannotDisplaceAnAllowedShortfall()
	{
		// The first coin would pay 1,011 after its 40-satoshi input fee and 10 fixed satoshis.
		BitcoinInput outsideUpperLimit = Input(1, 1_061, 20, scriptId: 1);
		BitcoinInput allowedShortfall = Input(2, 1_090, 50, scriptId: 2);

		var result = BitcoinCoinSelector.Select(
			[outsideUpperLimit, allowedShortfall], Money.Satoshis(1_000), new FeeRate(2m), 5,
			new SelectionOptions
			{
				Mode = SelectionMode.Closest,
				MaximumShortfall = 20,
				MaximumExcess = 10
			}, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Same(allowedShortfall, Assert.Single(result.SelectedInputs));
		Assert.Same(allowedShortfall.Coin, result.SelectedInputs[0].Coin);
		Assert.Equal(110, result.EstimatedFee.Satoshi);
		Assert.Equal(980, result.PaymentAmount.Satoshi);
		Assert.Equal(-20, result.PaymentDifference.Satoshi);
	}

	[Fact]
	public void ACloserPaymentOutsideTheLowerLimitCannotDisplaceAnAllowedExcess()
	{
		BitcoinInput outsideLowerLimit = Input(1, 1_089, 50, scriptId: 1);
		BitcoinInput allowedExcess = Input(2, 1_145, 50, scriptId: 2);

		var result = BitcoinCoinSelector.Select(
			[outsideLowerLimit, allowedExcess], Money.Satoshis(1_000), new FeeRate(2m), 5,
			new SelectionOptions
			{
				Mode = SelectionMode.Closest,
				MaximumShortfall = 20,
				MaximumExcess = 35
			}, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Same(allowedExcess, Assert.Single(result.SelectedInputs));
		Assert.Equal(110, result.EstimatedFee.Satoshi);
		Assert.Equal(1_035, result.PaymentAmount.Satoshi);
		Assert.Equal(35, result.PaymentDifference.Satoshi);
	}

	[Theory]
	[InlineData(1, false)]
	[InlineData(2, true)]
	public void ScriptGroupsRespectActualInputCountsWithinThePaymentInterval(int maximumInputCount, bool expectGroup)
	{
		BitcoinInput firstGroupMember = Input(1, 500, 50, scriptId: 9);
		BitcoinInput alternative = Input(2, 1_145, 50, scriptId: 8);
		BitcoinInput secondGroupMember = Input(3, 680, 40, scriptId: 9);

		var result = BitcoinCoinSelector.Select(
			[firstGroupMember, alternative, secondGroupMember], Money.Satoshis(1_000), new FeeRate(2m), 5,
			new SelectionOptions
			{
				Mode = SelectionMode.Closest,
				MaximumShortfall = 20,
				MaximumExcess = 35,
				MaximumInputCount = maximumInputCount
			}, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		if (expectGroup)
		{
			Assert.Collection(result.SelectedInputs,
				input => Assert.Same(firstGroupMember, input),
				input => Assert.Same(secondGroupMember, input));
			Assert.Equal(2, result.InputCount);
			Assert.Equal(1_180, result.TotalAmount.Satoshi);
			Assert.Equal(190, result.EstimatedFee.Satoshi);
			Assert.Equal(990, result.PaymentAmount.Satoshi);
			Assert.Equal(-10, result.PaymentDifference.Satoshi);
		}
		else
		{
			Assert.Same(alternative, Assert.Single(result.SelectedInputs));
			Assert.Equal(1, result.InputCount);
			Assert.Equal(1_145, result.TotalAmount.Satoshi);
			Assert.Equal(110, result.EstimatedFee.Satoshi);
			Assert.Equal(1_035, result.PaymentAmount.Satoshi);
			Assert.Equal(35, result.PaymentDifference.Satoshi);
		}
	}

	private static BitcoinInput Input(int id, long amount, int size, int scriptId)
	{
		byte[] scriptBytes = new byte[22];
		scriptBytes[1] = 20; // Witness v0 followed by a 20-byte public-key hash.
		BitConverter.GetBytes(scriptId).CopyTo(scriptBytes, 2);
		return new BitcoinInput(new Coin(uint256.Parse(id.ToString("x64")), 0,
			Money.Satoshis(amount), new Script(scriptBytes)), size);
	}
}
