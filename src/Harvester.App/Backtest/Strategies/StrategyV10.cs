using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

public sealed class V10Config
{
    public double RiskPerTradeDollars { get; set; } = 35.0;
    public double AccountSize { get; set; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.25;
    public int MaxShares { get; set; } = 10_000;
    public double MinRiskPerShare { get; set; } = 0.01;

    public bool UseNextBarOpenEntry { get; set; } = true;

    public int MarketOpenMinute { get; set; } = 570;
    public int OrMinutes { get; set; } = 10;
    public double MinRangeAtr { get; set; } = 0.2;
    public double MaxRangeAtr { get; set; } = 8.0;

    public (int Start, int End)[] EntryWindows { get; set; } =
        [(580, 700), (780, 945)];

    public int MaxEntriesPerDirectionPerDay { get; set; } = 2;
    public bool RequireCrossFromInside { get; set; } = true;
    public bool RequireHtfBias { get; set; } = true;

    public double MinPrice { get; set; } = 8.0;
    public double MaxPrice { get; set; } = 400.0;
    public double L2LiquidityMin { get; set; } = 18.0;
    public double SpreadZMax { get; set; } = 2.5;
    public double RvolMin { get; set; } = 0.6;
    public double VolAccelMin { get; set; } = -0.25;

    public double VwapStretchAtr { get; set; } = 1.15;
    public double BbEntryPctbLow { get; set; } = 0.12;
    public double BbEntryPctbHigh { get; set; } = 0.88;
    public int SqueezeBars { get; set; } = 6;

    public double RsiOversold { get; set; } = 35.0;
    public double RsiOverbought { get; set; } = 65.0;
    public double MinOrBreakDistanceAtr { get; set; } = 0.05;
    public double MinOfiForOrb { get; set; } = 0.02;

    public int MinEntryScore { get; set; } = 4;

    public double HardStopR { get; set; } = 0.95;
    public double BreakevenR { get; set; } = 0.45;
    public double TrailR { get; set; } = 0.40;
    public double GivebackPct { get; set; } = 0.30;
    public double Tp1R { get; set; } = 0.90;
    public double Tp2R { get; set; } = 1.80;
    public int MaxHoldBars { get; set; } = 45;

    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;

    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;
}

public sealed class StrategyV10 : BacktestStrategyBase
{
    private readonly V10Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public StrategyV10(V10Config? cfg = null)
    {
        _cfg = cfg ?? new V10Config();
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.20,
            UseFixedGivebackUsdCap = true,
            UseVariableGivebackUsdCap = true,
            GivebackUsdCap = 30.0,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
            Tp1TightenToBe = true,
            ReversalFlatten = true,
            MicroTrail = true,
            MicroTrailCents = 2.0,
            MicroTrailActivateCents = 3.0,
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

        var dayGroups = GroupByTradingDayEt(triggerBars);

        foreach (var day in dayGroups)
        {
            var (orHigh, orLow, orEndIdx) = ComputeOpeningRangeEt(day.StartIdx, day.EndIdx, triggerBars);
            if (orEndIdx < 0) continue;

            double orRange = orHigh - orLow;
            if (orRange <= 0) continue;

            double atrAtOrEnd = orEndIdx < triggerBars.Length ? triggerBars[orEndIdx].Atr14 : double.NaN;
            if (double.IsNaN(atrAtOrEnd) || atrAtOrEnd <= 0) continue;

            double rangeInAtr = orRange / atrAtOrEnd;
            if (rangeInAtr < _cfg.MinRangeAtr || rangeInAtr > _cfg.MaxRangeAtr) continue;

            int longEntries = 0;
            int shortEntries = 0;
            int squeezeCount = 0;

            for (int i = Math.Max(orEndIdx, day.StartIdx + 60); i < day.EndIdx && i < triggerBars.Length; i++)
            {
                var row = triggerBars[i];
                var prev = i > 0 ? triggerBars[i - 1] : row;

                double atrVal = row.Atr14;
                if (double.IsNaN(atrVal) || atrVal <= 0) continue;

                int minuteEt = TradingTime.GetMinuteOfDayEt(row.Bar.Timestamp);
                if (!InEntryWindow(minuteEt)) continue;

                // Compute HTF bias per-bar (no lookahead)
                string htfBias = HtfBiasAtTime(row.Bar.Timestamp, bars1h, bars1d);

                double price = row.Bar.Close;
                if (price < _cfg.MinPrice || price > _cfg.MaxPrice) continue;

                if (double.IsNaN(row.L2Liquidity) || row.L2Liquidity < _cfg.L2LiquidityMin) continue;
                if (double.IsNaN(row.SpreadZ) || row.SpreadZ > _cfg.SpreadZMax) continue;
                if (double.IsNaN(row.Rvol) || row.Rvol < _cfg.RvolMin) continue;
                if (double.IsNaN(row.VolAccel) || row.VolAccel < _cfg.VolAccelMin) continue;
                if (double.IsNaN(row.Sma20)) continue;

                double maDist = Math.Abs((price - row.Sma20) / atrVal);
                if (maDist > 1.5) continue;

                if (!double.IsNaN(row.BbUpper) && !double.IsNaN(row.KcUpper) &&
                    !double.IsNaN(row.BbLower) && !double.IsNaN(row.KcLower) &&
                    row.BbUpper < row.KcUpper && row.BbLower > row.KcLower)
                {
                    squeezeCount++;
                }
                else
                {
                    squeezeCount = 0;
                }

                double vwap = row.Vwap;
                if (double.IsNaN(vwap) || vwap <= 0) continue;

                double ofiSig = double.IsNaN(row.OfiSignal) ? 0.0 : row.OfiSignal;
                double rsi = double.IsNaN(row.Rsi14) ? 50.0 : row.Rsi14;
                double bbPctb = double.IsNaN(row.BbPctB) ? 0.5 : row.BbPctB;

                int longScore = 0;
                int shortScore = 0;
                string longTag = "";
                string shortTag = "";

                bool longBreak = _cfg.RequireCrossFromInside
                    ? (price > orHigh && prev.Bar.Close <= orHigh)
                    : price > orHigh;
                bool shortBreak = _cfg.RequireCrossFromInside
                    ? (price < orLow && prev.Bar.Close >= orLow)
                    : price < orLow;

                bool longOfiOk = _cfg.MinOfiForOrb <= 0 || ofiSig >= _cfg.MinOfiForOrb;
                bool shortOfiOk = _cfg.MinOfiForOrb <= 0 || ofiSig <= -_cfg.MinOfiForOrb;

                if (longBreak && longOfiOk && (price - orHigh) / atrVal >= _cfg.MinOrBreakDistanceAtr)
                {
                    longScore += 2;
                    longTag = "ORB";
                }
                if (shortBreak && shortOfiOk && (orLow - price) / atrVal >= _cfg.MinOrBreakDistanceAtr)
                {
                    shortScore += 2;
                    shortTag = "ORB";
                }

                bool vwapLongRev = (price - vwap) / atrVal <= -_cfg.VwapStretchAtr && rsi <= _cfg.RsiOversold && ofiSig > 0;
                bool vwapShortRev = (price - vwap) / atrVal >= _cfg.VwapStretchAtr && rsi >= _cfg.RsiOverbought && ofiSig < 0;
                if (vwapLongRev)
                {
                    longScore += 2;
                    longTag = longTag.Length == 0 ? "VWAP" : $"{longTag}+VWAP";
                }
                if (vwapShortRev)
                {
                    shortScore += 2;
                    shortTag = shortTag.Length == 0 ? "VWAP" : $"{shortTag}+VWAP";
                }

                bool bbLong = bbPctb <= _cfg.BbEntryPctbLow;
                bool bbShort = bbPctb >= _cfg.BbEntryPctbHigh;
                if (bbLong)
                {
                    longScore++;
                    longTag = longTag.Length == 0 ? "BB" : $"{longTag}+BB";
                }
                if (bbShort)
                {
                    shortScore++;
                    shortTag = shortTag.Length == 0 ? "BB" : $"{shortTag}+BB";
                }

                if (squeezeCount >= _cfg.SqueezeBars)
                {
                    if (price > row.Sma20)
                    {
                        longScore++;
                        longTag = longTag.Length == 0 ? "SQZ" : $"{longTag}+SQZ";
                    }
                    if (price < row.Sma20)
                    {
                        shortScore++;
                        shortTag = shortTag.Length == 0 ? "SQZ" : $"{shortTag}+SQZ";
                    }
                }

                if (price > vwap) longScore++;
                if (price < vwap) shortScore++;

                if (!double.IsNaN(row.Ema9) && !double.IsNaN(row.Ema21))
                {
                    if (row.Ema9 > row.Ema21) longScore++;
                    if (row.Ema9 < row.Ema21) shortScore++;
                }

                bool htfAllowsLong = !_cfg.RequireHtfBias || htfBias != "STRONG_BEAR";
                bool htfAllowsShort = !_cfg.RequireHtfBias || htfBias != "STRONG_BULL";

                bool canLong = _cfg.AllowLong && htfAllowsLong && longEntries < _cfg.MaxEntriesPerDirectionPerDay && longScore >= _cfg.MinEntryScore;
                bool canShort = _cfg.AllowShort && htfAllowsShort && shortEntries < _cfg.MaxEntriesPerDirectionPerDay && shortScore >= _cfg.MinEntryScore;

                if (canLong && (!canShort || longScore >= shortScore))
                {
                    var sig = BuildSignal(i, triggerBars, TradeSide.Long, atrVal, longTag.Length == 0 ? "HYBRID_LONG" : $"HYBRID_LONG_{longTag}", day.EndIdx, orHigh, orLow, longBreak);
                    if (sig != null)
                    {
                        signals.Add(sig);
                        longEntries++;
                        continue;
                    }
                }

                if (canShort)
                {
                    var sig = BuildSignal(i, triggerBars, TradeSide.Short, atrVal, shortTag.Length == 0 ? "HYBRID_SHORT" : $"HYBRID_SHORT_{shortTag}", day.EndIdx, orHigh, orLow, shortBreak);
                    if (sig != null)
                    {
                        signals.Add(sig);
                        shortEntries++;
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
        double atrVal,
        string subStrategy,
        int dayEndIdxExclusive,
        double orHigh,
        double orLow,
        bool isOrBreak)
    {
        int entryIndex = i;
        double entryPrice = bars[i].Bar.Close;
        DateTime entryTs = bars[i].Bar.Timestamp;

        if (_cfg.UseNextBarOpenEntry)
        {
            if (i + 1 >= bars.Length || i + 1 >= dayEndIdxExclusive) return null;
            entryIndex = i + 1;
            entryPrice = bars[entryIndex].Bar.Open;
            entryTs = bars[entryIndex].Bar.Timestamp;
        }

        double stopPrice;
        if (isOrBreak)
        {
            stopPrice = side == TradeSide.Long ? orLow : orHigh;
        }
        else
        {
            double stopDist = _cfg.HardStopR * atrVal;
            stopPrice = side == TradeSide.Long ? entryPrice - stopDist : entryPrice + stopDist;
        }

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

        if (orEndIdx < 0) return (double.NaN, double.NaN, -1);
        if (orHigh == double.MinValue || orLow == double.MaxValue) return (double.NaN, double.NaN, -1);

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
            if (idx < 1) continue;  // need at least 2 bars for slope
            var last = bars[idx];
            var prev = bars[idx - 1];
            int slope = last.Ema21 > prev.Ema21 ? 1 : -1;
            if (!double.IsNaN(last.Rsi14))
            {
                if (last.Rsi14 > 70) slope++;
                else if (last.Rsi14 < 30) slope--;
            }
            scores.Add(slope);
        }

        if (scores.Count == 0) return "NEUTRAL";
        double avg = scores.Average();
        if (avg >= 1.0) return "STRONG_BULL";
        if (avg <= -1.0) return "STRONG_BEAR";
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
        private static readonly TimeZoneInfo _et = ResolveEasternTimeZone();

        private static TimeZoneInfo ResolveEasternTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
            catch { }
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
            catch { }
            return TimeZoneInfo.Utc;
        }

        public static DateTime ToEt(DateTime ts)
        {
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



