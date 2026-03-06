using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg009LiquiditySpreadExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_009_LIQUIDITY_SPREAD_EXIT_GUARD";

    private readonly Tmg009LiquiditySpreadExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg009LiquiditySpreadExitStrategy(Tmg009LiquiditySpreadExitConfig? config = null)
    {
        _config = config ?? Tmg009LiquiditySpreadExitConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
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

        var bid = context.BidPrice;
        var ask = context.AskPrice;
        if (bid <= 0 || ask <= 0 || ask < bid)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var mid = (bid + ask) / 2.0;
        if (mid <= 1e-9)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var spreadPct = (ask - bid) / mid;
        if (spreadPct < Math.Max(0.0, _config.SpreadTriggerPct))
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var entry = context.AveragePrice;
        var adverseMovePct = entry > 1e-9
            ? (string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(0.0, (entry - context.MarkPrice) / entry)
                : Math.Max(0.0, (context.MarkPrice - entry) / entry))
            : 0.0;

        if (_config.RequireUnrealizedLoss && adverseMovePct < Math.Max(0.0, _config.MinAdverseMovePct))
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
                ? ask
                : bid)
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
                Source: $"trade-management:{StrategyId}:spread-guard-cancel",
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
                Source: $"trade-management:{StrategyId}:spread-guard-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        bool Triggered
    );
}
