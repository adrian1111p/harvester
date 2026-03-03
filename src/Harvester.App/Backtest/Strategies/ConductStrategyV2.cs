using Harvester.App.Backtest.Engine;
using Harvester.App.Backtest.Indicators;

namespace Harvester.App.Backtest.Strategies;

/// <summary>
/// Conduct Strategy V2.0 — Multi-timeframe trend-following (Long + Short).
/// Ported from Python ConductStrategyV13 (strategy.py).
///
/// Entry logic:
///   1. HTF bias via 1h/1D EMA slope + ADX + MACD
///   2. MTF momentum alignment via 5m/15m MACD + RSI
///   3. Trigger via Supertrend flip or EMA pullback + volume
///   4. 20MA exhaustion filter (V2.0)
///
/// Exit chain: HardStop → TP2 → TP1/BE → Trailing → Giveback → TimeStop
/// </summary>
public sealed class ConductStrategyV2 : IBacktestStrategy
{
    private readonly StrategyConfig _cfg;

    public ConductStrategyV2(StrategyConfig? cfg = null)
    {
        _cfg = cfg ?? new StrategyConfig();
    }

    // ── Higher-TF Bias ───────────────────────────────────────────────────

    private HtfBias ComputeHtfBias(EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        var scores = new List<int>();

        foreach (var (bars, label) in new[] { (bars1h, "1h"), (bars1d, "1D") })
        {
            if (bars == null || bars.Length < 50) continue;

            var last = bars[^1];
            var prev = bars.Length > 1 ? bars[^2] : last;

            // EMA slope
            int emaSlope = last.Ema21 > prev.Ema21 ? 1 : -1;

            // ADX + DI
            int diScore = 0;
            if (!double.IsNaN(last.Adx) && last.Adx > _cfg.AdxThreshold)
                diScore = last.PlusDi > last.MinusDi ? 1 : -1;

            // MACD histogram sign
            int macdScore = last.MacdHist > 0 ? 1 : -1;

            scores.Add(emaSlope + diScore + macdScore);
        }

        if (scores.Count == 0) return HtfBias.Neutral;
        double avg = scores.Average();
        if (avg >= 1.5) return HtfBias.Bull;
        if (avg <= -1.5) return HtfBias.Bear;
        return HtfBias.Neutral;
    }

    // ── Mid-TF Momentum ──────────────────────────────────────────────────

    private string ComputeMtfMomentum(
        EnrichedBar[]? bars5m,
        EnrichedBar[]? bars15m,
        TradeSide side)
    {
        int alignedCount = 0;
        int total = 0;

        foreach (var (bars, label) in new[] { (bars5m, "5m"), (bars15m, "15m") })
        {
            if (bars == null || bars.Length < 30) continue;

            var last = bars[^1];
            total++;

            bool macdOk = side == TradeSide.Long
                ? last.MacdHist > 0
                : last.MacdHist < 0;

            var (rsiLow, rsiHigh) = side == TradeSide.Long
                ? _cfg.RsiLongRange
                : _cfg.RsiShortRange;

            bool rsiOk = !double.IsNaN(last.Rsi14)
                && last.Rsi14 >= rsiLow
                && last.Rsi14 <= rsiHigh;

            if (macdOk && rsiOk) alignedCount++;
        }

        return total == 0 ? "CONFLICTING" : (alignedCount == total ? "ALIGNED" : "CONFLICTING");
    }

    // ── Signal Generation ────────────────────────────────────────────────

    public IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var signals = new List<BacktestSignal>();
        var htfBias = ComputeHtfBias(bars1h, bars1d);

