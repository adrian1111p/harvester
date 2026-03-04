using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

public sealed class V9Config
{
    public double RiskPerTradeDollars { get; set; } = 40.0;
    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;

    public double RvolMin { get; set; } = 0.5;
    public double L2LiquidityMin { get; set; } = 10.0;
    public double SpreadZMax { get; set; } = 3.0;
    public double MinVolAccel { get; set; } = -0.50;
    public double OfiSignalThreshold { get; set; } = 0.00;

    public double PullbackToEma9Atr { get; set; } = 0.30;
    public double MaxVwapDistAtr { get; set; } = 0.80;
    public bool UseTrendFilter { get; set; } = true;
    public bool RequirePullback { get; set; } = true;
    public int MinEntryScore { get; set; } = 6;
    public int SwingLookback { get; set; } = 4;
    public int CooldownBars { get; set; } = 2;

    public bool RequireHtfBias { get; set; } = true;
    public bool RequireMtfAlign { get; set; } = false;

    public int SkipFirstNMinutes { get; set; } = 5;
    public (int Start, int End)[] EntryWindows { get; set; } =
        [(575, 690), (780, 955)];

    public double RsiMinLong { get; set; } = 36.0;
    public double RsiMaxLong { get; set; } = 72.0;
    public double RsiMinShort { get; set; } = 28.0;
    public double RsiMaxShort { get; set; } = 64.0;

    public double HardStopR { get; set; } = 1.0;
    public double BreakevenR { get; set; } = 0.55;
    public double TrailR { get; set; } = 0.45;
    public double GivebackPct { get; set; } = 0.35;
    public bool UseFixedGivebackUsdCap { get; set; } = true;
    public double GivebackUsdCap { get; set; } = 30.0;
    public double Tp1R { get; set; } = 1.0;
    public double Tp2R { get; set; } = 2.1;
    public int MaxHoldBars { get; set; } = 50;

    public bool ReversalFlatten { get; set; } = true;
    public double MicroTrailCents { get; set; } = 2.5;
    public double MicroTrailActivateCents { get; set; } = 4.0;

    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;
}

