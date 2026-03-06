using Harvester.App.Backtest.Engine;

namespace Harvester.App.Strategy;

// ── Phase 2: Market regime classification ────────────────────────────────

/// <summary>
/// Market regime classification used to dynamically adjust signal thresholds.
/// </summary>
public enum MarketRegime
{
    /// <summary>ADX >= 25, normal volatility — standard parameters.</summary>
    Trending,

    /// <summary>ADX &lt; 20, low BB bandwidth — tighter stops, higher score requirement.</summary>
    Ranging,

    /// <summary>ATR ratio > 1.4 or BB bandwidth > 0.06 — wider stops, require stronger signals.</summary>
    Volatile,

    /// <summary>Insufficient data to classify — use default parameters.</summary>
    Unknown
}

/// <summary>
/// Signal engine implementing V11-style composite scoring with regime filters.
///
/// NaN policy: indicators using NaN follow a consistent "no-data = no-contribution" rule.
///   - Filters (ATR, ADX, price): NaN causes rejection or bypass — never contributes a false pass.
///   - Scorers (RVOL, VWAP/RSI, BB, Squeeze, OFI): NaN means the indicator does not add to score.
///   - All NaN comparisons use explicit double.IsNaN guards for clarity.
/// </summary>
public sealed class V3LiveSignalEngine : ILiveSignalEngine
{
    /// <summary>Per-symbol squeeze state for tracking bar count and release transitions.</summary>
    private readonly Dictionary<string, SqueezeState> _squeezeBySymbol = new(StringComparer.OrdinalIgnoreCase);

