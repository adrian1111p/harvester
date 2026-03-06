using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

/// <summary>
/// Common exit-simulation engine used by all strategy variants.
/// Factors out the shared exit chain (HardStop → TP2 → TP1 → BE → Trail → Giveback → TimeStop)
/// with optional micro-trail, 9EMA trail, and reversal-flatten features.
/// </summary>
public static class ExitEngine
{
    /// <summary>Configuration for the exit simulation.</summary>
    public sealed class ExitConfig
    {
        // ── Core exit parameters ──
        public double HardStopR { get; init; } = 1.0;
        public double BreakevenR { get; init; } = 1.0;
        public double TrailR { get; init; } = 0.5;
        public double GivebackPct { get; init; } = 0.50;
        public double Tp1R { get; init; } = 1.5;
        public double Tp2R { get; init; } = 3.0;
        public int MaxHoldBars { get; init; } = 180;

        // ── Giveback threshold (minimum peak_r before checking giveback) ──
        public double GivebackMinPeakR { get; init; } = 0.0;
        public bool UseFixedGivebackUsdCap { get; init; } = false;
        public bool UseNotionalGivebackCap { get; init; } = false;
        public double GivebackPctOfNotional { get; init; } = 0.01;
        public double GivebackUsdCap { get; init; } = 38.0;
        public bool UseVariableGivebackUsdCap { get; init; } = true;
        public double GivebackCapAnchorLowPrice { get; init; } = 1.0;
        public double GivebackCapAnchorHighPrice { get; init; } = 300.0;
        public double GivebackCapAtLowPrice { get; init; } = 8.0;
        public double GivebackCapAtHighPrice { get; init; } = 30.0;
        public double GivebackCapMinUsd { get; init; } = 5.0;
        public double GivebackCapMaxUsd { get; init; } = 60.0;
        public bool UseTightTrailOnFixedGiveback { get; init; } = true;
        public double TightTrailAnchorLowPrice { get; init; } = 1.0;
        public double TightTrailAnchorHighPrice { get; init; } = 300.0;
        public double TightTrailAtLowPrice { get; init; } = 0.30;
        public double TightTrailAtHighPrice { get; init; } = 1.00;

        // ── Slippage & Commission ──
        public double SlippageCents { get; init; } = 1.0;
        public double CommissionPerShare { get; init; } = 0.005;
        public bool DeductCommission { get; init; } = true;

        // ── Optional features ──
        public bool Tp1TightenToBe { get; init; } = true;
        public bool ReversalFlatten { get; init; } = false;
        public bool MicroTrail { get; init; } = false;
        public double MicroTrailCents { get; init; } = 3.0;
        public double MicroTrailActivateCents { get; init; } = 5.0;
        public bool EmaTrail { get; init; } = false;
        public double EmaTrailBufferAtr { get; init; } = 0.1;
    }

