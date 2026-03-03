using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

/// <summary>
/// V9 config (score-based L1/L2).
/// "_1" version includes:
/// - Exchange-time (America/New_York) aware entry windows
/// - Conservative missing-data policy (NaN fails critical filters used in scoring)
/// - Persistent cooldown based on time (works across repeated GenerateSignals calls)
/// - Directional MTF alignment (no ambiguous trendUp/trendDown boolean coupling)
/// - Optional next-bar-open entry modeling
/// - Position sizing caps using AccountSize
/// - Consistent commission deduction
/// </summary>
public sealed class V9Config_1
{
    public double RiskPerTradeDollars { get; set; } = 40.0;
    public double AccountSize { get; set; } = 25_000.0;

    /// <summary>Cap position notional as % of AccountSize (0..1).</summary>
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.25;

    /// <summary>Absolute hard cap on shares.</summary>
    public int MaxShares { get; set; } = 10_000;

    /// <summary>Minimum risk per share to avoid unrealistic huge sizing when stop is too tight.</summary>
    public double MinRiskPerShare { get; set; } = 0.01;

    /// <summary>
    /// If true: signal computed on bar i close, but entry assumed at bar i+1 open (more realistic).
    /// If false: entry at bar i close (legacy behavior).
    /// </summary>
    public bool UseNextBarOpenEntry { get; set; } = true;

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

    /// <summary>Cooldown in minutes for 1m trigger bars.</summary>
    public int CooldownBars { get; set; } = 2;

    public bool RequireHtfBias { get; set; } = true;
    public bool RequireMtfAlign { get; set; } = false;

    public int SkipFirstNMinutes { get; set; } = 5;

    /// <summary>Market open minute-of-day in ET. Default 09:30 => 570.</summary>
    public int MarketOpenMinute { get; set; } = 570;

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

public sealed class StrategyV9_1 : IBacktestStrategy
{
    private readonly V9Config_1 _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    // Persistent cooldown across repeated calls (live-like use)
    private DateTime _lastSignalEt = DateTime.MinValue;

