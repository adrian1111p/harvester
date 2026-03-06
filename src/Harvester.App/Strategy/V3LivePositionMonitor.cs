using Harvester.App.Backtest.Engine;

namespace Harvester.App.Strategy;

/// <summary>
/// Continuous position monitor ("backside monitoring") that evaluates every second
/// whether an open position should be closed. Uses L1, L2, multi-timeframe candles,
/// technical indicators, and the position's P&amp;L trajectory.
///
/// Exit conditions evaluated (in priority order):
///   E1: Hard stop (price beyond stop distance)
///   E2: Time stop (max hold duration exceeded with insufficient progress)
///   E3: MTF reversal signal (short-term candles reversed against position)
///   E4: Giveback (peak unrealized PnL gave back too much)
///   E5: Break-even ratchet (move stop to entry after BreakevenR reached)
///   E6: Trailing stop (trail from peak)
///   E7: Take-profit 1 / Take-profit 2
///   E8: L1 data stale (no fresh quotes)
///   E9: L2 depth dried up (liquidity withdrawal)
///   E10: Squeeze release exit (was in squeeze, now broken out against position)
///   E11: Session end / close-only mode
/// </summary>
public sealed class V3LivePositionMonitor
{
    private readonly V3LiveConfig _config;
    private readonly ExitCascadeParams _cascadeParams;

    public V3LivePositionMonitor(V3LiveConfig config)
    {
        _config = config;
        _cascadeParams = ExitCascadeParams.FromLiveConfig(config);
    }

    /// <summary>
    /// Evaluate whether an open position should be exited.
    /// Called on every data tick (~1 second) for each symbol with an open position.
    /// Delegates core R-based exits to <see cref="ExitCascadeEngine"/>,
    /// then checks live-only conditions (L1 stale, L2 depth, squeeze, MTF reversal, session end).
    /// </summary>
    public V3LiveExitDecision Evaluate(
        V3LiveTrackedPosition position,
        V3LiveFeatureSnapshot features,
        V3LiveMtfAlignment mtfAlignment,
        V3LiveCandleSnapshot? candleSnapshot,
        bool closeOnlyMode,
        DateTime nowUtc)
    {
        if (position.IsFlat || position.Quantity == 0)
            return V3LiveExitDecision.Hold("flat");

        var price = features.Price;
        if (price <= 0)
            return V3LiveExitDecision.Hold("no-price");

        var isLong = position.Side == PositionSide.Long;
        var entryPrice = position.EntryPrice;
        var atrForRisk = position.EntryAtr14 > 0 && !double.IsNaN(position.EntryAtr14)
            ? position.EntryAtr14
            : (double.IsNaN(features.Atr14) || features.Atr14 <= 0 ? 1.0 : features.Atr14);
        var riskPerShare = _config.HardStopR * atrForRisk;
        var holdSeconds = (nowUtc - position.EntryUtc).TotalSeconds;

        // ─── Live-only pre-checks (run before shared cascade) ───

        // --- E11: Session end / close-only mode ---
        if (closeOnlyMode)
        {
            return V3LiveExitDecision.Exit("session-end-flatten",
                $"Close-only mode active, flatten all positions");
        }

        // --- E8: L1 data stale ---
        if (features.L1.HasQuote)
        {
            var quotAge = (nowUtc - features.L1.TimestampUtc).TotalSeconds;
            if (quotAge > _config.MaxQuoteStalenessSeconds * _config.ExitQuoteStalenessMultiplier)
            {
                return V3LiveExitDecision.Exit("l1-stale-exit",
                    $"L1 quote stale for {quotAge:F0}s (3× threshold)");
            }
        }

        // ─── Shared exit cascade (hard stop, TP2, giveback, trail, TP1, BE, time) ───
        var cascadeInput = new ExitCascadeInput
        {
            IsLong = isLong,
            EntryPrice = entryPrice,
            CurrentPrice = price,
            StopPrice = position.StopPrice,
            AtrAtEntry = atrForRisk,
            RiskPerShare = riskPerShare,
            PeakFavorablePrice = position.MostFavorablePriceSinceEntry,
            TrailingStopPrice = position.StopPrice, // live uses stop as trailing baseline
            BreakevenActivated = position.Tp1Activated, // BE is TP1-gated in live
            Tp1Activated = position.Tp1Activated,
            UnrealizedPnlPeak = position.UnrealizedPnlPeak,
            Quantity = position.Quantity,
            HoldSeconds = holdSeconds,
            Params = _cascadeParams,
        };

        var cascadeResult = ExitCascadeEngine.Evaluate(cascadeInput);

        // Apply state updates from cascade engine to tracked position
        position.StopPrice = cascadeResult.UpdatedStopPrice;
        position.Tp1Activated = cascadeResult.UpdatedTp1Activated;
        // MostFavorablePrice is managed by the tracker via UpdateMarkPrice, not overwritten here

        if (cascadeResult.ShouldExit)
        {
            if (cascadeResult.IsPartialExit)
            {
                return V3LiveExitDecision.PartialExit(cascadeResult.ExitReason!,
                    cascadeResult.Detail, cascadeResult.PartialQuantity);
            }
            return V3LiveExitDecision.Exit(cascadeResult.ExitReason!, cascadeResult.Detail);
        }

        // ─── Live-only post-checks (run after shared cascade) ───

        // --- E3: MTF reversal signal (short-term candles all reversed) ---
        if (isLong && mtfAlignment.ShortTermBearish)
        {
            if (position.UnrealizedPnl >= 0)
            {
                return V3LiveExitDecision.Exit("mtf-reversal",
                    "Short-term MTF (1m+5m+15m) all bearish, position at break-even or better");
            }
        }
        if (!isLong && mtfAlignment.ShortTermBullish)
        {
            if (position.UnrealizedPnl >= 0)
            {
                return V3LiveExitDecision.Exit("mtf-reversal",
                    "Short-term MTF (1m+5m+15m) all bullish, position at break-even or better");
            }
        }

        // --- E9: L2 depth dried up (liquidity withdrawal) ---
        if (_config.RequireL2Depth && features.L2.HasDepth)
        {
            var totalDepth = features.L2.BidDepthN + features.L2.AskDepthN;
            if (totalDepth < _config.MinDepthPerSideShares * _config.DepthDriedUpMultiplier)
            {
                return V3LiveExitDecision.Exit("l2-depth-dried",
                    $"L2 total depth {totalDepth:F0} < 30% of threshold {_config.MinDepthPerSideShares:F0}");
            }
        }

        // --- E10: Squeeze release exit ---
        if (candleSnapshot is not null)
        {
            var squeeze = EvaluateSqueezeRelease(position, features, candleSnapshot);
            if (squeeze is not null)
                return squeeze;
        }

        return V3LiveExitDecision.Hold("monitoring");
    }

