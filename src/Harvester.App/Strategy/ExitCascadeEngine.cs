using Harvester.App.Backtest.Engine;

namespace Harvester.App.Strategy;

/// <summary>
/// Shared stateless exit cascade engine that encapsulates the core R-based exit
/// conditions used by both <see cref="V3LivePositionMonitor"/> (live trading)
/// and <c>LivePaperBot</c> (backtest simulation). Eliminates duplication of
/// exit logic across the two code paths.
///
/// Exit conditions evaluated (in priority order):
///   1. Micro-trail (cents-based V5-style trailing)
///   2. Hard stop (price beyond stop distance)
///   3. Take-profit 2 (full close)
///   4. Giveback (peak unrealized gave back too much — pct or USD cap)
///   5. Trailing stop (after break-even activation)
///   6. Take-profit 1 → breakeven tighten + optional partial close
///   7. Break-even activation
///   8. Time stop (max hold duration with insufficient progress)
///
/// Live-only conditions (L1 stale, L2 depth, squeeze, MTF reversal, session end)
/// are NOT included here — those remain in <see cref="V3LivePositionMonitor"/>.
/// </summary>
public static class ExitCascadeEngine
{
    /// <summary>
    /// Evaluate core R-based exit conditions given the current position state and params.
    /// Returns both an exit decision and any position-state mutations the caller should apply.
    /// </summary>
    public static ExitCascadeResult Evaluate(in ExitCascadeInput input)
    {
        var p = input.Params;
        var isLong = input.IsLong;
        var price = input.CurrentPrice;
        var entryPrice = input.EntryPrice;
        var rps = input.RiskPerShare;
        if (rps <= 0)
            return ExitCascadeResult.Hold();

        // Compute R-ratios
        double unrealizedR = isLong
            ? (price - entryPrice) / rps
            : (entryPrice - price) / rps;

        double profitPerShare = isLong
            ? price - entryPrice
            : entryPrice - price;

        // Track peak / compute peakR
        var peakPrice = input.PeakFavorablePrice;
        if (isLong)
            peakPrice = Math.Max(peakPrice, price);
        else if (peakPrice > 0)
            peakPrice = Math.Min(peakPrice, price);
        else
            peakPrice = price;

        double peakR = isLong
            ? (peakPrice - entryPrice) / rps
            : (entryPrice - peakPrice) / rps;

        var stopPrice = input.StopPrice;
        var trailingStop = input.TrailingStopPrice;
        var beActivated = input.BreakevenActivated;
        var tp1Activated = input.Tp1Activated;

        // ═══ 1. MICRO-TRAIL (V5-style cents-based trailing) ═══
        if (p.MicroTrailCents > 0 && p.MicroTrailActivateCents > 0)
        {
            if (profitPerShare >= p.MicroTrailActivateCents / 100.0)
            {
                var microDist = p.MicroTrailCents / 100.0;
                if (isLong)
                {
                    var microStop = peakPrice - microDist;
                    trailingStop = Math.Max(trailingStop, microStop);
                    if (price <= trailingStop)
                    {
                        return ExitCascadeResult.Exit("MICRO_TRAIL",
                            $"Micro-trail: peak={peakPrice:F2} trail={trailingStop:F2}",
                            peakPrice, trailingStop, stopPrice: trailingStop,
                            beActivated, tp1Activated);
                    }
                }
                else
                {
                    var microStop = peakPrice + microDist;
                    trailingStop = Math.Min(trailingStop, microStop);
                    if (price >= trailingStop)
                    {
                        return ExitCascadeResult.Exit("MICRO_TRAIL",
                            $"Micro-trail: peak={peakPrice:F2} trail={trailingStop:F2}",
                            peakPrice, trailingStop, stopPrice: trailingStop,
                            beActivated, tp1Activated);
                    }
                }

                stopPrice = trailingStop;
            }
        }

        // ═══ 2. HARD STOP ═══
        if (p.CheckHardStop)
        {
            var hardStopPrice = isLong
                ? entryPrice - p.HardStopR * input.AtrAtEntry
                : entryPrice + p.HardStopR * input.AtrAtEntry;

            // Use the tighter of configured stop and bracket stop
            if (stopPrice > 0)
                hardStopPrice = isLong
                    ? Math.Max(hardStopPrice, stopPrice)
                    : Math.Min(hardStopPrice, stopPrice);

            if ((isLong && price <= hardStopPrice) || (!isLong && price >= hardStopPrice))
            {
                return ExitCascadeResult.Exit("hard-stop",
                    $"Price {price:F2} hit stop {hardStopPrice:F2}",
                    peakPrice, trailingStop, stopPrice: hardStopPrice,
                    beActivated, tp1Activated);
            }
        }

        // ═══ 3. TAKE-PROFIT 2 (full close) ═══
        if (unrealizedR >= p.Tp2R)
        {
            return ExitCascadeResult.Exit("TP2",
                $"Price {price:F2} reached {unrealizedR:F2}R (TP2={p.Tp2R:F2}R)",
                peakPrice, trailingStop, stopPrice,
                beActivated, tp1Activated);
        }

        // ═══ 4. GIVEBACK ═══
        if (peakR > 0 && unrealizedR > 0)
        {
            var effectiveUsdCap = p.UseFixedGivebackUsdCap
                ? Math.Max(0.0, p.GivebackUsdCap) : 0.0;

            if (effectiveUsdCap > 0)
            {
                var unrealizedPnl = profitPerShare * Math.Abs(input.Quantity);
                var peakPnl = input.UnrealizedPnlPeak > 0
                    ? input.UnrealizedPnlPeak
                    : peakR * rps * Math.Abs(input.Quantity);
                var giveback = peakPnl - unrealizedPnl;
                if (unrealizedPnl > 0 && giveback >= effectiveUsdCap)
                {
                    return ExitCascadeResult.Exit("giveback-usd-cap",
                        $"Gave back ${giveback:F2} (cap ${effectiveUsdCap:F2})",
                        peakPrice, trailingStop, stopPrice,
                        beActivated, tp1Activated);
                }
            }
            else
            {
                var givebackPct = (peakR - unrealizedR) / peakR;
                if (givebackPct >= p.GivebackPct)
                {
                    return ExitCascadeResult.Exit("GIVEBACK",
                        $"Gave back {givebackPct:P0} of peak ({peakR:F2}R → {unrealizedR:F2}R)",
                        peakPrice, trailingStop, stopPrice,
                        beActivated, tp1Activated);
                }
            }
        }

        // ═══ 5. TRAILING STOP (after break-even activation) ═══
        if (beActivated)
        {
            var trailDist = p.TrailR * rps;
            if (isLong)
            {
                var newTrail = peakPrice - trailDist;
                trailingStop = Math.Max(trailingStop, newTrail);
                if (price <= trailingStop)
                {
                    return ExitCascadeResult.Exit("TRAIL",
                        $"Price {price:F2} <= trail {trailingStop:F2} (peak {peakPrice:F2})",
                        peakPrice, trailingStop, stopPrice,
                        beActivated, tp1Activated);
                }
            }
            else
            {
                var newTrail = peakPrice + trailDist;
                trailingStop = Math.Min(trailingStop, newTrail);
                if (price >= trailingStop)
                {
                    return ExitCascadeResult.Exit("TRAIL",
                        $"Price {price:F2} >= trail {trailingStop:F2} (peak {peakPrice:F2})",
                        peakPrice, trailingStop, stopPrice,
                        beActivated, tp1Activated);
                }
            }
        }

        // ═══ 6. TAKE-PROFIT 1 → breakeven tighten + optional partial close ═══
        if (unrealizedR >= p.Tp1R && !tp1Activated)
        {
            tp1Activated = true;
            beActivated = true;

            if (p.Tp1TightenToBe)
            {
                var buffer = p.Tp1BreakevenBufferAtr * input.AtrAtEntry;
                var newStop = isLong
                    ? Math.Max(stopPrice, entryPrice + buffer)
                    : (stopPrice > 0
                        ? Math.Min(stopPrice, entryPrice - buffer)
                        : entryPrice - buffer);
                stopPrice = newStop;
                trailingStop = isLong
                    ? Math.Max(trailingStop, stopPrice)
                    : Math.Min(trailingStop, stopPrice);
            }
            else
            {
                // Tighten to breakeven without buffer
                stopPrice = isLong
                    ? Math.Max(stopPrice, entryPrice)
                    : (stopPrice > 0
                        ? Math.Min(stopPrice, entryPrice)
                        : entryPrice);
                trailingStop = stopPrice;
            }

            // Partial close at TP1
            if (p.Tp1PartialClosePct > 0)
            {
                var totalQty = (int)Math.Abs(input.Quantity);
                var closeQty = Math.Max(1, (int)Math.Floor(totalQty * p.Tp1PartialClosePct));
                if (closeQty >= totalQty) closeQty = totalQty;

                return ExitCascadeResult.PartialExit("TP1_PARTIAL",
                    $"TP1: closing {closeQty}/{totalQty} ({p.Tp1PartialClosePct:P0}), stop→BE",
                    closeQty,
                    peakPrice, trailingStop, stopPrice,
                    beActivated, tp1Activated);
            }

            // TP1 hit but no partial — just state update (tightened stop, set flags)
            // Fall through to continue evaluation with updated state
        }

        // ═══ 7. BREAK-EVEN ACTIVATION (without TP1 requirement) ═══
        if (!beActivated && unrealizedR >= p.BreakevenR)
        {
            beActivated = true;
            var newStop = entryPrice;
            stopPrice = isLong
                ? Math.Max(stopPrice, newStop)
                : (stopPrice > 0
                    ? Math.Min(stopPrice, newStop)
                    : newStop);
            trailingStop = stopPrice;
        }

        // ═══ 8. TIME STOP ═══
        if (input.HoldSeconds >= p.MaxHoldSeconds)
        {
            var progressR = unrealizedR;
            if (progressR < p.TimeStopMinProgressR)
            {
                return ExitCascadeResult.Exit("TIME",
                    $"Held {input.HoldSeconds:F0}s (max {p.MaxHoldSeconds}s), progress {progressR:F2}R < {p.TimeStopMinProgressR:F2}R",
                    peakPrice, trailingStop, stopPrice,
                    beActivated, tp1Activated);
            }
        }

        // No exit triggered — return hold with state updates
        return ExitCascadeResult.HoldWithUpdates(
            peakPrice, trailingStop, stopPrice,
            beActivated, tp1Activated);
    }
}

