using Harvester.App.Backtest.Engine;

namespace Harvester.App.Strategy;

/// <summary>
/// Signal engine evaluating three sub-strategies: VWAP Reversion, BB Bounce, Keltner Squeeze.
/// Refactored for backtest-live parity (H-02, BB confirmation, squeeze semantics).
/// </summary>
public sealed class V3LiveSignalEngine
{
    /// <summary>Per-symbol squeeze state for tracking bar count and release transitions.</summary>
    private readonly Dictionary<string, SqueezeState> _squeezeBySymbol = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Minimum consecutive squeeze bars before a breakout signal fires (matching backtest).</summary>
    private const int MinSqueezeBarCount = 10;

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

        // V3a: VWAP Reversion (strict inequality matching backtest: < not <=)
        var vwapLong = f.DistFromVwapAtr < -cfg.VwapStretchAtr && f.Rsi14 < cfg.RsiOversold && f.OfiSignal > 0;
        var vwapShort = f.DistFromVwapAtr > cfg.VwapStretchAtr && f.Rsi14 > cfg.RsiOverbought && f.OfiSignal < 0;

        if (vwapLong) longReasons.Add("VWAP");
        if (vwapShort) shortReasons.Add("VWAP");

        // V3b: BB Bounce (restored backtest confirmation: stochK threshold)
        var bbLong = f.BbPctB < cfg.BbEntryPctbLow
            && !double.IsNaN(f.StochK) && !double.IsNaN(f.StochD)
            && f.StochK < 25.0 && f.StochK > f.StochD;
        var bbShort = f.BbPctB > cfg.BbEntryPctbHigh
            && !double.IsNaN(f.StochK) && !double.IsNaN(f.StochD)
            && f.StochK > 75.0 && f.StochK < f.StochD;

        if (bbLong) longReasons.Add("BB");
        if (bbShort) shortReasons.Add("BB");

        // V3c: Keltner Squeeze Breakout (fixed: fires on RELEASE, not during squeeze)
        var squeezeSignal = EvaluateSqueezeBreakout(f, cfg, symbol);
        if (squeezeSignal.HasLong) longReasons.Add("SQUEEZE");
        if (squeezeSignal.HasShort) shortReasons.Add("SQUEEZE");

        // RVOL filter
        var longValid = longReasons.Count > 0 && (!double.IsNaN(f.Rvol) ? f.Rvol >= 0.5 : true);
        var shortValid = shortReasons.Count > 0 && (!double.IsNaN(f.Rvol) ? f.Rvol >= 0.5 : true);

        if (longValid && !shortValid)
        {
            return new V3LiveSignalDecision(true, TradeSide.Long, string.Join("+", longReasons), string.Empty);
        }

        if (shortValid && !longValid)
        {
            return new V3LiveSignalDecision(true, TradeSide.Short, string.Join("+", shortReasons), string.Empty);
        }

        if (longValid && shortValid)
        {
            if (Math.Abs(f.OfiSignal) >= OfiTiebreakerThreshold)
            {
                var side = f.OfiSignal >= 0 ? TradeSide.Long : TradeSide.Short;
                var setup = side == TradeSide.Long
                    ? string.Join("+", longReasons)
                    : string.Join("+", shortReasons);
                return new V3LiveSignalDecision(true, side, setup, "resolved-by-ofi");
            }

            return new V3LiveSignalDecision(false, null, "NONE", "long-short-conflict");
        }

        return new V3LiveSignalDecision(false, null, "NONE", "no-v3-setup");
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
