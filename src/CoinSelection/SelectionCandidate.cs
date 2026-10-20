namespace CoinSelection;

/// <summary>A selectable item, or an indivisible group of items, with integer value and cost.</summary>
/// <typeparam name="T">The caller's item or group identifier.</typeparam>
/// <param name="Item">The original item returned when this candidate is selected.</param>
/// <param name="Value">The positive contribution toward the target, in caller-defined integer units.</param>
/// <param name="Cost">The nonnegative selection cost, used to break equal-distance ties.</param>
/// <param name="InputCount">The positive number of underlying inputs represented by the candidate.</param>
/// <remarks>Values and costs should each use a consistent unit across the request.</remarks>
public sealed record SelectionCandidate<T>(T Item, long Value, long Cost = 0, int InputCount = 1);