    /// <summary>
    /// Evaluate whether a squeeze breakout has occurred against the position direction.
    /// Uses the 1m timeframe candles from the candle aggregator.
    /// </summary>
    private V3LiveExitDecision? EvaluateSqueezeRelease(
        V3LiveTrackedPosition position,
        V3LiveFeatureSnapshot features,
        V3LiveCandleSnapshot candleSnapshot)
    {
        if (!features.SqueezeOn) return null; // Not in a squeeze — no squeeze exit logic

        // If squeeze is on and price is moving against us, it's a warning
        var isLong = position.Side == PositionSide.Long;
        var price = features.Price;

        if (!double.IsNaN(features.KcMid) && features.KcMid > 0)
        {
            // Price broke through KC mid against position direction
            if (isLong && price < features.KcMid && features.L2.OfiSignal < -0.1)
            {
                return V3LiveExitDecision.Exit("squeeze-adverse",
                    $"Squeeze active, price {price:F2} below KC mid {features.KcMid:F2} with negative OFI");
            }
            if (!isLong && price > features.KcMid && features.L2.OfiSignal > 0.1)
            {
                return V3LiveExitDecision.Exit("squeeze-adverse",
                    $"Squeeze active, price {price:F2} above KC mid {features.KcMid:F2} with positive OFI");
            }
        }

        return null;
    }
}

// ─── Exit Decision Records ──────────────────────────────────────────────────

/// <summary>
/// Result of position monitor evaluation.
/// </summary>
public sealed record V3LiveExitDecision
{
    public V3LiveExitAction Action { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    /// <summary>Quantity to close for PartialExit decisions. 0 for full exits.</summary>
    public int PartialQuantity { get; init; }

    public bool ShouldExit => Action == V3LiveExitAction.Exit;
    public bool IsAdvisory => Action == V3LiveExitAction.Advisory;
    public bool IsPartialExit => Action == V3LiveExitAction.PartialExit;

    public static V3LiveExitDecision Hold(string detail) =>
        new() { Action = V3LiveExitAction.Hold, Reason = "hold", Detail = detail };

    public static V3LiveExitDecision Exit(string reason, string detail) =>
        new() { Action = V3LiveExitAction.Exit, Reason = reason, Detail = detail };

    public static V3LiveExitDecision Advisory(string reason, string detail) =>
        new() { Action = V3LiveExitAction.Advisory, Reason = reason, Detail = detail };

    public static V3LiveExitDecision PartialExit(string reason, string detail, int partialQuantity) =>
        new() { Action = V3LiveExitAction.PartialExit, Reason = reason, Detail = detail, PartialQuantity = partialQuantity };
}

public enum V3LiveExitAction
{
    Hold,
    Exit,
    Advisory,
    /// <summary>Close a partial quantity (e.g. 50% at TP1) while keeping the remainder open.</summary>
    PartialExit
}
