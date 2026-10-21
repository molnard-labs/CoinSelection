using CoinSelection;
using CoinSelection.NBitcoin;
using NBitcoin;

SelectionCandidate<string>[] candidates =
[
	new("coin-a", 70_000, Cost: 180),
	new("coin-b", 30_000, Cost: 80),
	new("coin-c", 20_000, Cost: 70)
];

Console.WriteLine("Core: exact target in integer units (Value is each coin's effective contribution).");
var exact = CoinSelector.Select(candidates, 50_000, new SelectionOptions
{
	Mode = SelectionMode.AtLeastTarget,
	MaximumDifference = 0,
	MaximumInputCount = 3,
	MaximumSearchNodes = 100_000
});
Console.WriteLine($"Status: {exact.Status}; optimal: {exact.IsOptimal}; nodes: {exact.VisitedNodes}");
if (exact.HasSelection)
{
	Console.WriteLine($"Selected: {string.Join(", ", exact.SelectedCandidates.Select(candidate => candidate.Item))}");
	Console.WriteLine($"Effective value: {exact.TotalValue}; cost: {exact.TotalCost}; inputs: {exact.InputCount}");
}

BitcoinInput[] bitcoinInputs =
[
	DemoInput(1, 60_000),
	DemoInput(2, 60_300),
	DemoInput(3, 30_000)
];
Money requestedPayment = Money.Satoshis(60_000);
FeeRate feeRate = new(Money.Satoshis(2_000)); // 2 sat/vB, expressed as satoshis per 1,000 vB.

Console.WriteLine();
Console.WriteLine("NBitcoin: one search for a changeless payment between 59500 and 60100 sat, target 60000 sat.");
var bitcoinResult = BitcoinCoinSelector.Select(bitcoinInputs, requestedPayment, feeRate,
	fixedVirtualSize: 50, // Conservative non-input estimate for this small, single-output example.
	options: new SelectionOptions
	{
		Mode = SelectionMode.Closest,
		MaximumShortfall = 500,
		MaximumExcess = 100,
		MaximumInputCount = 3,
		MaximumSearchNodes = 100_000
	});

Console.WriteLine($"Closest: {bitcoinResult.Status}; optimal: {bitcoinResult.IsOptimal}; nodes: {bitcoinResult.VisitedNodes}");
if (bitcoinResult.HasSelection)
{
	Console.WriteLine($"  Payment: {bitcoinResult.PaymentAmount.Satoshi} sat; difference: {bitcoinResult.PaymentDifference.Satoshi:+0;-0;0} sat");
	Console.WriteLine($"  Inputs: {bitcoinResult.InputCount}; amount: {bitcoinResult.TotalAmount.Satoshi} sat; estimated fee: {bitcoinResult.EstimatedFee.Satoshi} sat");
}

Console.WriteLine();
Console.WriteLine("NBitcoin: separate below/above payment suggestions from one shared search.");
var suggestions = BitcoinCoinSelector.SelectBoth(bitcoinInputs, requestedPayment, feeRate,
	fixedVirtualSize: 50,
	options: new SelectionOptions
	{
		MaximumShortfall = 500,
		MaximumExcess = 100,
		MaximumInputCount = 3,
		MaximumSearchNodes = 100_000
	});
PrintSuggestion("At most", suggestions.AtMostTarget);
PrintSuggestion("At least", suggestions.AtLeastTarget);
Console.WriteLine($"Shared nodes: {suggestions.VisitedNodes}");

Console.WriteLine();
Console.WriteLine("NBitcoin fee policy: 1.2 sat/vB rounds a 68 vB input to 81 sat and a 10 vB fixed part to 12 sat.");
var nbitcoinFees = BitcoinCoinSelector.Select(
	[DemoInput(4, 1_093)], Money.Satoshis(1_000), new FeeRate(1.2m), fixedVirtualSize: 10,
	feePolicy: BitcoinFeePolicy.NBitcoin, options: null, groupByScriptPubKey: true,
	cancellationToken: default);
PrintSuggestion("NBitcoin rounding", nbitcoinFees);

Console.WriteLine();
Console.WriteLine("SearchLimitReached may contain a usable selection, without proving optimality.");
Console.WriteLine("Callers must estimate all overhead (including CompactSize counts) and each input's signing path.");
Console.WriteLine("These are synthetic UTXOs; the example does not build, sign, or broadcast a transaction.");

static void PrintSuggestion(string label, BitcoinSelectionResult result)
{
	Console.WriteLine($"{label}: {result.Status}; optimal: {result.IsOptimal}");
	if (result.HasSelection)
	{
		Console.WriteLine($"  Payment: {result.PaymentAmount.Satoshi} sat; difference: {result.PaymentDifference.Satoshi:+0;-0;0} sat");
		Console.WriteLine($"  Inputs: {result.InputCount}; estimated fee: {result.EstimatedFee.Satoshi} sat");
	}
}

static BitcoinInput DemoInput(int id, long satoshis)
{
	byte[] keyHash = new byte[20];
	keyHash[^1] = (byte)id;
	var coin = new Coin(
		new OutPoint(uint256.Parse(id.ToString("x64")), 0),
		new TxOut(Money.Satoshis(satoshis), new WitKeyId(keyHash).ScriptPubKey));
	return new BitcoinInput(coin, InputVirtualSize: 68);
}
