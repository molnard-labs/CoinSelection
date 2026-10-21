using NBitcoin;
using NBitcoin.Policy;
using Xunit;

namespace CoinSelection.NBitcoin.Tests;

public class SignedTransactionIntegrationTests
{
	[Theory]
	[InlineData(false, BitcoinFeePolicy.RoundUp, 220)]
	[InlineData(false, BitcoinFeePolicy.NBitcoin, 217)]
	[InlineData(true, BitcoinFeePolicy.RoundUp, 278)]
	[InlineData(true, BitcoinFeePolicy.NBitcoin, 275)]
	public void ExactSelectionsSpendTheOriginalNativeOrWrappedWitnessCoins(
		bool wrapped, BitcoinFeePolicy policy, long expectedFee)
	{
		using Key spendingKey = TestKey(1);
		using Key unrelatedKey = TestKey(2);
		using Key recipientKey = TestKey(3);
		Script destination = recipientKey.PubKey.WitHash.ScriptPubKey;
		int fixedSize = FixedVirtualSize(destination);
		BitcoinInput first = Input(1, 40_000, spendingKey, wrapped);
		BitcoinInput second = Input(2, 60_000, spendingKey, wrapped);
		BitcoinInput[] wallet = [first, Input(3, 500, unrelatedKey, false), second];
		Money payment = Money.Satoshis(100_000 - expectedFee);
		FeeRate rate = new(1.234m);
		SelectionOptions options = new()
		{
			MaximumDifference = 0, MaximumCandidateCount = 1, MaximumInputCount = 2
		};
		BitcoinSelectionResult single = BitcoinCoinSelector.Select(wallet, payment, rate, fixedSize,
			policy, options, true, TestContext.Current.CancellationToken);
		BitcoinTwoSidedSelectionResult both = BitcoinCoinSelector.SelectBoth(wallet, payment, rate, fixedSize,
			policy, options, true, TestContext.Current.CancellationToken);

		foreach (BitcoinSelectionResult result in new[] { single, both.AtMostTarget, both.AtLeastTarget })
		{
			Assert.Equal(payment, result.PaymentAmount);
			Assert.Equal(0, result.PaymentDifference.Satoshi);
			Assert.Equal(expectedFee, result.EstimatedFee.Satoshi);
			AssertOriginalInputs(result, first, second);
			if (wrapped)
			{
				Assert.Same(((ScriptCoin)first.Coin).Redeem,
					Assert.IsType<ScriptCoin>(result.SelectedInputs[0].Coin).Redeem);
			}
			AssertSignedTransaction(result, spendingKey, destination, fixedSize);

			// Each component is rounded separately. Neither policy promises GetFee(total vsize).
			int estimatedSize = fixedSize + result.SelectedInputs.Sum(input => input.InputVirtualSize);
			Assert.NotEqual(rate.GetFee(estimatedSize), result.EstimatedFee);
		}
	}

	[Theory]
	[InlineData(BitcoinFeePolicy.RoundUp, 49_864, 136, 50_435, 165)]
	[InlineData(BitcoinFeePolicy.NBitcoin, 49_866, 134, 50_437, 163)]
	public void BothDistinctPaymentSuggestionsProduceValidSignedTransactions(
		BitcoinFeePolicy policy, long belowPayment, long belowFee, long abovePayment, long aboveFee)
	{
		using Key spendingKey = TestKey(4);
		using Key recipientKey = TestKey(5);
		Script destination = recipientKey.PubKey.WitHash.ScriptPubKey;
		int fixedSize = FixedVirtualSize(destination);
		BitcoinInput native = Input(4, 50_000, spendingKey, false);
		BitcoinInput wrapped = Input(5, 50_600, spendingKey, true);
		SelectionOptions options = new()
		{
			MaximumShortfall = 150, MaximumExcess = 450,
			MaximumCandidateCount = 1, MaximumInputCount = 1
		};
		BitcoinTwoSidedSelectionResult both = BitcoinCoinSelector.SelectBoth([native, wrapped],
			Money.Satoshis(50_000), new FeeRate(1.234m), fixedSize,
			policy, options, true, TestContext.Current.CancellationToken);

		AssertOriginalInputs(both.AtMostTarget, native);
		AssertOriginalInputs(both.AtLeastTarget, wrapped);
		Assert.Equal(belowPayment, both.AtMostTarget.PaymentAmount.Satoshi);
		Assert.Equal(abovePayment, both.AtLeastTarget.PaymentAmount.Satoshi);
		Assert.Equal(belowPayment - 50_000, both.AtMostTarget.PaymentDifference.Satoshi);
		Assert.Equal(abovePayment - 50_000, both.AtLeastTarget.PaymentDifference.Satoshi);
		Assert.Equal(belowFee, both.AtMostTarget.EstimatedFee.Satoshi);
		Assert.Equal(aboveFee, both.AtLeastTarget.EstimatedFee.Satoshi);
		AssertSignedTransaction(both.AtMostTarget, spendingKey, destination, fixedSize);
		AssertSignedTransaction(both.AtLeastTarget, spendingKey, destination, fixedSize);
	}

