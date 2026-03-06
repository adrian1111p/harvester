using System.Text.Json;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;


public sealed record ReplayScannerRankedSymbolRow(
    string Symbol,
    double WeightedScore,
    bool Eligible,
    double AverageRank
);

public sealed record ReplayScannerSymbolSelectionSnapshotRow(
    DateTime TimestampUtc,
    string SourcePath,
    IReadOnlyList<ReplayScannerRankedSymbolRow> RankedSymbols,
    IReadOnlyList<string> SelectedSymbols
);

public interface IReplayScannerSelectionSource
{
    ReplayScannerSymbolSelectionSnapshotRow GetScannerSelectionSnapshot();
}

public interface IReplaySimulationFeedbackSink
{
    void OnReplaySliceResult(StrategyDataSlice dataSlice, ReplaySliceSimulationResult result, string activeSymbol);
}

/// <summary>
/// Represents a normalized replay decision context for a single symbol and timestamp.
/// </summary>
public sealed record ReplayDayTradingContext(
    DateTime TimestampUtc,
    string Symbol,
    double MarkPrice,
    double BidPrice,
    double AskPrice,
    double PositionQuantity,
    double AveragePrice,
    double BarOpen = 0,
    double BarHigh = 0,
    double BarLow = 0,
    double BarClose = 0,
    double BarVolume = 0
);

/// <summary>
/// Represents overlay strategy output for the current replay slice.
/// </summary>
public sealed record ReplayDayTradingDecision(
    IReadOnlyList<ReplayOrderIntent> Orders,
    bool StopFurtherProcessing
);

/// <summary>
/// Defines an account-level or position-level safety overlay that can preempt normal strategy flow.
/// </summary>
public interface IReplayGlobalSafetyOverlayStrategy
{
    /// <summary>
    /// Evaluates the current context and returns orders and stop behavior.
    /// </summary>
    ReplayDayTradingDecision Evaluate(ReplayDayTradingContext context);
}

/// <summary>
/// Defines entry logic for scanner-selected symbols in replay mode.
/// </summary>
public interface IReplayEntryStrategy
{
    /// <summary>
    /// Produces new entry intents for the current context and scanner selection.
    /// </summary>
    IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context, ReplayScannerSymbolSelectionSnapshotRow selection);
}

/// <summary>
/// Defines post-entry trade management logic (stops, exits, scaling).
/// </summary>
public interface IReplayTradeManagementStrategy
{
    /// <summary>
    /// Produces management and exit intents for the current context.
    /// </summary>
    IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context);
}

/// <summary>
/// Defines session close handling logic for replay mode.
/// </summary>
public interface IReplayEndOfDayStrategy
{
    /// <summary>
    /// Produces close-of-day orders for the current context.
    /// </summary>
    IReadOnlyList<ReplayOrderIntent> Evaluate(ReplayDayTradingContext context);
}

