using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

/// <summary>V4 configuration — Image Pattern strategy parameters.</summary>
public sealed class V4Config
{
    public double RiskPerTradeDollars { get; set; } = 50.0;
    public double AccountSize { get; set; } = 25_000.0;

    // Pattern enables
    public bool EnableBuySetup { get; set; } = true;
    public bool EnableSellSetup { get; set; } = true;
    public bool Enable123Pattern { get; set; } = true;
    public bool EnableBreakout { get; set; } = true;
    public bool EnableBreakdown { get; set; } = true;
    public bool EnableExhaustion { get; set; } = true;

    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;

    // Buy/Sell Setup
    public int SetupLookback { get; set; } = 30;
    public double RetracementMin { get; set; } = 0.30;
    public double RetracementMax { get; set; } = 0.70;
    public int PullbackBarsMin { get; set; } = 3;
    public int SmaPeriod { get; set; } = 20;
    public bool RequireVolumeSpike { get; set; } = true;
    public double VolumeSpikeMultiplier { get; set; } = 1.5;
    public double MinRrRatio { get; set; } = 1.5;

    // 123 Pattern
    public int P123Lookback { get; set; } = 30;
    public double P123HigherLowPct { get; set; } = 0.02;

    // Breakout/Breakdown
    public int BreakoutLookback { get; set; } = 20;
    public double BreakoutVolumeMultiplier { get; set; } = 1.2;
    public double BreakoutAtrBuffer { get; set; } = 0.3;

    // Exhaustion
    public int ExhaustionLookback { get; set; } = 15;
    public double ExhaustionMinMoveAtr { get; set; } = 3.0;
    public int ExhaustionReversalBars { get; set; } = 3;

    // L2 Proxy
    public double L2LiquidityMin { get; set; } = 20.0;
    public double SpreadZMax { get; set; } = 2.5;
    public double RvolMin { get; set; } = 0.5;
    public bool OfiConfirm { get; set; } = true;

    // Enhanced score
    public int EnhancedMinScore { get; set; } = 3;

    // Exit rules
    public double HardStopR { get; set; } = 1.5;
    public double BreakevenR { get; set; } = 1.0;
    public double TrailR { get; set; } = 1.0;
    public double GivebackPct { get; set; } = 0.60;
    public double Tp1R { get; set; } = 1.5;
    public double Tp2R { get; set; } = 3.0;
    public int MaxHoldBars { get; set; } = 120;
    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;
}