// ─── Input / Output Records ─────────────────────────────────────────────────

/// <summary>
/// Immutable snapshot of position state for exit cascade evaluation.
/// Both LivePaperBot and V3LivePositionMonitor convert their own position
/// representations into this common input contract.
/// </summary>
public readonly record struct ExitCascadeInput
{
    public required bool IsLong { get; init; }
    public required double EntryPrice { get; init; }
    public required double CurrentPrice { get; init; }
    public required double StopPrice { get; init; }
    public required double AtrAtEntry { get; init; }
    public required double RiskPerShare { get; init; }
    public required double PeakFavorablePrice { get; init; }
    public required double TrailingStopPrice { get; init; }
    public required bool BreakevenActivated { get; init; }
    public required bool Tp1Activated { get; init; }
    public required double UnrealizedPnlPeak { get; init; }
    public required double Quantity { get; init; }
    public required double HoldSeconds { get; init; }
    public required ExitCascadeParams Params { get; init; }
}

/// <summary>
/// Common exit cascade parameters. Both LivePaperBot and V3LivePositionMonitor
/// map their per-strategy configs to this shared parameter set.
/// </summary>
public sealed record ExitCascadeParams
{
    // ── R-based thresholds ──
    public double HardStopR { get; init; } = 2.0;
    public double BreakevenR { get; init; } = 1.0;
    public double TrailR { get; init; } = 1.5;
    public double Tp1R { get; init; } = 1.5;
    public double Tp2R { get; init; } = 3.0;

