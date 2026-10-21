using global::NBitcoin;

namespace CoinSelection.NBitcoin;

/// <summary>Selects Bitcoin inputs using caller-supplied virtual-size estimates.</summary>
public static class BitcoinCoinSelector
{
	/// <summary>Searches for inputs whose value after estimated fees matches the requested payment policy.</summary>
	/// <param name="inputs">Available coins and their positive input virtual-size estimates.</param>
	/// <param name="paymentAmount">The positive payment amount requested by the caller.</param>
	/// <param name="feeRate">The nonnegative fee rate used for each size estimate.</param>
	/// <param name="fixedVirtualSize">
	/// Nonnegative virtual bytes for all transaction overhead other than inputs, including outputs,
	/// version, lock time, marker and flag where applicable, and CompactSize counts for the contemplated transaction.
	/// </param>
	/// <param name="options">
	/// Core search policy and resource limits, or defaults when omitted. MinimumValue constrains the
	/// selected value after input fees but before the fixed fee; a strictly positive payment is always required.
	/// MaximumShortfall and MaximumExcess constrain the suggested payment relative to paymentAmount,
	/// after both input fees and the fixed fee. Use Closest mode to allow both sides in one search.
	/// </param>
	/// <param name="groupByScriptPubKey">
	/// Whether all coins sharing a script must be selected together. MaximumCandidateCount counts script
	/// groups when enabled and individual coins otherwise; MaximumInputCount always counts actual inputs.
	/// </param>
	/// <param name="cancellationToken">Cancels the bounded search.</param>
	/// <returns>A snapshot of the selected references, estimated amounts, and core search status.</returns>
	/// <remarks>
	/// This method estimates a single payment without change. It does not build or sign a transaction.
	/// Each input fee and the fixed fee are rounded up separately to whole satoshis using the
	/// fee rate's satoshis per 1,000 virtual bytes. Zero size or a zero fee rate incurs no fee.
	/// Groups with nonpositive value after input fees are excluded; uneconomical members of a positive
	/// script group remain in that group. Size estimates can differ from an actual transaction's size.
	/// Do not mutate coins or the input collection during this call. Returned coin objects remain caller-owned.
	/// </remarks>
	/// <exception cref="ArgumentNullException">A required argument, input, coin, outpoint, or output is null.</exception>
	/// <exception cref="ArgumentException">Outpoints are null Bitcoin outpoints or duplicated, or a coin is not denominated in Bitcoin.</exception>
	/// <exception cref="ArgumentOutOfRangeException">An amount, fee rate, or size is outside the documented range.</exception>
	/// <exception cref="OverflowException">Amounts or fees cannot be represented by signed 64-bit satoshi values.</exception>
	public static BitcoinSelectionResult Select(
		IReadOnlyList<BitcoinInput> inputs,
		Money paymentAmount,
		FeeRate feeRate,
		int fixedVirtualSize,
		SelectionOptions? options = null,
		bool groupByScriptPubKey = true,
		CancellationToken cancellationToken = default)
		=> Select(inputs, paymentAmount, feeRate, fixedVirtualSize, BitcoinFeePolicy.RoundUp,
			options, groupByScriptPubKey, cancellationToken);

