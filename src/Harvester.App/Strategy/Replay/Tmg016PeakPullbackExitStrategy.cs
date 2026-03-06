using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg016PeakPullbackExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_016_PEAK_PULLBACK_EXIT";

    private readonly Tmg016PeakPullbackExitConfig _config;
    private readonly Dictionary<string, GuardState> _stateBySymbol;

    public Tmg016PeakPullbackExitStrategy(Tmg016PeakPullbackExitConfig? config = null)
    {
        _config = config ?? Tmg016PeakPullbackExitConfig.Default;
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

        var entry = context.AveragePrice;
        if (entry <= 1e-9)
        {
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new GuardState(Side: side, PeakPrice: context.MarkPrice, Triggered: false);
        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new GuardState(Side: side, PeakPrice: context.MarkPrice, Triggered: false);
        }

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var peakPrice = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(state.PeakPrice, context.MarkPrice)
            : Math.Min(state.PeakPrice <= 1e-9 ? context.MarkPrice : state.PeakPrice, context.MarkPrice);

        var profitPctFromEntry = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? (peakPrice - entry) / entry
            : (entry - peakPrice) / entry;
        var activationReached = profitPctFromEntry >= Math.Max(0.0, _config.ActivationProfitPct);
        if (!activationReached)
        {
            _stateBySymbol[symbol] = state with { PeakPrice = peakPrice };
            return [];
        }

        var pullbackPct = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(0.0, (peakPrice - context.MarkPrice) / peakPrice)
            : Math.Max(0.0, (context.MarkPrice - peakPrice) / peakPrice);
        if (pullbackPct < Math.Max(0.0, _config.PullbackFromPeakPct))
        {
            _stateBySymbol[symbol] = state with { PeakPrice = peakPrice };
            return [];
        }

        _stateBySymbol[symbol] = state with
        {
            PeakPrice = peakPrice,
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
                Source: $"trade-management:{StrategyId}:pullback-cancel",
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
                Source: $"trade-management:{StrategyId}:pullback-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record GuardState(
        string Side,
        double PeakPrice,
        bool Triggered
    );
}
