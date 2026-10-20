using System.Collections;
using NBitcoin;
using Xunit;

namespace CoinSelection.NBitcoin.Tests;

public class TwoSidedPaymentTests
{
	[Theory]
	[InlineData(SelectionMode.AtLeastTarget)]
	[InlineData(SelectionMode.AtMostTarget)]
	[InlineData(SelectionMode.Closest)]
	public void BothSidesUseIndependentPaymentLimitsAfterAllFeesAndIgnoreMode(SelectionMode mode)
	{
		BitcoinInput below = Input(1, 1_090, 50);
		BitcoinInput above = Input(2, 1_145, 50);
		BitcoinInput outsideLower = Input(3, 1_089, 50);
		BitcoinInput outsideUpper = Input(4, 1_146, 50);

		var result = BitcoinCoinSelector.SelectBoth(
			[outsideUpper, below, outsideLower, above], Money.Satoshis(1_000), new FeeRate(2m), 5,
			new SelectionOptions
			{
				Mode = mode, MaximumShortfall = 20, MaximumExcess = 35, MaximumInputCount = 1
			}, cancellationToken: TestContext.Current.CancellationToken);

		AssertSuggestion(result.AtMostTarget, below, 980, 110, -20);
		AssertSuggestion(result.AtLeastTarget, above, 1_035, 110, 35);
		Assert.Equal(result.VisitedNodes, result.AtMostTarget.VisitedNodes);
		Assert.Equal(result.VisitedNodes, result.AtLeastTarget.VisitedNodes);
	}

	[Fact]
	public void AnExactPaymentAppearsOnBothSidesWithTheCheaperOriginalCoin()
	{
		BitcoinInput expensive = Input(1, 1_160, 75);
		BitcoinInput cheap = Input(2, 1_110, 50);

		var result = BitcoinCoinSelector.SelectBoth(
			[expensive, cheap], Money.Satoshis(1_000), new FeeRate(2m), 5,
			new SelectionOptions { MaximumDifference = 0, MaximumInputCount = 1 },
			cancellationToken: TestContext.Current.CancellationToken);

		AssertSuggestion(result.AtMostTarget, cheap, 1_000, 110, 0);
		AssertSuggestion(result.AtLeastTarget, cheap, 1_000, 110, 0);
		Assert.Same(result.AtMostTarget.SelectedInputs[0], result.AtLeastTarget.SelectedInputs[0]);
	}

	[Fact]
	public void BothSidesPreserveSeparateUpwardRoundingForInputsAndTheFixedPart()
	{
		BitcoinInput first = Input(1, 51, 1);
		BitcoinInput second = Input(2, 52, 1);

		var result = BitcoinCoinSelector.SelectBoth(
			[first, second], Money.Satoshis(100), new FeeRate(0.1m), 1,
			new SelectionOptions { MaximumDifference = 0 }, cancellationToken: TestContext.Current.CancellationToken);

		foreach (BitcoinSelectionResult side in new[] { result.AtMostTarget, result.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.Optimal, side.Status);
			Assert.Equal(2, side.InputCount);
			Assert.Equal(103, side.TotalAmount.Satoshi);
			Assert.Equal(3, side.EstimatedFee.Satoshi);
			Assert.Equal(100, side.PaymentAmount.Satoshi);
			Assert.Equal(0, side.PaymentDifference.Satoshi);
			Assert.Collection(side.SelectedInputs,
				input => Assert.Same(first, input), input => Assert.Same(second, input));
		}
	}

	[Theory]
	[InlineData(1, false)]
	[InlineData(2, true)]
	public void ScriptGroupsCountEveryCoinAndKeepOriginalOrderOnEachSide(int maximumInputCount, bool expectBelow)
	{
		BitcoinInput first = Input(1, 500, 50, 9);
		BitcoinInput above = Input(2, 1_145, 50, 8);
		BitcoinInput second = Input(3, 680, 40, 9);

		var result = BitcoinCoinSelector.SelectBoth(
			[first, above, second], Money.Satoshis(1_000), new FeeRate(2m), 5,
			new SelectionOptions
			{
				MaximumShortfall = 20, MaximumExcess = 35, MaximumInputCount = maximumInputCount
			}, cancellationToken: TestContext.Current.CancellationToken);

		AssertSuggestion(result.AtLeastTarget, above, 1_035, 110, 35);
		Assert.Equal(expectBelow, result.AtMostTarget.HasSelection);
		if (expectBelow)
		{
			Assert.Equal(SelectionStatus.Optimal, result.AtMostTarget.Status);
			Assert.True(result.AtMostTarget.IsOptimal);
			Assert.Equal(2, result.AtMostTarget.InputCount);
			Assert.Equal(1_180, result.AtMostTarget.TotalAmount.Satoshi);
			Assert.Equal(190, result.AtMostTarget.EstimatedFee.Satoshi);
			Assert.Equal(990, result.AtMostTarget.PaymentAmount.Satoshi);
			Assert.Equal(-10, result.AtMostTarget.PaymentDifference.Satoshi);
			Assert.Collection(result.AtMostTarget.SelectedInputs,
				input => Assert.Same(first, input), input => Assert.Same(second, input));
		}
		else
		{
			AssertEmpty(result.AtMostTarget, SelectionStatus.NoSolution, 1_000);
		}
	}

