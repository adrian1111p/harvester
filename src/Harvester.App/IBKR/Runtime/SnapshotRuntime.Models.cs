using System.Text.Json.Serialization;
using Harvester.App.IBKR.Risk;

namespace Harvester.App.IBKR.Runtime;

public sealed record AppOptions(
    RunMode Mode,
    string Host,
    int Port,
    int ClientId,
    string Account,
    int TimeoutSeconds,
    string ExportDir,
    string Symbol,
    string PrimaryExchange,
    bool EnableLive,
    string LiveSymbol,
    string LiveAction,
    string LiveOrderType,
    double LiveQuantity,
    double LiveLimitPrice,
    bool LivePriceSanityRequireQuote,
    bool LiveMomentumGuardEnabled,
    double LiveMomentumMaxAdverseBps,
    int CancelOrderId,
    bool CancelOrderIdempotent,
    double MaxNotional,
    double MaxShares,
    double MaxPrice,
    string[] AllowedSymbols
    ,
    string LiveScannerCandidatesInputPath,
    string LiveScannerOpenPhaseInputPath,
    string LiveScannerPostOpenGainersInputPath,
    string LiveScannerPostOpenLosersInputPath,
    int LiveScannerOpenPhaseMinutes,
    int LiveScannerPostOpenMinutes,
    int LiveScannerTopN,
    double LiveScannerMinScore,
    string LiveAllocationMode,
    double LiveAllocationBudget,
    int LiveScannerKillSwitchMaxFileAgeMinutes,
    int LiveScannerKillSwitchMinCandidates,
    double LiveScannerKillSwitchMaxBudgetConcentrationPct,
    string WhatIfTemplate,
    int MarketDataType,
    int CaptureSeconds,
    int DepthRows,
    string DepthExchange,
    string RealTimeBarsWhatToShow,
    string HistoricalEndDateTime,
    string HistoricalDuration,
    string HistoricalBarSize,
    string HistoricalWhatToShow,
    int HistoricalUseRth,
    int HistoricalFormatDate,
    string HistogramPeriod,
    string HistoricalTickStart,
    string HistoricalTickEnd,
    int HistoricalTicksNumber,
    string HistoricalTicksWhatToShow,
    bool HistoricalTickIgnoreSize,
    string HeadTimestampWhatToShow,
    string UpdateAccount,
    string AccountSummaryGroup,
    string AccountSummaryTags,
    string AccountUpdatesMultiAccount,
    string PositionsMultiAccount,
    string ModelCode,
    string PnlAccount,
    int PnlConId,
    string OptionSymbol,
    string OptionExpiry,
    double OptionStrike,
    string OptionRight,
    string OptionExchange,
    string OptionCurrency,
    string OptionMultiplier,
    string OptionUnderlyingSecType,
    string OptionFutFopExchange,
    bool OptionExerciseAllow,
    int OptionExerciseAction,
    int OptionExerciseQuantity,
    int OptionExerciseOverride,
    string OptionExerciseManualTime,
    bool OptionGreeksAutoFallback,
    string CryptoSymbol,
    string CryptoExchange,
    string CryptoCurrency,
    bool CryptoOrderAllow,
    string CryptoOrderAction,
    double CryptoOrderQuantity,
    double CryptoOrderLimit,
    double CryptoMaxNotional,
    string FaAccount,
    string FaModelCode,
    bool FaOrderAllow,
    string FaOrderAccount,
    string FaOrderSymbol,
    string FaOrderAction,
    double FaOrderQuantity,
    double FaOrderLimit,
    double FaMaxNotional,
    string FaOrderGroup,
    string FaOrderMethod,
    string FaOrderPercentage,
    string FaOrderProfile,
    string FaOrderExchange,
    string FaOrderPrimaryExchange,
    string FaOrderCurrency,
    FaRoutingStrictness FaRoutingStrictness,
    string PreTradeControlsDsl,
    int PreTradeMaxDailyOrders,
    string PreTradeSessionStartUtc,
    string PreTradeSessionEndUtc,
    int MarketCloseWarningMinutes,
    PreTradeCostProfile PreTradeCostProfile,
    double PreTradeCommissionPerUnit,
    double PreTradeMinCommissionPerOrder,
    double PreTradeSlippageBps,
    string FundamentalReportType,
    string WshFilterJson,
    string ScannerInstrument,
    string ScannerLocationCode,
    string ScannerScanCode,
    int ScannerRows,
    double ScannerAbovePrice,
    double ScannerBelowPrice,
    int ScannerAboveVolume,
    double ScannerMarketCapAbove,
    double ScannerMarketCapBelow,
    string ScannerStockTypeFilter,
    string ScannerScannerSettingPairs,
    string ScannerFilterTagValues,
    string ScannerOptionsTagValues,
    string ScannerWorkbenchCodes,
    int ScannerWorkbenchRuns,
    int ScannerWorkbenchCaptureSeconds,
    int ScannerWorkbenchMinRows,
    int DisplayGroupId,
    string DisplayGroupContractInfo,
    int DisplayGroupCaptureSeconds,
    string ReplayInputPath,
    string ReplayOrdersInputPath,
    string ReplayCorporateActionsInputPath,
    string ReplaySymbolMappingsInputPath,
    string ReplayDelistEventsInputPath,
    string ReplayBorrowLocateInputPath,
    string ReplayScannerCandidatesInputPath,
    int ReplayScannerTopN,
    double ReplayScannerMinScore,
    double ReplayScannerOrderQuantity,
    string ReplayScannerOrderSide,
    string ReplayScannerOrderType,
    string ReplayScannerOrderTimeInForce,
    double ReplayScannerLimitOffsetBps,
    string ReplayPriceNormalization,
    int ReplayIntervalSeconds,
    int ReplayMaxRows,
    double ReplayInitialCash,
    double ReplayCommissionPerUnit,
    double ReplaySlippageBps,
    double ReplayInitialMarginRate,
    double ReplayMaintenanceMarginRate,
    double ReplaySecFeeRatePerDollar,
    double ReplayTafFeePerShare,
    double ReplayTafFeeCapPerOrder,
    double ReplayExchangeFeePerShare,
    double ReplayMaxFillParticipationRate,
    double ReplayPriceIncrement,
    bool ReplayEnforceQueuePriority,
    int ReplaySettlementLagDays,
    bool ReplayEnforceSettledCash,
    bool HeartbeatMonitorEnabled,
    int HeartbeatIntervalSeconds,
    int HeartbeatProbeTimeoutSeconds,
    int ReconnectMaxAttempts,
    int ReconnectBackoffSeconds,
    ClockSkewAction ClockSkewAction,
    double ClockSkewWarnSeconds,
    double ClockSkewFailSeconds,
    ReconciliationGateAction ReconciliationGateAction,
    double ReconciliationMinCommissionCoverage,
    double ReconciliationMinOrderCoverage,
    int ConductL1StaleSec,
    int MonitorUiPort,
    bool MonitorUiEnabled
)
{
    /// <summary>
    /// Parses runtime options from configuration-backed defaults and command-line overrides.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <returns>Resolved runtime option set.</returns>
    public static AppOptions Parse(string[] args)
    {
        return AppOptionsParser.Parse(args);
    }
}

