namespace CoinSelection.NBitcoin;

/// <summary>Controls the rounding of each input fee and the fixed fee, estimated separately.</summary>
/// <remarks>
/// Both policies use the fee rate's integer satoshis per 1,000 virtual bytes. Intermediate
/// multiplication is widened to avoid overflow; a fee outside signed 64-bit satoshis throws
/// <see cref="OverflowException"/>.
/// </remarks>
public enum BitcoinFeePolicy
{
	/// <summary>Rounds each fee up to whole satoshis. A zero size or zero fee rate incurs no fee. This is the default.</summary>
	RoundUp = 0,

	/// <summary>Rounds each fee down to whole satoshis, with a minimum of one satoshi even for a zero size or zero fee rate.</summary>
	/// <remarks>
	/// Matches the rounding and minimum of <see cref="global::NBitcoin.FeeRate.GetFee(int)"/>.
	/// Intermediate multiplication is widened rather than reproducing NBitcoin's unchecked overflow.
	/// </remarks>
	NBitcoin = 1
}
