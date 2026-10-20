using NBitcoin;
using Xunit;

namespace CoinSelection.NBitcoin.Tests;

public class SelectionOptimizationRegressionTests
{
	[Fact]
	public void EqualEffectiveGroupsPreserveInputCountTiesFeesAndOriginalReferences()
	{
		BitcoinInput firstGroupMember = Input(1, 401, 19, scriptId: 9);
		BitcoinInput separate = Input(2, 503, 20, scriptId: 8);
		BitcoinInput secondGroupMember = Input(3, 604, 20, scriptId: 9);
		BitcoinInput sameValueSingle = Input(4, 1_005, 39, scriptId: 7);

		var result = BitcoinCoinSelector.Select(
			[firstGroupMember, separate, secondGroupMember, sameValueSingle], Money.Satoshis(1_501), new FeeRate(0.1m), 9,
			new SelectionOptions { MaximumDifference = 0, MaximumInputCount = 3 },
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Collection(result.SelectedInputs,
			input => Assert.Same(separate, input), input => Assert.Same(sameValueSingle, input));
		Assert.Same(separate.Coin, result.SelectedInputs[0].Coin);
		Assert.Same(sameValueSingle.Coin, result.SelectedInputs[1].Coin);
		Assert.Equal(2, result.InputCount);
		Assert.Equal(1_508, result.TotalAmount.Satoshi);
		Assert.Equal(7, result.EstimatedFee.Satoshi);
		Assert.Equal(1_501, result.PaymentAmount.Satoshi);
		Assert.Equal(0, result.PaymentDifference.Satoshi);
	}

	[Fact]
	public void ALargePoolKeepsTheSelectedCoinReferenceAndFractionalFeeRounding()
	{
		BitcoinInput[] inputs = Enumerable.Range(1, 300).Select(index => Input(index, 1_000_000 + index, 1, index)).ToArray();
		BitcoinInput exact = Input(301, 1_004, 19, scriptId: 301);
		inputs[^1] = exact;

		var result = BitcoinCoinSelector.Select(inputs, Money.Satoshis(1_000), new FeeRate(0.1m), 19,
			new SelectionOptions { MaximumDifference = 0, MaximumInputCount = 8 },
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Same(exact, Assert.Single(result.SelectedInputs));
		Assert.Same(exact.Coin, result.SelectedInputs[0].Coin);
		Assert.Equal(4, result.EstimatedFee.Satoshi);
		Assert.Equal(1_000, result.PaymentAmount.Satoshi);
	}

	[Fact]
	public void ScriptCoinSigningMetadataRemainsOnTheOriginalCoinObject()
	{
		Script redeem = new(new byte[] { 0x51 });
		ICoin original = new ScriptCoin(new Coin(uint256.Parse(1.ToString("x64")), 0,
			Money.Satoshis(1_100), redeem.Hash.ScriptPubKey), redeem);
		Script originalRedeem = ((ScriptCoin)original).Redeem;
		BitcoinInput input = new(original, 50);

		var result = BitcoinCoinSelector.Select([input], Money.Satoshis(1_000), new FeeRate(1m), 50,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Same(input, Assert.Single(result.SelectedInputs));
		Assert.Same(original, result.SelectedInputs[0].Coin);
		Assert.Same(originalRedeem, Assert.IsType<ScriptCoin>(result.SelectedInputs[0].Coin).Redeem);
		Assert.Equal(100, result.EstimatedFee.Satoshi);
		Assert.Equal(1_000, result.PaymentAmount.Satoshi);
	}

	private static BitcoinInput Input(int id, long amount, int size, int scriptId) =>
		new(new Coin(uint256.Parse(id.ToString("x64")), 0, Money.Satoshis(amount),
			new Script(BitConverter.GetBytes(scriptId))), size);
}