public enum ReconciliationGateAction
{
    Off,
    Warn,
    Fail
}

public enum ClockSkewAction
{
    Off,
    Warn,
    Fail
}

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
}

public sealed record ContractDetailsRow(
    int ConId,
    string Symbol,
    string SecType,
    string Exchange,
    string PrimaryExchange,
    string Currency,
    string LocalSymbol,
    string TradingClass,
    string MarketName,
    string LongName,
    double MinTick
);

public sealed record OrderTemplateRow(
    string Name,
    int OrderId,
    int ParentId,
    string Action,
    string OrderType,
    double Quantity,
    double LimitPrice,
    double StopPrice,
    string Tif,
    bool Transmit
);

public sealed record LiveOrderPlacementRow(
    string TimestampUtc,
    int OrderId,
    string Symbol,
    string Action,
    double Quantity,
    double LimitPrice,
    double Notional,
    string Account,
    string OrderRef
);

public sealed record SimOrderCancellationRow(
    string TimestampUtc,
    int OrderId,
    string Account
);

internal sealed record LiveOrderPlacementPlan(
    string Symbol,
    string Action,
    double Quantity,
    double LimitPrice,
    string Source
);

internal sealed record LiveQuoteSnapshot(
    double Bid,
    double Ask,
    double Last,
    TopTickRow[] Ticks
);

