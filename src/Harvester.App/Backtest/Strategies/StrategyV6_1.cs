using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

/// <summary>
/// V6 config — Opening Range Breakout (ORB).
/// "_1" version includes:
/// - Exchange-time (America/New_York) aware day grouping + entry windows
/// - Opening Range anchored to 09:30 ET (ignores premarket bars)
/// - Conservative missing-data policy (NaN fails critical filters like RVOL)
/// - Optional next-bar-open entry modeling
/// - Position sizing caps using AccountSize
/// - Consistent commission deduction
/// </summary>
public sealed class V6Config_1
{
    public double RiskPerTradeDollars { get; set; } = 50.0;
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

    /// <summary>Market open minute-of-day in exchange time (ET). Default 09:30 => 570.</summary>
    public int MarketOpenMinute { get; set; } = 570;

    // Opening Range
    public int OrMinutes { get; set; } = 15;
    public double MinRangeAtr { get; set; } = 0.3;
    public double MaxRangeAtr { get; set; } = 10.0;

    // 20MA filter
    public double MaxMaDistAtr { get; set; } = 0.5;

    // VWAP alignment
    public bool RequireVwapAlign { get; set; } = true;
    public bool IgnoreHtfBias { get; set; } = false;

    // Time windows (minutes from midnight ET): (start, end)
    // Default: 9:45-11:30 (585-690) and 14:00-15:30 (840-930)
    public (int Start, int End)[] EntryWindows { get; set; } =
        [(585, 690), (840, 930)];

    public bool RequireCrossFromInside { get; set; } = true;
    public int MaxEntriesPerDirectionPerDay { get; set; } = 1;

    // Stop placement
    public bool StopAtOpposite { get; set; } = true;
    public bool StopAtMidpoint { get; set; } = false;

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

    // Micro-trail (kept as-is; consider converting to ticks/ATR in ExitEngine for true scaling)
    public double MicroTrailCents { get; set; } = 3.0;
    public double MicroTrailActivateCents { get; set; } = 5.0;

    // Reversal flatten
    public bool ReversalFlatten { get; set; } = true;

    // Costs
    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;
}

