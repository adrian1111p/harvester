using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg008SessionVwapReversionExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_008_SESSION_VWAP_REVERSION_EXIT";

    private readonly Tmg008SessionVwapReversionConfig _config;
    private readonly Dictionary<string, VwapState> _stateBySymbol;

    public Tmg008SessionVwapReversionExitStrategy(Tmg008SessionVwapReversionConfig? config = null)
    {
        _config = config ?? Tmg008SessionVwapReversionConfig.Default;
        _stateBySymbol = new Dictionary<string, VwapState>(StringComparer.OrdinalIgnoreCase);
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
            : new VwapState(Side: side, SampleCount: 0, CumPrice: 0.0, Triggered: false);

        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new VwapState(Side: side, SampleCount: 0, CumPrice: 0.0, Triggered: false);
        }

        state = state with
        {
            SampleCount = state.SampleCount + 1,
            CumPrice = state.CumPrice + context.MarkPrice
        };

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var minSamples = Math.Max(1, _config.MinSamples);
        if (state.SampleCount < minSamples)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var sessionVwap = state.CumPrice / Math.Max(1, state.SampleCount);
        var deviationPct = (context.MarkPrice - sessionVwap) / Math.Max(1e-9, sessionVwap);
        var adverseDeviation = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? -deviationPct
            : deviationPct;
        if (adverseDeviation < Math.Max(0.0, _config.AdverseDeviationPct))
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
                ? context.MarkPrice * 1.001
                : context.MarkPrice * 0.999)
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
                Source: $"trade-management:{StrategyId}:vwap-reversion-cancel",
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
                Source: $"trade-management:{StrategyId}:vwap-reversion-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record VwapState(
        string Side,
        int SampleCount,
        double CumPrice,
        bool Triggered
    );
}