internal sealed class LiveScannerCandidateRow
{
    public string Symbol { get; set; } = string.Empty;
    public double WeightedScore { get; set; }
    public bool? Eligible { get; set; }
    public double AverageRank { get; set; }
    public double Bid { get; set; }
    public double Ask { get; set; }
    public double Mark { get; set; }
}

internal sealed record PositionMonitorPlan(
    string Symbol,
    string EntryAction,
    double Quantity,
    double SeedLimit
);

internal sealed record LiveOrderExecutionResult(
    string Symbol,
    double RequestedQuantity,
    double FilledQuantity,
    bool PlacementAccepted
);

/// <summary>
/// DT_V1.1_CONDUCT configuration record. All tuneable parameters for the conduct exit engine.
/// Instantiate with defaults using <c>new ConductExitConfig()</c>, override via <c>with { ... }</c>.
/// </summary>
internal sealed record ConductExitConfig
{
    public int L1StaleSec { get; init; } = 3;
    public int MonitorPollSeconds { get; init; } = 1;
    public int ImmediateWindowSec { get; init; } = 5;
    public double ImmediateAdverseMovePct { get; init; } = 0.002;
    public double ImmediateAdverseMoveUsd { get; init; } = 10.0;
    public double GivebackPctOfNotional { get; init; } = 0.01;
    public double GivebackUsdCap { get; init; } = 30.0;
    public double HardStopDistanceMultiplier { get; init; } = 2.0;
    public double InitialMaxDrawdownPct { get; init; } = 0.01;
    public double TightenedMaxDrawdownPct { get; init; } = 0.005;
    public double TightenTriggerWinPct { get; init; } = 0.01;
    public double TrailingProfitMinUsd { get; init; } = 10.0;
    public double TrailingProfitMinRMultiple { get; init; } = 0.5;
    public double TrailKSpread { get; init; } = 2.0;
    public double TrailKTicks { get; init; } = 3;
    public double TrailKAtr { get; init; } = 0.10;
    public int AtrWindowSize { get; init; } = 120;
    public double AtrEmaAlpha { get; init; } = 0.1;
    public int AtrWarmupMinSamples { get; init; } = 10;
    public double BreakEvenActivationR { get; init; } = 1.0;
    public double ProfitLockActivationR { get; init; } = 2.0;
    public double ProfitLockGuaranteeR { get; init; } = 0.5;
    public int TimeStopSec { get; init; } = 90;
    public double MinProgressR { get; init; } = 0.5;
    public string EodFlattenTimeET { get; init; } = "15:55";
    public int PositionRecheckIntervalSec { get; init; } = 15;
    public bool SafetyOverlayEnabled { get; init; } = true;
    public string KillSwitchFilePath { get; init; } = "kill_switch.txt";
    public double DailyMaxLossUsd { get; init; } = 200.0;
    public int DisconnectFlattenSec { get; init; } = 10;
    public bool EnsureExitsEnabled { get; init; } = true;
    public int ExitAckTimeoutSec { get; init; } = 2;
    public int ExitRepairRetries { get; init; } = 2;
    public int ExitRecheckIntervalSec { get; init; } = 30;
    public bool TakeProfitEnabled { get; init; } = false;
    public double Tp1RMultiple { get; init; } = 1.0;
    public double Tp1ScaleOutPct { get; init; } = 0.50;
    public double Tp2RMultiple { get; init; } = 2.0;
    public bool CandleMaintenanceEnabled { get; init; } = true;
    public int AtrCandlePeriod { get; init; } = 14;
    public bool TradeEpisodeJournalEnabled { get; init; } = true;
}

internal sealed class ConductPositionState
{
    public required string Symbol { get; init; }
    public required bool IsLong { get; init; }
    public required double FilledQuantity { get; set; }
    public required double EntryPrice { get; init; }
    public required DateTime LoopStartUtc { get; init; }
    public double PeakPrice { get; set; }
    public double TroughPrice { get; set; }
    public double PeakUnrealPnlUsd { get; set; }
    public double MfeUsd { get; set; }
    public double MaeUsd { get; set; }
    public bool TrailingActive { get; set; }
    public bool TightenedRuleActive { get; set; }
    public double ActiveMaxDrawdownPct { get; set; }
    public bool BreakEvenActive { get; set; }
    public bool ProfitLocked { get; set; }
    public double FloorPricePerShare { get; set; }
    public DateTime LastFreshL1Utc { get; set; }
    public DateTime TimeStopDeadlineUtc { get; set; }
    public Queue<double> MarkHistory { get; } = new();
    public int TicksSincePositionRecheck { get; set; }
    public DateTime LastConnectedUtc { get; set; } = DateTime.UtcNow;
    public double SessionRealizedPnlUsd { get; set; }
    public int? StopOrderId { get; set; }
    public int? Tp1OrderId { get; set; }
    public int? Tp2OrderId { get; set; }
    public bool Tp1Done { get; set; }
    public int ExitsVerifiedCount { get; set; }
    public int ExitsRepairAttempts { get; set; }
    public int TicksSinceExitRecheck { get; set; }
    public double OriginalFilledQuantity { get; init; }
    public List<ConductCandleBar> CandleBars1M { get; } = [];
    public ConductCandleBarBuilder? CurrentCandle { get; set; }
    public double Atr1M { get; set; }
    public double RiskPerTradeUsd { get; set; }
    public double RoundTripCommission { get; set; }
}