	/// <summary>Searches for a changeless payment using an explicit fee calculation policy.</summary>
	/// <param name="inputs">Available coins and their positive input virtual-size estimates.</param>
	/// <param name="paymentAmount">The positive payment amount requested by the caller.</param>
	/// <param name="feeRate">The nonnegative fee rate used for each size estimate.</param>
	/// <param name="fixedVirtualSize">
	/// Nonnegative virtual bytes for all non-input transaction overhead and outputs, including
	/// version, lock time, marker and flag where applicable, and CompactSize counts.
	/// </param>
	/// <param name="feePolicy">The rounding and minimum applied to each input fee and the fixed fee separately.</param>
	/// <param name="options">
	/// Core search policy and resource limits, or defaults when null. MinimumValue constrains selected value
	/// after input fees but before the fixed fee; a strictly positive payment is always required.
	/// MaximumShortfall and MaximumExcess constrain payment after all estimated fees.
	/// </param>
	/// <param name="groupByScriptPubKey">
	/// Whether all coins sharing a script must be selected together. MaximumCandidateCount counts script
	/// groups when enabled and individual coins otherwise; MaximumInputCount always counts actual inputs.
	/// </param>
	/// <param name="cancellationToken">Cancels the bounded search.</param>
	/// <returns>A snapshot of the selected references, estimated amounts, and core search status.</returns>
	/// <remarks>
	/// Fees are estimated per input and for the fixed part before grouping, rather than on their combined size.
	/// The NBitcoin policy applies a one-satoshi minimum to every component, including a zero fixed size
	/// or zero fee rate. Both policies use widened multiplication and checked final satoshi amounts.
	/// Groups with nonpositive value after input fees are excluded; uneconomical members of a positive
	/// script group remain in that group. Size estimates can differ from an actual transaction's size.
	/// This method does not build or sign a transaction. Do not mutate coins or the input collection
	/// during this call. Returned coin objects remain caller-owned.
	/// </remarks>
	/// <exception cref="ArgumentNullException">A required argument, input, coin, outpoint, or output is null.</exception>
	/// <exception cref="ArgumentException">Outpoints are null Bitcoin outpoints or duplicated, or a coin is not denominated in Bitcoin.</exception>
	/// <exception cref="ArgumentOutOfRangeException">An amount, fee rate, size, fee policy, or search constraint is outside the documented range.</exception>
	/// <exception cref="OverflowException">Amounts or fees cannot be represented by signed 64-bit satoshi values.</exception>
	public static BitcoinSelectionResult Select(
		IReadOnlyList<BitcoinInput> inputs,
		Money paymentAmount,
		FeeRate feeRate,
		int fixedVirtualSize,
		BitcoinFeePolicy feePolicy,
		SelectionOptions? options,
		bool groupByScriptPubKey,
		CancellationToken cancellationToken)
	{
		PreparedSelection prepared = Prepare(inputs, paymentAmount, feeRate, fixedVirtualSize, feePolicy, options, groupByScriptPubKey);
		var selection = CoinSelector.Select(prepared.Candidates, prepared.Target, prepared.Options, cancellationToken);
		return MapResult(selection, prepared.FixedFee, paymentAmount);
	}

	/// <summary>Searches once for separate changeless payment suggestions on or below and on or above the requested amount.</summary>
	/// <param name="inputs">Available coins and their positive input virtual-size estimates.</param>
	/// <param name="paymentAmount">The positive payment amount requested by the caller.</param>
	/// <param name="feeRate">The nonnegative fee rate used for each size estimate.</param>
	/// <param name="fixedVirtualSize">Nonnegative virtual bytes for all non-input transaction overhead and outputs.</param>
	/// <param name="options">
	/// Shared search constraints and resource limits, or defaults when omitted. Mode is ignored.
	/// MaximumShortfall and MaximumExcess constrain the respective payment suggestions after all estimated fees.
	/// MinimumValue constrains selected value after input fees and before the fixed fee; every suggested payment is positive.
	/// </param>
	/// <param name="groupByScriptPubKey">
	/// Whether all coins sharing a script must be selected together. MaximumCandidateCount counts script
	/// groups when enabled and individual coins otherwise; MaximumInputCount always counts actual inputs.
	/// </param>
	/// <param name="cancellationToken">Cancels the shared search, preserving each side's best valid suggestion.</param>
	/// <returns>Independent results for both inclusive sides and the shared number of visited nodes.</returns>
	/// <remarks>
	/// An exact payment is eligible on both sides. Inputs are validated, grouped and assigned estimated fees once.
	/// The two results share one node budget and cancellation token. Check each side's HasSelection and Status independently.
	/// Each input fee and the fixed fee are rounded up separately; a zero size or zero fee rate incurs no fee.
	/// Groups with nonpositive value after input fees are excluded; uneconomical members of a positive
	/// script group remain in that group. Returned coin objects remain caller-owned.
	/// Do not mutate coins or the input collection during this call.
	/// This method does not construct, sign or broadcast a transaction.
	/// </remarks>
	/// <exception cref="ArgumentNullException">A required argument, input, coin, outpoint, or output is null.</exception>
	/// <exception cref="ArgumentException">Outpoints are null Bitcoin outpoints or duplicated, or a coin is not denominated in Bitcoin.</exception>
	/// <exception cref="ArgumentOutOfRangeException">An amount, fee rate, size, or search constraint is outside the documented range.</exception>
	/// <exception cref="OverflowException">Amounts or fees cannot be represented by signed 64-bit satoshi values.</exception>
	public static BitcoinTwoSidedSelectionResult SelectBoth(
		IReadOnlyList<BitcoinInput> inputs,
		Money paymentAmount,
		FeeRate feeRate,
		int fixedVirtualSize,
		SelectionOptions? options = null,
		bool groupByScriptPubKey = true,
		CancellationToken cancellationToken = default)
		=> SelectBoth(inputs, paymentAmount, feeRate, fixedVirtualSize, BitcoinFeePolicy.RoundUp,
			options, groupByScriptPubKey, cancellationToken);