    public StrategyV9_1(V9Config_1? cfg = null)
    {
        _cfg = cfg ?? new V9Config_1();
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
            DeductCommission = true, // fixed: deduct commission consistently
            Tp1TightenToBe = true,
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
        if (triggerBars.Length < 80) return signals;

        string htfBias = ComputeHtfBias(bars1h, bars1d);

        for (int i = 60; i < triggerBars.Length; i++)
        {
            var row = triggerBars[i];
            var prev = triggerBars[i - 1];

            var rowEt = TradingTime.ToEt(row.Bar.Timestamp);
            if (_lastSignalEt != DateTime.MinValue)
            {
                // Cooldown interpreted as minutes for 1m trigger bars (works even if window slides).
                if ((rowEt - _lastSignalEt).TotalMinutes < _cfg.CooldownBars) continue;
            }

            double atr = row.Atr14;
            if (double.IsNaN(atr) || atr <= 0) continue;

            int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
            if (minuteEt < _cfg.MarketOpenMinute + _cfg.SkipFirstNMinutes) continue;
            if (!InEntryWindow(minuteEt)) continue;

            // ---- Conservative missing-data policy for critical filters ----
            if (double.IsNaN(row.Rvol) || row.Rvol < _cfg.RvolMin) continue;
            if (double.IsNaN(row.L2Liquidity) || row.L2Liquidity < _cfg.L2LiquidityMin) continue;
            if (double.IsNaN(row.SpreadZ) || row.SpreadZ > _cfg.SpreadZMax) continue;
            if (double.IsNaN(row.VolAccel) || row.VolAccel < _cfg.MinVolAccel) continue;

            if (double.IsNaN(row.Ema9) || double.IsNaN(row.Ema21)) continue;

            // VWAP distance guard (conservative: NaN VWAP fails this guard)
            if (double.IsNaN(row.Vwap) || row.Vwap <= 0) continue;
            double vwapDistAtr = Math.Abs(row.Bar.Close - row.Vwap) / atr;
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

            // VWAP side checks (VWAP is guaranteed non-NaN above)
            bool vwapLongOk = row.Bar.Close >= row.Vwap;
            bool vwapShortOk = row.Bar.Close <= row.Vwap;

            // ---- Score model (keeps original structure, but NaNs no longer auto-pass) ----
            int longScore = 0;
            if (candleBull) longScore++;
            if (trendUp) longScore++;
            if (pullbackToFastMa) longScore++;
            if (vwapLongOk) longScore++;
            if (ofiBull) longScore++;
            longScore += 4; // rvolOk, liqOk, spreadOk, volAccelOk are guaranteed true by gates

            int shortScore = 0;
            if (candleBear) shortScore++;
            if (trendDown) shortScore++;
            if (pullbackToFastMa) shortScore++;
            if (vwapShortOk) shortScore++;
            if (ofiBear) shortScore++;
            shortScore += 4;

            // Optional MTF alignment: now directional
            if (_cfg.RequireMtfAlign)
            {
                // We'll only check alignment for the direction we might take.
                // If both directions are plausible, each direction's check is applied in its branch below.
            }

            // ---- LONG branch ----
            if (_cfg.AllowLong
                && trendUp
                && longScore >= _cfg.MinEntryScore
                && (double.IsNaN(row.Rsi14) || (row.Rsi14 >= _cfg.RsiMinLong && row.Rsi14 <= _cfg.RsiMaxLong))
                && (!_cfg.RequireHtfBias || htfBias is "BULL" or "STRONG_BULL" or "NEUTRAL"))
            {
                if (_cfg.RequireMtfAlign && !HasMtfAlignment(row.Bar.Timestamp, bars5m, bars15m, TradeSide.Long))
                    goto TRY_SHORT;

                double swingLow = row.Bar.Low;
                for (int k = Math.Max(0, i - _cfg.SwingLookback); k <= i; k++)
                    swingLow = Math.Min(swingLow, triggerBars[k].Bar.Low);

                // Entry model
                int entryIndex = i;
                double entry = row.Bar.Close;
                DateTime entryTs = row.Bar.Timestamp;
                if (_cfg.UseNextBarOpenEntry)
                {
                    if (i + 1 >= triggerBars.Length) goto TRY_SHORT;
                    entryIndex = i + 1;
                    entry = triggerBars[entryIndex].Bar.Open;
                    entryTs = triggerBars[entryIndex].Bar.Timestamp;
                }

                double stopBySwing = swingLow - (0.05 * atr);
                double maxStop = entry - (_cfg.HardStopR * atr);
                double stop = Math.Max(stopBySwing, maxStop);

                double riskPerShare = entry - stop;
                if (riskPerShare > 0 && riskPerShare >= _cfg.MinRiskPerShare)
                {
                    int qty = ComputePositionSize(entry, riskPerShare);
                    if (qty > 0)
                    {
                        signals.Add(new BacktestSignal(
                            BarIndex: entryIndex,
                            Timestamp: entryTs,
                            Side: TradeSide.Long,
                            EntryPrice: entry,
                            StopPrice: stop,
                            RiskPerShare: riskPerShare,
                            PositionSize: qty,
                            AtrValue: atr,
                            HtfTrend: HtfBias.Bull,
                            MtfMomentum: _cfg.RequireMtfAlign ? "ALIGNED" : "N/A",
                            SubStrategy: "V9_L1L2_LONG"));
                        _lastSignalEt = TradingTime.ToEt(entryTs);
                        continue;
                    }
                }
            }

            TRY_SHORT:

            // ---- SHORT branch ----
            if (_cfg.AllowShort
                && trendDown
                && shortScore >= _cfg.MinEntryScore
                && (double.IsNaN(row.Rsi14) || (row.Rsi14 >= _cfg.RsiMinShort && row.Rsi14 <= _cfg.RsiMaxShort))
                && (!_cfg.RequireHtfBias || htfBias is "BEAR" or "STRONG_BEAR" or "NEUTRAL"))
            {
                if (_cfg.RequireMtfAlign && !HasMtfAlignment(row.Bar.Timestamp, bars5m, bars15m, TradeSide.Short))
                    continue;

                double swingHigh = row.Bar.High;
                for (int k = Math.Max(0, i - _cfg.SwingLookback); k <= i; k++)
                    swingHigh = Math.Max(swingHigh, triggerBars[k].Bar.High);

                // Entry model
                int entryIndex = i;
                double entry = row.Bar.Close;
                DateTime entryTs = row.Bar.Timestamp;
                if (_cfg.UseNextBarOpenEntry)
                {
                    if (i + 1 >= triggerBars.Length) continue;
                    entryIndex = i + 1;
                    entry = triggerBars[entryIndex].Bar.Open;
                    entryTs = triggerBars[entryIndex].Bar.Timestamp;
                }

                double stopBySwing = swingHigh + (0.05 * atr);
                double maxStop = entry + (_cfg.HardStopR * atr);
                double stop = Math.Min(stopBySwing, maxStop);

                double riskPerShare = stop - entry;
                if (riskPerShare > 0 && riskPerShare >= _cfg.MinRiskPerShare)
                {
                    int qty = ComputePositionSize(entry, riskPerShare);
                    if (qty > 0)
                    {
                        signals.Add(new BacktestSignal(
                            BarIndex: entryIndex,
                            Timestamp: entryTs,
                            Side: TradeSide.Short,
                            EntryPrice: entry,
                            StopPrice: stop,
                            RiskPerShare: riskPerShare,
                            PositionSize: qty,
                            AtrValue: atr,
                            HtfTrend: HtfBias.Bear,
                            MtfMomentum: _cfg.RequireMtfAlign ? "ALIGNED" : "N/A",
                            SubStrategy: "V9_L1L2_SHORT"));
                        _lastSignalEt = TradingTime.ToEt(entryTs);
                    }
                }
            }
        }

        return signals;
    }

