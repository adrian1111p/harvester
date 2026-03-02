using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

/// <summary>V7 config — 9 EMA Momentum Scalp ("Ride the 9").</summary>
public sealed class V7Config
{
    public double RiskPerTradeDollars { get; set; } = 50.0;
    public double AccountSize { get; set; } = 25_000.0;

    // EMA parameters
    public int EmaFast { get; set; } = 9;
    public int EmaSlow { get; set; } = 20;
    public double PullbackAtrProximity { get; set; } = 0.2;
    public int EmaSlopeBars { get; set; } = 5;
    public double EmaMinSlopeAtr { get; set; } = 0.02;

    // RSI filter
    public double RsiMaxLong { get; set; } = 75.0;
    public double RsiMinShort { get; set; } = 25.0;

    // Volume
    public bool RequireVolumeContraction { get; set; } = true;
    public bool RequireVolumeExpansion { get; set; } = true;

    // 20MA exhaustion
    public double MaxMaDistAtr { get; set; } = 0.5;

    // 9 EMA trail
    public bool UseEmaTrail { get; set; } = true;
    public double EmaTrailBufferAtr { get; set; } = 0.1;

    // Micro-trail
    public double MicroTrailCents { get; set; } = 3.0;
    public double MicroTrailActivateCents { get; set; } = 5.0;

    // Time filter
    public int SkipFirstNMinutes { get; set; } = 10;

    // Reversal flatten
    public bool ReversalFlatten { get; set; } = true;

    // Exit rules
    public double HardStopR { get; set; } = 1.0;
    public double BreakevenR { get; set; } = 0.5;
    public double TrailR { get; set; } = 0.5;
    public double GivebackPct { get; set; } = 0.50;
    public double Tp1R { get; set; } = 1.5;
    public double Tp2R { get; set; } = 3.0;
    public int MaxHoldBars { get; set; } = 45;

    // Costs
    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;
}