/// <summary>
/// V6 "Opening Range Breakout" — classic ORB strategy with 20MA filter,
/// VWAP confirmation, time windows, and one-entry-per-direction limits.
/// </summary>
public sealed class StrategyV6_1 : BacktestStrategyBase
{
    private readonly V6Config_1 _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public StrategyV6_1(V6Config_1? cfg = null)
    {
        _cfg = cfg ?? new V6Config_1();
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.0,
            UseFixedGivebackUsdCap = true,
            UseVariableGivebackUsdCap = true,
            GivebackUsdCap = 30.0,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,   // fixed: deduct commission consistently
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

        // Group bars by exchange trading day (ET) and compute opening ranges
        var dayGroups = GroupByTradingDayEt(triggerBars);

        foreach (var day in dayGroups)
        {
            var (orHigh, orLow, orEndIdx) = ComputeOpeningRangeEt(day.StartIdx, day.EndIdx, triggerBars);
            if (orEndIdx < 0) continue;

            double orRange = orHigh - orLow;
            if (orRange <= 0) continue;

            // Validate OR range against ATR (use bar at OR end)
            double atrAtOrEnd = orEndIdx < triggerBars.Length ? triggerBars[orEndIdx].Atr14 : double.NaN;
            if (double.IsNaN(atrAtOrEnd) || atrAtOrEnd <= 0) continue;
            double rangeInAtr = orRange / atrAtOrEnd;
            if (rangeInAtr < _cfg.MinRangeAtr || rangeInAtr > _cfg.MaxRangeAtr) continue;

            int longEntries = 0, shortEntries = 0;

            for (int i = orEndIdx; i < day.EndIdx && i < triggerBars.Length; i++)
            {
                var row = triggerBars[i];
                var prev = i > 0 ? triggerBars[i - 1] : row;

                double atrVal = row.Atr14;
                if (double.IsNaN(atrVal) || atrVal <= 0) continue;

                // Time window check (exchange/ET)
                int minuteOfDay = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
                if (!InEntryWindow(minuteOfDay)) continue;

                // Compute HTF bias per-bar (no lookahead)
                string htfBias = HtfBiasAtTime(row.Bar.Timestamp, bars1h, bars1d);

                // 20MA distance
                if (double.IsNaN(row.Sma20)) continue;
                double maDist = Math.Abs((row.Bar.Close - row.Sma20) / atrVal);
                if (maDist > _cfg.MaxMaDistAtr) continue;

                // Volume (conservative: NaN fails when volume filter is in use)
                if (double.IsNaN(row.Rvol) || row.Rvol < _cfg.RvolMin) continue;

                double evalPrice = row.Bar.Close;

                // LONG: breakout above OR high
                bool longBreak = _cfg.RequireCrossFromInside
                    ? (evalPrice > orHigh && prev.Bar.Close <= orHigh)
                    : (evalPrice > orHigh);

                bool htfAllowsLong = _cfg.IgnoreHtfBias || htfBias != "BEAR";
                if (longEntries < _cfg.MaxEntriesPerDirectionPerDay && longBreak && htfAllowsLong)
                {
                    if (!_cfg.RequireVwapAlign || (!double.IsNaN(row.Vwap) && evalPrice > row.Vwap))
                    {
                        var sig = BuildSignal(i, triggerBars, TradeSide.Long, evalPrice, orHigh, orLow, atrVal, "ORB_BREAKOUT_HIGH", day.EndIdx);
                        if (sig != null)
                        {
                            signals.Add(sig);
                            longEntries++;
                            continue;
                        }
                    }
                }

                // SHORT: breakdown below OR low
                bool shortBreak = _cfg.RequireCrossFromInside
                    ? (evalPrice < orLow && prev.Bar.Close >= orLow)
                    : (evalPrice < orLow);

                bool htfAllowsShort = _cfg.IgnoreHtfBias || htfBias != "BULL";
                if (shortEntries < _cfg.MaxEntriesPerDirectionPerDay && shortBreak && htfAllowsShort)
                {
                    if (!_cfg.RequireVwapAlign || (!double.IsNaN(row.Vwap) && evalPrice < row.Vwap))
                    {
                        var sig = BuildSignal(i, triggerBars, TradeSide.Short, evalPrice, orHigh, orLow, atrVal, "ORB_BREAKOUT_LOW", day.EndIdx);
                        if (sig != null)
                        {
                            signals.Add(sig);
                            shortEntries++;
                        }
                    }
                }
            }
        }

        return signals;
    }

    private BacktestSignal? BuildSignal(
        int i,
        EnrichedBar[] bars,
        TradeSide side,
        double evalPrice,
        double orHigh,
        double orLow,
        double atrVal,
        string subStrategy,
        int dayEndIdxExclusive)
    {
        // Entry model: next-bar open (preferred) vs same-bar close (legacy)
        int entryIndex = i;
        double entryPrice = evalPrice;
        DateTime entryTs = bars[i].Bar.Timestamp;

        if (_cfg.UseNextBarOpenEntry)
        {
            if (i + 1 >= bars.Length) return null;
            if (i + 1 >= dayEndIdxExclusive) return null; // do not enter beyond day slice
            entryIndex = i + 1;
            entryPrice = bars[entryIndex].Bar.Open;
            entryTs = bars[entryIndex].Bar.Timestamp;
        }

        double stopPrice = side == TradeSide.Long
            ? (_cfg.StopAtOpposite ? orLow : _cfg.StopAtMidpoint ? (orHigh + orLow) / 2.0 : entryPrice - _cfg.HardStopR * atrVal)
            : (_cfg.StopAtOpposite ? orHigh : _cfg.StopAtMidpoint ? (orHigh + orLow) / 2.0 : entryPrice + _cfg.HardStopR * atrVal);

        double riskPerShare = Math.Abs(entryPrice - stopPrice);
        if (riskPerShare <= 0 || riskPerShare < _cfg.MinRiskPerShare) return null;

        int posSize = ComputePositionSize(entryPrice, riskPerShare);
        if (posSize <= 0) return null;

        return new BacktestSignal(
            BarIndex: entryIndex,
            Timestamp: entryTs,
            Side: side,
            EntryPrice: entryPrice,
            StopPrice: stopPrice,
            RiskPerShare: riskPerShare,
            PositionSize: posSize,
            AtrValue: atrVal,
            HtfTrend: HtfBias.Neutral,
            MtfMomentum: "N/A",
            SubStrategy: subStrategy);
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

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);

