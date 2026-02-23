using Harvester.App.IBKR.Broker;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed record StrategyRuntimeContext(
    string Mode,
    string Account,
    string Symbol,
    string? ModelCode,
    DateTime RunStartedUtc,
    string OutputDirectory,
    string SessionStartUtc,
    string SessionEndUtc,
    int ScheduledIntervalSeconds
);

public sealed record StrategyDataSlice(
    DateTime TimestampUtc,
    string Mode,
    IReadOnlyList<TopTickRow> TopTicks,
    IReadOnlyList<HistoricalBarRow> HistoricalBars,
    IReadOnlyList<PositionRow> Positions,
    IReadOnlyList<AccountSummaryRow> AccountSummary,
    IReadOnlyList<CanonicalOrderEvent> CanonicalOrderEvents
);
