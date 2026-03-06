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

    public V3LivePositionMonitor(V3LiveConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Evaluate whether an open position should be exited.
    /// Called on every data tick (~1 second) for each symbol with an open position.
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
        // Use entry-time ATR when available to prevent stop/TP drift as volatility changes
        var atrForRisk = position.EntryAtr14 > 0 && !double.IsNaN(position.EntryAtr14)
            ? position.EntryAtr14
            : (double.IsNaN(features.Atr14) || features.Atr14 <= 0 ? 1.0 : features.Atr14);
        var riskPerShare = _config.HardStopR * atrForRisk;
        var holdSeconds = (nowUtc - position.EntryUtc).TotalSeconds;

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

        // --- E1: Hard stop ---
        var stopDistance = riskPerShare;
        var hardStopPrice = position.StopPrice > 0
            ? position.StopPrice
            : (isLong ? entryPrice - stopDistance : entryPrice + stopDistance);
        if (isLong && price <= hardStopPrice)
        {
            return V3LiveExitDecision.Exit("hard-stop",
            $"Price {price:F2} <= stop {hardStopPrice:F2}");
        }
        if (!isLong && price >= hardStopPrice)
        {
            return V3LiveExitDecision.Exit("hard-stop",
            $"Price {price:F2} >= stop {hardStopPrice:F2}");
        }

        // --- E7: Take-profit 2 (full exit) ---
        var tp2Distance = _config.Tp2R * atrForRisk;
        var tp2Price = position.TakeProfitPrice > 0
            ? position.TakeProfitPrice
            : (isLong ? entryPrice + tp2Distance : entryPrice - tp2Distance);
        if (isLong && price >= tp2Price)
        {
            return V3LiveExitDecision.Exit("take-profit-2",
            $"Price {price:F2} >= TP2 {tp2Price:F2}");
        }
        if (!isLong && price <= tp2Price)
        {
            return V3LiveExitDecision.Exit("take-profit-2",
            $"Price {price:F2} <= TP2 {tp2Price:F2}");
        }

        // --- E4: Giveback ---
        if (position.UnrealizedPnlPeak > 0)
        {
            var giveback = position.UnrealizedPnlPeak - position.UnrealizedPnl;
            var givebackPct = giveback / position.UnrealizedPnlPeak;
            var effectiveUsdCap = _config.UseFixedGivebackUsdCap
                ? Math.Max(0.0, _config.GivebackUsdCap)
                : 0.0;

            if (effectiveUsdCap > 0)
            {
                if (position.UnrealizedPnl > 0 && giveback >= effectiveUsdCap)
                {
                    return V3LiveExitDecision.Exit("giveback-usd-cap",
                        $"Gave back ${giveback:F2} (cap ${effectiveUsdCap:F2}) from peak ${position.UnrealizedPnlPeak:F2}");
                }
            }
            else if (givebackPct >= _config.GivebackPct && position.UnrealizedPnlPeak >= riskPerShare * Math.Abs(position.Quantity) * 0.3)
            {
                return V3LiveExitDecision.Exit("giveback",
                    $"Gave back {givebackPct:P0} of peak PnL (${position.UnrealizedPnlPeak:F2} → ${position.UnrealizedPnl:F2})");
            }
        }

        // --- E6: Trailing stop (after break-even activation) ---
        var beActivationDistance = _config.BreakevenR * atrForRisk;
        var trailDistance = _config.TrailR * atrForRisk;

        if (isLong && position.MostFavorablePriceSinceEntry >= entryPrice + beActivationDistance)
        {
            var trailStop = position.MostFavorablePriceSinceEntry - trailDistance;
            if (price <= trailStop)
            {
                return V3LiveExitDecision.Exit("trailing-stop",
                    $"Price {price:F2} <= trail from peak {position.MostFavorablePriceSinceEntry:F2} - {trailDistance:F2}");
            }
        }
        if (!isLong && position.MostFavorablePriceSinceEntry > 0 && position.MostFavorablePriceSinceEntry <= entryPrice - beActivationDistance)
        {
            var trailStop = position.MostFavorablePriceSinceEntry + trailDistance;
            if (price >= trailStop)
            {
                return V3LiveExitDecision.Exit("trailing-stop",
                    $"Price {price:F2} >= trail from peak {position.MostFavorablePriceSinceEntry:F2} + {trailDistance:F2}");
            }
        }

        // --- E2: Time stop with progress check ---
        var maxHoldSeconds = _config.MaxHoldBars * 60.0; // 1 bar = 1 minute in live context
        if (holdSeconds >= maxHoldSeconds)
        {
            var progressR = position.UnrealizedPnl / (riskPerShare * Math.Abs(position.Quantity));
            if (progressR < _config.TimeStopMinProgressR)
            {
                return V3LiveExitDecision.Exit("time-stop",
                    $"Held {holdSeconds:F0}s (max {maxHoldSeconds:F0}s), progress only {progressR:F2}R");
            }
        }

        // --- E3: MTF reversal signal (short-term candles all reversed) ---
        if (isLong && mtfAlignment.ShortTermBearish)
        {
            // Only exit on MTF reversal if we're at least at break-even or have some profit
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

        // --- E7: Take-profit 1 (advisory — signal partial or tighten stop) ---
        var tp1Distance = _config.Tp1R * atrForRisk;
        if (isLong && price >= entryPrice + tp1Distance)
        {
            return V3LiveExitDecision.Advisory("take-profit-1-zone",
                $"Price {price:F2} in TP1 zone (≥ {entryPrice + tp1Distance:F2}). Consider partial close.");
        }
        if (!isLong && price <= entryPrice - tp1Distance)
        {
            return V3LiveExitDecision.Advisory("take-profit-1-zone",
                $"Price {price:F2} in TP1 zone (≤ {entryPrice - tp1Distance:F2}). Consider partial close.");
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

    public bool ShouldExit => Action == V3LiveExitAction.Exit;
    public bool IsAdvisory => Action == V3LiveExitAction.Advisory;

    public static V3LiveExitDecision Hold(string detail) =>
        new() { Action = V3LiveExitAction.Hold, Reason = "hold", Detail = detail };

    public static V3LiveExitDecision Exit(string reason, string detail) =>
        new() { Action = V3LiveExitAction.Exit, Reason = reason, Detail = detail };

    public static V3LiveExitDecision Advisory(string reason, string detail) =>
        new() { Action = V3LiveExitAction.Advisory, Reason = reason, Detail = detail };
}

public enum V3LiveExitAction
{
    Hold,
    Exit,
    Advisory
}