internal sealed record ConductCandleBar(
    DateTime MinuteUtc,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume,
    int TickCount
);

internal sealed class ConductCandleBarBuilder
{
    public DateTime MinuteUtc { get; init; }
    public double Open { get; init; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double Volume { get; set; }
    public int TickCount { get; set; }

    public ConductCandleBar Finalize() => new(
        MinuteUtc, Open, High, Low, Close, Volume, TickCount);
}

internal sealed class ConductTradeEpisode
{
    public string TradeId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public double EntryPrice { get; init; }
    public double ExitPrice { get; init; }
    public double Quantity { get; init; }
    public DateTime EntryUtc { get; init; }
    public DateTime ExitUtc { get; init; }
    public double HoldDurationSec { get; init; }
    public string ExitReason { get; init; } = string.Empty;
    public double RealizedPnlUsd { get; init; }
    public double RealizedPnlR { get; init; }
    public double MfeUsd { get; init; }
    public double MaeUsd { get; init; }
    public double PeakUnrealPnlUsd { get; init; }
    public double RiskPerTradeUsd { get; init; }
    public double CommissionUsd { get; init; }
    public double PeakPrice { get; init; }
    public double TroughPrice { get; init; }
    public bool TrailingActivated { get; init; }
    public bool BreakEvenActivated { get; init; }
    public bool ProfitLocked { get; init; }
    public bool Tp1Done { get; init; }
    public double FinalFloorPrice { get; init; }
    public double Atr1MAtExit { get; init; }
    public int CandleBarCount { get; init; }
    public int LoopIterations { get; init; }
    public string EngineVersion { get; init; } = "V1.2";
}

public sealed record ScannerPreviewSummaryRow(
    DateTime TimestampUtc,
    string Action,
    string ResolvedInputPath,
    string ResolvedInputFullPath,
    bool FileConfigured,
    bool FileExists,
    bool FileIsTempLock,
    string Phase,
    bool IsTradingDay,
    DateTime SessionOpenUtc,
    DateTime OpenPhaseEndUtc,
    DateTime PostOpenEndUtc,
    int RawRowCount,
    int NormalizedRowCount,
    int SelectedRowCount,
    string[] SelectedSymbols,
    string[] Notes
);

public sealed record ScannerPreviewCandidateRow(
    string Symbol,
    double WeightedScore,
    bool? Eligible,
    double AverageRank,
    bool AllowListed,
    bool MeetsScoreAndEligibility,
    bool Selected
);

public sealed record ManagedAccountRow(
    DateTime TimestampUtc,
    string AccountId
);

public sealed record OptionExerciseRequestRow(
    DateTime TimestampUtc,
    string Symbol,
    string Expiry,
    double Strike,
    string Right,
    int Action,
    int Quantity,
    string Account,
    int Override,
    string ManualTime
);

public sealed record CryptoPermissionRow(
    DateTime TimestampUtc,
    string Symbol,
    string Exchange,
    string Currency,
    bool ContractDetailsResolved,
    int ContractDetailsCount,
    int TopTicksCaptured,
    string[] RelatedErrors
);

public sealed record CryptoOrderRequestRow(
    DateTime TimestampUtc,
    int OrderId,
    string Symbol,
    string Exchange,
    string Currency,
    string Action,
    double Quantity,
    double LimitPrice,
    double Notional,
    string Account,
    string OrderRef
);

public sealed record FaAllocationMethodRow(
    string Category,
    string Method,
    int TypeNumber,
    string Notes
);

public sealed record FaUnificationRow(
    DateTime TimestampUtc,
    int GroupPayloadCount,
    int ProfilePayloadCount,
    bool ProfileRequestErrored,
    bool LikelyUnified,
    string Note
);

public sealed record FaOrderRequestRow(
    DateTime TimestampUtc,
    int OrderId,
    string Symbol,
    string Action,
    double Quantity,
    double LimitPrice,
    double Notional,
    string Account,
    string FaGroup,
    string FaProfile,
    string FaMethod,
    string FaPercentage,
    string OrderRef
);

public sealed record WshFilterSupportRow(
    DateTime TimestampUtc,
    bool IsWshSupported,
    bool HasWshMetaRequest,
    bool HasWshEventRequest,
    bool HasWshMetaCallback,
    bool HasWshEventCallback,
    string RequestedFilterJson,
    string Note
);

public sealed record ErrorCodeSeedRow(
    int Code,
    string Name,
    string Description
);

public sealed record ErrorCodeRow(
    int Code,
    string Name,
    string Description,
    int ObservedCount
);

public sealed record ObservedErrorRow(
    int Code,
    string Message,
    string Raw
);

public sealed record ScannerRequestRow(
    int RequestId,
    string Instrument,
    string LocationCode,
    string ScanCode,
    int NumberOfRows,
    string ScannerSettingPairs,
    string FilterTagPairs,
    string OptionTagPairs
);

public sealed record ScannerWorkbenchRunRow(
    DateTime TimestampUtc,
    int RequestId,
    string ScanCode,
    int RunIndex,
    int Rows,
    double DurationSeconds,
    double? FirstRowSeconds,
    int ErrorCount,
    string ErrorCodes
);

public sealed record ScannerWorkbenchScoreRow(
    string ScanCode,
    int Runs,
    double AverageRows,
    double AverageFirstRowSeconds,
    double AverageErrors,
    double CoverageScore,
    double SpeedScore,
    double StabilityScore,
    double CleanlinessScore,
    double WeightedScore,
    bool HardFail
);

public sealed record ScannerWorkbenchCandidateObservationRow(
    DateTime TimestampUtc,
    int RequestId,
    string ScanCode,
    int RunIndex,
    string Symbol,
    int ConId,
    int Rank,
    string Exchange,
    string PrimaryExchange,
    string Currency,
    string Distance,
    string Benchmark,
    string Projection
);

public sealed record ScannerWorkbenchCandidateRow(
    string Symbol,
    int ConId,
    string Exchange,
    string Currency,
    int ObservationCount,
    int DistinctScanCodes,
    double AverageRank,
    int BestRank,
    double? AverageProjection,
    double RankScore,
    double ConsistencyScore,
    double CoverageScore,
    double SignalScore,
    double WeightedScore,
    bool Eligible,
    string RejectReason
);

public sealed record DisplayGroupActionRow(
    DateTime TimestampUtc,
    int RequestId,
    int GroupId,
    string ContractInfo,
    string Action
);

public sealed record AdapterTraceArtifactRow(
    Guid CorrelationId,
    int? RequestId,
    string Operation,
    string Adapter,
    RequestStatus Status,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    string? Metadata
);

public sealed record StrategySchedulerEventArtifactRow(
    DateTime TimestampUtc,
    string Mode,
    string ExchangeCalendar,
    string EventName
);

public sealed record ReplayFeeBreakdownArtifactRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    double Quantity,
    double FillPrice,
    double BrokerCommission,
    double SecFee,
    double TafFee,
    double ExchangeFee,
    double TotalFees,
    string Source
);

public sealed record ReplayPartialFillArtifactRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    DateTime SubmittedAtUtc,
    string OrderType,
    double RequestedQuantity,
    double FilledQuantity,
    double RemainingQuantity,
    double FillPrice,
    string Source
);

public sealed record ReplayCostDeltaArtifactRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    string OrderType,
    double Quantity,
    double FillPrice,
    double? ReferencePrice,
    double EstimatedCommission,
    double RealizedCommission,
    double CommissionDelta,
    double? EstimatedSlippage,
    double? RealizedSlippage,
    double? SlippageDelta,
    string Source
);

public sealed record ReplayValidationSummaryRow(
    DateTime TimestampUtc,
    string StrategyRuntime,
    string Symbol,
    int SliceCount,
    int OrderCount,
    int FillCount,
    int LocateRejectionCount,
    int MarginRejectionCount,
    int CashRejectionCount,
    int CancellationCount,
    IReadOnlyDictionary<string, int> OrderSourceCounts,
    double TotalReturn,
    double MaxDrawdown,
    double SharpeLike,
    double WinRate,
    int SummaryFillCount,
    string ScannerCandidatesInputPath,
    int ScannerTopN,
    double ScannerMinScore,
    double ScannerOrderQuantity,
    string ScannerOrderSide,
    string ScannerOrderType,
    string ScannerOrderTimeInForce,
    double ScannerLimitOffsetBps
);

