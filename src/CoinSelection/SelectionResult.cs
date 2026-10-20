namespace CoinSelection;

/// <summary>Describes why a selection search stopped.</summary>
public enum SelectionStatus
{
	/// <summary>A selection was found and no selection can rank better.</summary>
	Optimal,

	/// <summary>The search proved that no nonempty selection satisfies the constraints.</summary>
	NoSolution,

	/// <summary>The node budget was exhausted; the result may contain a valid selection.</summary>
	SearchLimitReached,

	/// <summary>Cancellation was observed; the result may contain a valid selection.</summary>
	Cancelled
}

/// <summary>An immutable selection and the search's completion state.</summary>
/// <typeparam name="T">The caller's item or group identifier.</typeparam>
public sealed class SelectionResult<T>
{
	internal SelectionResult(
		SelectionCandidate<T>[] selectedCandidates,
		long totalValue,
		long totalCost,
		int inputCount,
		long visitedNodes,
		SelectionStatus status)
	{
		SelectedCandidates = Array.AsReadOnly(selectedCandidates);
		TotalValue = totalValue;
		TotalCost = totalCost;
		InputCount = inputCount;
		VisitedNodes = visitedNodes;
		Status = status;
	}

	/// <summary>Gets the selected candidates in their original input order.</summary>
	/// <remarks>The collection is immutable; caller-owned items are not deep-cloned.</remarks>
	public IReadOnlyList<SelectionCandidate<T>> SelectedCandidates { get; }

	/// <summary>Gets whether a nonempty, valid selection was found.</summary>
	public bool HasSelection => SelectedCandidates.Count != 0;

	/// <summary>Gets the sum of selected values, or zero when no selection was found.</summary>
	public long TotalValue { get; }

	/// <summary>Gets the sum of selected costs, or zero when no selection was found.</summary>
	public long TotalCost { get; }

	/// <summary>Gets the sum of selected candidates' underlying input counts.</summary>
	public int InputCount { get; }

	/// <summary>Gets the number of search nodes visited, including the root when visited.</summary>
	public long VisitedNodes { get; }

	/// <summary>Gets the reason the search stopped.</summary>
	public SelectionStatus Status { get; }

	/// <summary>Gets whether this selection was proven optimal.</summary>
	public bool IsOptimal => Status == SelectionStatus.Optimal;
}