        for (int i = 50; i < triggerBars.Length; i++)
        {
            var row = triggerBars[i];
            var prev = triggerBars[i - 1];

            double atrVal = row.Atr14;
            if (double.IsNaN(atrVal) || atrVal <= 0) continue;

            // ── Determine candidate sides ──
            var candidates = new List<TradeSide>();

            if (htfBias is HtfBias.Bull or HtfBias.Neutral)
            {
                bool stFlipLong = row.StDirection == 1 && prev.StDirection == -1;
                bool emaPullbackLong = row.Bar.Close >= row.Ema9 && prev.Bar.Close < prev.Ema9;
                bool priceAboveEma21 = row.Bar.Close > row.Ema21;

                if ((stFlipLong || emaPullbackLong) && priceAboveEma21)
                    candidates.Add(TradeSide.Long);
            }

            if (htfBias is HtfBias.Bear or HtfBias.Neutral)
            {
                bool stFlipShort = row.StDirection == -1 && prev.StDirection == 1;
                bool emaPullbackShort = row.Bar.Close <= row.Ema9 && prev.Bar.Close > prev.Ema9;
                bool priceBelowEma21 = row.Bar.Close < row.Ema21;

                if ((stFlipShort || emaPullbackShort) && priceBelowEma21)
                    candidates.Add(TradeSide.Short);
            }

            foreach (var side in candidates)
            {
                // ── Volume filter ──
                if (!double.IsNaN(row.Rvol) && row.Rvol < _cfg.RvolMin)
                    continue;

                // ── RSI filter ──
                if (!double.IsNaN(row.Rsi14))
                {
                    var (rsiLow, rsiHigh) = side == TradeSide.Long
                        ? _cfg.RsiLongRange
                        : _cfg.RsiShortRange;
                    if (row.Rsi14 < rsiLow || row.Rsi14 > rsiHigh)
                        continue;
                }

                // ── 20MA exhaustion filter (V2.0) ──
                if (!double.IsNaN(row.Sma20) && atrVal > 0)
                {
                    double maDist = (row.Bar.Close - row.Sma20) / atrVal;
                    if (side == TradeSide.Long && maDist > _cfg.MaxMaDistAtr)
                        continue;
                    if (side == TradeSide.Short && maDist < -_cfg.MaxMaDistAtr)
                        continue;
                }

                // ── Mid-TF momentum ──
                string mtfMom = ComputeMtfMomentum(bars5m, bars15m, side);

                // ── Compute stop & size ──
                double entryPrice = row.Bar.Close;
                double stopDist = _cfg.HardStopR * atrVal;
                double stopPrice = side == TradeSide.Long
                    ? entryPrice - stopDist
                    : entryPrice + stopDist;
                double riskPerShare = Math.Abs(entryPrice - stopPrice);

                if (riskPerShare <= 0) continue;

                int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));