public sealed record ReplayLimitOrderCaseMatrixRow(
    string CaseId,
    string Reference,
    string Side,
    string Trigger,
    string ExpectedBehavior,
    bool Implemented,
    string Evidence
);

public sealed record ReplaySelfLearningStoreRow(
    int Version,
    long UpdatedTsNs,
    int TotalRuns,
    string LastDatasetReference,
    int LastTradeClosedCount,
    double LastTradeClosedPnlSum,
    IReadOnlyDictionary<string, double> SymbolBias
);

public sealed record ReplaySelfLearningSymbolOverrideRow(
    string Symbol,
    double ScannerScoreShift,
    string Action
);

public sealed record ReplaySelfLearningM9InputsRow(
    int TradeCount,
    double PnlSum,
    double WinRate,
    int SymbolBiasCount
);

public sealed record ReplaySelfLearningM9RecommendationsRow(
    double ScannerWeightAdjust,
    double StopDistMultiplier,
    IReadOnlyList<ReplaySelfLearningSymbolOverrideRow> SymbolOverrides
);

public sealed record ReplaySelfLearningM9GuardrailsRow(
    bool OfflineOnly,
    bool LiveRiskExecutionMutationAllowed
);

public sealed record ReplaySelfLearningM9PostprocessRow(
    long GeneratedTsNs,
    ReplaySelfLearningM9InputsRow Inputs,
    ReplaySelfLearningM9RecommendationsRow Recommendations,
    ReplaySelfLearningM9GuardrailsRow Guardrails
);

public sealed record ReplaySelfLearningPromotionApprovalsDocumentRow(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("generated_ts_ns")] long GeneratedTsNs,
    [property: JsonPropertyName("approvals")] IReadOnlyList<ReplaySelfLearningPromotionApprovalRow> Approvals
);

public sealed record ReplaySelfLearningPromotionApprovalRow(
    [property: JsonPropertyName("approval_id")] string ApprovalId,
    [property: JsonPropertyName("model_id")] string ModelId,
    [property: JsonPropertyName("from_stage")] string FromStage,
    [property: JsonPropertyName("to_stage")] string ToStage,
    [property: JsonPropertyName("approved_by")] string ApprovedBy,
    [property: JsonPropertyName("change_ticket")] string ChangeTicket,
    [property: JsonPropertyName("issued_ts_ns")] long IssuedTsNs,
    [property: JsonPropertyName("expires_ts_ns")] long ExpiresTsNs,
    [property: JsonPropertyName("team")] string Team,
    [property: JsonPropertyName("signature")] string Signature,
    bool SignatureVerified
);

public sealed record ReplaySelfLearningPromotionCheckRow(
    string CheckName,
    bool Passed,
    string Details
);

public sealed record ReplaySelfLearningHumanWorkflowRow(
    bool Required,
    IReadOnlyList<string> RequiredTeams,
    IReadOnlyList<string> TeamsApproved,
    IReadOnlyList<string> TeamsMissing,
    bool RequireDistinctApprovers,
    bool DistinctApproversOk,
    int MaxApprovalAgeHours,
    int FreshApprovalCount
);

public sealed record ReplaySelfLearningPromotionGovernanceRow(
    long GeneratedTsNs,
    string ModelId,
    string CurrentStage,
    string TargetStage,
    string Status,
    string Reason,
    bool ApprovalRequired,
    bool SignatureRequired,
    string ApprovalsPath,
    IReadOnlyList<ReplaySelfLearningPromotionApprovalRow> TransitionApprovals,
    ReplaySelfLearningHumanWorkflowRow HumanWorkflow,
    IReadOnlyList<ReplaySelfLearningPromotionCheckRow> Checks
);