/// <summary>
/// V7 "9 EMA Momentum Scalp" — ride the 9 EMA with micro-pullback entries.
/// Uses 9/20 EMA alignment, volume contraction→expansion, and 9 EMA trailing stop.
/// </summary>
public sealed class StrategyV7 : IBacktestStrategy
{
    private readonly V7Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public StrategyV7(V7Config? cfg = null)
    {
        _cfg = cfg ?? new V7Config();
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.0,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = false,  // V7 doesn't deduct commission
            Tp1TightenToBe = false,
            ReversalFlatten = _cfg.ReversalFlatten,
            MicroTrail = true,
            MicroTrailCents = _cfg.MicroTrailCents,
            MicroTrailActivateCents = _cfg.MicroTrailActivateCents,
            EmaTrail = _cfg.UseEmaTrail,
            EmaTrailBufferAtr = _cfg.EmaTrailBufferAtr,
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
        string htfBias = HtfBiasHelper(bars1h, bars1d);

        int startBar = Math.Max(50, _cfg.EmaSlopeBars + 1);

        for (int i = startBar; i < triggerBars.Length; i++)
        {
            var row = triggerBars[i];
            double atrVal = row.Atr14;
            if (double.IsNaN(atrVal) || atrVal <= 0) continue;

            // Time filter: skip first N minutes after open (9:30 ET = minute 570)
            int minute = row.Bar.Timestamp.Hour * 60 + row.Bar.Timestamp.Minute;
            if (minute < 570 + _cfg.SkipFirstNMinutes) continue;

            double price = row.Bar.Close;
            double ema9 = row.Ema9;
            double ema20 = row.Sma20; // Using SMA_20 as the "slow" reference

            if (double.IsNaN(ema9) || double.IsNaN(ema20)) continue;

            // 20MA exhaustion filter
            double maDist = Math.Abs((price - ema20) / atrVal);
            if (maDist > _cfg.MaxMaDistAtr) continue;

            // EMA alignment and slope
            bool bullishAlignment = ema9 > ema20;
            bool bearishAlignment = ema9 < ema20;

            // EMA slope over N bars
            double ema9Slope = 0;
            if (i >= _cfg.EmaSlopeBars && !double.IsNaN(triggerBars[i - _cfg.EmaSlopeBars].Ema9))
            {
                ema9Slope = (ema9 - triggerBars[i - _cfg.EmaSlopeBars].Ema9)
                    / (_cfg.EmaSlopeBars * atrVal);
            }

            bool bullishSlope = ema9Slope > _cfg.EmaMinSlopeAtr;
            bool bearishSlope = ema9Slope < -_cfg.EmaMinSlopeAtr;

            // Pullback proximity: price within 0.2 ATR of 9 EMA
            double distToEma9 = Math.Abs(price - ema9) / atrVal;
            if (distToEma9 > _cfg.PullbackAtrProximity) continue;

            // Candle direction
            bool isGreen = row.Bar.Close > row.Bar.Open;
            bool isRed = row.Bar.Close < row.Bar.Open;

            // Volume expansion check
            bool volExpansion = true;
            if (_cfg.RequireVolumeExpansion && i > 0)
            {
                double prevVol = triggerBars[i - 1].Bar.Volume;
                if (prevVol > 0)
                    volExpansion = row.Bar.Volume >= prevVol * 0.8;
            }

            BacktestSignal? sig = null;

            // LONG entry
            if (bullishAlignment && bullishSlope && isGreen && volExpansion && htfBias != "BEAR")
            {
                if (!double.IsNaN(row.Rsi14) && row.Rsi14 > _cfg.RsiMaxLong) continue;  // Reject overbought

                // Stop below min of last 3 bars' lows, capped at 1 ATR
                double stopLow = double.MaxValue;
                for (int k = Math.Max(0, i - 2); k <= i; k++)
                    stopLow = Math.Min(stopLow, triggerBars[k].Bar.Low);
                double stopPrice = stopLow;
                double riskPerShare = price - stopPrice;
                if (riskPerShare > atrVal) riskPerShare = atrVal;
                stopPrice = price - riskPerShare;

                if (riskPerShare > 0)
                {
                    int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));
                    sig = new BacktestSignal(i, row.Bar.Timestamp, TradeSide.Long,
                        price, stopPrice, riskPerShare, posSize, atrVal,
                        HtfBias.Neutral, "N/A", "9EMA_PULLBACK_LONG");
                }
            }

            // SHORT entry
            if (sig == null && bearishAlignment && bearishSlope && isRed && volExpansion && htfBias != "BULL")
            {
                if (!double.IsNaN(row.Rsi14) && row.Rsi14 < _cfg.RsiMinShort) continue;  // Reject oversold

                double stopHigh = double.MinValue;
                for (int k = Math.Max(0, i - 2); k <= i; k++)
                    stopHigh = Math.Max(stopHigh, triggerBars[k].Bar.High);
                double stopPrice = stopHigh;
                double riskPerShare = stopPrice - price;
                if (riskPerShare > atrVal) riskPerShare = atrVal;
                stopPrice = price + riskPerShare;

                if (riskPerShare > 0)
                {
                    int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));
                    sig = new BacktestSignal(i, row.Bar.Timestamp, TradeSide.Short,
                        price, stopPrice, riskPerShare, posSize, atrVal,
                        HtfBias.Neutral, "N/A", "9EMA_PULLBACK_SHORT");
                }
            }

            if (sig != null) signals.Add(sig);
        }

        return signals;
    }

    public BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);

    private static string HtfBiasHelper(EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        var scores = new List<int>();
        foreach (var bars in new[] { bars1h, bars1d })
        {
            if (bars == null || bars.Length < 30) continue;
            var last = bars[^1];
            int vote = last.Bar.Close > last.Ema21 ? 1 : -1;
            scores.Add(vote);
        }
        if (scores.Count == 0) return "NEUTRAL";
        double avg = scores.Average();
        if (avg >= 0.5) return "BULL";
        if (avg <= -0.5) return "BEAR";
        return "NEUTRAL";
    }
}