	/// <summary>Searches once for changeless payments on or below and on or above the requested amount using an explicit fee policy.</summary>
	/// <param name="inputs">Available coins and their positive input virtual-size estimates.</param>
	/// <param name="paymentAmount">The positive payment amount requested by the caller.</param>
	/// <param name="feeRate">The nonnegative fee rate used for each size estimate.</param>
	/// <param name="fixedVirtualSize">Nonnegative virtual bytes for all non-input transaction overhead and outputs.</param>
	/// <param name="feePolicy">The rounding and minimum applied to each input fee and the fixed fee separately.</param>
	/// <param name="options">
	/// Shared search constraints and resource limits, or defaults when null. Mode is ignored.
	/// MaximumShortfall and MaximumExcess constrain the respective payments after all estimated fees.
	/// MinimumValue constrains selected value after input fees and before the fixed fee; every suggested payment is positive.
	/// </param>
	/// <param name="groupByScriptPubKey">
	/// Whether all coins sharing a script must be selected together. MaximumCandidateCount counts script
	/// groups when enabled and individual coins otherwise; MaximumInputCount always counts actual inputs.
	/// </param>
	/// <param name="cancellationToken">Cancels the shared search, preserving each side's best valid suggestion.</param>
	/// <returns>Independent results for both inclusive sides and the shared number of visited nodes.</returns>
	/// <remarks>
	/// An exact payment is eligible on both sides. Inputs are validated, grouped and assigned estimated fees once.
	/// Each input fee and the fixed fee are rounded separately before grouping. The NBitcoin policy applies
	/// a one-satoshi minimum to every component, including a zero fixed size or zero fee rate.
	/// Both policies use widened multiplication and checked final satoshi amounts.
	/// The two results share one node budget and cancellation token. Check each side's HasSelection and Status independently.
	/// Groups with nonpositive value after input fees are excluded; uneconomical members of a positive
	/// script group remain in that group. Do not mutate coins or the input collection during this call.
	/// Returned coin objects remain caller-owned. This method does not construct, sign or broadcast a transaction.
	/// </remarks>
	/// <exception cref="ArgumentNullException">A required argument, input, coin, outpoint, or output is null.</exception>
	/// <exception cref="ArgumentException">Outpoints are null Bitcoin outpoints or duplicated, or a coin is not denominated in Bitcoin.</exception>
	/// <exception cref="ArgumentOutOfRangeException">An amount, fee rate, size, fee policy, or search constraint is outside the documented range.</exception>
	/// <exception cref="OverflowException">Amounts or fees cannot be represented by signed 64-bit satoshi values.</exception>
	public static BitcoinTwoSidedSelectionResult SelectBoth(
		IReadOnlyList<BitcoinInput> inputs,
		Money paymentAmount,
		FeeRate feeRate,
		int fixedVirtualSize,
		BitcoinFeePolicy feePolicy,
		SelectionOptions? options,
		bool groupByScriptPubKey,
		CancellationToken cancellationToken)
	{
		PreparedSelection prepared = Prepare(inputs, paymentAmount, feeRate, fixedVirtualSize, feePolicy, options, groupByScriptPubKey);
		var selection = CoinSelector.SelectBoth(prepared.Candidates, prepared.Target, prepared.Options, cancellationToken);
		return new BitcoinTwoSidedSelectionResult(
			MapResult(selection.AtMostTarget, prepared.FixedFee, paymentAmount),
			MapResult(selection.AtLeastTarget, prepared.FixedFee, paymentAmount), selection.VisitedNodes);
	}

	private static PreparedSelection Prepare(
		IReadOnlyList<BitcoinInput> inputs,
		Money paymentAmount,
		FeeRate feeRate,
		int fixedVirtualSize,
		BitcoinFeePolicy feePolicy,
		SelectionOptions? options,
		bool groupByScriptPubKey)
	{
		ArgumentNullException.ThrowIfNull(inputs);
		ArgumentNullException.ThrowIfNull(paymentAmount);
		ArgumentNullException.ThrowIfNull(feeRate);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentAmount.Satoshi, nameof(paymentAmount));
		ArgumentOutOfRangeException.ThrowIfNegative(feeRate.FeePerK.Satoshi, nameof(feeRate));
		ArgumentOutOfRangeException.ThrowIfNegative(fixedVirtualSize);
		if (feePolicy is not BitcoinFeePolicy.RoundUp and not BitcoinFeePolicy.NBitcoin)
		{
			throw new ArgumentOutOfRangeException(nameof(feePolicy));
		}