/// <summary>
/// V4 "Image Pattern" Strategy: Buy-Setup / Sell-Setup / 123 / Breakout / Breakdown / Exhaustion.
/// Six chart-pattern families with L2 proxy filters and enhanced scoring.
/// </summary>
public sealed class StrategyV4 : IBacktestStrategy
{
    private readonly V4Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public StrategyV4(V4Config? cfg = null)
    {
        _cfg = cfg ?? new V4Config();
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.0,
            UseFixedGivebackUsdCap = true,
            GivebackUsdCap = 30.0,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
            Tp1TightenToBe = true,
        };
    }

    public IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var signals = new List<BacktestSignal>();
        string htfBias = ComputeHtfBias(bars1h, bars1d);

        int lookback = Math.Max(Math.Max(_cfg.SetupLookback, _cfg.P123Lookback),
            Math.Max(_cfg.BreakoutLookback, _cfg.ExhaustionLookback));
        int startBar = Math.Max(50, lookback + 5);

        for (int i = startBar; i < triggerBars.Length; i++)
        {
            var row = triggerBars[i];
            double atrVal = row.Atr14;
            if (double.IsNaN(atrVal) || atrVal <= 0) continue;

            // L2 proxy gate
            double l2Liq = double.IsNaN(row.L2Liquidity) ? 50.0 : row.L2Liquidity;
            if (l2Liq < _cfg.L2LiquidityMin) continue;

            double spreadZ = double.IsNaN(row.SpreadZ) ? 0.0 : row.SpreadZ;
            if (spreadZ > _cfg.SpreadZMax) continue;

            if (!double.IsNaN(row.Rvol) && row.Rvol < _cfg.RvolMin) continue;

            double ofiSig = double.IsNaN(row.OfiSignal) ? 0.0 : row.OfiSignal;

            // Priority: BuySetup → SellSetup → 123 → Breakout → Breakdown → Exhaustion
            BacktestSignal? sig = null;

            if (sig == null && _cfg.EnableBuySetup && _cfg.AllowLong && htfBias != "STRONG_BEAR")
                sig = CheckBuySetup(i, triggerBars, atrVal, l2Liq, ofiSig);

            if (sig == null && _cfg.EnableSellSetup && _cfg.AllowShort && htfBias != "STRONG_BULL")
                sig = CheckSellSetup(i, triggerBars, atrVal, l2Liq, ofiSig);

            if (sig == null && _cfg.Enable123Pattern)
                sig = Check123Pattern(i, triggerBars, atrVal, l2Liq, ofiSig, htfBias);

            if (sig == null && _cfg.EnableBreakout && _cfg.AllowLong && htfBias != "STRONG_BEAR")
                sig = CheckBreakout(i, triggerBars, atrVal, l2Liq, ofiSig);

            if (sig == null && _cfg.EnableBreakdown && _cfg.AllowShort && htfBias != "STRONG_BULL")
                sig = CheckBreakdown(i, triggerBars, atrVal, l2Liq, ofiSig);

            if (sig == null && _cfg.EnableExhaustion)
                sig = CheckExhaustion(i, triggerBars, atrVal, l2Liq, ofiSig, htfBias);

            if (sig != null) signals.Add(sig);
        }

        return signals;
    }

    public BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);

    // ── Buy Setup ────────────────────────────────────────────────────────

    private BacktestSignal? CheckBuySetup(int i, EnrichedBar[] bars, double atrVal, double l2Liq, double ofiSig)
    {
        int lb = _cfg.SetupLookback;
        if (i < lb + 1) return null;

        var row = bars[i];
        var prev = bars[i - 1];

        // Find peak high in lookback
        int peakPos = i - lb;
        double peakHigh = bars[peakPos].Bar.High;
        for (int k = i - lb; k <= i; k++)
        {
            if (bars[k].Bar.High > peakHigh) { peakHigh = bars[k].Bar.High; peakPos = k; }
        }
        int winPeakPos = peakPos - (i - lb);  // relative position in window

        if (winPeakPos < 5 || winPeakPos > lb - 3) return null;

        // Rally low before peak
        double rallyLow = double.MaxValue;
        for (int k = i - lb; k < peakPos; k++)
            rallyLow = Math.Min(rallyLow, bars[k].Bar.Low);
        double rallyRange = peakHigh - rallyLow;
        if (rallyRange <= 0) return null;

        // Pullback low after peak
        double pullbackLow = double.MaxValue;
        for (int k = peakPos; k <= i; k++)
            pullbackLow = Math.Min(pullbackLow, bars[k].Bar.Low);

        double retracement = (peakHigh - pullbackLow) / rallyRange;
        if (retracement < _cfg.RetracementMin || retracement > _cfg.RetracementMax)
            return null;

        // Pullback structure: count red bars and lower highs after peak
        int pbLen = i - peakPos;
        if (pbLen < _cfg.PullbackBarsMin) return null;

        int redCount = 0, lowerHighs = 0;
        for (int k = peakPos + 1; k <= i; k++)
        {
            if (bars[k].Bar.Close < bars[k].Bar.Open) redCount++;
            if (k > peakPos + 1 && bars[k].Bar.High < bars[k - 1].Bar.High) lowerHighs++;
        }
        if (redCount < _cfg.PullbackBarsMin && lowerHighs < _cfg.PullbackBarsMin - 1)
            return null;

        // SMA(20) rising & price above
        if (double.IsNaN(row.Sma20) || double.IsNaN(prev.Sma20)) return null;
        if (row.Sma20 <= prev.Sma20) return null;
        if (row.Bar.Close < row.Sma20) return null;

        // Current bar: green + closes above prior high
        if (row.Bar.Close <= row.Bar.Open) return null;
        if (row.Bar.Close <= prev.Bar.High) return null;

        // Enhanced scoring (7 criteria)
        int score = 0;

        // 1) Narrow range or bottoming tail
        double barRange = row.Bar.High - row.Bar.Low;
        if (barRange <= atrVal * 0.85) score++;
        double lowerWick = Math.Min(row.Bar.Open, row.Bar.Close) - row.Bar.Low;
        if (barRange > 0 && lowerWick / barRange >= 0.35) score++;

        // 2) Contracting pullback ranges
        if (pbLen >= 3)
        {
            bool contracting = true;
            for (int k = peakPos + 2; k <= Math.Min(peakPos + 4, i); k++)
            {
                double r1 = bars[k].Bar.High - bars[k].Bar.Low;
                double r0 = bars[k - 1].Bar.High - bars[k - 1].Bar.Low;
                if (r1 > r0) { contracting = false; break; }
            }
            if (contracting) score++;
        }

        // 3) R:R ratio
        double stopPrice = pullbackLow - 0.02 * rallyRange;
        double risk = row.Bar.Close - stopPrice;
        double reward = peakHigh - row.Bar.Close;
        if (risk > 0 && reward / risk >= _cfg.MinRrRatio) score++;

        // 4) Volume spike
        double volAvg = AvgVolume(bars, i, 8);
        if (volAvg > 0 && row.Bar.Volume >= volAvg * _cfg.VolumeSpikeMultiplier) score++;

        // 5) OFI positive
        if (_cfg.OfiConfirm && ofiSig > 0) score++;

        // 6) RSI in range
        if (!double.IsNaN(row.Rsi14) && row.Rsi14 >= 30 && row.Rsi14 <= 65) score++;

        if (score < _cfg.EnhancedMinScore) return null;

        double entryPrice = row.Bar.Close;
        double riskPerShare = Math.Abs(entryPrice - stopPrice);
        if (riskPerShare <= 0) return null;
        int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));

        return new BacktestSignal(i, row.Bar.Timestamp, TradeSide.Long,
            entryPrice, stopPrice, riskPerShare, posSize, atrVal,
            HtfBias.Neutral, "N/A", "BUY_SETUP");
    }

    // ── Sell Setup (mirror) ──────────────────────────────────────────────

    private BacktestSignal? CheckSellSetup(int i, EnrichedBar[] bars, double atrVal, double l2Liq, double ofiSig)
    {
        int lb = _cfg.SetupLookback;
        if (i < lb + 1) return null;

        var row = bars[i];
        var prev = bars[i - 1];

        // Find trough low
        int troughPos = i - lb;
        double troughLow = bars[troughPos].Bar.Low;
        for (int k = i - lb; k <= i; k++)
        {
            if (bars[k].Bar.Low < troughLow) { troughLow = bars[k].Bar.Low; troughPos = k; }
        }
        int winTroughPos = troughPos - (i - lb);

        if (winTroughPos < 5 || winTroughPos > lb - 3) return null;

        double dropHigh = double.MinValue;
        for (int k = i - lb; k < troughPos; k++)
            dropHigh = Math.Max(dropHigh, bars[k].Bar.High);
        double dropRange = dropHigh - troughLow;
        if (dropRange <= 0) return null;

        double pullupHigh = double.MinValue;
        for (int k = troughPos; k <= i; k++)
            pullupHigh = Math.Max(pullupHigh, bars[k].Bar.High);

        double retracement = (pullupHigh - troughLow) / dropRange;
        if (retracement < _cfg.RetracementMin || retracement > _cfg.RetracementMax) return null;

        int puLen = i - troughPos;
        if (puLen < _cfg.PullbackBarsMin) return null;

        int greenCount = 0, higherLows = 0;
        for (int k = troughPos + 1; k <= i; k++)
        {
            if (bars[k].Bar.Close > bars[k].Bar.Open) greenCount++;
            if (k > troughPos + 1 && bars[k].Bar.Low > bars[k - 1].Bar.Low) higherLows++;
        }
        if (greenCount < _cfg.PullbackBarsMin && higherLows < _cfg.PullbackBarsMin - 1) return null;

        if (double.IsNaN(row.Sma20) || double.IsNaN(prev.Sma20)) return null;
        if (row.Sma20 >= prev.Sma20) return null;  // SMA must be falling
        if (row.Bar.Close > row.Sma20) return null;  // Price below SMA

        if (row.Bar.Close >= row.Bar.Open) return null;  // Must be red
        if (row.Bar.Close >= prev.Bar.Low) return null;   // Below prior low

        // Scoring
        int score = 0;
        double barRange = row.Bar.High - row.Bar.Low;
        double upperWick = row.Bar.High - Math.Max(row.Bar.Open, row.Bar.Close);
        if (barRange > 0 && upperWick / barRange >= 0.35) score++;

        if (puLen >= 3)
        {
            bool contracting = true;
            for (int k = troughPos + 2; k <= Math.Min(troughPos + 4, i); k++)
            {
                double r1 = bars[k].Bar.High - bars[k].Bar.Low;
                double r0 = bars[k - 1].Bar.High - bars[k - 1].Bar.Low;
                if (r1 > r0) { contracting = false; break; }
            }
            if (contracting) score++;
        }

        double stopPrice = pullupHigh + 0.02 * dropRange;
        double risk = stopPrice - row.Bar.Close;
        double reward = row.Bar.Close - troughLow;
        if (risk > 0 && reward / risk >= _cfg.MinRrRatio) score++;

        double volAvg = AvgVolume(bars, i, 8);
        if (volAvg > 0 && row.Bar.Volume >= volAvg * _cfg.VolumeSpikeMultiplier) score++;

        if (_cfg.OfiConfirm && ofiSig < 0) score++;
        if (!double.IsNaN(row.Rsi14) && row.Rsi14 >= 35 && row.Rsi14 <= 70) score++;

        if (score < _cfg.EnhancedMinScore) return null;

        double entryPrice = row.Bar.Close;
        double riskPerShare = Math.Abs(stopPrice - entryPrice);
        if (riskPerShare <= 0) return null;
        int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));

        return new BacktestSignal(i, row.Bar.Timestamp, TradeSide.Short,
            entryPrice, stopPrice, riskPerShare, posSize, atrVal,
            HtfBias.Neutral, "N/A", "SELL_SETUP");
    }

    // ── 123 Pattern ──────────────────────────────────────────────────────

    private BacktestSignal? Check123Pattern(int i, EnrichedBar[] bars, double atrVal, double l2Liq, double ofiSig, string htfBias)
    {
        int lb = _cfg.P123Lookback;
        if (i < lb + 1) return null;

        var row = bars[i];
        var prev = bars[i - 1];

        // LONG 123
        if (_cfg.AllowLong && htfBias != "STRONG_BEAR")
        {
            // Point 1: swing low in first half
            int halfEnd = i - lb / 2;
            double p1Low = double.MaxValue;
            int p1Idx = i - lb;
            for (int k = i - lb; k < halfEnd; k++)
            {
                if (bars[k].Bar.Low < p1Low) { p1Low = bars[k].Bar.Low; p1Idx = k; }
            }

            // Point 2: peak high after P1
            double p2High = double.MinValue;
            int p2Idx = p1Idx;
            for (int k = p1Idx; k <= i; k++)
            {
                if (bars[k].Bar.High > p2High) { p2High = bars[k].Bar.High; p2Idx = k; }
            }

            if (p2Idx > p1Idx + 2)
            {
                // Point 3: pullback low after P2, higher than P1
                double p3Low = double.MaxValue;
                for (int k = p2Idx; k <= i; k++)
                    p3Low = Math.Min(p3Low, bars[k].Bar.Low);

                if (p3Low > p1Low * (1 + _cfg.P123HigherLowPct))
                {
                    if (row.Bar.Close > prev.Bar.High && row.Bar.Close > row.Bar.Open)
                    {
                        if (!double.IsNaN(row.Sma20) && row.Bar.Close > row.Sma20)
                        {
                            double stopPrice = p3Low - 0.5 * atrVal;
                            double entryPrice = row.Bar.Close;
                            double riskPerShare = Math.Abs(entryPrice - stopPrice);
                            if (riskPerShare > 0)
                            {
                                double rr = (p2High - entryPrice) / riskPerShare;
                                if (rr >= _cfg.MinRrRatio)
                                {
                                    int score = Score123(row, prev, bars, i, ofiSig, atrVal, TradeSide.Long);
                                    if (score >= _cfg.EnhancedMinScore)
                                    {
                                        int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));
                                        return new BacktestSignal(i, row.Bar.Timestamp, TradeSide.Long,
                                            entryPrice, stopPrice, riskPerShare, posSize, atrVal,
                                            HtfBias.Neutral, "N/A", "123_LONG");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // SHORT 123 (mirror)
        if (_cfg.AllowShort && htfBias != "STRONG_BULL")
        {
            int halfEnd = i - lb / 2;
            double p1High = double.MinValue;
            int p1Idx = i - lb;
            for (int k = i - lb; k < halfEnd; k++)
            {
                if (bars[k].Bar.High > p1High) { p1High = bars[k].Bar.High; p1Idx = k; }
            }

            double p2Low = double.MaxValue;
            int p2Idx = p1Idx;
            for (int k = p1Idx; k <= i; k++)
            {
                if (bars[k].Bar.Low < p2Low) { p2Low = bars[k].Bar.Low; p2Idx = k; }
            }

            if (p2Idx > p1Idx + 2)
            {
                double p3High = double.MinValue;
                for (int k = p2Idx; k <= i; k++)
                    p3High = Math.Max(p3High, bars[k].Bar.High);

                if (p3High < p1High * (1 - _cfg.P123HigherLowPct))
                {
                    if (row.Bar.Close < prev.Bar.Low && row.Bar.Close < row.Bar.Open)
                    {
                        if (!double.IsNaN(row.Sma20) && row.Bar.Close < row.Sma20)
                        {
                            double stopPrice = p3High + 0.5 * atrVal;
                            double entryPrice = row.Bar.Close;
                            double riskPerShare = Math.Abs(stopPrice - entryPrice);
                            if (riskPerShare > 0)
                            {
                                double rr = (entryPrice - p2Low) / riskPerShare;
                                if (rr >= _cfg.MinRrRatio)
                                {
                                    int score = Score123(row, prev, bars, i, ofiSig, atrVal, TradeSide.Short);
                                    if (score >= _cfg.EnhancedMinScore)
                                    {
                                        int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));
                                        return new BacktestSignal(i, row.Bar.Timestamp, TradeSide.Short,
                                            entryPrice, stopPrice, riskPerShare, posSize, atrVal,
                                            HtfBias.Neutral, "N/A", "123_SHORT");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    private int Score123(EnrichedBar row, EnrichedBar prev, EnrichedBar[] bars, int i, double ofiSig, double atrVal, TradeSide side)
    {
        int score = 0;
        bool isGreen = row.Bar.Close > row.Bar.Open;
        if ((side == TradeSide.Long && isGreen) || (side == TradeSide.Short && !isGreen)) score++;

        double volAvg = AvgVolume(bars, i, 8);
        if (volAvg > 0 && row.Bar.Volume >= volAvg * 1.2) score++;

        if ((side == TradeSide.Long && ofiSig > 0) || (side == TradeSide.Short && ofiSig < 0)) score++;

        if (!double.IsNaN(row.Rsi14))
        {
            if (side == TradeSide.Long && row.Rsi14 >= 30 && row.Rsi14 <= 65) score++;
            else if (side == TradeSide.Short && row.Rsi14 >= 35 && row.Rsi14 <= 70) score++;
        }

        if (!double.IsNaN(row.StochK))
        {
            if (side == TradeSide.Long && row.StochK < 70) score++;
            else if (side == TradeSide.Short && row.StochK > 30) score++;
        }

        double br = row.Bar.High - row.Bar.Low;
        if (br >= 0.3 * atrVal && br <= 1.5 * atrVal) score++;

        if (!double.IsNaN(row.Ema9) && Math.Abs(row.Bar.Close - row.Ema9) < 1.5 * atrVal) score++;

        return score;
    }

    // ── Breakout ─────────────────────────────────────────────────────────

    private BacktestSignal? CheckBreakout(int i, EnrichedBar[] bars, double atrVal, double l2Liq, double ofiSig)
    {
        int lb = _cfg.BreakoutLookback;
        if (i < lb + 1) return null;

        var row = bars[i];
        double lookbackHigh = double.MinValue;
        for (int k = i - lb; k < i; k++)
            lookbackHigh = Math.Max(lookbackHigh, bars[k].Bar.High);

        double threshold = lookbackHigh + _cfg.BreakoutAtrBuffer * atrVal;
        if (row.Bar.Close <= threshold) return null;

        double volAvg = AvgVolume(bars, i, 8);
        if (volAvg > 0 && row.Bar.Volume < volAvg * _cfg.BreakoutVolumeMultiplier) return null;

        int score = 0;
        if (row.Bar.Close > row.Bar.Open) score++;
        if (ofiSig > 0) score++;
        if (!double.IsNaN(row.Rsi14) && row.Rsi14 < 80) score++;
        if (!double.IsNaN(row.StochK) && row.StochK > 50) score++;
        if (!double.IsNaN(row.Adx) && row.Adx > 20) score++;
        if (i >= 3 && bars[i].Bar.Volume >= bars[i - 1].Bar.Volume && bars[i - 1].Bar.Volume >= bars[i - 2].Bar.Volume)
            score++;

        if (score < _cfg.EnhancedMinScore) return null;

        double stopPrice = lookbackHigh - 0.5 * atrVal;
        double entryPrice = row.Bar.Close;
        double riskPerShare = Math.Abs(entryPrice - stopPrice);
        if (riskPerShare <= 0) return null;
        int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));

        return new BacktestSignal(i, row.Bar.Timestamp, TradeSide.Long,
            entryPrice, stopPrice, riskPerShare, posSize, atrVal,
            HtfBias.Neutral, "N/A", "BREAKOUT");
    }

    // ── Breakdown (mirror) ───────────────────────────────────────────────

    private BacktestSignal? CheckBreakdown(int i, EnrichedBar[] bars, double atrVal, double l2Liq, double ofiSig)
    {
        int lb = _cfg.BreakoutLookback;
        if (i < lb + 1) return null;

        var row = bars[i];
        double lookbackLow = double.MaxValue;
        for (int k = i - lb; k < i; k++)
            lookbackLow = Math.Min(lookbackLow, bars[k].Bar.Low);

        double threshold = lookbackLow - _cfg.BreakoutAtrBuffer * atrVal;
        if (row.Bar.Close >= threshold) return null;

        double volAvg = AvgVolume(bars, i, 8);
        if (volAvg > 0 && row.Bar.Volume < volAvg * _cfg.BreakoutVolumeMultiplier) return null;

        int score = 0;
        if (row.Bar.Close < row.Bar.Open) score++;
        if (ofiSig < 0) score++;
        if (!double.IsNaN(row.Rsi14) && row.Rsi14 > 20) score++;
        if (!double.IsNaN(row.StochK) && row.StochK < 50) score++;
        if (!double.IsNaN(row.Adx) && row.Adx > 20) score++;
        if (i >= 3 && bars[i].Bar.Volume >= bars[i - 1].Bar.Volume && bars[i - 1].Bar.Volume >= bars[i - 2].Bar.Volume)
            score++;

        if (score < _cfg.EnhancedMinScore) return null;

        double stopPrice = lookbackLow + 0.5 * atrVal;
        double entryPrice = row.Bar.Close;
        double riskPerShare = Math.Abs(stopPrice - entryPrice);
        if (riskPerShare <= 0) return null;
        int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));

        return new BacktestSignal(i, row.Bar.Timestamp, TradeSide.Short,
            entryPrice, stopPrice, riskPerShare, posSize, atrVal,
            HtfBias.Neutral, "N/A", "BREAKDOWN");
    }

    // ── Exhaustion ───────────────────────────────────────────────────────

    private BacktestSignal? CheckExhaustion(int i, EnrichedBar[] bars, double atrVal, double l2Liq, double ofiSig, string htfBias)
    {
        int lb = _cfg.ExhaustionLookback;
        int rb = _cfg.ExhaustionReversalBars;
        if (i < lb + rb + 1) return null;

        var row = bars[i];

        // Extended move UP → SHORT
        if (_cfg.AllowShort && htfBias != "STRONG_BULL")
        {
            double moveUp = bars[i - rb].Bar.Close - bars[i - lb - rb].Bar.Close;
            if (moveUp > _cfg.ExhaustionMinMoveAtr * atrVal)
            {
                bool allBearish = true;
                for (int k = i - rb; k <= i; k++)
                {
                    if (bars[k].Bar.Close >= bars[k].Bar.Open) { allBearish = false; break; }
                }
                if (allBearish)
                {
                    int exhScore = 0;
                    if (!double.IsNaN(row.StochK) && row.StochK > 70) exhScore++;
                    if (!double.IsNaN(row.Rsi14) && row.Rsi14 > 60) exhScore++;
                    if (!double.IsNaN(row.WillR14) && row.WillR14 > -30) exhScore++;
                    if (ofiSig < 0) exhScore++;
                    double volAvg = AvgVolume(bars, i, 8);
                    if (volAvg > 0 && row.Bar.Volume < volAvg * 0.8) exhScore++;
                    if (!double.IsNaN(row.BbPctB) && row.BbPctB > 0.85) exhScore++;

                    if (exhScore >= _cfg.EnhancedMinScore)
                    {
                        double highOfRun = double.MinValue;
                        for (int k = i - lb - rb; k <= i - rb; k++)
                            highOfRun = Math.Max(highOfRun, bars[k].Bar.High);

                        double stopPrice = highOfRun + 0.3 * atrVal;
                        double entryPrice = row.Bar.Close;
                        double riskPerShare = Math.Abs(stopPrice - entryPrice);
                        if (riskPerShare > 0)
                        {
                            int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));
                            return new BacktestSignal(i, row.Bar.Timestamp, TradeSide.Short,
                                entryPrice, stopPrice, riskPerShare, posSize, atrVal,
                                HtfBias.Neutral, "N/A", "EXHAUSTION_SHORT");
                        }
                    }
                }
            }
        }

        // Extended move DOWN → LONG
        if (_cfg.AllowLong && htfBias != "STRONG_BEAR")
        {
            double moveDown = bars[i - lb - rb].Bar.Close - bars[i - rb].Bar.Close;
            if (moveDown > _cfg.ExhaustionMinMoveAtr * atrVal)
            {
                bool allBullish = true;
                for (int k = i - rb; k <= i; k++)
                {
                    if (bars[k].Bar.Close <= bars[k].Bar.Open) { allBullish = false; break; }
                }
                if (allBullish)
                {
                    int exhScore = 0;
                    if (!double.IsNaN(row.StochK) && row.StochK < 30) exhScore++;
                    if (!double.IsNaN(row.Rsi14) && row.Rsi14 < 40) exhScore++;
                    if (!double.IsNaN(row.WillR14) && row.WillR14 < -70) exhScore++;
                    if (ofiSig > 0) exhScore++;
                    double volAvg = AvgVolume(bars, i, 8);
                    if (volAvg > 0 && row.Bar.Volume < volAvg * 0.8) exhScore++;
                    if (!double.IsNaN(row.BbPctB) && row.BbPctB < 0.15) exhScore++;

                    if (exhScore >= _cfg.EnhancedMinScore)
                    {
                        double lowOfRun = double.MaxValue;
                        for (int k = i - lb - rb; k <= i - rb; k++)
                            lowOfRun = Math.Min(lowOfRun, bars[k].Bar.Low);

                        double stopPrice = lowOfRun - 0.3 * atrVal;
                        double entryPrice = row.Bar.Close;
                        double riskPerShare = Math.Abs(entryPrice - stopPrice);
                        if (riskPerShare > 0)
                        {
                            int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));
                            return new BacktestSignal(i, row.Bar.Timestamp, TradeSide.Long,
                                entryPrice, stopPrice, riskPerShare, posSize, atrVal,
                                HtfBias.Neutral, "N/A", "EXHAUSTION_LONG");
                        }
                    }
                }
            }
        }

        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static double AvgVolume(EnrichedBar[] bars, int i, int lookback)
    {
        double sum = 0;
        int count = 0;
        int start = Math.Max(0, i - lookback);
        for (int k = start; k < i; k++)
        {
            sum += bars[k].Bar.Volume;
            count++;
        }
        return count > 0 ? sum / count : 0;
    }

    private static string ComputeHtfBias(EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        var scores = new List<int>();
        foreach (var bars in new[] { bars1h, bars1d })
        {
            if (bars == null || bars.Length < 50) continue;
            var last = bars[^1];
            var prev = bars[^2];
            int s = 0;
            s += last.Ema21 > prev.Ema21 ? 1 : -1;
            if (!double.IsNaN(last.Adx) && last.Adx > 25)
                s += last.PlusDi > last.MinusDi ? 1 : -1;
            s += last.MacdHist > 0 ? 1 : -1;
            scores.Add(s);
        }
        if (scores.Count == 0) return "NEUTRAL";
        double avg = scores.Average();
        if (avg >= 2.0) return "STRONG_BULL";
        if (avg <= -2.0) return "STRONG_BEAR";
        return "NEUTRAL";
    }
}
