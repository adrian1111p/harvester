using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

/// <summary>
/// Conduct Strategy V3 — timestamp-aligned multi-timeframe trend/pullback strategy.
/// Improvements over V2:
/// - Removes HTF/MTF lookahead by aligning context bars to signal timestamp
/// - Uses shared ExitEngine (single source of truth for exits)
/// - Supports next-bar-open entry and capped position sizing
/// - Adds optional strict missing-data policy and MTF alignment gating
/// - Entry time windows and price filter for realistic market-hours-only trading
/// - Optional VWAP reversion and BB bounce alternate entries
/// </summary>
public sealed class ConductStrategyV3 : BacktestStrategyBase
{
    private readonly StrategyConfig _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public ConductStrategyV3(StrategyConfig? cfg = null)
    {
        _cfg = cfg ?? new StrategyConfig();
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.0,
            UseFixedGivebackUsdCap = !_cfg.UseNotionalGivebackCap && _cfg.GivebackUsdCap > 0,
            UseNotionalGivebackCap = _cfg.UseNotionalGivebackCap,
            GivebackPctOfNotional = _cfg.GivebackPctOfNotional,
            GivebackUsdCap = _cfg.GivebackUsdCap,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            DeductCommission = true,
            Tp1TightenToBe = true,
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
        if (triggerBars.Length < 60)
            return signals;

        int lastSignalBar = -10_000;

        for (int i = 50; i < triggerBars.Length; i++)
        {
            if (i - lastSignalBar < Math.Max(0, _cfg.CooldownBars))
                continue;

            var row = triggerBars[i];
            var prev = triggerBars[i - 1];
            var ts = row.Bar.Timestamp;

            // ── Time & price filters ──
            int minuteEt = TradingTime.GetMinuteOfDayEt(ts);
            if (minuteEt < _cfg.MarketOpenMinute + _cfg.SkipFirstNMinutes) continue;
            if (!InEntryWindow(minuteEt)) continue;

            double price = row.Bar.Close;
            if (price < _cfg.MinPrice || price > _cfg.MaxPrice) continue;

            int entryIndex = _cfg.UseNextBarOpenEntry ? i + 1 : i;
            if (entryIndex >= triggerBars.Length)
                break;

            double atrVal = row.Atr14;
            if (double.IsNaN(atrVal) || atrVal <= 0)
                continue;

            var htfBias = ComputeHtfBiasAt(ts, bars1h, bars1d);
            var candidates = GetCandidateSides(row, prev, htfBias);

            // ── Alternate entries: VWAP reversion ──
            if (_cfg.VwapReversionEnabled && candidates.Count == 0)
            {
                double vwapVal = row.Vwap;
                if (!double.IsNaN(vwapVal) && vwapVal > 0)
                {
                    double distFromVwap = (price - vwapVal) / atrVal;
                    if (distFromVwap < -_cfg.VwapStretchAtr && htfBias is HtfBias.Bull or HtfBias.Neutral)
                        candidates.Add(TradeSide.Long);
                    else if (distFromVwap > _cfg.VwapStretchAtr && htfBias is HtfBias.Bear or HtfBias.Neutral)
                        candidates.Add(TradeSide.Short);
                }
            }

            // ── Alternate entries: BB bounce ──
            if (_cfg.BbBounceEnabled && candidates.Count == 0)
            {
                double bbPctb = double.IsNaN(row.BbPctB) ? 0.5 : row.BbPctB;
                if (bbPctb < _cfg.BbEntryPctbLow && htfBias is HtfBias.Bull or HtfBias.Neutral)
                    candidates.Add(TradeSide.Long);
                else if (bbPctb > _cfg.BbEntryPctbHigh && htfBias is HtfBias.Bear or HtfBias.Neutral)
                    candidates.Add(TradeSide.Short);
            }

            foreach (var side in candidates)
            {
                if (!PassesFilters(row, atrVal, side))
                    continue;

                string mtfMomentum = ComputeMtfMomentumAt(ts, bars5m, bars15m, side);
                if (_cfg.RequireMtfAlignment && mtfMomentum != "ALIGNED")
                    continue;

                double entryPrice = _cfg.UseNextBarOpenEntry
                    ? triggerBars[entryIndex].Bar.Open
                    : row.Bar.Close;

                double stopDist = _cfg.HardStopR * atrVal;
                double stopPrice = side == TradeSide.Long
                    ? entryPrice - stopDist
                    : entryPrice + stopDist;
                double riskPerShare = Math.Abs(entryPrice - stopPrice);
                if (riskPerShare < _cfg.MinRiskPerShare)
                    continue;

                int positionSize = ComputePositionSize(entryPrice, riskPerShare);
                if (positionSize <= 0)
                    continue;

                signals.Add(new BacktestSignal(
                    BarIndex: entryIndex,
                    Timestamp: triggerBars[entryIndex].Bar.Timestamp,
                    Side: side,
                    EntryPrice: entryPrice,
                    StopPrice: stopPrice,
                    RiskPerShare: riskPerShare,
                    PositionSize: positionSize,
                    AtrValue: atrVal,
                    HtfTrend: htfBias,
                    MtfMomentum: mtfMomentum));

                lastSignalBar = entryIndex;
            }
        }

        return signals;
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);