    public V3LiveSignalDecision Evaluate(V3LiveFeatureSnapshot f, V3LiveConfig cfg, string symbol = "")
    {
        if (!f.IsReady)
        {
            return new V3LiveSignalDecision(false, null, "NONE", "features-not-ready", MarketRegime.Unknown);
        }

        if (double.IsNaN(f.Atr14) || f.Atr14 <= 0)
        {
            return new V3LiveSignalDecision(false, null, "NONE", "atr-invalid", MarketRegime.Unknown);
        }

        // ── Phase 2: Classify market regime ─────────────────────────────
        var regime = ClassifyRegime(f);

        // ── Phase 2: Time-of-day score adjustment ───────────────────────
        var todBonus = ComputeTimeOfDayBonus(f.TimestampUtc, cfg);

        var longReasons = new List<string>();
        var shortReasons = new List<string>();
        var longScore = 0;
        var shortScore = 0;

        var priceValid = f.Price >= cfg.MinPrice && f.Price <= cfg.MaxPrice;
        if (!priceValid)
            return new V3LiveSignalDecision(false, null, "NONE", "price-out-of-range", regime);

        var adxValid = double.IsNaN(f.Adx14) || (f.Adx14 >= cfg.AdxMin && f.Adx14 <= cfg.AdxMax);
        if (!adxValid)
            return new V3LiveSignalDecision(false, null, "NONE", "adx-out-of-range", regime);

        if (!double.IsNaN(f.Rvol) && f.Rvol >= cfg.RvolMin)
        {
            longScore++;
            shortScore++;
            longReasons.Add("RVOL");
            shortReasons.Add("RVOL");
        }

        if (f.VolAccel >= cfg.VolAccelMin)
        {
            longScore++;
            shortScore++;
            longReasons.Add("VOLACC");
            shortReasons.Add("VOLACC");
        }

        if (f.L2.HasDepth)
        {
            var liquidity = Math.Min(f.L2.BidDepthN, f.L2.AskDepthN) / 100.0;
            if (liquidity >= cfg.L2LiquidityMin)
            {
                longScore++;
                shortScore++;
                longReasons.Add("LIQ");
                shortReasons.Add("LIQ");
            }
        }

        // Mean-reversion setup components (NaN-safe: no score when indicator missing)
        var vwapLong = !double.IsNaN(f.DistFromVwapAtr) && !double.IsNaN(f.Rsi14)
            && f.DistFromVwapAtr <= -cfg.VwapDeviationAtr && f.Rsi14 <= cfg.RsiOversold;
        var vwapShort = !double.IsNaN(f.DistFromVwapAtr) && !double.IsNaN(f.Rsi14)
            && f.DistFromVwapAtr >= cfg.VwapDeviationAtr && f.Rsi14 >= cfg.RsiOverbought;
        if (vwapLong)
        {
            longScore += 2;
            longReasons.Add("VWAP");
        }
        if (vwapShort)
        {
            shortScore += 2;
            shortReasons.Add("VWAP");
        }

        var bbLong = !double.IsNaN(f.BbPctB) && f.BbPctB <= cfg.BbEntryPctbLow;
        var bbShort = !double.IsNaN(f.BbPctB) && f.BbPctB >= cfg.BbEntryPctbHigh;
        if (bbLong)
        {
            longScore += 2;
            longReasons.Add("BB");
        }
        if (bbShort)
        {
            shortScore += 2;
            shortReasons.Add("BB");
        }

        var squeezeSignal = EvaluateSqueezeBreakout(f, cfg, symbol);
        if (squeezeSignal.HasLong)
        {
            longScore += 2;
            longReasons.Add("SQUEEZE");
        }
        if (squeezeSignal.HasShort)
        {
            shortScore += 2;
            shortReasons.Add("SQUEEZE");
        }

        if (f.OfiSignal > 0)
        {
            longScore++;
            longReasons.Add("OFI");
        }
        if (f.OfiSignal < 0)
        {
            shortScore++;
            shortReasons.Add("OFI");
        }

        // ── Phase 2: Apply time-of-day bonus/penalty ────────────────────
        if (todBonus != 0)
        {
            longScore += todBonus;
            shortScore += todBonus;
            var todLabel = todBonus > 0 ? $"TOD+{todBonus}" : $"TOD{todBonus}";
            longReasons.Add(todLabel);
            shortReasons.Add(todLabel);
        }

        // ── Phase 2: Regime-adjusted minimum score ──────────────────────
        // Ranging markets require higher score to compensate for lower edge.
        // Volatile markets also require higher score to avoid false breakouts.
        var effectiveMinScore = regime switch
        {
            MarketRegime.Ranging  => cfg.MinScore + cfg.RegimeRangingScoreBoost,
            MarketRegime.Volatile => cfg.MinScore + cfg.RegimeVolatileScoreBoost,
            _                     => cfg.MinScore
        };

        var longValid = cfg.AllowLong && longScore >= effectiveMinScore;
        var shortValid = cfg.AllowShort && shortScore >= effectiveMinScore;

        var regimeTag = regime != MarketRegime.Unknown ? $"|{regime}" : "";

        if (longValid && !shortValid)
        {
            return new V3LiveSignalDecision(true, TradeSide.Long, $"V11[{longScore}]::{string.Join("+", longReasons)}{regimeTag}", string.Empty, regime);
        }

        if (shortValid && !longValid)
        {
            return new V3LiveSignalDecision(true, TradeSide.Short, $"V11[{shortScore}]::{string.Join("+", shortReasons)}{regimeTag}", string.Empty, regime);
        }

        if (longValid && shortValid)
        {
            if (Math.Abs(f.OfiSignal) >= cfg.OfiTiebreakerThreshold)
            {
                var side = f.OfiSignal >= 0 ? TradeSide.Long : TradeSide.Short;
                var setup = side == TradeSide.Long
                    ? $"V11[{longScore}]::{string.Join("+", longReasons)}{regimeTag}"
                    : $"V11[{shortScore}]::{string.Join("+", shortReasons)}{regimeTag}";
                return new V3LiveSignalDecision(true, side, setup, "resolved-by-ofi", regime);
            }

            return new V3LiveSignalDecision(false, null, "NONE", "long-short-conflict", regime);
        }

        return new V3LiveSignalDecision(false, null, "NONE", $"score-below-min:{Math.Max(longScore, shortScore)}/{effectiveMinScore}", regime);
    }

    /// <summary>
    /// Track squeeze state and detect the RELEASE transition (BB exits KC envelope).
    /// Matching backtest behavior: accumulate squeeze bar count, signal only on first bar
    /// after squeeze ends if count >= MinSqueezeBarCount.
    /// </summary>
    private (bool HasLong, bool HasShort) EvaluateSqueezeBreakout(V3LiveFeatureSnapshot f, V3LiveConfig cfg, string symbol)
    {
        var key = string.IsNullOrWhiteSpace(symbol) ? "_default" : symbol.Trim().ToUpperInvariant();
        if (!_squeezeBySymbol.TryGetValue(key, out var state))
        {
            state = new SqueezeState();
            _squeezeBySymbol[key] = state;
        }

        if (f.SqueezeOn)
        {
            // Currently in squeeze — increment bar count
            state.ConsecutiveSqueezeCount++;
            state.WasSqueezing = true;
            return (false, false);
        }

        // Squeeze is OFF now
        if (state.WasSqueezing && state.ConsecutiveSqueezeCount >= cfg.MinSqueezeBarCount)
        {
            // Squeeze just released after sufficient buildup — check breakout direction
            state.WasSqueezing = false;
            state.ConsecutiveSqueezeCount = 0;

            if (!double.IsNaN(f.KcMid) && f.KcMid > 0)
            {
                if (f.Price > f.KcMid)
                    return (true, false); // Bullish breakout
                if (f.Price < f.KcMid)
                    return (false, true); // Bearish breakout
            }
        }
        else
        {
            // Not in squeeze and wasn't squeezing (or not enough bars)
            state.WasSqueezing = false;
            state.ConsecutiveSqueezeCount = 0;
        }

        return (false, false);
    }