public sealed record ReplaySelfLearningWorkflowApprovalRow(
    string ApprovalId,
    string Team,
    string ApprovedBy,
    string ChangeTicket,
    long IssuedTsNs,
    long ExpiresTsNs,
    bool Signed
);

public sealed record ReplaySelfLearningPromotionApprovalMetaRow(
    bool Required,
    bool SignatureRequired,
    string Source,
    bool Ok,
    bool ApprovalFound,
    bool Signed,
    string ApprovalId,
    string ApprovedBy,
    string ChangeTicket,
    string Error
);

public sealed record ReplaySelfLearningPromotionHumanWorkflowMetaRow(
    bool Required,
    string Source,
    IReadOnlyList<string> RequiredTeams,
    int MaxApprovalAgeHours,
    bool RequireDistinctApprovers,
    bool Ok,
    IReadOnlyList<string> TeamsApproved,
    IReadOnlyList<string> TeamsMissing,
    bool DistinctApproversOk,
    IReadOnlyList<ReplaySelfLearningWorkflowApprovalRow> ApprovalsUsed,
    string Error
);

public sealed record ReplaySelfLearningLifecycleBaselineRow(
    double WinRate,
    double PnlSum,
    int Samples
);

public sealed record ReplaySelfLearningLifecycleHistoryRow(
    long TsNs,
    string DatasetDir,
    string ModelId,
    string Stage,
    int TradeCount,
    double PnlSum,
    double WinRate,
    bool QualityPassed,
    bool DriftDetected,
    IReadOnlyList<string> DriftReasons,
    int ConsecutiveDrift,
    ReplaySelfLearningPromotionApprovalMetaRow PromotionApproval,
    ReplaySelfLearningPromotionHumanWorkflowMetaRow PromotionHumanWorkflow,
    ReplaySelfLearningLifecycleBaselineRow Baseline,
    double DecodeRatio = 1.0,
    double ParseFailRatio = 0.0,
    ReplaySelfLearningLineageRow? Lineage = null
);

public sealed record ReplaySelfLearningLineageRow(
    IReadOnlyList<string> RequiredUpstreamPrefixes,
    bool RequireTraceId,
    double MinTraceCoverage,
    double MaxParseFailRatio,
    bool RequireClean,
    bool ContaminationDetected,
    bool Passed,
    double TraceCoverage,
    IReadOnlyList<string> Flags,
    IReadOnlyDictionary<string, int> Feeds
);

public sealed record ReplaySelfLearningDriftControlsRow(
    bool Active,
    IReadOnlyList<string> Reasons,
    int RollbackThreshold,
    double WinrateDropWarn,
    double PnlDropWarn
);

public sealed record ReplaySelfLearningQualityControlsRow(
    double MinDecodeRatio,
    double MaxParseFailRatio,
    bool Passed
);

public sealed record ReplaySelfLearningLineageControlsRow(
    IReadOnlyList<string> RequiredUpstreamPrefixes,
    bool RequireTraceId,
    double MinTraceCoverage,
    double MaxParseFailRatio,
    bool RequireClean,
    bool ContaminationDetected,
    bool Passed,
    IReadOnlyList<string> Flags,
    IReadOnlyDictionary<string, int> Feeds
);

public sealed record ReplaySelfLearningLifecycleEventRow(
    long TsNs,
    string DatasetDir,
    string Type,
    string From,
    string To,
    string Reason,
    int StageRuns,
    int RequiredRuns,
    string ModelId,
    ReplaySelfLearningPromotionApprovalMetaRow Approval,
    ReplaySelfLearningPromotionHumanWorkflowMetaRow HumanWorkflow
);

public sealed record ReplaySelfLearningLifecycleModelRegistryRow(
    string Path,
    string ModelId,
    string PromotionApprovalPath,
    bool PromotionRequireApproval,
    bool PromotionSignatureRequired,
    string PromotionSigningKeyEnv,
    ReplaySelfLearningPromotionApprovalMetaRow LastPromotionApproval,
    bool ProductionHumanWorkflowRequired,
    IReadOnlyList<string> ProductionHumanWorkflowRequiredTeams,
    int ProductionHumanWorkflowMaxApprovalAgeHours,
    bool ProductionHumanWorkflowRequireDistinctApprovers,
    ReplaySelfLearningPromotionHumanWorkflowMetaRow LastPromotionHumanWorkflow
);

