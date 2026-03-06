using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg024RangeCompressionExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_024_RANGE_COMPRESSION_EXIT";

    private readonly Tmg024RangeCompressionExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg024RangeCompressionExitStrategy(Tmg024RangeCompressionExitConfig? config = null)
    {
        _config = config ?? Tmg024RangeCompressionExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0 || context.AveragePrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, RecentMarks: [], Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, RecentMarks: [], Triggered: false);
        }

        if (state.Triggered)
        {
            return [];
        }

        var windowBars = Math.Max(1, _config.WindowBars);
        var marks = state.RecentMarks.Count > 0
            ? state.RecentMarks.ToList()
            : [];
        marks.Add(context.MarkPrice);
        if (marks.Count > windowBars)
        {
            marks.RemoveAt(0);
        }

        _stateBySymbol[symbol] = state with { RecentMarks = marks };
        if (marks.Count < windowBars)
        {
            return [];
        }

        var minMark = marks.Min();
        var maxMark = marks.Max();
        var baseMark = Math.Max(1e-9, minMark);
        var rangePct = (maxMark - minMark) / baseMark;
        if (rangePct > Math.Max(0.0, _config.MaxRangePct))
        {
            return [];
        }

        var entry = Math.Max(1e-9, context.AveragePrice);
        var qty = Math.Abs(context.PositionQuantity);
        var unrealizedPnl = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entry) * qty
            : (entry - context.MarkPrice) * qty;
        if (_config.RequireAdverseUnrealized && unrealizedPnl >= 0)
        {
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            RecentMarks = marks,
            Triggered = true
        };

        var flattenSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        return
        [
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
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
                Source: $"trade-management:{StrategyId}:compression-cancel",
                Route: _config.FlattenRoute),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
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
                Source: $"trade-management:{StrategyId}:compression-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        IReadOnlyList<double> RecentMarks,
        bool Triggered
    );
}
