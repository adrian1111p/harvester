using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg005TimeStopStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_005_TIME_STOP";

    private readonly Tmg005TimeStopConfig _config;
    private readonly Dictionary<string, TimeStopState> _stateBySymbol;

    public Tmg005TimeStopStrategy(Tmg005TimeStopConfig? config = null)
    {
        _config = config ?? Tmg005TimeStopConfig.Default;
        _stateBySymbol = new Dictionary<string, TimeStopState>(StringComparer.OrdinalIgnoreCase);
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
        if (!_stateBySymbol.TryGetValue(symbol, out var state)
            || !string.Equals(state.Side, side, StringComparison.OrdinalIgnoreCase))
        {
            state = new TimeStopState(
                Side: side,
                EntryTimestampUtc: context.TimestampUtc,
                BarsHeld: 0,
                Triggered: false);
        }

        state = state with { BarsHeld = state.BarsHeld + 1 };

        if (state.Triggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        var elapsedMinutes = Math.Max(0, (context.TimestampUtc - state.EntryTimestampUtc).TotalMinutes);
        var barsTriggered = _config.MaxHoldingBars > 0 && state.BarsHeld >= _config.MaxHoldingBars;
        var minutesTriggered = _config.MaxHoldingMinutes > 0 && elapsedMinutes >= _config.MaxHoldingMinutes;
        if (!barsTriggered && !minutesTriggered)
        {
            _stateBySymbol[symbol] = state;
            return [];
        }

        state = state with { Triggered = true };
        _stateBySymbol[symbol] = state;

        var qty = Math.Abs(context.PositionQuantity);
        var flattenSide = context.PositionQuantity > 0 ? "SELL" : "BUY";
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
                Source: $"trade-management:{StrategyId}:time-stop-cancel",
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
                Source: $"trade-management:{StrategyId}:time-stop-flatten",
                Route: _config.FlattenRoute)
        ];
    }

    private sealed record TimeStopState(
        string Side,
        DateTime EntryTimestampUtc,
        int BarsHeld,
        bool Triggered
    );
}
