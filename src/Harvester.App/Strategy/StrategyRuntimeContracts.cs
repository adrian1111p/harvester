using Harvester.App.IBKR.Broker;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed record StrategyRuntimeContext(
    string Mode,
    string Account,
    string Symbol,
    string? ModelCode,
    string ExchangeCalendar,
    DateTime RunStartedUtc,
    string OutputDirectory,
    string SessionStartUtc,
    string SessionEndUtc,
    int ScheduledIntervalSeconds,
    int MarketCloseWarningMinutes
);

public sealed record StrategyDataSlice(
    DateTime TimestampUtc,
    string Mode,
    IReadOnlyList<TopTickRow> TopTicks,
    IReadOnlyList<DepthRow> DepthRows,
    IReadOnlyList<HistoricalBarRow> HistoricalBars,
    IReadOnlyList<PositionRow> Positions,
    IReadOnlyList<AccountSummaryRow> AccountSummary,
    IReadOnlyList<CanonicalOrderEvent> CanonicalOrderEvents
);
