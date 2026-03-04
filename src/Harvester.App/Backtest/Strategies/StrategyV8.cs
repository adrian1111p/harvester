using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

public sealed class V8Config
{
    public double RiskPerTradeDollars { get; set; } = 50.0;
    public double AccountSize { get; set; } = 25_000.0;

    public bool ShortOnly { get; set; } = true;
    public double RvolMin { get; set; } = 0.8;
    public double L2LiquidityMin { get; set; } = 20.0;
    public double SpreadZMax { get; set; } = 2.5;

    public double PullbackToEma9Atr { get; set; } = 0.30;
    public double RejectCloseBelowEma9Atr { get; set; } = 0.08;
    public double RsiMinShort { get; set; } = 42.0;
    public double RsiMaxShort { get; set; } = 70.0;
    public double MinAdx { get; set; } = 0.0;
    public bool RequireMtfBearAlignment { get; set; } = false;

    public int SwingLookback { get; set; } = 4;
    public double HardStopR { get; set; } = 1.0;
    public double BreakevenR { get; set; } = 0.5;
    public double TrailR { get; set; } = 0.35;
    public double GivebackPct { get; set; } = 0.35;
    public bool UseFixedGivebackUsdCap { get; set; } = true;
    public double GivebackUsdCap { get; set; } = 30.0;
    public double Tp1R { get; set; } = 0.8;
    public double Tp2R { get; set; } = 1.8;
    public int MaxHoldBars { get; set; } = 45;

    public bool ReversalFlatten { get; set; } = true;
    public double MicroTrailCents { get; set; } = 2.0;
    public double MicroTrailActivateCents { get; set; } = 4.0;

    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;
}

public sealed class StrategyV8 : BacktestStrategyBase
{
    private readonly V8Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public StrategyV8(V8Config? cfg = null)
    {
        _cfg = cfg ?? new V8Config();
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
            DeductCommission = true,
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
        string htfBias = ComputeHtfBias(bars1h, bars1d);

        for (int i = 55; i < triggerBars.Length; i++)
        {
            if (_cfg.ShortOnly == false) continue;

            var row = triggerBars[i];
            var prev = triggerBars[i - 1];
            double atr = row.Atr14;
            if (double.IsNaN(atr) || atr <= 0) continue;

            if (htfBias != "BEAR" && htfBias != "STRONG_BEAR") continue;

            if (!double.IsNaN(row.Rvol) && row.Rvol < _cfg.RvolMin) continue;
            if (!double.IsNaN(row.L2Liquidity) && row.L2Liquidity < _cfg.L2LiquidityMin) continue;
            if (!double.IsNaN(row.SpreadZ) && row.SpreadZ > _cfg.SpreadZMax) continue;

            if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21) || double.IsNaN(row.Sma20)) continue;
            if (row.Ema9 >= row.Ema21 || row.Bar.Close >= row.Sma20) continue;

            if (double.IsNaN(row.Rsi14) || row.Rsi14 < _cfg.RsiMinShort || row.Rsi14 > _cfg.RsiMaxShort) continue;
            if (!double.IsNaN(row.Adx) && row.Adx < _cfg.MinAdx) continue;
            if (!double.IsNaN(row.OfiSignal) && row.OfiSignal > 0) continue;

            if (_cfg.RequireMtfBearAlignment)
            {
                if (!HasBearMtfAlignment(row.Bar.Timestamp, bars5m, bars15m))
                    continue;
            }

            // Pullback touches EMA9 then rejects lower
            bool touchedEma9 = Math.Abs(row.Bar.High - row.Ema9) / atr <= _cfg.PullbackToEma9Atr
                               || Math.Abs(row.Bar.Close - row.Ema9) / atr <= _cfg.PullbackToEma9Atr;
            if (!touchedEma9) continue;

            bool rejection = row.Bar.Close < row.Ema9 - (_cfg.RejectCloseBelowEma9Atr * atr)
                             && row.Bar.Close < row.Bar.Open
                             && prev.Bar.Close >= prev.Ema9;
            if (!rejection) continue;

            // Stop above local swing high, capped by HardStopR * ATR
            double swingHigh = row.Bar.High;
            int start = Math.Max(0, i - _cfg.SwingLookback);
            for (int k = start; k <= i; k++)
                swingHigh = Math.Max(swingHigh, triggerBars[k].Bar.High);

            double entry = row.Bar.Close;
            double stopBySwing = swingHigh + (0.05 * atr);
            double maxStop = entry + (_cfg.HardStopR * atr);
            double stop = Math.Min(stopBySwing, maxStop);
            double riskPerShare = stop - entry;
            if (riskPerShare <= 0) continue;

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
                SubStrategy: "V8_SHORT_PULLBACK"));
        }

        return signals;
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);

    private static string ComputeHtfBias(EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        var scores = new List<int>();
        foreach (var bars in new[] { bars1h, bars1d })
        {
            if (bars == null || bars.Length < 30) continue;
            var last = bars[^1];
            var prev = bars[^2];

            int s = 0;
            s += last.Ema21 < prev.Ema21 ? 1 : -1;
            s += last.Bar.Close < last.Ema21 ? 1 : -1;
            s += last.MacdHist < 0 ? 1 : -1;
            if (!double.IsNaN(last.Adx) && last.Adx > 20 && last.MinusDi > last.PlusDi) s += 1;
            scores.Add(s);
        }

        if (scores.Count == 0) return "NEUTRAL";
        double avg = scores.Average();
        if (avg >= 3.0) return "STRONG_BEAR";
        if (avg >= 1.5) return "BEAR";
        if (avg <= -3.0) return "STRONG_BULL";
        if (avg <= -1.5) return "BULL";
        return "NEUTRAL";
    }

    private static bool HasBearMtfAlignment(DateTime ts, EnrichedBar[]? bars5m, EnrichedBar[]? bars15m)
    {
        bool ok5 = false;
        bool ok15 = false;

        int i5 = FindAsOfIndex(bars5m, ts);
        if (i5 >= 0 && bars5m != null)
        {
            var b = bars5m[i5];
            ok5 = !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 < b.Ema21
                  && !double.IsNaN(b.MacdHist) && b.MacdHist <= 0;
        }

        int i15 = FindAsOfIndex(bars15m, ts);
        if (i15 >= 0 && bars15m != null)
        {
            var b = bars15m[i15];
            ok15 = !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 < b.Ema21
                   && !double.IsNaN(b.MacdHist) && b.MacdHist <= 0;
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



