using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class Tmg004PartialTakeProfitRunnerTrailStrategy : IReplayTradeManagementStrategy
{
    public const string StrategyId = "TMG_004_PARTIAL_TP_RUNNER_TRAIL";

    private readonly Tmg004PartialTakeProfitRunnerTrailConfig _config;
    private readonly HashSet<string> _activatedSymbols;

    public Tmg004PartialTakeProfitRunnerTrailStrategy(Tmg004PartialTakeProfitRunnerTrailConfig? config = null)
    {
        _config = config ?? Tmg004PartialTakeProfitRunnerTrailConfig.Default;
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
        var fraction = Math.Clamp(_config.TakeProfitFraction, 0.0, 1.0);
        var takeProfitQty = qty * fraction;
        var runnerQty = Math.Max(0.0, qty - takeProfitQty);
        var exitSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
        var takeProfitPrice = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase)
            ? entry * (1.0 + _config.TakeProfitPct)
            : entry * (1.0 - _config.TakeProfitPct);
        var trailAmount = Math.Max(0.0001, context.MarkPrice * Math.Max(0.0, _config.RunnerTrailOffsetPct));

        var orders = new List<ReplayOrderIntent>
        {
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
                Source: $"trade-management:{StrategyId}:activate-cancel")
        };

        if (takeProfitQty > 1e-9)
        {
            orders.Add(new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: exitSide,
                Quantity: takeProfitQty,
                OrderType: "LMT",
                LimitPrice: takeProfitPrice,
                StopPrice: null,
                TrailAmount: null,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:partial-take-profit"));
        }

        if (runnerQty > 1e-9)
        {
            orders.Add(new ReplayOrderIntent(
                TimestampUtc: context.TimestampUtc,
                Symbol: symbol,
                Side: exitSide,
                Quantity: runnerQty,
                OrderType: "TRAIL",
                LimitPrice: null,
                StopPrice: null,
                TrailAmount: trailAmount,
                TrailPercent: null,
                TimeInForce: _config.TimeInForce,
                ExpireAtUtc: null,
                Source: $"trade-management:{StrategyId}:runner-trailing"));
        }

        return orders;
    }
}
