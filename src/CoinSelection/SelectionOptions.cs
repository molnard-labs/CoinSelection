namespace CoinSelection;

/// <summary>Controls which side of the target may contain a solution.</summary>
public enum SelectionMode
{
	/// <summary>The selected value must equal or exceed the target.</summary>
	AtLeastTarget,

	/// <summary>The selected value must equal or fall below the target.</summary>
	AtMostTarget,

	/// <summary>The selected value may be on either side of the target.</summary>
	Closest
}

/// <summary>Constraints and deterministic work limits for a selection request.</summary>
public sealed record SelectionOptions
{
	/// <summary>Gets the permitted side of the target. Defaults to <see cref="SelectionMode.AtLeastTarget"/>.</summary>
	public SelectionMode Mode { get; init; } = SelectionMode.AtLeastTarget;

	/// <summary>Gets the inclusive, positive minimum summed value of a selection. Defaults to one.</summary>
	public long MinimumValue { get; init; } = 1;

	/// <summary>Gets the inclusive, nonnegative absolute difference permitted from the target.</summary>
	/// <remarks>This limit intersects <see cref="MaximumShortfall"/> and <see cref="MaximumExcess"/>.</remarks>
	public long MaximumDifference { get; init; } = long.MaxValue;

	/// <summary>Gets the positive maximum sum of selected candidates' underlying input counts.</summary>
	/// <remarks>Applies independently of <see cref="MaximumCandidateCount"/>. Defaults to unlimited.</remarks>
	public int MaximumInputCount { get; init; } = int.MaxValue;

	/// <summary>Gets the positive maximum number of selected candidates. Defaults to unlimited.</summary>
	/// <remarks>
	/// Each candidate counts once, including an indivisible group representing several inputs.
	/// <see cref="MaximumInputCount"/> separately limits the sum of their underlying input counts.
	/// Both limits apply independently to each result returned by <see cref="CoinSelector.SelectBoth{T}"/>.
	/// </remarks>
	public int MaximumCandidateCount { get; init; } = int.MaxValue;

	/// <summary>Gets the positive maximum number of visited search nodes, including the root node.</summary>
	/// <remarks>Forced omission chains and exhausted suffixes are contracted without entering additional nodes.</remarks>
	public long MaximumSearchNodes { get; init; } = 100_000;

	/// <summary>Gets the inclusive, nonnegative amount permitted below the target. Defaults to unlimited.</summary>
	/// <remarks>Applies when <see cref="Mode"/> permits below-target results and also respects <see cref="MaximumDifference"/>.</remarks>
	public long MaximumShortfall { get; init; } = long.MaxValue;

	/// <summary>Gets the inclusive, nonnegative amount permitted above the target. Defaults to unlimited.</summary>
	/// <remarks>Applies when <see cref="Mode"/> permits above-target results and also respects <see cref="MaximumDifference"/>.</remarks>
	public long MaximumExcess { get; init; } = long.MaxValue;
}