    // ── Giveback ──
    public double GivebackPct { get; init; } = 0.70;
    public bool UseFixedGivebackUsdCap { get; init; }
    public double GivebackUsdCap { get; init; }

    // ── Time ──
    public int MaxHoldSeconds { get; init; } = 90 * 60;
    public double TimeStopMinProgressR { get; init; } = 0.5;

    // ── TP1 behaviour ──
    public bool Tp1TightenToBe { get; init; } = true;
    public double Tp1PartialClosePct { get; init; }
    public double Tp1BreakevenBufferAtr { get; init; }

    // ── Micro-trail (V5-style) ──
    public double MicroTrailCents { get; init; }
    public double MicroTrailActivateCents { get; init; }

    /// <summary>
    /// Whether the engine should evaluate the hard stop condition.
    /// LivePaperBot sets false (broker bracket handles stop), V3LivePositionMonitor sets true.
    /// </summary>
    public bool CheckHardStop { get; init; }

    // ─── Factory methods for common configurations ───

    /// <summary>
    /// Create exit cascade params from V3LiveConfig (used by V3LivePositionMonitor).
    /// </summary>
    public static ExitCascadeParams FromLiveConfig(V3LiveConfig config) => new()
    {
        HardStopR = config.HardStopR,
        BreakevenR = config.BreakevenR,
        TrailR = config.TrailR,
        Tp1R = config.Tp1R,
        Tp2R = config.Tp2R,
        GivebackPct = config.GivebackPct,
        UseFixedGivebackUsdCap = config.UseFixedGivebackUsdCap,
        GivebackUsdCap = config.GivebackUsdCap,
        MaxHoldSeconds = config.MaxHoldBars * 60,
        TimeStopMinProgressR = config.TimeStopMinProgressR,
        Tp1TightenToBe = config.Tp1TightenToBe,
        Tp1PartialClosePct = config.Tp1PartialClosePct,
        Tp1BreakevenBufferAtr = config.Tp1BreakevenBufferAtr,
        CheckHardStop = true,
    };
}

