using global::NBitcoin;

namespace CoinSelection.NBitcoin;

/// <summary>An immutable snapshot of a Bitcoin coin selection and its estimated payment.</summary>
/// <remarks>
/// Monetary values describe the supplied size estimates, not a built transaction.
/// Selected coin references remain owned by the caller. Check <see cref="HasSelection"/> before using payment values.
/// </remarks>
public sealed class BitcoinSelectionResult
{
	internal BitcoinSelectionResult(
		BitcoinInput[] selectedInputs,
		SelectionStatus status,
		bool hasSelection,
		bool isOptimal,
		long visitedNodes,
		int inputCount,
		Money totalAmount,
		Money estimatedFee,
		Money paymentAmount,
		Money paymentDifference)
	{
		SelectedInputs = Array.AsReadOnly(selectedInputs);
		Status = status;
		HasSelection = hasSelection;
		IsOptimal = isOptimal;
		VisitedNodes = visitedNodes;
		InputCount = inputCount;
		TotalAmount = totalAmount;
		EstimatedFee = estimatedFee;
		PaymentAmount = paymentAmount;
		PaymentDifference = paymentDifference;
	}

	/// <summary>The selected original coin references in their original input order, in a read-only collection.</summary>
	public IReadOnlyList<BitcoinInput> SelectedInputs { get; }

	/// <summary>The search completion status supplied by the core selector.</summary>
	public SelectionStatus Status { get; }

	/// <summary>Whether the search found a selection satisfying the requested constraints.</summary>
	public bool HasSelection { get; }

	/// <summary>Whether the search proved its selection optimal among the admitted positive-effective-value groups.</summary>
	public bool IsOptimal { get; }

	/// <summary>The number of search nodes visited by the core selector.</summary>
	public long VisitedNodes { get; }

	/// <summary>The number of actual Bitcoin inputs, including every member of selected script groups.</summary>
	public int InputCount { get; }

	/// <summary>The sum of the selected input amounts, or zero without a selection.</summary>
	public Money TotalAmount { get; }

	/// <summary>The sum of separately estimated input fees and the fixed fee, or zero without a selection.</summary>
	public Money EstimatedFee { get; }

	/// <summary>The strictly positive total input amount minus estimated fees, or zero without a selection.</summary>
	public Money PaymentAmount { get; }

	/// <summary>The estimated payment amount minus the originally requested payment amount.</summary>
	public Money PaymentDifference { get; }
}
