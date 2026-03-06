using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

/// <summary>
/// Builds a feature snapshot from the current data slice for signal evaluation.
/// Extracted from <see cref="V3LiveFeatureBuilder"/> for testability and DI.
/// </summary>
public interface ILiveFeatureBuilder
{
    /// <summary>
    /// Build a feature snapshot from the current data slice.
    /// </summary>
    /// <param name="dataSlice">Market data slice from the host runtime.</param>
    /// <param name="depthLevels">Number of L2 depth levels to include.</param>
    /// <param name="symbol">Optional symbol key for indicator caching.</param>
    V3LiveFeatureSnapshot Build(StrategyDataSlice dataSlice, int depthLevels, string? symbol = null);
}

/// <summary>
/// Evaluates features + config to produce a composite signal decision (long/short/pass).
/// Extracted from <see cref="V3LiveSignalEngine"/> for testability and DI.
/// </summary>
public interface ILiveSignalEngine
{
    /// <summary>
    /// Evaluate the current feature snapshot for entry signals.
    /// </summary>
    V3LiveSignalDecision Evaluate(V3LiveFeatureSnapshot features, V3LiveConfig config, string symbol = "");

    /// <summary>
    /// Reset per-symbol internal state (e.g. squeeze trackers). Called on session reset.
    /// </summary>
    void ResetState();
}

/// <summary>
/// Pre-trade risk gate: evaluates whether a proposed order should be allowed.
/// Extracted from <see cref="V3LiveRiskGuard"/> for testability and DI.
/// </summary>
public interface ILiveRiskGuard
{
    /// <summary>
    /// Evaluate risk checks for a proposed order.
    /// </summary>
    V3LiveRiskCheckResult Evaluate(
        string symbol,
        DateTime timestampUtc,
        V3LiveFeatureSnapshot features,
        V3LiveSymbolRiskState state,
        V3LiveProposedOrder order);
}
