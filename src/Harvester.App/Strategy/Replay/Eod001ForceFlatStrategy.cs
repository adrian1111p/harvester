using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Eod001ForceFlatStrategy : IReplayEndOfDayStrategy
{
    public const string StrategyId = "EOD_001_FORCE_FLAT";

    private readonly Eod001ForceFlatConfig _config;
    private readonly HashSet<string> _flattenedBySymbolAndDate;

    public Eod001ForceFlatStrategy(Eod001ForceFlatConfig? config = null)
    {
        _config = config ?? Eod001ForceFlatConfig.Default;
        _flattenedBySymbolAndDate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            return [];
        }

        var ts = context.TimestampUtc;
        var sessionCloseUtc = new DateTime(
            ts.Year,
            ts.Month,
            ts.Day,
            Math.Clamp(_config.SessionCloseHourUtc, 0, 23),
            Math.Clamp(_config.SessionCloseMinuteUtc, 0, 59),
            0,
            DateTimeKind.Utc);
        var triggerAtUtc = sessionCloseUtc.AddMinutes(-Math.Max(0, _config.FlattenLeadMinutes));
        if (ts < triggerAtUtc)
        {
            return [];
        }

        var key = $"{symbol}:{ts:yyyyMMdd}";
        if (_flattenedBySymbolAndDate.Contains(key))
        {
            return [];
        }

        _flattenedBySymbolAndDate.Add(key);

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = context.PositionQuantity > 0 ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? context.MarkPrice * 1.001
                : context.MarkPrice * 0.999)
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: ts,
                Symbol: symbol,
                Side: string.Empty,
                Quantity: 0,
                OrderType: "CANCEL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"end-of-day:{StrategyId}:cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: ts,
                Symbol: symbol,
                Side: flattenSide,
                Quantity: qty,
                OrderType: flattenOrderType,
                LimitPrice: flattenLimitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.FlattenTif,
                ExpireAtUtc: null,
                Source: $"end-of-day:{StrategyId}:flatten",
                Route: _config.FlattenRoute)
        ];
    }
}
