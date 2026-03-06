using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg006VolatilityAdaptiveExitStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_006_VOLATILITY_ADAPTIVE_EXIT";

    private readonly Tmg006VolatilityAdaptiveExitConfig _config;
    private readonly Dictionary<string, AdaptiveState> _stateBySymbol;

    public Tmg006VolatilityAdaptiveExitStrategy(Tmg006VolatilityAdaptiveExitConfig? config = null)
    {
        _config = config ?? Tmg006VolatilityAdaptiveExitConfig.Default;
        _stateBySymbol = new Dictionary<string, AdaptiveState>(StringComparer.OrdinalIgnoreCase);
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
        var shares = Math.Abs(context.PositionQuantity);
        var entry = context.AveragePrice;
        if (entry <= 0 || context.MarkPrice <= 0)
        {
            return [];
        }

        var state = _stateBySymbol.TryGetValue(symbol, out var existing)
            ? existing
            : new AdaptiveState(
                Side: side,
                Shares: shares,
                LastMarkPrice: context.MarkPrice,
                EmaAbsReturnPct: 0.0,
                ActiveRegime: "MID");

        if (!string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = state with
            {
                Side = side,
                Shares = shares,
                LastMarkPrice = context.MarkPrice,
                EmaAbsReturnPct = 0.0,
                ActiveRegime = "MID"
            };
        }

        var absReturnPct = state.LastMarkPrice > 1e-9
            ? Math.Abs(context.MarkPrice - state.LastMarkPrice) / state.LastMarkPrice
            : 0.0;
        var ema = state.EmaAbsReturnPct <= 0
            ? absReturnPct
            : (0.3 * absReturnPct) + (0.7 * state.EmaAbsReturnPct);
        var regime = ResolveRegime(ema);
        var (stopLossPct, takeProfitPct) = ResolveProfile(regime);

        var refreshNeeded = !string.Equals(state.ActiveRegime, regime, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase)
            || Math.Abs(state.Shares - shares) > 1e-9;

        state = state with
        {
            Side = side,
            Shares = shares,
            LastMarkPrice = context.MarkPrice,
            EmaAbsReturnPct = ema,
            ActiveRegime = regime
        };
        _stateBySymbol[symbol] = state;

        if (!refreshNeeded)
        {
            return [];
        }

        var exitSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var limitPrice = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? entry * (1.0 + takeProfitPct)
            : entry * (1.0 - takeProfitPct);
        var stopPrice = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? entry * (1.0 - stopLossPct)
            : entry * (1.0 + stopLossPct);
        var ocoGroup = $"{StrategyId}:{symbol}:{context.TimestampUtc:yyyyMMddHHmmssfff}";

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
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:refresh-cancel"),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: exitSide,
                Quantity: shares,
                OrderType: "LMT",
                LimitPrice: limitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:{regime}:take-profit",
                OcoGroup: ocoGroup),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: exitSide,
                Quantity: shares,
                OrderType: "STP",
                LimitPrice: null,
                StopPrice: stopPrice,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:{regime}:stop-loss",
                OcoGroup: ocoGroup)
        ];
    }

    private string ResolveRegime(double emaAbsReturnPct)
    {
        if (emaAbsReturnPct <= _config.LowVolThresholdPct)
        {
            return "LOW";
        }

        if (emaAbsReturnPct >= _config.HighVolThresholdPct)
        {
            return "HIGH";
        }

        return "MID";
    }

    private (double StopLossPct, double TakeProfitPct) ResolveProfile(string regime)
    {
        return regime switch
        {
            "LOW" => (_config.LowStopLossPct, _config.LowTakeProfitPct),
            "HIGH" => (_config.HighStopLossPct, _config.HighTakeProfitPct),
            _ => (_config.MidStopLossPct, _config.MidTakeProfitPct)
        };
    }

    private sealed record AdaptiveState(
        string Side,
        double Shares,
        double LastMarkPrice,
        double EmaAbsReturnPct,
        string ActiveRegime
    );
}