    private int ComputePositionSize(double entryPrice, double riskPerShare)
    {
        int qtyByRisk = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));

        double maxNotional = Math.Max(0.0, _cfg.AccountSize * _cfg.MaxPositionNotionalPctOfAccount);
        int qtyByNotional = maxNotional > 0 && entryPrice > 0 ? Math.Max(1, (int)(maxNotional / entryPrice)) : _cfg.MaxShares;

        int qty = Math.Min(qtyByRisk, qtyByNotional);
        qty = Math.Min(qty, _cfg.MaxShares);
        return Math.Max(1, qty);
    }

    public BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);

    private bool InEntryWindow(int minuteEt)
    {
        foreach (var (start, end) in _cfg.EntryWindows)
        {
            if (minuteEt >= start && minuteEt <= end) return true;
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

    private static bool HasMtfAlignment(DateTime ts, EnrichedBar[]? bars5m, EnrichedBar[]? bars15m, TradeSide direction)
    {
        bool ok5 = true;
        bool ok15 = true;

        if (bars5m != null && bars5m.Length > 0)
        {
            int i5 = FindAsOfIndex(bars5m, ts);
            if (i5 >= 0)
            {
                var b = bars5m[i5];
                ok5 = direction == TradeSide.Long
                    ? !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 > b.Ema21 && b.MacdHist >= 0
                    : !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 < b.Ema21 && b.MacdHist <= 0;
            }
        }

        if (bars15m != null && bars15m.Length > 0)
        {
            int i15 = FindAsOfIndex(bars15m, ts);
            if (i15 >= 0)
            {
                var b = bars15m[i15];
                ok15 = direction == TradeSide.Long
                    ? !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 > b.Ema21 && b.MacdHist >= 0
                    : !double.IsNaN(b.Ema9) && !double.IsNaN(b.Ema21) && b.Ema9 < b.Ema21 && b.MacdHist <= 0;
            }
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

    private static class TradingTime
    {
        // Works on Windows ("Eastern Standard Time") and Linux ("America/New_York").
        private static readonly TimeZoneInfo _et = ResolveEasternTimeZone();

        private static TimeZoneInfo ResolveEasternTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
            catch { /* ignore */ }
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
            catch { /* ignore */ }
            return TimeZoneInfo.Utc;
        }

        public static DateTime ToEt(DateTime ts)
        {
            // Conservative conversion policy:
            // - Utc -> ET conversion
            // - Local -> ET conversion
            // - Unspecified -> assume already ET (avoids double-shifting unknown feeds)
            return ts.Kind switch
            {
                DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc(ts, _et),
                DateTimeKind.Local => TimeZoneInfo.ConvertTime(ts, _et),
                _ => ts
            };
        }

        public static int GetMinuteOfDayEt(DateTime ts)
        {
            var et = ToEt(ts);
            return et.Hour * 60 + et.Minute;
        }
    }
}