		long fixedFee = EstimateFee(feeRate, fixedVirtualSize, feePolicy);
		long target = checked(paymentAmount.Satoshi + fixedFee);
		options ??= new SelectionOptions();
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MinimumValue, nameof(options));
		options = options with { MinimumValue = Math.Max(options.MinimumValue, checked(fixedFee + 1)) };
		HashSet<string> outpoints = new(StringComparer.Ordinal);
		Dictionary<string, InputGroup> scriptGroups = new(StringComparer.Ordinal);
		List<InputGroup> groups = new();

		for (int i = 0; i < inputs.Count; i++)
		{
			BitcoinInput input = inputs[i];
			ArgumentNullException.ThrowIfNull(input, nameof(inputs));
			ArgumentNullException.ThrowIfNull(input.Coin, nameof(inputs));
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.InputVirtualSize, nameof(inputs));

			ICoin coin = input.Coin;
			ArgumentNullException.ThrowIfNull(coin.Outpoint, nameof(inputs));
			ArgumentNullException.ThrowIfNull(coin.TxOut, nameof(inputs));
			ArgumentNullException.ThrowIfNull(coin.TxOut.ScriptPubKey, nameof(inputs));
			if (coin.Outpoint.IsNull || !outpoints.Add(coin.Outpoint.ToString()))
			{
				throw new ArgumentException("Every coin must have a unique, non-null Bitcoin outpoint.", nameof(inputs));
			}

			if (coin.Amount is not Money amount)
			{
				throw new ArgumentException("Every coin must be denominated in Bitcoin.", nameof(inputs));
			}

			ArgumentOutOfRangeException.ThrowIfNegative(amount.Satoshi, nameof(inputs));
			long inputFee = EstimateFee(feeRate, input.InputVirtualSize, feePolicy);
			string script = Convert.ToHexString(coin.TxOut.ScriptPubKey.ToBytes());
			InputGroup group;
			if (groupByScriptPubKey && scriptGroups.TryGetValue(script, out InputGroup? existing))
			{
				group = existing;
			}
			else
			{
				group = new InputGroup();
				groups.Add(group);
				if (groupByScriptPubKey)
				{
					scriptGroups.Add(script, group);
				}
			}

			group.Inputs.Add((i, input));
			group.Amount = checked(group.Amount + amount.Satoshi);
			group.InputFee = checked(group.InputFee + inputFee);
		}

		List<SelectionCandidate<InputGroup>> candidates = new();
		foreach (InputGroup group in groups)
		{
			long effectiveValue = checked(group.Amount - group.InputFee);
			if (effectiveValue > 0)
			{
				candidates.Add(new SelectionCandidate<InputGroup>(group, effectiveValue, group.InputFee, group.Inputs.Count));
			}
		}

		return new PreparedSelection(candidates, target, fixedFee, options);
	}

	private static BitcoinSelectionResult MapResult(SelectionResult<InputGroup> selection, long fixedFee, Money paymentAmount)
	{
		List<(int Index, BitcoinInput Input)> selectedInputs = new();
		long totalAmount = 0;
		foreach (SelectionCandidate<InputGroup> candidate in selection.SelectedCandidates)
		{
			selectedInputs.AddRange(candidate.Item.Inputs);
			totalAmount = checked(totalAmount + candidate.Item.Amount);
		}

		selectedInputs.Sort(static (left, right) => left.Index.CompareTo(right.Index));
		long estimatedFee = selection.HasSelection ? checked(selection.TotalCost + fixedFee) : 0;
		long payment = selection.HasSelection ? checked(totalAmount - estimatedFee) : 0;
		return new BitcoinSelectionResult(
			selectedInputs.Select(input => input.Input).ToArray(), selection.Status, selection.HasSelection, selection.IsOptimal,
			selection.VisitedNodes, selection.InputCount, Money.Satoshis(totalAmount),
			Money.Satoshis(estimatedFee), Money.Satoshis(payment), Money.Satoshis(checked(payment - paymentAmount.Satoshi)));
	}

	private static long EstimateFee(FeeRate feeRate, int virtualSize, BitcoinFeePolicy feePolicy)
	{
		Int128 product = (Int128)feeRate.FeePerK.Satoshi * virtualSize;
		return feePolicy == BitcoinFeePolicy.RoundUp
			? checked((long)((product + 999) / 1_000))
			: Math.Max(1, checked((long)(product / 1_000)));
	}

	private sealed class InputGroup
	{
		public List<(int Index, BitcoinInput Input)> Inputs { get; } = new();
		public long Amount { get; set; }
		public long InputFee { get; set; }
	}

	private readonly record struct PreparedSelection(
		List<SelectionCandidate<InputGroup>> Candidates, long Target, long FixedFee, SelectionOptions Options);
}