	[Fact]
	public void PositiveGroupsKeepUneconomicalMembersWhileNonpositiveGroupsAreExcluded()
	{
		BitcoinInput uneconomicalMember = Input(1, 10, 20, 9);
		BitcoinInput excludedGroup = Input(2, 20, 20, 8);
		BitcoinInput economicalMember = Input(3, 1_010, 20, 9);

		var result = BitcoinCoinSelector.SelectBoth(
			[uneconomicalMember, excludedGroup, economicalMember], Money.Satoshis(980), new FeeRate(1m), 0,
			new SelectionOptions { MaximumDifference = 0 }, cancellationToken: TestContext.Current.CancellationToken);

		foreach (BitcoinSelectionResult side in new[] { result.AtMostTarget, result.AtLeastTarget })
		{
			Assert.Equal(SelectionStatus.Optimal, side.Status);
			Assert.Equal(2, side.InputCount);
			Assert.Equal(1_020, side.TotalAmount.Satoshi);
			Assert.Equal(40, side.EstimatedFee.Satoshi);
			Assert.Equal(980, side.PaymentAmount.Satoshi);
			Assert.Collection(side.SelectedInputs,
				input => Assert.Same(uneconomicalMember, input), input => Assert.Same(economicalMember, input));
		}
	}

	[Fact]
	public void GroupingCanBeDisabledForBothSides()
	{
		BitcoinInput below = Input(1, 400, 1, 9);
		BitcoinInput exact = Input(2, 600, 1, 9);

		var result = BitcoinCoinSelector.SelectBoth(
			[below, exact], Money.Satoshis(600), FeeRate.Zero, 0,
			new SelectionOptions { MaximumDifference = 0 }, groupByScriptPubKey: false,
			cancellationToken: TestContext.Current.CancellationToken);

		AssertSuggestion(result.AtMostTarget, exact, 600, 0, 0);
		AssertSuggestion(result.AtLeastTarget, exact, 600, 0, 0);
	}

	[Fact]
	public void InputsAreReadOnlyOnceForTheSharedPreparation()
	{
		BitcoinInput below = Input(1, 990, 1);
		BitcoinInput above = Input(2, 1_015, 1);
		SingleReadInputs inputs = new([below, above]);

		var result = BitcoinCoinSelector.SelectBoth(inputs, Money.Satoshis(1_000), FeeRate.Zero, 0,
			new SelectionOptions { MaximumInputCount = 1 }, cancellationToken: TestContext.Current.CancellationToken);

		AssertSuggestion(result.AtMostTarget, below, 990, 0, -10);
		AssertSuggestion(result.AtLeastTarget, above, 1_015, 0, 15);
		Assert.All(inputs.ReadCounts, count => Assert.Equal(1, count));
	}

	[Fact]
	public void APreCancelledSearchReturnsBothCancelledSidesWithoutVisitingNodes()
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		var result = BitcoinCoinSelector.SelectBoth(
			[Input(1, 100, 1)], Money.Satoshis(100), FeeRate.Zero, 0,
			cancellationToken: cancellation.Token);

