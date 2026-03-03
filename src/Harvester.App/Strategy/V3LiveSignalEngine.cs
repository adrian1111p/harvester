using Harvester.App.Backtest.Engine;

namespace Harvester.App.Strategy;

public sealed class V3LiveSignalEngine
{
    public V3LiveSignalDecision Evaluate(V3LiveFeatureSnapshot f, V3LiveConfig cfg)
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

        var vwapLong = f.DistFromVwapAtr <= -cfg.VwapStretchAtr && f.Rsi14 <= cfg.RsiOversold && f.OfiSignal > 0;
        var vwapShort = f.DistFromVwapAtr >= cfg.VwapStretchAtr && f.Rsi14 >= cfg.RsiOverbought && f.OfiSignal < 0;

        if (vwapLong) longReasons.Add("VWAP");
        if (vwapShort) shortReasons.Add("VWAP");

        var bbLong = f.BbPctB <= cfg.BbEntryPctbLow && (!double.IsNaN(f.StochK) && !double.IsNaN(f.StochD) && f.StochK >= f.StochD);
        var bbShort = f.BbPctB >= cfg.BbEntryPctbHigh && (!double.IsNaN(f.StochK) && !double.IsNaN(f.StochD) && f.StochK <= f.StochD);

        if (bbLong) longReasons.Add("BB");
        if (bbShort) shortReasons.Add("BB");

        var sqzLong = f.SqueezeOn && f.Price > f.KcMid && f.OfiSignal > 0;
        var sqzShort = f.SqueezeOn && f.Price < f.KcMid && f.OfiSignal < 0;

        if (sqzLong) longReasons.Add("SQUEEZE");
        if (sqzShort) shortReasons.Add("SQUEEZE");

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
            if (Math.Abs(f.OfiSignal) >= 0.05)
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
}

public sealed record V3LiveSignalDecision(
    bool HasSignal,
    TradeSide? Side,
    string Setup,
    string Reason);
