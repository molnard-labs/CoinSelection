using global::NBitcoin;

namespace CoinSelection.NBitcoin;

/// <summary>A Bitcoin coin and the caller's estimate of its input virtual size.</summary>
/// <param name="Coin">The coin to consider. Its outpoint must be unique and non-null.</param>
/// <param name="InputVirtualSize">A positive input size in virtual bytes, including its witness contribution.</param>
/// <remarks>The adapter preserves the original coin reference and does not infer spending sizes.</remarks>
public sealed record BitcoinInput(ICoin Coin, int InputVirtualSize);