		Assert.Equal(0, result.VisitedNodes);
		AssertEmpty(result.AtMostTarget, SelectionStatus.Cancelled, 100);
		AssertEmpty(result.AtLeastTarget, SelectionStatus.Cancelled, 100);
	}

	[Fact]
	public void OneSharedNodeBudgetPreservesEachSidesBestValidSuggestion()
	{
		BitcoinInput above = Input(1, 111, 1);
		BitcoinInput below = Input(2, 99, 1);

		var result = BitcoinCoinSelector.SelectBoth(
			[above, below], Money.Satoshis(100), FeeRate.Zero, 0,
			new SelectionOptions { MaximumSearchNodes = 2 }, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(2, result.VisitedNodes);
		Assert.Equal(result.VisitedNodes, result.AtMostTarget.VisitedNodes);
		Assert.Equal(result.VisitedNodes, result.AtLeastTarget.VisitedNodes);
		AssertEmpty(result.AtMostTarget, SelectionStatus.SearchLimitReached, 100);
		Assert.True(result.AtLeastTarget.HasSelection);
		Assert.False(result.AtLeastTarget.IsOptimal);
		Assert.Equal(SelectionStatus.SearchLimitReached, result.AtLeastTarget.Status);
		Assert.Same(above, Assert.Single(result.AtLeastTarget.SelectedInputs));
		Assert.Equal(111, result.AtLeastTarget.PaymentAmount.Satoshi);
		Assert.Equal(11, result.AtLeastTarget.PaymentDifference.Satoshi);
	}

	[Fact]
	public void WideBoundsStillRequireAStrictlyPositivePaymentOnBothSides()
	{
		BitcoinInput zeroPayment = Input(1, 1_001, 1);
		BitcoinInput positivePayment = Input(2, 1_021, 1);

		var result = BitcoinCoinSelector.SelectBoth(
			[zeroPayment, positivePayment], Money.Satoshis(1), new FeeRate(1m), 1_000,
			new SelectionOptions { MaximumInputCount = 1 }, cancellationToken: TestContext.Current.CancellationToken);

		AssertEmpty(result.AtMostTarget, SelectionStatus.NoSolution, 1);
		AssertSuggestion(result.AtLeastTarget, positivePayment, 20, 1_001, 19);
	}

	[Fact]
	public void EmptyInputsProveNoSolutionOnBothSides()
	{
		var result = BitcoinCoinSelector.SelectBoth([], Money.Satoshis(100), FeeRate.Zero, 0,
			cancellationToken: TestContext.Current.CancellationToken);

		AssertEmpty(result.AtMostTarget, SelectionStatus.NoSolution, 100);
		AssertEmpty(result.AtLeastTarget, SelectionStatus.NoSolution, 100);
	}

	[Fact]
	public void ZeroSharedNodeBudgetIsRejected()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => BitcoinCoinSelector.SelectBoth(
			[Input(1, 100, 1)], Money.Satoshis(100), FeeRate.Zero, 0,
			new SelectionOptions { MaximumSearchNodes = 0 }, cancellationToken: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void CancellationDoesNotBypassDuplicateOutpointValidation()
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		Assert.Throws<ArgumentException>(() => BitcoinCoinSelector.SelectBoth(
			[Input(1, 100, 1, 1), Input(1, 200, 1, 2)], Money.Satoshis(100), FeeRate.Zero, 0,
			cancellationToken: cancellation.Token));
	}

	private static void AssertSuggestion(BitcoinSelectionResult result, BitcoinInput expected,
		long payment, long fee, long difference)
	{
		Assert.True(result.HasSelection);
		Assert.True(result.IsOptimal);
		Assert.Equal(SelectionStatus.Optimal, result.Status);
		Assert.Same(expected, Assert.Single(result.SelectedInputs));
		Assert.Same(expected.Coin, result.SelectedInputs[0].Coin);
		Assert.Equal(1, result.InputCount);
		Assert.Equal(payment + fee, result.TotalAmount.Satoshi);
		Assert.Equal(fee, result.EstimatedFee.Satoshi);
		Assert.Equal(payment, result.PaymentAmount.Satoshi);
		Assert.Equal(difference, result.PaymentDifference.Satoshi);
	}

	private static void AssertEmpty(BitcoinSelectionResult result, SelectionStatus status, long requestedAmount)
	{
		Assert.False(result.HasSelection);
		Assert.False(result.IsOptimal);
		Assert.Equal(status, result.Status);
		Assert.Empty(result.SelectedInputs);
		Assert.Equal(0, result.InputCount);
		Assert.Equal(0, result.TotalAmount.Satoshi);
		Assert.Equal(0, result.EstimatedFee.Satoshi);
		Assert.Equal(0, result.PaymentAmount.Satoshi);
		Assert.Equal(-requestedAmount, result.PaymentDifference.Satoshi);
	}

	private static BitcoinInput Input(int id, long amount, int size, int? scriptId = null)
	{
		byte[] scriptBytes = new byte[22];
		scriptBytes[1] = 20;
		BitConverter.GetBytes(scriptId ?? id).CopyTo(scriptBytes, 2);
		return new BitcoinInput(new Coin(uint256.Parse(id.ToString("x64")), 0,
			Money.Satoshis(amount), new Script(scriptBytes)), size);
	}

	private sealed class SingleReadInputs(BitcoinInput[] inputs) : IReadOnlyList<BitcoinInput>
	{
		public int[] ReadCounts { get; } = new int[inputs.Length];
		public int Count => inputs.Length;
		public BitcoinInput this[int index]
		{
			get
			{
				ReadCounts[index]++;
				if (ReadCounts[index] != 1) throw new InvalidOperationException("Input preparation was repeated.");
				return inputs[index];
			}
		}
		public IEnumerator<BitcoinInput> GetEnumerator() => ((IEnumerable<BitcoinInput>)inputs).GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
