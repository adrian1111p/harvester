using Harvester.App.Backtest.Engine;

namespace Harvester.App.Strategy;

/// <summary>
/// Signal engine implementing V11-style composite scoring with regime filters.
/// </summary>
public sealed class V3LiveSignalEngine
{
    /// <summary>Per-symbol squeeze state for tracking bar count and release transitions.</summary>
    private readonly Dictionary<string, SqueezeState> _squeezeBySymbol = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Minimum consecutive squeeze bars before a breakout signal fires.</summary>
    private const int MinSqueezeBarCount = 8;

    /// <summary>OFI tiebreaker threshold for resolving long/short conflicts.</summary>
    private const double OfiTiebreakerThreshold = 0.05;

    public V3LiveSignalDecision Evaluate(V3LiveFeatureSnapshot f, V3LiveConfig cfg, string symbol = "")
    {
        if (!f.IsReady)
        {
            return new V3LiveSignalDecision(false, null, "NONE", "features-not-ready");
        }

        if (double.IsNaN(f.Atr14) || f.Atr14 <= 0)
        {
            return new V3LiveSignalDecision(false, null, "NONE", "atr-invalid");
        }

        var longReasons = new List<string>();
        var shortReasons = new List<string>();
        var longScore = 0;
        var shortScore = 0;

        var priceValid = f.Price >= cfg.MinPrice && f.Price <= cfg.MaxPrice;
        if (!priceValid)
            return new V3LiveSignalDecision(false, null, "NONE", "price-out-of-range");

        var adxValid = double.IsNaN(f.Adx14) || (f.Adx14 >= cfg.AdxMin && f.Adx14 <= cfg.AdxMax);
        if (!adxValid)
            return new V3LiveSignalDecision(false, null, "NONE", "adx-out-of-range");

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

        // Mean-reversion setup components
        var vwapLong = f.DistFromVwapAtr <= -cfg.VwapDeviationAtr && f.Rsi14 <= cfg.RsiOversold;
        var vwapShort = f.DistFromVwapAtr >= cfg.VwapDeviationAtr && f.Rsi14 >= cfg.RsiOverbought;
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

        var bbLong = f.BbPctB <= cfg.BbEntryPctbLow;
        var bbShort = f.BbPctB >= cfg.BbEntryPctbHigh;
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

        var longValid = cfg.AllowLong && longScore >= cfg.MinScore;
        var shortValid = cfg.AllowShort && shortScore >= cfg.MinScore;

        if (longValid && !shortValid)
        {
            return new V3LiveSignalDecision(true, TradeSide.Long, $"V11[{longScore}]::{string.Join("+", longReasons)}", string.Empty);
        }

        if (shortValid && !longValid)
        {
            return new V3LiveSignalDecision(true, TradeSide.Short, $"V11[{shortScore}]::{string.Join("+", shortReasons)}", string.Empty);
        }

        if (longValid && shortValid)
        {
            if (Math.Abs(f.OfiSignal) >= OfiTiebreakerThreshold)
            {
                var side = f.OfiSignal >= 0 ? TradeSide.Long : TradeSide.Short;
                var setup = side == TradeSide.Long
                    ? $"V11[{longScore}]::{string.Join("+", longReasons)}"
                    : $"V11[{shortScore}]::{string.Join("+", shortReasons)}";
                return new V3LiveSignalDecision(true, side, setup, "resolved-by-ofi");
            }

            return new V3LiveSignalDecision(false, null, "NONE", "long-short-conflict");
        }

        return new V3LiveSignalDecision(false, null, "NONE", $"score-below-min:{Math.Max(longScore, shortScore)}/{cfg.MinScore}");
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
        if (state.WasSqueezing && state.ConsecutiveSqueezeCount >= MinSqueezeBarCount)
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
    string Reason);
