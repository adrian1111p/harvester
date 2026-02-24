using System.Text.Json;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class ScannerCandidateReplayRuntime : IStrategyRuntime, IReplayOrderSignalSource
{
    private readonly HashSet<string> _selectedSymbols;
    private readonly HashSet<string> _submittedSymbols;
    private readonly double _orderQuantity;
    private readonly string _orderType;
    private readonly string _timeInForce;

    public ScannerCandidateReplayRuntime(
        string candidatesInputPath,
        int topN,
        double minScore,
        double orderQuantity,
        string orderType,
        string timeInForce)
    {
        if (string.IsNullOrWhiteSpace(candidatesInputPath))
        {
            throw new ArgumentException("Replay scanner candidates input path is required.", nameof(candidatesInputPath));
        }

        var fullPath = Path.GetFullPath(candidatesInputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Replay scanner candidates input not found: {fullPath}");
        }

        var rows = JsonSerializer.Deserialize<ScannerCandidateInputRow[]>(File.ReadAllText(fullPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        _selectedSymbols = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
            .Where(x => x.Eligible is not false)
            .Where(x => x.WeightedScore >= minScore)
            .Select(x => new ScannerCandidateInputRow
            {
                Symbol = x.Symbol.Trim().ToUpperInvariant(),
                WeightedScore = x.WeightedScore,
                Eligible = x.Eligible,
                AverageRank = x.AverageRank
            })
            .OrderByDescending(x => x.WeightedScore)
            .ThenBy(x => x.AverageRank)
            .Take(Math.Max(1, topN))
            .Select(x => x.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _submittedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _orderQuantity = Math.Max(0, orderQuantity);
        _orderType = NormalizeOrderType(orderType);
        _timeInForce = NormalizeTimeInForce(timeInForce);
    }

    public Task InitializeAsync(StrategyRuntimeContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OnScheduledEventAsync(string eventName, StrategyRuntimeContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OnDataAsync(StrategyDataSlice dataSlice, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OnShutdownAsync(StrategyRuntimeContext context, int exitCode, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public IReadOnlyList<ReplayOrderIntent> GetReplayOrderIntents(StrategyDataSlice dataSlice, StrategyRuntimeContext context)
    {
        if (_orderQuantity <= 0)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        if (!_selectedSymbols.Contains(symbol) || _submittedSymbols.Contains(symbol))
        {
            return [];
        }

        if (dataSlice.Positions.Any(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase) && Math.Abs(x.Quantity) > 1e-9))
        {
            _submittedSymbols.Add(symbol);
            return [];
        }

        var markPrice = dataSlice.HistoricalBars.LastOrDefault()?.Close
            ?? dataSlice.TopTicks.LastOrDefault()?.Price
            ?? 0;

        double? limitPrice = null;
        if (_orderType == "LMT")
        {
            if (markPrice <= 0)
            {
                return [];
            }

            limitPrice = markPrice;
        }

        _submittedSymbols.Add(symbol);

        return [
            new ReplayOrderIntent(
                dataSlice.TimestampUtc,
                symbol,
                "BUY",
                _orderQuantity,
                _orderType,
                limitPrice,
                null,
                null,
                null,
                _timeInForce,
                null,
                "scanner-candidate")
        ];
    }

    private static string NormalizeOrderType(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "MKT" : value.Trim().ToUpperInvariant();
        return normalized is "MKT" or "LMT"
            ? normalized
            : "MKT";
    }

    private static string NormalizeTimeInForce(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "DAY" : value.Trim().ToUpperInvariant();
        return normalized is "DAY" or "GTC" or "IOC" or "FOK"
            ? normalized
            : "DAY";
    }

    private sealed class ScannerCandidateInputRow
    {
        public string Symbol { get; set; } = string.Empty;
        public double WeightedScore { get; set; }
        public bool? Eligible { get; set; }
        public double AverageRank { get; set; }
    }
}
