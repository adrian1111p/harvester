using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

/// <summary>V6 config — Opening Range Breakout (ORB).</summary>
public sealed class V6Config
{
    public double RiskPerTradeDollars { get; set; } = 50.0;
    public double AccountSize { get; set; } = 25_000.0;

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

    // Micro-trail
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
public sealed class StrategyV6 : IBacktestStrategy
{
    private readonly V6Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public StrategyV6(V6Config? cfg = null)
    {
        _cfg = cfg ?? new V6Config();
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
            DeductCommission = false,  // V6 doesn't deduct commission
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

        // Group bars by trading day and compute opening ranges
        var dayGroups = GroupByTradingDay(triggerBars);

        foreach (var day in dayGroups)
        {
            var (orHigh, orLow, orEndIdx) = ComputeOpeningRange(day.Bars, day.StartIdx, triggerBars);
            if (orEndIdx < 0) continue;

            double orRange = orHigh - orLow;
            if (orRange <= 0) continue;

            // Validate OR range against ATR
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

                // Time window check
                int minuteOfDay = GetMinuteOfDay(row.Bar.Timestamp);
                if (!InEntryWindow(minuteOfDay)) continue;

                // 20MA distance
                if (double.IsNaN(row.Sma20)) continue;
                double maDist = Math.Abs((row.Bar.Close - row.Sma20) / atrVal);
                if (maDist > _cfg.MaxMaDistAtr) continue;

                // Volume
                if (!double.IsNaN(row.Rvol) && row.Rvol < _cfg.RvolMin) continue;

                double price = row.Bar.Close;

                // LONG: breakout above OR high
                bool longBreak = _cfg.RequireCrossFromInside
                    ? (price > orHigh && prev.Bar.Close <= orHigh)
                    : (price > orHigh);

                bool htfAllowsLong = _cfg.IgnoreHtfBias || htfBias != "BEAR";
                if (longEntries < _cfg.MaxEntriesPerDirectionPerDay && longBreak && htfAllowsLong)
                {
                    if (!_cfg.RequireVwapAlign || (!double.IsNaN(row.Vwap) && price > row.Vwap))
                    {
                        double stopPrice = _cfg.StopAtOpposite ? orLow
                            : _cfg.StopAtMidpoint ? (orHigh + orLow) / 2.0
                            : price - _cfg.HardStopR * atrVal;

                        double riskPerShare = Math.Abs(price - stopPrice);
                        if (riskPerShare > 0)
                        {
                            int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));
                            signals.Add(new BacktestSignal(i, row.Bar.Timestamp, TradeSide.Long,
                                price, stopPrice, riskPerShare, posSize, atrVal,
                                HtfBias.Neutral, "N/A", "ORB_BREAKOUT_HIGH"));
                            longEntries++;
                        }
                    }
                }

                // SHORT: breakdown below OR low
                bool shortBreak = _cfg.RequireCrossFromInside
                    ? (price < orLow && prev.Bar.Close >= orLow)
                    : (price < orLow);

                bool htfAllowsShort = _cfg.IgnoreHtfBias || htfBias != "BULL";
                if (shortEntries < _cfg.MaxEntriesPerDirectionPerDay && shortBreak && htfAllowsShort)
                {
                    if (!_cfg.RequireVwapAlign || (!double.IsNaN(row.Vwap) && price < row.Vwap))
                    {
                        double stopPrice = _cfg.StopAtOpposite ? orHigh
                            : _cfg.StopAtMidpoint ? (orHigh + orLow) / 2.0
                            : price + _cfg.HardStopR * atrVal;

                        double riskPerShare = Math.Abs(stopPrice - price);
                        if (riskPerShare > 0)
                        {
                            int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));
                            signals.Add(new BacktestSignal(i, row.Bar.Timestamp, TradeSide.Short,
                                price, stopPrice, riskPerShare, posSize, atrVal,
                                HtfBias.Neutral, "N/A", "ORB_BREAKOUT_LOW"));
                            shortEntries++;
                        }
                    }
                }
            }
        }

        return signals;
    }

    public BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);

    // ── Day Grouping & Opening Range ─────────────────────────────────────

    private sealed record DayGroup(DateOnly Date, int StartIdx, int EndIdx, EnrichedBar[] Bars);

    private static List<DayGroup> GroupByTradingDay(EnrichedBar[] bars)
    {
        var groups = new List<DayGroup>();
        if (bars.Length == 0) return groups;

        int start = 0;
        DateOnly currentDay = DateOnly.FromDateTime(bars[0].Bar.Timestamp);

        for (int i = 1; i < bars.Length; i++)
        {
            var day = DateOnly.FromDateTime(bars[i].Bar.Timestamp);
            if (day != currentDay)
            {
                groups.Add(new DayGroup(currentDay, start, i, bars));
                start = i;
                currentDay = day;
            }
        }
        groups.Add(new DayGroup(currentDay, start, bars.Length, bars));
        return groups;
    }

    private (double OrHigh, double OrLow, int OrEndIdx) ComputeOpeningRange(
        EnrichedBar[] bars, int dayStartIdx, EnrichedBar[] allBars)
    {
        if (dayStartIdx < 0 || dayStartIdx >= allBars.Length)
            return (double.NaN, double.NaN, -1);

        int marketOpenMinute = GetMinuteOfDay(allBars[dayStartIdx].Bar.Timestamp);
        int orEndMinute = marketOpenMinute + _cfg.OrMinutes;

        double orHigh = double.MinValue;
        double orLow = double.MaxValue;
        int orEndIdx = -1;

        for (int i = dayStartIdx; i < allBars.Length; i++)
        {
            if (DateOnly.FromDateTime(allBars[i].Bar.Timestamp) !=
                DateOnly.FromDateTime(allBars[dayStartIdx].Bar.Timestamp))
            {
                orEndIdx = i;
                break;
            }

            int minute = GetMinuteOfDay(allBars[i].Bar.Timestamp);
            if (minute >= orEndMinute)
            {
                orEndIdx = i;
                break;
            }
            orHigh = Math.Max(orHigh, allBars[i].Bar.High);
            orLow = Math.Min(orLow, allBars[i].Bar.Low);
        }

        if (orEndIdx < 0)
            orEndIdx = allBars.Length - 1;

        if (orHigh == double.MinValue || orLow == double.MaxValue)
            return (double.NaN, double.NaN, -1);

        return (orHigh, orLow, orEndIdx);
    }

    private static int GetMinuteOfDay(DateTime dt)
    {
        // Assume timestamps are already in ET or that we calculate approximate minute
        return dt.Hour * 60 + dt.Minute;
    }

    private bool InEntryWindow(int minute)
    {
        foreach (var (start, end) in _cfg.EntryWindows)
        {
            if (minute >= start && minute <= end) return true;
        }
        return false;
    }

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