	private static void AssertOriginalInputs(BitcoinSelectionResult result, params BitcoinInput[] expected)
	{
		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.True(result.HasSelection);
		Assert.Equal(expected.Length, result.InputCount);
		Assert.Equal(expected.Length, result.SelectedInputs.Count);
		for (int i = 0; i < expected.Length; i++)
		{
			Assert.Same(expected[i], result.SelectedInputs[i]);
			Assert.Same(expected[i].Coin, result.SelectedInputs[i].Coin);
		}
	}

	private static void AssertSignedTransaction(
		BitcoinSelectionResult result, Key key, Script destination, int fixedSize)
	{
		ICoin[] coins = result.SelectedInputs.Select(input => input.Coin).ToArray();
		TransactionBuilder builder = Network.RegTest.CreateTransactionBuilder();
		builder.ShuffleInputs = false;
		builder.ShuffleOutputs = false;
		builder.AddCoins(coins);
		builder.AddKeys(key);
		builder.Send(destination, result.PaymentAmount);
		builder.SendFees(result.EstimatedFee);
		Transaction transaction = builder.BuildTransaction(true);

		Assert.True(builder.Verify(transaction, result.EstimatedFee, out TransactionPolicyError[] errors),
			string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
		TxOut output = Assert.Single(transaction.Outputs);
		Assert.Equal(destination, output.ScriptPubKey);
		Assert.Equal(result.PaymentAmount, output.Value);
		Assert.Equal(result.InputCount, transaction.Inputs.Count);
		Assert.Equal(result.TotalAmount.Satoshi, coins.Sum(coin => ((Money)coin.Amount).Satoshi));
		Assert.Equal(result.EstimatedFee.Satoshi, result.TotalAmount.Satoshi - transaction.Outputs.Sum(txOut => txOut.Value.Satoshi));
		Assert.Equal(result.EstimatedFee, transaction.GetFee(coins));

		// These small fixtures use one-byte CompactSize counts and one-byte script/push lengths.
		// Base bytes: version, input/output counts, lock time, output, and each serialized input.
		int strippedSize = 4 + 1 + 1 + 4 + 8 + 1 + destination.ToBytes().Length;
		int witnessSize = 2; // SegWit marker and flag are witness bytes, hence carry weight one.
		for (int i = 0; i < transaction.Inputs.Count; i++)
		{
			TxIn input = transaction.Inputs[i];
			Assert.Equal(coins[i].Outpoint, input.PrevOut);
			int scriptSize = input.ScriptSig.ToBytes().Length;
			Assert.Equal(coins[i] is ScriptCoin ? 23 : 0, scriptSize);
			byte[][] pushes = input.WitScript.Pushes.ToArray();
			Assert.Equal(2, pushes.Length);
			Assert.InRange(pushes[0].Length, 1, 72); // Low-S DER signature plus sighash byte.
			Assert.Equal(33, pushes[1].Length); // Compressed public key.
			int inputBaseSize = 36 + 1 + scriptSize + 4;
			int inputWitnessSize = 1 + pushes.Sum(push => 1 + push.Length);
			Assert.True(4 * inputBaseSize + inputWitnessSize <= 4 * result.SelectedInputs[i].InputVirtualSize);
			strippedSize += inputBaseSize;
			witnessSize += inputWitnessSize;
		}
		Assert.Equal(strippedSize + witnessSize, transaction.ToBytes().Length);
		Assert.Equal((4 * strippedSize + witnessSize + 3) / 4, transaction.GetVirtualSize());
		int estimatedSize = fixedSize + result.SelectedInputs.Sum(input => input.InputVirtualSize);
		Assert.True(transaction.GetVirtualSize() <= estimatedSize);

		// The successful Verify above must validate signatures, not merely nonempty witnesses.
		Transaction altered = transaction.Clone();
		altered.Outputs[0].Value += Money.Satoshis(1);
		Assert.False(builder.Verify(altered));
	}

	private static int FixedVirtualSize(Script destination)
	{
		Assert.Equal(22, destination.ToBytes().Length);
		// One P2WPKH output: amount (8), CompactSize script length (1), script (22).
		// Also version (4), CompactSize input/output counts (1 each), lock time (4),
		// and ceil((marker + flag) / 4) = 1 vbyte. Counts stay below 253 in these fixtures.
		return 8 + 1 + 22 + 4 + 1 + 1 + 4 + 1;
	}

	private static BitcoinInput Input(int id, long amount, Key key, bool wrapped)
	{
		Script redeem = key.PubKey.WitHash.ScriptPubKey;
		Coin coin = new(uint256.Parse(id.ToString("x64")), 0, Money.Satoshis(amount),
			wrapped ? redeem.Hash.ScriptPubKey : redeem);
		// P2WPKH: 41 base bytes and at most 108 witness bytes = 68 vbytes.
		// P2SH-P2WPKH adds a 23-byte scriptSig, giving 91 vbytes.
		return new BitcoinInput(wrapped ? new ScriptCoin(coin, redeem) : coin, wrapped ? 91 : 68);
	}

	// Deterministic throwaway keys and fabricated outpoints; no funded coins or node are needed.
	private static Key TestKey(byte value) => new(Enumerable.Repeat(value, 32).ToArray());
}