    /// <summary>
    /// Run the shared exit simulation for a single trade.
    /// </summary>
    public static BacktestTradeResult SimulateTrade(
        BacktestSignal signal,
        EnrichedBar[] triggerBars,
        ExitConfig cfg)
    {
        var side = signal.Side;
        double entryPrice = signal.EntryPrice
            + (side == TradeSide.Long ? cfg.SlippageCents / 100.0 : -cfg.SlippageCents / 100.0);
        double stopPrice = signal.StopPrice;
        double riskPerShare = signal.RiskPerShare;
        int posSize = signal.PositionSize;

        double peakPrice = entryPrice;
        double troughPrice = entryPrice;
        bool beActivated = false;
        double trailingStop = stopPrice;
        ExitReason exitReason = ExitReason.TimeStop;
        double exitPrice = entryPrice;
        int exitBar = signal.BarIndex;
        bool exited = false;

        // Micro-trail state
        bool microTrailActive = false;
        double microTrailStop = double.NaN;
        bool tightGivebackTrailActive = false;
        double tightGivebackStop = double.NaN;

        int lastBar = Math.Min(signal.BarIndex + cfg.MaxHoldBars + 1, triggerBars.Length);

        for (int j = signal.BarIndex + 1; j < lastBar; j++)
        {
            var bar = triggerBars[j].Bar;
            double price = bar.Close;
            double high = bar.High;
            double low = bar.Low;

            // Track peak/trough
            if (side == TradeSide.Long)
                peakPrice = Math.Max(peakPrice, high);
            else
                troughPrice = Math.Min(troughPrice, low);

            // ── Priority 1: Hard stop (handle gap-through: fill at worse of open vs stop) ──
            if (side == TradeSide.Long && low <= stopPrice)
            {
                exitPrice = Math.Min(bar.Open, stopPrice);
                exitReason = ExitReason.HardStop;
                exitBar = j; exited = true; break;
            }
            if (side == TradeSide.Short && high >= stopPrice)
            {
                exitPrice = Math.Max(bar.Open, stopPrice);
                exitReason = ExitReason.HardStop;
                exitBar = j; exited = true; break;
            }

            if (tightGivebackTrailActive)
            {
                double trailPerShare = ComputeTightGivebackTrailPerShare(price, cfg);
                double candidateStop = side == TradeSide.Long
                    ? peakPrice - trailPerShare
                    : troughPrice + trailPerShare;

                if (double.IsNaN(tightGivebackStop))
                    tightGivebackStop = candidateStop;
                else if (side == TradeSide.Long)
                    tightGivebackStop = Math.Max(tightGivebackStop, candidateStop);
                else
                    tightGivebackStop = Math.Min(tightGivebackStop, candidateStop);

                if ((side == TradeSide.Long && low <= tightGivebackStop) ||
                    (side == TradeSide.Short && high >= tightGivebackStop))
                {
                    exitPrice = side == TradeSide.Long
                        ? Math.Min(bar.Open, tightGivebackStop)
                        : Math.Max(bar.Open, tightGivebackStop);
                    exitReason = ExitReason.Giveback;
                    exitBar = j; exited = true; break;
                }
            }

            // ── Unrealised R ──
            double unrealizedR = side == TradeSide.Long
                ? (price - entryPrice) / riskPerShare
                : (entryPrice - price) / riskPerShare;

            // Best intra-bar unrealised R (uses H/L for TP detection)
            double bestUnrealizedR = side == TradeSide.Long
                ? (high - entryPrice) / riskPerShare
                : (entryPrice - low) / riskPerShare;

            // Worst intra-bar unrealised R (uses L/H for giveback detection)
            double worstUnrealizedR = side == TradeSide.Long
                ? (low - entryPrice) / riskPerShare
                : (entryPrice - high) / riskPerShare;

            double peakR = side == TradeSide.Long
                ? (peakPrice - entryPrice) / riskPerShare
                : (entryPrice - troughPrice) / riskPerShare;

            // ── Reversal Flatten (V5/V6/V7 feature) ──
            if (cfg.ReversalFlatten && unrealizedR > 0.2 && j > signal.BarIndex + 1)
            {
                var prevBar = triggerBars[j - 1].Bar;
                bool engulfing = side == TradeSide.Long
                    ? (bar.Close < bar.Open && bar.Open > prevBar.Close && bar.Close < prevBar.Open)
                    : (bar.Close > bar.Open && bar.Open < prevBar.Close && bar.Close > prevBar.Open);
                double range = high - low;
                bool bigWick = side == TradeSide.Long
                    ? range > 0 && (high - Math.Max(bar.Open, bar.Close)) / range > 0.60
                    : range > 0 && (Math.Min(bar.Open, bar.Close) - low) / range > 0.60;

                if (engulfing || bigWick)
                {
                    exitPrice = price;
                    exitReason = ExitReason.ReversalFlatten;
                    exitBar = j; exited = true; break;
                }
            }

            // ── TP2 (full close) — use intra-bar H/L, fill at exact TP level ──
            if (bestUnrealizedR >= cfg.Tp2R)
            {
                exitPrice = side == TradeSide.Long
                    ? entryPrice + cfg.Tp2R * riskPerShare
                    : entryPrice - cfg.Tp2R * riskPerShare;
                exitReason = ExitReason.Tp2;
                exitBar = j; exited = true; break;
            }

            // ── TP1 scale-out → tighten to BE ──
            if (cfg.Tp1TightenToBe && bestUnrealizedR >= cfg.Tp1R && !beActivated)
            {
                beActivated = true;
                if (side == TradeSide.Long)
                {
                    stopPrice = Math.Max(stopPrice, entryPrice);
                    trailingStop = Math.Max(trailingStop, entryPrice);
                }
                else
                {
                    stopPrice = Math.Min(stopPrice, entryPrice);
                    trailingStop = Math.Min(trailingStop, entryPrice);
                }
            }

            // ── Break-even ──
            if (!beActivated && bestUnrealizedR >= cfg.BreakevenR)
            {
                beActivated = true;
                if (side == TradeSide.Long)
                    stopPrice = Math.Max(stopPrice, entryPrice);
                else
                    stopPrice = Math.Min(stopPrice, entryPrice);
            }

            // ── Micro-trail update (V5/V6/V7 feature) ──
            if (cfg.MicroTrail)
            {
                double profitCents = side == TradeSide.Long
                    ? (price - entryPrice) * 100.0
                    : (entryPrice - price) * 100.0;

                if (profitCents >= cfg.MicroTrailActivateCents)
                {
                    microTrailActive = true;
                    double newMicroStop = side == TradeSide.Long
                        ? price - cfg.MicroTrailCents / 100.0
                        : price + cfg.MicroTrailCents / 100.0;

                    if (double.IsNaN(microTrailStop))
                        microTrailStop = newMicroStop;
                    else if (side == TradeSide.Long)
                        microTrailStop = Math.Max(microTrailStop, newMicroStop);
                    else
                        microTrailStop = Math.Min(microTrailStop, newMicroStop);
                }
            }

            // ── 9 EMA Trail update (V7 feature) — always update when BE active ──
            if (cfg.EmaTrail && beActivated)
            {
                double ema9 = triggerBars[j].Ema9;
                double atr = triggerBars[j].Atr14;
                if (!double.IsNaN(ema9) && !double.IsNaN(atr))
                {
                    double emaStop = side == TradeSide.Long
                        ? ema9 - cfg.EmaTrailBufferAtr * atr
                        : ema9 + cfg.EmaTrailBufferAtr * atr;

                    if (side == TradeSide.Long)
                        trailingStop = Math.Max(trailingStop, emaStop);
                    else
                        trailingStop = Math.Min(trailingStop, emaStop);
                }
            }

            // ── Standard trailing stop update — always update when BE active ──
            if (beActivated)
            {
                double trailDist = cfg.TrailR * riskPerShare;
                if (side == TradeSide.Long)
                    trailingStop = Math.Max(trailingStop, peakPrice - trailDist);
                else
                    trailingStop = Math.Min(trailingStop, troughPrice + trailDist);
            }

            // ── Combined trail exit: use the stop that locks in the most profit ──
            {
                double bestStop = double.NaN;
                ExitReason bestReason = ExitReason.Trailing;

                // Standard / EMA trail stop
                if (beActivated)
                {
                    bestStop = trailingStop;
                    bestReason = cfg.EmaTrail ? ExitReason.EmaTrail : ExitReason.Trailing;
                }

                // Micro-trail: use whichever stop protects more profit
                if (microTrailActive && !double.IsNaN(microTrailStop))
                {
                    if (double.IsNaN(bestStop))
                    {
                        bestStop = microTrailStop;
                        bestReason = ExitReason.MicroTrail;
                    }
                    else if (side == TradeSide.Long ? microTrailStop > bestStop : microTrailStop < bestStop)
                    {
                        bestStop = microTrailStop;
                        bestReason = ExitReason.MicroTrail;
                    }
                }

                if (!double.IsNaN(bestStop) &&
                    ((side == TradeSide.Long && low <= bestStop) ||
                     (side == TradeSide.Short && high >= bestStop)))
                {
                    exitPrice = bestStop;
                    exitReason = bestReason;
                    exitBar = j; exited = true; break;
                }
            }

            // ── Giveback from peak (fixed USD cap mode) ──
            if ((cfg.UseFixedGivebackUsdCap && cfg.GivebackUsdCap > 0) || cfg.UseNotionalGivebackCap)
            {
                double effectiveGivebackUsdCap;
                if (cfg.UseNotionalGivebackCap)
                {
                    double positionNotional = Math.Abs(entryPrice * posSize);
                    double notionalCap = Math.Max(0.0, cfg.GivebackPctOfNotional * positionNotional);
                    effectiveGivebackUsdCap = cfg.GivebackUsdCap > 0
                        ? Math.Min(notionalCap, cfg.GivebackUsdCap)
                        : notionalCap;
                }
                else
                {
                    effectiveGivebackUsdCap = cfg.UseVariableGivebackUsdCap
                        ? ComputeVariableGivebackUsdCap(price, cfg)
                        : cfg.GivebackUsdCap;
                }

                if (effectiveGivebackUsdCap > 0)
                {
                    // Compute the giveback stop level: the price at which dollar giveback = cap
                    double givebackStopLevel = side == TradeSide.Long
                        ? peakPrice - effectiveGivebackUsdCap / posSize
                        : troughPrice + effectiveGivebackUsdCap / posSize;

                    // Only trigger if the stop would still be in profit territory
                    bool stopInProfit = side == TradeSide.Long
                        ? givebackStopLevel > entryPrice
                        : givebackStopLevel < entryPrice;

                    if (stopInProfit)
                    {
                        // Use H/L for intra-bar detection (like a stop order at the giveback level)
                        bool hit = side == TradeSide.Long
                            ? low <= givebackStopLevel
                            : high >= givebackStopLevel;

                        if (hit)
                        {
                            if (!cfg.UseTightTrailOnFixedGiveback)
                            {
                                exitPrice = side == TradeSide.Long
                                    ? Math.Min(bar.Open, givebackStopLevel)
                                    : Math.Max(bar.Open, givebackStopLevel);
                                exitReason = ExitReason.Giveback;
                                exitBar = j; exited = true; break;
                            }

                            tightGivebackTrailActive = true;
                            double trailPerShare = ComputeTightGivebackTrailPerShare(price, cfg);
                            double candidateStop = side == TradeSide.Long
                                ? peakPrice - trailPerShare
                                : troughPrice + trailPerShare;

                            if (double.IsNaN(tightGivebackStop))
                                tightGivebackStop = candidateStop;
                            else if (side == TradeSide.Long)
                                tightGivebackStop = Math.Max(tightGivebackStop, candidateStop);
                            else
                                tightGivebackStop = Math.Min(tightGivebackStop, candidateStop);

                            if ((side == TradeSide.Long && low <= tightGivebackStop) ||
                                (side == TradeSide.Short && high >= tightGivebackStop))
                            {
                                exitPrice = tightGivebackStop;
                                exitReason = ExitReason.Giveback;
                                exitBar = j; exited = true; break;
                            }
                        }
                    }
                }
            }

            // ── Giveback from peak (R-percent mode) ──
            else if (peakR > cfg.GivebackMinPeakR)
            {
                // Use worst intra-bar unrealised R to detect giveback via H/L
                double worstGiveback = peakR > 0 ? (peakR - worstUnrealizedR) / peakR : 0;
                if (worstGiveback >= cfg.GivebackPct && worstUnrealizedR > 0)
                {
                    // Compute exact trigger price where giveback% was hit
                    double givebackTriggerPrice = side == TradeSide.Long
                        ? entryPrice + riskPerShare * peakR * (1.0 - cfg.GivebackPct)
                        : entryPrice - riskPerShare * peakR * (1.0 - cfg.GivebackPct);
                    exitPrice = side == TradeSide.Long
                        ? Math.Min(bar.Open, givebackTriggerPrice)
                        : Math.Max(bar.Open, givebackTriggerPrice);
                    exitReason = ExitReason.Giveback;
                    exitBar = j; exited = true; break;
                }
            }

            // ── Time stop ──
            if (j - signal.BarIndex >= cfg.MaxHoldBars)
            {
                exitPrice = price;
                exitReason = ExitReason.TimeStop;
                exitBar = j; exited = true; break;
            }
        }

        // Exhausted bars → force close
        if (!exited)
        {
            int forceBar = Math.Min(signal.BarIndex + cfg.MaxHoldBars, triggerBars.Length - 1);
            exitPrice = triggerBars[forceBar].Bar.Close;
            exitReason = ExitReason.TimeStop;
            exitBar = forceBar;
        }

        // Exit slippage
        if (side == TradeSide.Long) exitPrice -= cfg.SlippageCents / 100.0;
        else exitPrice += cfg.SlippageCents / 100.0;

        // PnL
        double pnlPerShare = side == TradeSide.Long
            ? exitPrice - entryPrice
            : entryPrice - exitPrice;
        double commission = cfg.DeductCommission ? cfg.CommissionPerShare * posSize * 2 : 0;
        double pnl = pnlPerShare * posSize - commission;
        double pnlR = riskPerShare > 0 ? pnlPerShare / riskPerShare : 0;

        double finalPeakR = side == TradeSide.Long
            ? (peakPrice - entryPrice) / riskPerShare
            : (entryPrice - troughPrice) / riskPerShare;

        return new BacktestTradeResult(
            EntryBar: signal.BarIndex,
            EntryTime: signal.Timestamp,
            ExitBar: exitBar,
            ExitTime: triggerBars[exitBar].Bar.Timestamp,
            Side: side,
            EntryPrice: entryPrice,
            ExitPrice: exitPrice,
            StopPrice: signal.StopPrice,
            PositionSize: posSize,
            Pnl: pnl,
            PnlR: pnlR,
            ExitReason: exitReason,
            PeakR: finalPeakR,
            BarsHeld: exitBar - signal.BarIndex);
    }

