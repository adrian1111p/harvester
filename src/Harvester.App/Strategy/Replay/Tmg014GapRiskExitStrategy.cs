using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg014GapRiskExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_014_GAP_RISK_EXIT";

    private readonly Tmg014GapRiskExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg014GapRiskExitStrategy(Tmg014GapRiskExitConfig? config = null)
    {
        _config = config ?? Tmg014GapRiskExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0 || Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            return [];
        }

        var entry = context.AveragePrice;
        if (entry <= 1e-9)
        {
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var gapMovePct = Math.Abs(context.MarkPrice - entry) / entry;
        if (gapMovePct < Math.Max(0.0, _config.GapMovePct))
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var adverseDirection = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? context.MarkPrice < entry
            : context.MarkPrice > entry;
        if (_config.RequireAdverseDirection && !adverseDirection)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        state = state with { Triggered = true };
        _stateBySymbol[symbol] = state;

        var qty = Math.Abs(context.PositionQuantity);
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
                Source: $"trade-management:{StrategyId}:gap-cancel",
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
                Source: $"trade-management:{StrategyId}:gap-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        bool Triggered
    );
}