public sealed class StrategyV9 : BacktestStrategyBase
{
    private readonly V9Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public StrategyV9(V9Config? cfg = null)
    {
        _cfg = cfg ?? new V9Config();
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.20,
            UseFixedGivebackUsdCap = _cfg.UseFixedGivebackUsdCap,
            GivebackUsdCap = _cfg.GivebackUsdCap,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = false,
            Tp1TightenToBe = true,
            ReversalFlatten = _cfg.ReversalFlatten,
            MicroTrail = true,
            MicroTrailCents = _cfg.MicroTrailCents,
            MicroTrailActivateCents = _cfg.MicroTrailActivateCents,
        };
    }

    public override IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        var signals = new List<BacktestSignal>();
        if (triggerBars.Length < 80) return signals;

        string htfBias = ComputeHtfBias(bars1h, bars1d);
        int lastSignalBar = -10_000;

        for (int i = 60; i < triggerBars.Length; i++)
        {
            if (i - lastSignalBar < _cfg.CooldownBars) continue;

            var row = triggerBars[i];
            var prev = triggerBars[i - 1];
            double atr = row.Atr14;
            if (double.IsNaN(atr) || atr <= 0) continue;

            int minute = row.Bar.Timestamp.Hour * 60 + row.Bar.Timestamp.Minute;
            if (minute < 570 + _cfg.SkipFirstNMinutes) continue;
            if (!InEntryWindow(minute)) continue;

            bool rvolOk = double.IsNaN(row.Rvol) || row.Rvol >= _cfg.RvolMin;
            bool liqOk = double.IsNaN(row.L2Liquidity) || row.L2Liquidity >= _cfg.L2LiquidityMin;
            bool spreadOk = double.IsNaN(row.SpreadZ) || row.SpreadZ <= _cfg.SpreadZMax;
            bool volAccelOk = double.IsNaN(row.VolAccel) || row.VolAccel >= _cfg.MinVolAccel;

            if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21)) continue;

            double vwapDistAtr = double.IsNaN(row.Vwap) ? 0.0 : Math.Abs(row.Bar.Close - row.Vwap) / atr;
            if (vwapDistAtr > _cfg.MaxVwapDistAtr) continue;

            bool ofiBull = !double.IsNaN(row.OfiSignal)
                ? row.OfiSignal >= _cfg.OfiSignalThreshold
                : row.Bar.Close >= prev.Bar.Close;
            bool ofiBear = !double.IsNaN(row.OfiSignal)
                ? row.OfiSignal <= -_cfg.OfiSignalThreshold
                : row.Bar.Close <= prev.Bar.Close;

            bool candleBull = row.Bar.Close > row.Bar.Open;
            bool candleBear = row.Bar.Close < row.Bar.Open;

            bool trendUp = _cfg.UseTrendFilter
                ? row.Ema9 > row.Ema21 && row.Bar.Close >= row.Ema21
                : row.Bar.Close >= row.Ema9;
            bool trendDown = _cfg.UseTrendFilter
                ? row.Ema9 < row.Ema21 && row.Bar.Close <= row.Ema21
                : row.Bar.Close <= row.Ema9;
            bool pullbackToFastMa = Math.Abs(row.Bar.Close - row.Ema9) / atr <= _cfg.PullbackToEma9Atr
                                    || (row.Bar.Low <= row.Ema9 && row.Bar.High >= row.Ema9);
            if (!_cfg.RequirePullback) pullbackToFastMa = true;

            bool vwapLongOk = double.IsNaN(row.Vwap) || row.Bar.Close >= row.Vwap;
            bool vwapShortOk = double.IsNaN(row.Vwap) || row.Bar.Close <= row.Vwap;

            int longScore = 0;
            if (candleBull) longScore++;
            if (trendUp) longScore++;
            if (pullbackToFastMa) longScore++;
            if (vwapLongOk) longScore++;
            if (ofiBull) longScore++;
            if (rvolOk) longScore++;
            if (liqOk) longScore++;
            if (spreadOk) longScore++;
            if (volAccelOk) longScore++;

            int shortScore = 0;
            if (candleBear) shortScore++;
            if (trendDown) shortScore++;
            if (pullbackToFastMa) shortScore++;
            if (vwapShortOk) shortScore++;
            if (ofiBear) shortScore++;
            if (rvolOk) shortScore++;
            if (liqOk) shortScore++;
            if (spreadOk) shortScore++;
            if (volAccelOk) shortScore++;

            if (_cfg.RequireMtfAlign)
            {
                bool aligned = HasMtfAlignment(row.Bar.Timestamp, bars5m, bars15m, trendUp, trendDown);
                if (!aligned) continue;
            }

            if (_cfg.AllowLong
                && trendUp
                && longScore >= _cfg.MinEntryScore
                && (double.IsNaN(row.Rsi14) || (row.Rsi14 >= _cfg.RsiMinLong && row.Rsi14 <= _cfg.RsiMaxLong))
                && (!_cfg.RequireHtfBias || htfBias is "BULL" or "STRONG_BULL" or "NEUTRAL"))
            {
                double swingLow = row.Bar.Low;
                for (int k = Math.Max(0, i - _cfg.SwingLookback); k <= i; k++)
                    swingLow = Math.Min(swingLow, triggerBars[k].Bar.Low);

                double entry = row.Bar.Close;
                double stopBySwing = swingLow - (0.05 * atr);
                double maxStop = entry - (_cfg.HardStopR * atr);
                double stop = Math.Max(stopBySwing, maxStop);
                double riskPerShare = entry - stop;

                if (riskPerShare > 0)
                {
                    int qty = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));
                    signals.Add(new BacktestSignal(
                        BarIndex: i,
                        Timestamp: row.Bar.Timestamp,
                        Side: TradeSide.Long,
                        EntryPrice: entry,
                        StopPrice: stop,
                        RiskPerShare: riskPerShare,
                        PositionSize: qty,
                        AtrValue: atr,
                        HtfTrend: HtfBias.Bull,
                        MtfMomentum: "ALIGNED",
                        SubStrategy: "V9_L1L2_LONG"));
                    lastSignalBar = i;
                    continue;
                }
            }

            if (_cfg.AllowShort
                && trendDown
                && shortScore >= _cfg.MinEntryScore
                && (double.IsNaN(row.Rsi14) || (row.Rsi14 >= _cfg.RsiMinShort && row.Rsi14 <= _cfg.RsiMaxShort))
                && (!_cfg.RequireHtfBias || htfBias is "BEAR" or "STRONG_BEAR" or "NEUTRAL"))
            {
                double swingHigh = row.Bar.High;
                for (int k = Math.Max(0, i - _cfg.SwingLookback); k <= i; k++)
                    swingHigh = Math.Max(swingHigh, triggerBars[k].Bar.High);

                double entry = row.Bar.Close;
                double stopBySwing = swingHigh + (0.05 * atr);
                double maxStop = entry + (_cfg.HardStopR * atr);
                double stop = Math.Min(stopBySwing, maxStop);
                double riskPerShare = stop - entry;

                if (riskPerShare > 0)
                {
                    int qty = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));
                    signals.Add(new BacktestSignal(
                        BarIndex: i,
                        Timestamp: row.Bar.Timestamp,
                        Side: TradeSide.Short,
                        EntryPrice: entry,
                        StopPrice: stop,
                        RiskPerShare: riskPerShare,
                        PositionSize: qty,
                        AtrValue: atr,
                        HtfTrend: HtfBias.Bear,
                        MtfMomentum: "ALIGNED",
                        SubStrategy: "V9_L1L2_SHORT"));
                    lastSignalBar = i;
                }
            }
        }

        return signals;
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);

    private bool InEntryWindow(int minute)
    {
        foreach (var (start, end) in _cfg.EntryWindows)
        {
            if (minute >= start && minute <= end) return true;
        }
        return false;
    }

    private static string ComputeHtfBias(EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        var scores = new List<int>();
        foreach (var bars in new[] { bars1h, bars1d })
        {
            if (bars == null || bars.Length < 30) continue;
            var last = bars[^1];

            int s = 0;
            s += last.Ema21 > last.Ema50 ? 1 : -1;
            s += last.Bar.Close > last.Ema21 ? 1 : -1;
            s += last.MacdHist >= 0 ? 1 : -1;
            if (!double.IsNaN(last.Adx) && last.Adx > 20)
                s += last.PlusDi >= last.MinusDi ? 1 : -1;
            scores.Add(s);
        }

        if (scores.Count == 0) return "NEUTRAL";
        double avg = scores.Average();
        if (avg >= 2.5) return "STRONG_BULL";
        if (avg >= 1.0) return "BULL";
        if (avg <= -2.5) return "STRONG_BEAR";
        if (avg <= -1.0) return "BEAR";
        return "NEUTRAL";
    }

    private static bool HasMtfAlignment(DateTime ts, EnrichedBar[]? bars5m, EnrichedBar[]? bars15m, bool trendUp, bool trendDown)
    {
        bool ok5 = false;
        bool ok15 = false;

        int i5 = FindAsOfIndex(bars5m, ts);
        if (i5 >= 0 && bars5m != null)
        {
            var b = bars5m[i5];
            ok5 = trendUp
                ? !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 > b.Ema21 && b.MacdHist >= 0
                : !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 < b.Ema21 && b.MacdHist <= 0;
        }

        int i15 = FindAsOfIndex(bars15m, ts);
        if (i15 >= 0 && bars15m != null)
        {
            var b = bars15m[i15];
            ok15 = trendUp
                ? !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 > b.Ema21 && b.MacdHist >= 0
                : !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 < b.Ema21 && b.MacdHist <= 0;
        }

        return ok5 && ok15;
    }

    private static int FindAsOfIndex(EnrichedBar[]? bars, DateTime timestamp)
    {
        if (bars == null || bars.Length == 0) return -1;

        int lo = 0;
        int hi = bars.Length - 1;
        int best = -1;

        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            var ts = bars[mid].Bar.Timestamp;
            if (ts <= timestamp)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return best;
    }
}