                signals.Add(new BacktestSignal(
                    BarIndex: i,
                    Timestamp: row.Bar.Timestamp,
                    Side: side,
                    EntryPrice: entryPrice,
                    StopPrice: stopPrice,
                    RiskPerShare: riskPerShare,
                    PositionSize: posSize,
                    AtrValue: atrVal,
                    HtfTrend: htfBias,
                    MtfMomentum: mtfMom));
            }
        }

        return signals;
    }

    // ── Trade Simulation ─────────────────────────────────────────────────

    public BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
    {
        var side = signal.Side;
        double entryPrice = signal.EntryPrice
            + (side == TradeSide.Long ? _cfg.SlippageCents / 100.0 : -_cfg.SlippageCents / 100.0);
        double stopPrice = signal.StopPrice;
        double riskPerShare = signal.RiskPerShare;
        int posSize = signal.PositionSize;

        double peakPrice = entryPrice;
        double troughPrice = entryPrice;
        bool breakevenActivated = false;
        double trailingStop = stopPrice;
        ExitReason? exitReason = null;
        double exitPrice = entryPrice;
        int exitBar = signal.BarIndex;

        int lastBar = Math.Min(signal.BarIndex + _cfg.MaxHoldBars + 1, triggerBars.Length);
        bool exited = false;

        for (int j = signal.BarIndex + 1; j < lastBar; j++)
        {
            var bar = triggerBars[j].Bar;
            double price = bar.Close;
            double high = bar.High;
            double low = bar.Low;

            // Track peak / trough
            if (side == TradeSide.Long)
                peakPrice = Math.Max(peakPrice, high);
            else
                troughPrice = Math.Min(troughPrice, low);

            // ── Priority 1: Hard stop ──
            if (side == TradeSide.Long && low <= stopPrice)
            {
                exitPrice = stopPrice;
                exitReason = ExitReason.HardStop;
                exitBar = j;
                exited = true;
                break;
            }
            if (side == TradeSide.Short && high >= stopPrice)
            {
                exitPrice = stopPrice;
                exitReason = ExitReason.HardStop;
                exitBar = j;
                exited = true;
                break;
            }

            // ── Unrealised R ──
            double unrealizedR = side == TradeSide.Long
                ? (price - entryPrice) / riskPerShare
                : (entryPrice - price) / riskPerShare;
            double peakR = side == TradeSide.Long
                ? (peakPrice - entryPrice) / riskPerShare
                : (entryPrice - troughPrice) / riskPerShare;

            // ── Priority 2: TP2 (full close) ──
            if (unrealizedR >= _cfg.Tp2R)
            {
                exitPrice = price;
                exitReason = ExitReason.Tp2;
                exitBar = j;
                exited = true;
                break;
            }

            // ── Priority 3: TP1 scale-out (tighten stop) ──
            if (unrealizedR >= _cfg.Tp1R && !breakevenActivated)
            {
                breakevenActivated = true;
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

            // ── Priority 4: Break-even ──
            if (!breakevenActivated && unrealizedR >= _cfg.BreakevenR)
            {
                breakevenActivated = true;
                if (side == TradeSide.Long)
                    stopPrice = Math.Max(stopPrice, entryPrice);
                else
                    stopPrice = Math.Min(stopPrice, entryPrice);
            }

            // ── Priority 5: Trailing stop ──
            if (breakevenActivated)
            {
                double trailDist = _cfg.TrailR * riskPerShare;
                if (side == TradeSide.Long)
                {
                    double newTrail = peakPrice - trailDist;
                    trailingStop = Math.Max(trailingStop, newTrail);
                    if (low <= trailingStop)
                    {
                        exitPrice = trailingStop;
                        exitReason = ExitReason.Trailing;
                        exitBar = j;
                        exited = true;
                        break;
                    }
                }
                else
                {
                    double newTrail = troughPrice + trailDist;
                    trailingStop = Math.Min(trailingStop, newTrail);
                    if (high >= trailingStop)
                    {
                        exitPrice = trailingStop;
                        exitReason = ExitReason.Trailing;
                        exitBar = j;
                        exited = true;
                        break;
                    }
                }
            }

            // ── Priority 6: Giveback from peak ──
            if (_cfg.UseNotionalGivebackCap)
            {
                double positionNotional = entryPrice * posSize;
                double givebackLimitUsd = Math.Min(
                    _cfg.GivebackPctOfNotional * positionNotional,
                    _cfg.GivebackUsdCap);

                if (givebackLimitUsd > 0)
                {
                    double peakPnlUsd = side == TradeSide.Long
                        ? (peakPrice - entryPrice) * posSize
                        : (entryPrice - troughPrice) * posSize;
                    double currentPnlUsd = side == TradeSide.Long
                        ? (price - entryPrice) * posSize
                        : (entryPrice - price) * posSize;
                    double givebackUsd = peakPnlUsd - currentPnlUsd;

                    if (currentPnlUsd > 0 && givebackUsd >= givebackLimitUsd)
                    {
                        exitPrice = price;
                        exitReason = ExitReason.Giveback;
                        exitBar = j;
                        exited = true;
                        break;
                    }
                }
            }
            else if (peakR > 0)
            {
                double giveback = (peakR - unrealizedR) / peakR;
                if (giveback >= _cfg.GivebackPct && unrealizedR > 0)
                {
                    exitPrice = price;
                    exitReason = ExitReason.Giveback;
                    exitBar = j;
                    exited = true;
                    break;
                }
            }

            // ── Priority 7: Time stop ──
            int barsHeld = j - signal.BarIndex;
            if (barsHeld >= _cfg.MaxHoldBars)
            {
                exitPrice = price;
                exitReason = ExitReason.TimeStop;
                exitBar = j;
                exited = true;
                break;
            }
        }

        // Exhausted bars without exit → force close
        if (!exited)
        {
            int forceBar = Math.Min(signal.BarIndex + _cfg.MaxHoldBars, triggerBars.Length - 1);
            exitPrice = triggerBars[forceBar].Bar.Close;
            exitReason = ExitReason.TimeStop;
            exitBar = forceBar;
        }

        // Apply slippage on exit
        if (side == TradeSide.Long)
            exitPrice -= _cfg.SlippageCents / 100.0;
        else
            exitPrice += _cfg.SlippageCents / 100.0;

        // PnL
        double pnlPerShare = side == TradeSide.Long
            ? exitPrice - entryPrice
            : entryPrice - exitPrice;
        double commission = _cfg.CommissionPerShare * posSize * 2; // round-trip
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
            ExitReason: exitReason ?? ExitReason.TimeStop,
            PeakR: finalPeakR,
            BarsHeld: exitBar - signal.BarIndex);
    }
}