    private static double ComputeTightGivebackTrailPerShare(double price, ExitConfig cfg)
    {
        double lowPrice = Math.Max(0.01, cfg.TightTrailAnchorLowPrice);
        double highPrice = Math.Max(lowPrice + 0.01, cfg.TightTrailAnchorHighPrice);
        double lowTrail = Math.Max(0.01, cfg.TightTrailAtLowPrice);
        double highTrail = Math.Max(0.01, cfg.TightTrailAtHighPrice);

        double clampedPrice = Math.Clamp(price, lowPrice, highPrice);

        double logLo = Math.Log(lowPrice);
        double logHi = Math.Log(highPrice);
        double logPx = Math.Log(clampedPrice);

        double t = (logPx - logLo) / Math.Max(1e-9, logHi - logLo);
        double trail = lowTrail + (highTrail - lowTrail) * t;

        return Math.Max(0.01, trail);
    }

    private static double ComputeVariableGivebackUsdCap(double price, ExitConfig cfg)
    {
        double lowPrice = Math.Max(0.01, cfg.GivebackCapAnchorLowPrice);
        double highPrice = Math.Max(lowPrice + 0.01, cfg.GivebackCapAnchorHighPrice);
        double lowCap = Math.Max(0.01, cfg.GivebackCapAtLowPrice);
        double highCap = Math.Max(0.01, cfg.GivebackCapAtHighPrice);
        double capMin = Math.Max(0.01, Math.Min(cfg.GivebackCapMinUsd, cfg.GivebackCapMaxUsd));
        double capMax = Math.Max(capMin, Math.Max(cfg.GivebackCapMinUsd, cfg.GivebackCapMaxUsd));

        double clampedPrice = Math.Clamp(price, lowPrice, highPrice);

        double logLo = Math.Log(lowPrice);
        double logHi = Math.Log(highPrice);
        double logPx = Math.Log(clampedPrice);
        double t = (logPx - logLo) / Math.Max(1e-9, logHi - logLo);

        double cap = lowCap + (highCap - lowCap) * t;
        return Math.Clamp(cap, capMin, capMax);
    }
}
