using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg002BreakEvenEscalationStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_002_BREAK_EVEN_ESCALATION";

    private readonly Tmg002BreakEvenConfig _config;
    private readonly HashSet<string> _activatedSymbols;

    public Tmg002BreakEvenEscalationStrategy(Tmg002BreakEvenConfig? config = null)
    {
        _config = config ?? Tmg002BreakEvenConfig.Default;
        _activatedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            _activatedSymbols.Remove(symbol);
            return [];
        }

        if (_activatedSymbols.Contains(symbol))
        {
            return [];
        }

        var entry = context.AveragePrice;
        if (entry <= 0 || context.MarkPrice <= 0)
        {
            return [];
        }

        var side = context.PositionQuantity > 0 ? "LONG" : "SHORT";
        var triggerPx = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? entry * (1.0 + _config.TriggerProfitPct)
            : entry * (1.0 - _config.TriggerProfitPct);
        var triggered = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? context.MarkPrice >= triggerPx
            : context.MarkPrice <= triggerPx;
        if (!triggered)
        {
            return [];
        }

        _activatedSymbols.Add(symbol);

        var qty = Math.Abs(context.PositionQuantity);
        var stopSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var baseStop = entry;
        var stopOffset = Math.Max(0.0, _config.StopOffsetPct) * entry;
        var stopPrice = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? baseStop + stopOffset
            : Math.Max(0.0001, baseStop - stopOffset);

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
                Source: $"trade-management:{StrategyId}:escalate-cancel"),
            new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: stopSide,
                Quantity: qty,
                OrderType: "STP",
                LimitPrice: null,
                StopPrice: stopPrice,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:break-even-stop")
        ];
    }
}
