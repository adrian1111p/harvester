namespace Harvester.Contracts;

/// <summary>Runtime operation mode. Determines which IBKR workflow SnapshotRuntime executes.</summary>
public enum RunMode
{
    Connect,
    Orders,
    OrdersAllOpen,
    Positions,
    PositionsMonitor1Pct,
    PositionsMonitor1PctLoop,
    PositionsAutoReplaceScanLoop,
    SnapshotAll,
    ContractsValidate,
    OrdersDryRun,
    OrdersPlaceSim,
    OrdersCancelSim,
    OrdersWhatIf,
    TopData,
    MarketDepth,
    RealtimeBars,
    MarketDataAll,
    HistoricalBars,
    HistoricalBarsKeepUpToDate,
    Histogram,
    HistoricalTicks,
    HeadTimestamp,
    ManagedAccounts,
    FamilyCodes,
    AccountUpdates,
    AccountUpdatesMulti,
    AccountSummaryOnly,
    PositionsMulti,
    PnlAccount,
    PnlSingle,
    OptionChains,
    OptionExercise,
    OptionGreeks,
    CryptoPermissions,
    CryptoContract,
    CryptoStreaming,
    CryptoHistorical,
    CryptoOrder,
    FaAllocationGroups,
    FaGroupsProfiles,
    FaUnification,
    FaModelPortfolios,
    FaOrder,
    FundamentalData,
    WshFilters,
    ErrorCodes,
    ScannerExamples,
    ScannerComplex,
    ScannerParameters,
    ScannerWorkbench,
    ScannerPreview,
    DisplayGroupsQuery,
    DisplayGroupsSubscribe,
    DisplayGroupsUpdate,
    DisplayGroupsUnsubscribe,
    StrategyReplay,
    StrategyLiveV3,
    PositionsMonitorUi,
    BacktestRun,
    BacktestSweep,
    BacktestOptimize,
    BacktestScan,
    BacktestLiveSim,
    BacktestCompare,
    BacktestWalkForward,
}

/// <summary>Action to take when reconciliation gate check fails.</summary>
public enum ReconciliationGateAction
{
    Off,
    Warn,
    Fail
}

/// <summary>Action to take when clock skew exceeds thresholds.</summary>
public enum ClockSkewAction
{
    Off,
    Warn,
    Fail
}