    private int ComputePositionSize(double entryPrice, double riskPerShare)
    {
        int qtyByRisk = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));

        double maxNotional = Math.Max(0.0, _cfg.AccountSize * _cfg.MaxPositionNotionalPctOfAccount);
        int qtyByNotional = maxNotional > 0 && entryPrice > 0
            ? Math.Max(1, (int)(maxNotional / entryPrice))
            : _cfg.MaxShares;

        int qty = Math.Min(qtyByRisk, qtyByNotional);
        qty = Math.Min(qty, _cfg.MaxShares);
        return Math.Max(1, qty);
    }

    private bool PassesFilters(EnrichedBar row, double atrVal, TradeSide side)
    {
        if (_cfg.StrictMissingDataChecks)
        {
            if (double.IsNaN(row.Rvol) || double.IsNaN(row.Rsi14) || double.IsNaN(row.Sma20))
                return false;
        }

        if (!double.IsNaN(row.Rvol) && row.Rvol < _cfg.RvolMin)
            return false;

        if (!double.IsNaN(row.Rsi14))
        {
            var (rsiLow, rsiHigh) = side == TradeSide.Long
                ? _cfg.RsiLongRange
                : _cfg.RsiShortRange;
            if (row.Rsi14 < rsiLow || row.Rsi14 > rsiHigh)
                return false;
        }
        else if (_cfg.StrictMissingDataChecks)
        {
            return false;
        }

        if (!double.IsNaN(row.Sma20) && atrVal > 0)
        {
            double maDist = (row.Bar.Close - row.Sma20) / atrVal;
            if (side == TradeSide.Long && maDist > _cfg.MaxMaDistAtr)
                return false;
            if (side == TradeSide.Short && maDist < -_cfg.MaxMaDistAtr)
                return false;
        }

        return true;
    }

    private List<TradeSide> GetCandidateSides(EnrichedBar row, EnrichedBar prev, HtfBias htfBias)
    {
        var candidates = new List<TradeSide>(2);

        if (htfBias is HtfBias.Bull or HtfBias.Neutral)
        {
            bool stFlipLong = row.StDirection == 1 && prev.StDirection == -1;
            bool emaPullbackLong = row.Bar.Close >= ResolvePullbackEma(row) && prev.Bar.Close < ResolvePullbackEma(prev);
            bool triggerLong = _cfg.RequireSupertrend ? stFlipLong : (stFlipLong || emaPullbackLong);

            if (triggerLong && row.Bar.Close > row.Ema21)
                candidates.Add(TradeSide.Long);
        }

        if (htfBias is HtfBias.Bear or HtfBias.Neutral)
        {
            bool stFlipShort = row.StDirection == -1 && prev.StDirection == 1;
            bool emaPullbackShort = row.Bar.Close <= ResolvePullbackEma(row) && prev.Bar.Close > ResolvePullbackEma(prev);
            bool triggerShort = _cfg.RequireSupertrend ? stFlipShort : (stFlipShort || emaPullbackShort);

            if (triggerShort && row.Bar.Close < row.Ema21)
                candidates.Add(TradeSide.Short);
        }

        return candidates;
    }

    private double ResolvePullbackEma(EnrichedBar row)
        => _cfg.PullbackEmaPeriod <= 9 ? row.Ema9 : row.Ema21;

    private bool InEntryWindow(int minuteEt)
    {
        foreach (var (start, end) in _cfg.EntryWindows)
        {
            if (minuteEt >= start && minuteEt <= end) return true;
        }
        return false;
    }

    private static class TradingTime
    {
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

    private HtfBias ComputeHtfBiasAt(DateTime ts, EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        var scores = new List<int>();

        foreach (var bars in new[] { bars1h, bars1d })
        {
            if (bars == null || bars.Length < 2)
                continue;

            int idx = FindBarAtOrBefore(bars, ts);
            if (idx < 1)
                continue;

            var last = bars[idx];
            var prev = bars[idx - 1];

            int emaSlope = last.Ema21 > prev.Ema21 ? 1 : -1;

            int diScore = 0;
            if (!double.IsNaN(last.Adx) && last.Adx > _cfg.AdxThreshold)
                diScore = last.PlusDi > last.MinusDi ? 1 : -1;

            int macdScore = 0;
            if (!double.IsNaN(last.MacdHist))
                macdScore = last.MacdHist > 0 ? 1 : -1;

            scores.Add(emaSlope + diScore + macdScore);
        }

        if (scores.Count == 0) return HtfBias.Neutral;

        double avg = scores.Average();
        if (avg >= 1.5) return HtfBias.Bull;
        if (avg <= -1.5) return HtfBias.Bear;
        return HtfBias.Neutral;
    }

    private string ComputeMtfMomentumAt(
        DateTime ts,
        EnrichedBar[]? bars5m,
        EnrichedBar[]? bars15m,
        TradeSide side)
    {
        int alignedCount = 0;
        int total = 0;

        foreach (var bars in new[] { bars5m, bars15m })
        {
            if (bars == null || bars.Length == 0)
                continue;

            int idx = FindBarAtOrBefore(bars, ts);
            if (idx < 0)
                continue;

            var last = bars[idx];
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

            if (macdOk && rsiOk)
                alignedCount++;
        }

        if (total == 0)
            return "CONFLICTING";

        return alignedCount == total ? "ALIGNED" : "CONFLICTING";
    }

    private static int FindBarAtOrBefore(EnrichedBar[] bars, DateTime ts)
    {
        int lo = 0;
        int hi = bars.Length - 1;
        int best = -1;

        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            DateTime midTs = bars[mid].Bar.Timestamp;

            if (midTs <= ts)
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
