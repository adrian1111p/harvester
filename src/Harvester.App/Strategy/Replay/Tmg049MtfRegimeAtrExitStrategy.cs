using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg049MtfRegimeAtrExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_049_MTF_REGIME_ATR_EXIT";

    private readonly Tmg049MtfRegimeAtrExitConfig _config;
    private readonly IReplayMtfSignalSource _mtfSignalSource;
    private readonly Dictionary<string, AtrState> _stateBySymbol;
    private readonly HashSet<string> _triggeredSymbols;

    public Tmg049MtfRegimeAtrExitStrategy(
        IReplayMtfSignalSource mtfSignalSource,
        Tmg049MtfRegimeAtrExitConfig? config = null)
    {
        _mtfSignalSource = mtfSignalSource;
        _config = config ?? Tmg049MtfRegimeAtrExitConfig.Default;
        _stateBySymbol = new Dictionary<string, AtrState>(StringComparer.OrdinalIgnoreCase);
        _triggeredSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            _triggeredSymbols.Remove(symbol);
            return [];
        }

        if (_triggeredSymbols.Contains(symbol))
        {
            return [];
        }

        if (!_mtfSignalSource.TryGetSnapshot(symbol, out var snapshot))
        {
            return [];
        }

        if (_config.RequireAllTimeframes && !snapshot.HasAllTimeframes)
        {
            return [];
        }

        var currentState = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new AtrState(
                LastMarkPrice: context.MarkPrice,
                AtrProxyPct: 0.0,
                LastPositionSide: context.PositionQuantity > 0 ? "LONG" : "SHORT");

        var positionSide = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        if (!string.Equals(currentState.LastPositionSide, positionSide, StringComparison.OrdinalIgnoreCase))
        {
            currentState = new AtrState(
                LastMarkPrice: context.MarkPrice,
                AtrProxyPct: 0.0,
                LastPositionSide: positionSide);
        }

        if (currentState.LastMarkPrice > 0)
        {
            var trueRangePct = Math.Abs(context.MarkPrice - currentState.LastMarkPrice) / currentState.LastMarkPrice;
            var lookback = Math.Max(1, _config.AtrLookbackBars);
            var alpha = 2.0 / (lookback + 1.0);
            var nextAtr = currentState.AtrProxyPct <= 1e-12
                ? trueRangePct
                : ((alpha * trueRangePct) + ((1.0 - alpha) * currentState.AtrProxyPct));
            currentState = currentState with
            {
                LastMarkPrice = context.MarkPrice,
                AtrProxyPct = nextAtr,
                LastPositionSide = positionSide
            };
        }
        else
        {
            currentState = currentState with
            {
                LastMarkPrice = context.MarkPrice,
                LastPositionSide = positionSide
            };
        }

        _stateBySymbol[symbol] = currentState;

        var entry = Math.Max(1e-9, context.AveragePrice);
        var adverseMovePct = string.Equals(positionSide, "LONG", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(0.0, (entry - context.MarkPrice) / entry)
            : Math.Max(0.0, (context.MarkPrice - entry) / entry);
        var atrStopPct = Math.Max(0.0, _config.AtrStopMultiple) * Math.Max(0.0, currentState.AtrProxyPct);
        var atrStopTriggered = adverseMovePct >= atrStopPct;

        var oppositeRegimeTriggered = string.Equals(positionSide, "LONG", StringComparison.OrdinalIgnoreCase)
            ? snapshot.BearishEntryReady
            : snapshot.BullishEntryReady;
        var regimeTriggered = _config.RegimeExitRequiresOppositeAlignment
            ? oppositeRegimeTriggered
            : (snapshot.ExitLongSignal || snapshot.ExitShortSignal);

        if (!regimeTriggered && !atrStopTriggered)
        {
            return [];
        }

        _triggeredSymbols.Add(symbol);

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = string.Equals(positionSide, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var flattenOrderType = string.Equals(_config.FlattenOrderType, "MARKETABLE_LIMIT", StringComparison.OrdinalIgnoreCase)
            ? "LMT"
            : "MKT";
        var flattenLimitPrice = flattenOrderType == "LMT"
            ? (flattenSide == "BUY"
                ? (context.AskPrice > 0 ? context.AskPrice : context.MarkPrice * 1.001)
                : (context.BidPrice > 0 ? context.BidPrice : context.MarkPrice * 0.999))
            : (double?)null;

        var reason = regimeTriggered && atrStopTriggered
            ? "regime-and-atr"
            : (regimeTriggered ? "regime" : "atr");

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
                Source: $"trade-management:{StrategyId}:{reason}-cancel",
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
                Source: $"trade-management:{StrategyId}:{reason}-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record AtrState(
        double LastMarkPrice,
        double AtrProxyPct,
        string LastPositionSide
    );
}
