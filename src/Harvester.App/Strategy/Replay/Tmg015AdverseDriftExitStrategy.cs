using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg015AdverseDriftExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_015_ADVERSE_DRIFT_EXIT";

    private readonly Tmg015AdverseDriftExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg015AdverseDriftExitStrategy(Tmg015AdverseDriftExitConfig? config = null)
    {
        _config = config ?? Tmg015AdverseDriftExitConfig.Default;
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

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, LastMarkPrice: context.MarkPrice, ConsecutiveAdverseBars: 0, CumulativeAdverseMovePct: 0.0, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, LastMarkPrice: context.MarkPrice, ConsecutiveAdverseBars: 0, CumulativeAdverseMovePct: 0.0, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var previousMark = state.LastMarkPrice;
        if (previousMark <= 1e-9)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var delta = context.MarkPrice - previousMark;
        var adverseMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (delta < 0 ? Math.Abs(delta) / previousMark : 0.0)
            : (delta > 0 ? Math.Abs(delta) / previousMark : 0.0);
        var adverseBar = adverseMovePct > 0.0;

        var consecutiveAdverseBars = adverseBar ? state.ConsecutiveAdverseBars + 1 : 0;
        var cumulativeAdverseMovePct = adverseBar
            ? state.CumulativeAdverseMovePct + adverseMovePct
            : 0.0;

        var minBars = Math.Max(1, _config.MinConsecutiveAdverseBars);
        var minCumMove = Math.Max(0.0, _config.MinCumulativeAdverseMovePct);
        var shouldFlatten = consecutiveAdverseBars >= minBars
            && cumulativeAdverseMovePct >= minCumMove;

        if (!shouldFlatten)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                ConsecutiveAdverseBars = consecutiveAdverseBars,
                CumulativeAdverseMovePct = cumulativeAdverseMovePct
            };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            LastMarkPrice = context.MarkPrice,
            ConsecutiveAdverseBars = consecutiveAdverseBars,
            CumulativeAdverseMovePct = cumulativeAdverseMovePct,
            Triggered = true
        };

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
                Source: $"trade-management:{StrategyId}:drift-cancel",
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
                Source: $"trade-management:{StrategyId}:drift-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double LastMarkPrice,
        int ConsecutiveAdverseBars,
        double CumulativeAdverseMovePct,
        bool Triggered
    );
}