/// <summary>
/// Result of shared exit cascade evaluation. Contains both the decision and
/// any position-state mutations the caller should apply.
/// </summary>
public sealed class ExitCascadeResult
{
    public string? ExitReason { get; init; }
    public string Detail { get; init; } = string.Empty;
    public bool IsPartialExit { get; init; }
    public int PartialQuantity { get; init; }

    // ── State updates the caller should apply back to their position representation ──
    public double UpdatedPeakPrice { get; init; }
    public double UpdatedTrailingStop { get; init; }
    public double UpdatedStopPrice { get; init; }
    public bool UpdatedBreakevenActivated { get; init; }
    public bool UpdatedTp1Activated { get; init; }

    public bool ShouldExit => ExitReason is not null;

    internal static ExitCascadeResult Hold() => new();

    internal static ExitCascadeResult HoldWithUpdates(
        double peakPrice, double trailingStop, double stopPrice,
        bool beActivated, bool tp1Activated) => new()
    {
        UpdatedPeakPrice = peakPrice,
        UpdatedTrailingStop = trailingStop,
        UpdatedStopPrice = stopPrice,
        UpdatedBreakevenActivated = beActivated,
        UpdatedTp1Activated = tp1Activated,
    };

    internal static ExitCascadeResult Exit(
        string reason, string detail,
        double peakPrice, double trailingStop, double stopPrice,
        bool beActivated, bool tp1Activated) => new()
    {
        ExitReason = reason,
        Detail = detail,
        UpdatedPeakPrice = peakPrice,
        UpdatedTrailingStop = trailingStop,
        UpdatedStopPrice = stopPrice,
        UpdatedBreakevenActivated = beActivated,
        UpdatedTp1Activated = tp1Activated,
    };

    internal static ExitCascadeResult PartialExit(
        string reason, string detail, int partialQuantity,
        double peakPrice, double trailingStop, double stopPrice,
        bool beActivated, bool tp1Activated) => new()
    {
        ExitReason = reason,
        Detail = detail,
        IsPartialExit = true,
        PartialQuantity = partialQuantity,
        UpdatedPeakPrice = peakPrice,
        UpdatedTrailingStop = trailingStop,
        UpdatedStopPrice = stopPrice,
        UpdatedBreakevenActivated = beActivated,
        UpdatedTp1Activated = tp1Activated,
    };
}
