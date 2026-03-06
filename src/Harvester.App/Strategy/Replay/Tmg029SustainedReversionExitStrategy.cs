using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg029SustainedReversionExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_029_SUSTAINED_REVERSION_EXIT";

    private readonly Tmg029SustainedReversionExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg029SustainedReversionExitStrategy(Tmg029SustainedReversionExitConfig? config = null)
    {
        _config = config ?? Tmg029SustainedReversionExitConfig.Default;
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
            : new GuardState(
                Side: side,
                LastMarkPrice: context.MarkPrice,
                PeakProfitPct: 0,
                ConsecutiveAdverseBars: 0,
                AdverseMoveSumPct: 0,
                Triggered: false);

        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(
                Side: side,
                LastMarkPrice: context.MarkPrice,
                PeakProfitPct: 0,
                ConsecutiveAdverseBars: 0,
                AdverseMoveSumPct: 0,
                Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state with { LastMarkPrice = context.MarkPrice };
            return [];
        }

        var entryPrice = Math.Max(1e-9, context.AveragePrice);
        var openProfitPct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entryPrice) / entryPrice
            : (entryPrice - context.MarkPrice) / entryPrice;
        var peakProfitPct = Math.Max(state.PeakProfitPct, openProfitPct);

        var previousMark = state.LastMarkPrice;
        var consecutiveAdverseBars = state.ConsecutiveAdverseBars;
        var adverseMoveSumPct = state.AdverseMoveSumPct;
        if (previousMark > 1e-9)
        {
            var signedMovePct = (context.MarkPrice - previousMark) / previousMark;
            var adverseMovePct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(0.0, -signedMovePct)
                : Math.Max(0.0, signedMovePct);
            if (adverseMovePct > 0)
            {
                consecutiveAdverseBars += 1;
                adverseMoveSumPct += adverseMovePct;
            }
            else
            {
                consecutiveAdverseBars = 0;
                adverseMoveSumPct = 0;
            }
        }

        var minPeakProfitPct = Math.Max(0.0, _config.MinPeakProfitPct);
        var requiredAdverseBars = Math.Max(1, _config.ConsecutiveAdverseBars);
        var minAdverseMoveSumPct = Math.Max(0.0, _config.MinAdverseMoveSumPct);

        var qty = Math.Abs(context.PositionQuantity);
        var unrealizedPnl = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (context.MarkPrice - entryPrice) * qty
            : (entryPrice - context.MarkPrice) * qty;

        var shouldTrigger = peakProfitPct >= minPeakProfitPct
            && consecutiveAdverseBars >= requiredAdverseBars
            && adverseMoveSumPct >= minAdverseMoveSumPct
            && (!_config.RequireAdverseUnrealized || unrealizedPnl < 0);

        if (!shouldTrigger)
        {
            _stateBySymbol[symbol] = state with
            {
                LastMarkPrice = context.MarkPrice,
                PeakProfitPct = peakProfitPct,
                ConsecutiveAdverseBars = consecutiveAdverseBars,
                AdverseMoveSumPct = adverseMoveSumPct
            };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            LastMarkPrice = context.MarkPrice,
            PeakProfitPct = peakProfitPct,
            ConsecutiveAdverseBars = consecutiveAdverseBars,
            AdverseMoveSumPct = adverseMoveSumPct,
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
                Source: $"trade-management:{StrategyId}:reversion-cancel",
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
                Source: $"trade-management:{StrategyId}:reversion-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double LastMarkPrice,
        double PeakProfitPct,
        int ConsecutiveAdverseBars,
        double AdverseMoveSumPct,
        bool Triggered
    );
}
