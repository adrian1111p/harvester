using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg010EventRiskCooldownGuardStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_010_EVENT_RISK_COOLDOWN_GUARD";

    private readonly Tmg010EventRiskCooldownConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg010EventRiskCooldownGuardStrategy(Tmg010EventRiskCooldownConfig? config = null)
    {
        _config = config ?? Tmg010EventRiskCooldownConfig.Default;
        _stateBySymbol = new Dictionary<string, GuardState>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context)
    {
        if (!_config.Enabled)
        {
            return [];
        }

        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || context.MarkPrice <= 0)
        {
            return [];
        }

        if (Math.Abs(context.PositionQuantity) <= 1e-9)
        {
            _stateBySymbol.Remove(symbol);
            return [];
        }

        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(LastMarkPrice: context.MarkPrice, CooldownBarsRemaining: 0);

        var shockMovePct = state.LastMarkPrice > 1e-9
            ? Math.Abs(context.MarkPrice - state.LastMarkPrice) / state.LastMarkPrice
            : 0.0;
        var spreadPct = 0.0;
        if (context.BidPrice > 0 && context.AskPrice > 0 && context.AskPrice >= context.BidPrice)
        {
            var mid = (context.BidPrice + context.AskPrice) / 2.0;
            if (mid > 1e-9)
            {
                spreadPct = (context.AskPrice - context.BidPrice) / mid;
            }
        }

        var riskEvent = shockMovePct >= Math.Max(0.0, _config.ShockMovePct)
            || spreadPct >= Math.Max(0.0, _config.SpreadTriggerPct);

        if (state.CooldownBarsRemaining > 0)
        {
            state = state with
            {
                LastMarkPrice = context.MarkPrice,
                CooldownBarsRemaining = state.CooldownBarsRemaining - 1
            };
            _stateBySymbol[symbol] = state;
            return [];
        }

        if (!riskEvent)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
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

        _stateBySymbol[symbol] = state with
        {
            LastMarkPrice = context.MarkPrice,
            CooldownBarsRemaining = Math.Max(0, _config.CooldownBars)
        };

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
                Source: $"trade-management:{StrategyId}:risk-cancel",
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
                Source: $"trade-management:{StrategyId}:risk-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        double LastMarkPrice,
        int CooldownBarsRemaining
    );
}
