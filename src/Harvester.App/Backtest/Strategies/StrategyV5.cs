using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

/// <summary>V5 config — Smart Mean-Reversion with micro-trail.</summary>
public sealed class V5Config
{
    public double RiskPerTradeDollars { get; set; } = 50.0;
    public double AccountSize { get; set; } = 25_000.0;

    // 20MA filter
    public int MaPeriod { get; set; } = 20;
    public double MaxMaDistAtr { get; set; } = 0.5;
    public double ExhaustionDistAtr { get; set; } = 2.0;

    // V5a: Pullback
    public double PullbackRsiLow { get; set; } = 40.0;
    public double PullbackRsiHigh { get; set; } = 60.0;

    // V5b: VWAP Tag
    public double VwapTouchAtr { get; set; } = 0.3;

    // V5c: Exhaustion Fade
    public bool ExhaustionFadeEnabled { get; set; } = true;

    // Candle confirmation
    public bool RequireCandleConfirm { get; set; } = true;

    // Volume
    public double RvolMin { get; set; } = 0.8;

    // Exit rules
    public double HardStopR { get; set; } = 1.0;
    public double BreakevenR { get; set; } = 0.5;
    public double TrailR { get; set; } = 0.5;
    public double GivebackPct { get; set; } = 0.50;
    public double Tp1R { get; set; } = 1.5;
    public double Tp2R { get; set; } = 3.0;
    public int MaxHoldBars { get; set; } = 60;

    // Micro-trail
    public double MicroTrailCents { get; set; } = 3.0;
    public double MicroTrailActivateCents { get; set; } = 5.0;

    // Reversal flatten
    public bool ReversalFlatten { get; set; } = true;

    // Costs
    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;

    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;
}

/// <summary>
/// V5 "Smart Mean-Reversion" — 20MA filter + micro-trail.
/// Three sub-strategies: Pullback to 20MA, VWAP Tag, Exhaustion Fade.
/// </summary>
public sealed class StrategyV5 : IBacktestStrategy
{
    private readonly V5Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public StrategyV5(V5Config? cfg = null)
    {
        _cfg = cfg ?? new V5Config();
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.3,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
            Tp1TightenToBe = false,
            ReversalFlatten = _cfg.ReversalFlatten,
            MicroTrail = true,
            MicroTrailCents = _cfg.MicroTrailCents,
            MicroTrailActivateCents = _cfg.MicroTrailActivateCents,
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

        for (int i = 50; i < triggerBars.Length; i++)
        {
            var row = triggerBars[i];
            var prev = triggerBars[i - 1];
            double atrVal = row.Atr14;
            if (double.IsNaN(atrVal) || atrVal <= 0) continue;

            double price = row.Bar.Close;

            // 20MA distance (mandatory)
            if (double.IsNaN(row.Sma20)) continue;
            double maDist = (price - row.Sma20) / atrVal;

            // Volume filter
            if (!double.IsNaN(row.Rvol) && row.Rvol < _cfg.RvolMin) continue;

            // Candle confirmation
            bool bullCandle = false, bearCandle = false;
            if (_cfg.RequireCandleConfirm)
            {
                double body = Math.Abs(row.Bar.Close - row.Bar.Open);
                double range = row.Bar.High - row.Bar.Low;
                if (range > 0)
                {
                    bullCandle = row.Bar.Close > row.Bar.Open && body / range >= 0.40;
                    bearCandle = row.Bar.Close < row.Bar.Open && body / range >= 0.40;
                }
            }
            else
            {
                bullCandle = row.Bar.Close > row.Bar.Open;
                bearCandle = row.Bar.Close < row.Bar.Open;
            }

            BacktestSignal? sig = null;

            // V5a: Pullback to 20MA
            if (sig == null && _cfg.AllowLong && htfBias != "BEAR" && maDist <= _cfg.MaxMaDistAtr)
            {
                if (!double.IsNaN(row.Rsi14) && row.Rsi14 < _cfg.PullbackRsiLow && bullCandle)
                    sig = MakeSignal(i, triggerBars, TradeSide.Long, atrVal, "PULLBACK", maDist);
            }
            if (sig == null && _cfg.AllowShort && htfBias != "BULL" && maDist >= -_cfg.MaxMaDistAtr)
            {
                if (!double.IsNaN(row.Rsi14) && row.Rsi14 > _cfg.PullbackRsiHigh && bearCandle)
                    sig = MakeSignal(i, triggerBars, TradeSide.Short, atrVal, "PULLBACK", maDist);
            }

            // V5b: VWAP Tag
            if (sig == null && !double.IsNaN(row.Vwap) && row.Vwap > 0)
            {
                double vwapDist = Math.Abs(price - row.Vwap) / atrVal;
                if (vwapDist <= _cfg.VwapTouchAtr)
                {
                    if (_cfg.AllowLong && htfBias != "BEAR" && Math.Abs(maDist) <= _cfg.MaxMaDistAtr)
                    {
                        if (prev.Bar.Close < prev.Vwap && price >= row.Vwap && bullCandle)
                            sig = MakeSignal(i, triggerBars, TradeSide.Long, atrVal, "VWAP_TAG", maDist);
                    }
                    if (sig == null && _cfg.AllowShort && htfBias != "BULL" && Math.Abs(maDist) <= _cfg.MaxMaDistAtr)
                    {
                        if (prev.Bar.Close > prev.Vwap && price <= row.Vwap && bearCandle)
                            sig = MakeSignal(i, triggerBars, TradeSide.Short, atrVal, "VWAP_TAG", maDist);
                    }
                }
            }

            // V5c: Exhaustion Fade
            if (sig == null && _cfg.ExhaustionFadeEnabled)
            {
                if (_cfg.AllowShort && maDist > _cfg.ExhaustionDistAtr && bearCandle)
                {
                    if (!double.IsNaN(row.Rsi14) && row.Rsi14 > 65)
                        sig = MakeSignal(i, triggerBars, TradeSide.Short, atrVal, "EXHAUSTION_FADE", maDist);
                }
                if (sig == null && _cfg.AllowLong && maDist < -_cfg.ExhaustionDistAtr && bullCandle)
                {
                    if (!double.IsNaN(row.Rsi14) && row.Rsi14 < 35)
                        sig = MakeSignal(i, triggerBars, TradeSide.Long, atrVal, "EXHAUSTION_FADE", maDist);
                }
            }

            if (sig != null) signals.Add(sig);
        }

        return signals;
    }

    private BacktestSignal? MakeSignal(int i, EnrichedBar[] bars, TradeSide side, double atrVal, string subStrategy, double maDist)
    {
        double price = bars[i].Bar.Close;
        double stopDist = _cfg.HardStopR * atrVal;
        double stopPrice = side == TradeSide.Long ? price - stopDist : price + stopDist;
        double riskPerShare = stopDist;
        if (riskPerShare <= 0) return null;
        int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));

        return new BacktestSignal(i, bars[i].Bar.Timestamp, side,
            price, stopPrice, riskPerShare, posSize, atrVal,
            HtfBias.Neutral, "N/A", subStrategy);
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
            var prev = bars[^2];
            int slope = last.Ema21 > prev.Ema21 ? 1 : -1;
            slope += last.MacdHist > 0 ? 1 : -1;
            scores.Add(slope);
        }
        if (scores.Count == 0) return "NEUTRAL";
        double avg = scores.Average();
        if (avg >= 1.5) return "BULL";
        if (avg <= -1.5) return "BEAR";
        return "NEUTRAL";
    }
}