    /// <summary>Reset all per-symbol squeeze tracking state (call on session reset).</summary>
    public void ResetState()
    {
        _squeezeBySymbol.Clear();
    }

    // ── Phase 2: Regime classification ──────────────────────────────────

    /// <summary>
    /// Classify current market regime using ADX, BB Bandwidth, and ATR ratio.
    /// <list type="bullet">
    ///   <item><b>Trending:</b> ADX >= 25, ATR ratio &lt;= 1.4</item>
    ///   <item><b>Ranging:</b> ADX &lt; 20, BB bandwidth &lt; 0.03</item>
    ///   <item><b>Volatile:</b> ATR ratio > 1.4 OR BB bandwidth > 0.06</item>
    ///   <item><b>Unknown:</b> Insufficient data or ambiguous conditions</item>
    /// </list>
    /// </summary>
    internal static MarketRegime ClassifyRegime(V3LiveFeatureSnapshot f)
    {
        var hasAdx = !double.IsNaN(f.Adx14);
        var hasBbBw = !double.IsNaN(f.BbBandwidth);
        var hasAtrR = !double.IsNaN(f.AtrRatio);

        if (!hasAdx && !hasBbBw && !hasAtrR)
            return MarketRegime.Unknown;

        // Volatile check first — high ATR ratio or extreme BB width dominates
        if ((hasAtrR && f.AtrRatio > 1.4) || (hasBbBw && f.BbBandwidth > 0.06))
            return MarketRegime.Volatile;

        // Ranging — low directional movement AND tight bands
        if (hasAdx && f.Adx14 < 20.0 && (!hasBbBw || f.BbBandwidth < 0.03))
            return MarketRegime.Ranging;

        // Trending — strong directional movement
        if (hasAdx && f.Adx14 >= 25.0)
            return MarketRegime.Trending;

        // Default: if ADX is between 20-25 with normal volatility, treat as trending (benefit of the doubt)
        if (hasAdx && f.Adx14 >= 20.0)
            return MarketRegime.Trending;

        return MarketRegime.Unknown;
    }

    // ── Phase 2: Time-of-day score adjustment ───────────────────────────

    /// <summary>
    /// Compute a score bonus/penalty based on time of day (UTC).
    /// US market open = 13:30 UTC. First 90 min (13:30-15:00) = best edge.
    /// Midday (15:00-18:00) = neutral. Late session (18:00-20:00) = weaker edge.
    /// </summary>
    internal static int ComputeTimeOfDayBonus(DateTime timestampUtc, V3LiveConfig cfg)
    {
        if (!cfg.EnableTimeOfDayFilter)
            return 0;

        var hour = timestampUtc.Hour;
        var minute = timestampUtc.Minute;
        var minuteOfDay = hour * 60 + minute;

        // 13:30-15:00 UTC = first 90 min of US session (strongest edge)
        if (minuteOfDay >= 810 && minuteOfDay < 900)
            return cfg.TodOpeningBonus;

        // 15:00-18:00 UTC = midday session (neutral)
        if (minuteOfDay >= 900 && minuteOfDay < 1080)
            return 0;

        // 18:00-20:00 UTC = late session (weaker edge, apply penalty)
        if (minuteOfDay >= 1080 && minuteOfDay < 1200)
            return cfg.TodAfternoonPenalty;

        // Outside regular session — no adjustment
        return 0;
    }

    private sealed class SqueezeState
    {
        public int ConsecutiveSqueezeCount { get; set; }
        public bool WasSqueezing { get; set; }
    }
}

public sealed record V3LiveSignalDecision(
    bool HasSignal,
    TradeSide? Side,
    string Setup,
    string Reason,
    MarketRegime Regime = MarketRegime.Unknown);
