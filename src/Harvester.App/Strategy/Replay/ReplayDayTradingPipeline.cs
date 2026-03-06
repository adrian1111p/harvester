using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class ReplayDayTradingPipeline
{
    private readonly IReadOnlyList<IReplayGlobalSafetyOverlayStrategy> _globalSafetyOverlays;
    private readonly IReadOnlyList<IReplayEntryStrategy> _entryStrategies;
    private readonly IReadOnlyList<IReplayTradeManagementStrategy> _tradeManagementStrategies;
    private readonly IReadOnlyList<IReplayEndOfDayStrategy> _endOfDayStrategies;

    public ReplayDayTradingPipeline(
        IReadOnlyList<IReplayGlobalSafetyOverlayStrategy> globalSafetyOverlays,
        IReadOnlyList<IReplayEntryStrategy> entryStrategies,
        IReadOnlyList<IReplayTradeManagementStrategy> tradeManagementStrategies,
        IReadOnlyList<IReplayEndOfDayStrategy> endOfDayStrategies)
    {
        _globalSafetyOverlays = globalSafetyOverlays;
        _entryStrategies = entryStrategies;
        _tradeManagementStrategies = tradeManagementStrategies;
        _endOfDayStrategies = endOfDayStrategies;
    }

    public IReadOnlyList<ReplayOrderIntent> Evaluate(
        ReplayDayTradingContext context,
        ReplayScannerSymbolSelectionSnapshotRow selection)
    {
        var orders = new List<ReplayOrderIntent>();

        foreach (var overlay in _globalSafetyOverlays)
        {
            var decision = overlay.Evaluate(context);
            if (decision.Orders.Count > 0)
            {
                orders.AddRange(decision.Orders);
            }

            if (decision.StopFurtherProcessing)
            {
                return orders;
            }
        }

        foreach (var entry in _entryStrategies)
        {
            var entryOrders = entry.Evaluate(context, selection);
            if (entryOrders.Count > 0)
            {
                orders.AddRange(entryOrders);
            }
        }

        foreach (var management in _tradeManagementStrategies)
        {
            var managementOrders = management.Evaluate(context);
            if (managementOrders.Count > 0)
            {
                orders.AddRange(managementOrders);
            }
        }

        foreach (var endOfDay in _endOfDayStrategies)
        {
            var eodOrders = endOfDay.Evaluate(context);
            if (eodOrders.Count > 0)
            {
                orders.AddRange(eodOrders);
            }
        }

        return orders;
    }
}
