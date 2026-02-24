using System.Text.Json;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed record ReplayOrderIntent(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    double Quantity,
    string OrderType,
    double? LimitPrice,
    string Source
);

public sealed record ReplayFillRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    double Quantity,
    string OrderType,
    double FillPrice,
    double Commission,
    double RealizedPnlDelta,
    string Source
);

public sealed record ReplayPortfolioRow(
    DateTime TimestampUtc,
    string Symbol,
    double PositionQuantity,
    double AveragePrice,
    double MarketPrice,
    double Cash,
    double RealizedPnl,
    double UnrealizedPnl,
    double Equity
);

public sealed record ReplaySliceSimulationResult(
    IReadOnlyList<ReplayOrderIntent> Orders,
    IReadOnlyList<ReplayFillRow> Fills,
    ReplayPortfolioRow Portfolio
);

public sealed class ReplayExecutionSimulator
{
    private readonly double _commissionPerUnit;
    private readonly double _slippageBps;
    private double _cash;
    private double _positionQuantity;
    private double _averagePrice;
    private double _realizedPnl;

    public ReplayExecutionSimulator(double initialCash, double commissionPerUnit, double slippageBps)
    {
        _cash = initialCash;
        _commissionPerUnit = Math.Max(0, commissionPerUnit);
        _slippageBps = Math.Max(0, slippageBps);
    }

    public static IReadOnlyList<ReplayOrderIntent> LoadOrderIntents(string inputPath, int maxRows)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return [];
        }

        var fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Replay orders input not found: {fullPath}");
        }

        var rows = JsonSerializer.Deserialize<ReplayOrderIntent[]>(File.ReadAllText(fullPath)) ?? [];
        return rows
            .Where(r => r.TimestampUtc != default)
            .OrderBy(r => r.TimestampUtc)
            .Take(Math.Max(1, maxRows))
            .ToArray();
    }

    public ReplaySliceSimulationResult ProcessSlice(StrategyDataSlice slice, string defaultSymbol, IReadOnlyList<ReplayOrderIntent> intents)
    {
        var symbol = string.IsNullOrWhiteSpace(defaultSymbol)
            ? (slice.TopTicks.FirstOrDefault()?.Value ?? "N/A")
            : defaultSymbol;

        var fills = new List<ReplayFillRow>();
        var accepted = new List<ReplayOrderIntent>();

        var bar = slice.HistoricalBars.FirstOrDefault();
        var markPrice = bar is not null
            ? bar.Close
            : (slice.TopTicks.FirstOrDefault()?.Price ?? 0);

        foreach (var intent in intents)
        {
            if (intent.Quantity <= 0)
            {
                continue;
            }

            var normalized = intent with
            {
                TimestampUtc = intent.TimestampUtc == default ? slice.TimestampUtc : intent.TimestampUtc,
                Side = intent.Side.ToUpperInvariant(),
                OrderType = intent.OrderType.ToUpperInvariant(),
                Source = string.IsNullOrWhiteSpace(intent.Source) ? "strategy" : intent.Source
            };

            var fillPrice = ResolveFillPrice(normalized, bar, markPrice);
            if (fillPrice is null)
            {
                continue;
            }

            accepted.Add(normalized);
            var fill = ApplyFill(normalized, fillPrice.Value);
            fills.Add(fill);
        }

        var unrealizedPnl = (_positionQuantity == 0 || markPrice <= 0)
            ? 0
            : (markPrice - _averagePrice) * _positionQuantity;

        var equity = _cash + (_positionQuantity * markPrice);
        var portfolio = new ReplayPortfolioRow(
            slice.TimestampUtc,
            symbol,
            _positionQuantity,
            _averagePrice,
            markPrice,
            _cash,
            _realizedPnl,
            unrealizedPnl,
            equity);

        return new ReplaySliceSimulationResult(accepted, fills, portfolio);
    }

    private double? ResolveFillPrice(ReplayOrderIntent intent, HistoricalBarRow? bar, double markPrice)
    {
        var side = ParseSide(intent.Side);
        if (side == 0)
        {
            return null;
        }

        if (string.Equals(intent.OrderType, "LMT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intent.OrderType, "LIMIT", StringComparison.OrdinalIgnoreCase))
        {
            if (!intent.LimitPrice.HasValue || intent.LimitPrice <= 0)
            {
                return null;
            }

            var limit = intent.LimitPrice.Value;
            if (bar is null)
            {
                var canFillNoBar = side > 0 ? markPrice <= limit : markPrice >= limit;
                return canFillNoBar ? limit : null;
            }

            var canFill = side > 0
                ? bar.Low <= limit
                : bar.High >= limit;
            return canFill ? limit : null;
        }

        if (markPrice <= 0)
        {
            return null;
        }

        var slippageFactor = 1 + (side * (_slippageBps / 10000.0));
        return markPrice * slippageFactor;
    }

    private ReplayFillRow ApplyFill(ReplayOrderIntent intent, double fillPrice)
    {
        var side = ParseSide(intent.Side);
        var signedQuantity = side * intent.Quantity;
        var commission = intent.Quantity * _commissionPerUnit;

        var realizedDelta = 0.0;
        if (_positionQuantity != 0 && Math.Sign(_positionQuantity) != Math.Sign(signedQuantity))
        {
            var closingQuantity = Math.Min(Math.Abs(_positionQuantity), Math.Abs(signedQuantity));
            realizedDelta = (fillPrice - _averagePrice) * closingQuantity * Math.Sign(_positionQuantity);
            _realizedPnl += realizedDelta;
        }

        _cash -= signedQuantity * fillPrice;
        _cash -= commission;

        var previousQuantity = _positionQuantity;
        var nextQuantity = previousQuantity + signedQuantity;

        if (previousQuantity == 0 || Math.Sign(previousQuantity) == Math.Sign(signedQuantity))
        {
            var newAbs = Math.Abs(nextQuantity);
            if (newAbs > 0)
            {
                _averagePrice = ((Math.Abs(previousQuantity) * _averagePrice) + (Math.Abs(signedQuantity) * fillPrice)) / newAbs;
            }
            else
            {
                _averagePrice = 0;
            }
        }
        else
        {
            if (nextQuantity == 0)
            {
                _averagePrice = 0;
            }
            else if (Math.Sign(nextQuantity) != Math.Sign(previousQuantity))
            {
                _averagePrice = fillPrice;
            }
        }

        _positionQuantity = nextQuantity;

        return new ReplayFillRow(
            intent.TimestampUtc,
            intent.Symbol,
            intent.Side,
            intent.Quantity,
            intent.OrderType,
            fillPrice,
            commission,
            realizedDelta,
            intent.Source);
    }

    private static int ParseSide(string side)
    {
        return side.ToUpperInvariant() switch
        {
            "BUY" => 1,
            "SELL" => -1,
            _ => 0
        };
    }
}