    // ── Day Grouping & Opening Range (exchange/ET aware) ─────────────────

    private sealed record DayGroup(DateOnly DateEt, int StartIdx, int EndIdx);

    private static List<DayGroup> GroupByTradingDayEt(EnrichedBar[] bars)
    {
        var groups = new List<DayGroup>();
        if (bars.Length == 0) return groups;

        int start = 0;
        DateOnly currentDay = TradingTime.GetDateEt(bars[0].Bar.Timestamp);

        for (int i = 1; i < bars.Length; i++)
        {
            var day = TradingTime.GetDateEt(bars[i].Bar.Timestamp);
            if (day != currentDay)
            {
                groups.Add(new DayGroup(currentDay, start, i));
                start = i;
                currentDay = day;
            }
        }

        groups.Add(new DayGroup(currentDay, start, bars.Length));
        return groups;
    }

    private (double OrHigh, double OrLow, int OrEndIdx) ComputeOpeningRangeEt(int dayStartIdx, int dayEndIdx, EnrichedBar[] allBars)
    {
        int orStartMinute = _cfg.MarketOpenMinute;
        int orEndMinute = orStartMinute + _cfg.OrMinutes;

        double orHigh = double.MinValue;
        double orLow = double.MaxValue;
        int orEndIdx = -1;

        // Ignore premarket bars; start collecting at 09:30 ET.
        for (int i = dayStartIdx; i < dayEndIdx && i < allBars.Length; i++)
        {
            int minute = TradingTime.GetMinuteOfDayEt(allBars[i].Bar.Timestamp);

            if (minute < orStartMinute) continue;

            if (minute >= orEndMinute)
            {
                orEndIdx = i;
                break;
            }

            orHigh = Math.Max(orHigh, allBars[i].Bar.High);
            orLow = Math.Min(orLow, allBars[i].Bar.Low);
        }

        if (orEndIdx < 0)
            return (double.NaN, double.NaN, -1);

        if (orHigh == double.MinValue || orLow == double.MaxValue)
            return (double.NaN, double.NaN, -1);

        return (orHigh, orLow, orEndIdx);
    }

    private bool InEntryWindow(int minuteEt)
    {
        foreach (var (start, end) in _cfg.EntryWindows)
        {
            if (minuteEt >= start && minuteEt <= end) return true;
        }
        return false;
    }

    private static string HtfBiasAtTime(DateTime ts, EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        var scores = new List<int>();
        foreach (var bars in new[] { bars1h, bars1d })
        {
            if (bars == null || bars.Length < 2) continue;
            int idx = FindBarAtOrBefore(bars, ts);
            if (idx < 0) continue;
            var last = bars[idx];
            int vote = last.Bar.Close > last.Ema21 ? 1 : -1;
            scores.Add(vote);
        }
        if (scores.Count == 0) return "NEUTRAL";
        double avg = scores.Average();
        if (avg >= 0.5) return "BULL";
        if (avg <= -0.5) return "BEAR";
        return "NEUTRAL";
    }

    private static int FindBarAtOrBefore(EnrichedBar[] bars, DateTime ts)
    {
        int lo = 0, hi = bars.Length - 1, best = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (bars[mid].Bar.Timestamp <= ts) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
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

        public static DateOnly GetDateEt(DateTime ts) => DateOnly.FromDateTime(ToEt(ts));
    }
}