public sealed record ReplaySelfLearningLifecycleRow(
    int Version,
    long UpdatedTsNs,
    string CurrentStage,
    string? PreviousStage,
    int ConsecutiveDrift,
    IReadOnlyDictionary<string, int> PromotionMinRuns,
    ReplaySelfLearningLifecycleHistoryRow? LastRun,
    IReadOnlyList<ReplaySelfLearningLifecycleHistoryRow> History,
    IReadOnlyList<ReplaySelfLearningLifecycleEventRow> Events,
    ReplaySelfLearningLifecycleModelRegistryRow ModelRegistry,
    ReplaySelfLearningDriftControlsRow? Drift = null,
    ReplaySelfLearningQualityControlsRow? QualityControls = null,
    ReplaySelfLearningLineageControlsRow? LineageControls = null
);

public sealed record ReplaySelfLearningModelRegistryMetricsRow(
    double WinRate,
    double PnlSum,
    int TradeCount
);

public sealed record ReplaySelfLearningModelRegistryPromotionRow(
    long TsNs,
    string ModelId,
    string From,
    string To,
    string Reason,
    ReplaySelfLearningPromotionApprovalMetaRow Approval,
    ReplaySelfLearningPromotionHumanWorkflowMetaRow HumanWorkflow
);

public sealed record ReplaySelfLearningModelRegistryModelRow(
    string ModelId,
    long CreatedTsNs,
    long UpdatedTsNs,
    int Runs,
    string CurrentStage,
    string LatestDatasetDir,
    ReplaySelfLearningModelRegistryMetricsRow LatestMetrics,
    ReplaySelfLearningPromotionApprovalMetaRow LastPromotionApproval,
    ReplaySelfLearningPromotionHumanWorkflowMetaRow LastPromotionHumanWorkflow,
    ReplaySelfLearningModelRegistryPromotionRow? LastPromotion
);

public sealed record ReplaySelfLearningModelRegistryRow(
    int Version,
    long UpdatedTsNs,
    IReadOnlyDictionary<string, ReplaySelfLearningModelRegistryModelRow> Models,
    IReadOnlyList<ReplaySelfLearningModelRegistryPromotionRow> Promotions
);

public sealed record ReplaySelfLearningLifecycleRegistryUpdateRow(
    ReplaySelfLearningLifecycleRow Lifecycle,
    ReplaySelfLearningModelRegistryRow Registry
);

public sealed record PromotionReadinessCheckRow(
    string CheckName,
    bool Passed,
    string Details
);

public sealed record PromotionReadinessRow(
    DateTime TimestampUtc,
    string Mode,
    DateTime RunStartedUtc,
    DateTime RunCompletedUtc,
    int ExitCode,
    bool PaperReady,
    bool LiveReady,
    string NextStage,
    IReadOnlyList<PromotionReadinessCheckRow> Checks
);

public sealed record ResilienceAcceptanceCheckRow(
    string CheckName,
    bool Passed,
    string Details
);

public sealed record ResilienceDrillAcceptanceRow(
    DateTime TimestampUtc,
    string Mode,
    DateTime RunStartedUtc,
    DateTime RunCompletedUtc,
    int ExitCode,
    bool Passed,
    bool HeartbeatMonitorEnabled,
    int HeartbeatIntervalSeconds,
    int HeartbeatProbeTimeoutSeconds,
    int ReconnectMaxAttempts,
    int ReconnectBackoffSeconds,
    int ReconnectAttemptCount,
    int ReconnectRecoveryCount,
    int DegradedTransitionCount,
    int HaltingTransitionCount,
    int ReconnectTriggerErrorCount,
    int BlockingApiErrorCount,
    int TimedOutRequestCount,
    int FailedRequestCount,
    bool ConnectivityFailure,
    bool ReconciliationGateFailure,
    bool ClockSkewGateFailure,
    bool PreTradeHalt,
    string FinalLifecycleStage,
    IReadOnlyList<ResilienceAcceptanceCheckRow> Checks
);

public sealed record LeanZiplineParityChecklistRow(
    string ItemId,
    string Capability,
    string Source,
    bool Completed,
    string Evidence
);

public sealed record LeanZiplineParityChecklistSummaryRow(
    DateTime TimestampUtc,
    string Mode,
    DateTime RunStartedUtc,
    DateTime RunCompletedUtc,
    int ExitCode,
    int TotalItems,
    int CompletedItems,
    bool AllCompleted,
    IReadOnlyList<LeanZiplineParityChecklistRow> Items
);
