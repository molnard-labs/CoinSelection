namespace CoinSelection;

/// <summary>Separate selections at or below and at or above one target, from a shared search.</summary>
/// <typeparam name="T">The caller's item or group identifier.</typeparam>
public sealed class TwoSidedSelectionResult<T>
{
	internal TwoSidedSelectionResult(SelectionResult<T> atMostTarget, SelectionResult<T> atLeastTarget)
	{
		AtMostTarget = atMostTarget;
		AtLeastTarget = atLeastTarget;
	}

	/// <summary>Gets the best selection found whose total value does not exceed the target.</summary>
	public SelectionResult<T> AtMostTarget { get; }

	/// <summary>Gets the best selection found whose total value equals or exceeds the target.</summary>
	public SelectionResult<T> AtLeastTarget { get; }

	/// <summary>Gets the shared visited-node count, also reported by both directional results.</summary>
	public long VisitedNodes => AtMostTarget.VisitedNodes;
}
