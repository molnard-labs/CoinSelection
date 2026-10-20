namespace CoinSelection.NBitcoin;

/// <summary>Separate changeless payment suggestions from one shared search.</summary>
/// <remarks>
/// An exact payment is eligible on both inclusive sides. Check each side's selection and completion state independently.
/// Coin references remain caller-owned, and amounts depend on the supplied size estimates rather than a built transaction.
/// </remarks>
public sealed class BitcoinTwoSidedSelectionResult
{
	internal BitcoinTwoSidedSelectionResult(
		BitcoinSelectionResult atMostTarget,
		BitcoinSelectionResult atLeastTarget,
		long visitedNodes)
	{
		AtMostTarget = atMostTarget;
		AtLeastTarget = atLeastTarget;
		VisitedNodes = visitedNodes;
	}

	/// <summary>Gets the best valid payment found on or below the requested amount, including an exact payment.</summary>
	public BitcoinSelectionResult AtMostTarget { get; }

	/// <summary>Gets the best valid payment found on or above the requested amount, including an exact payment.</summary>
	public BitcoinSelectionResult AtLeastTarget { get; }

	/// <summary>Gets the number of nodes visited by the shared core search, rather than the sum of two searches.</summary>
	public long VisitedNodes { get; }
}
