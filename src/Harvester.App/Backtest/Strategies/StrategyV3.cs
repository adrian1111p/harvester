using Harvester.App.Backtest.Engine;
using Harvester.App.Backtest.Indicators;

namespace Harvester.App.Backtest.Strategies;

/// <summary>V3 config: calibrated for $10-$50 stocks with L2 proxy.</summary>
public sealed class V3Config
{
    public double RiskPerTradeDollars { get; set; } = 50.0;
    public double AccountSize { get; set; } = 25_000.0;
    public double MinPrice { get; set; } = 8.0;
    public double MaxPrice { get; set; } = 50.0;

    // V3a: VWAP Reversion
    public double VwapStretchAtr { get; set; } = 1.5;
    public bool VwapEnabled { get; set; } = true;

    // V3b: BB Bounce
    public double BbEntryPctbLow { get; set; } = 0.05;
    public double BbEntryPctbHigh { get; set; } = 0.95;
    public bool BbEnabled { get; set; } = true;

    // V3c: Keltner Squeeze
    public bool SqueezeEnabled { get; set; } = true;
    public int SqueezeBars { get; set; } = 10;

    // L2 Proxy Filters
    public double L2LiquidityMin { get; set; } = 25.0;
    public double SpreadZMax { get; set; } = 2.0;
    public double VolAccelMin { get; set; } = -0.3;
    public double RvolMin { get; set; } = 0.5;

    // Confirmations
    public double RsiOversold { get; set; } = 35.0;
    public double RsiOverbought { get; set; } = 65.0;
    public bool RequireVolumeConfirm { get; set; } = true;

    // Exit rules (wider for cheap stocks)
    public double HardStopR { get; set; } = 1.5;
    public double TrailR { get; set; } = 1.0;
    public double GivebackPct { get; set; } = 0.60;
    public double Tp1R { get; set; } = 1.0;
    public double Tp1ScalePct { get; set; } = 0.50;
    public double Tp2R { get; set; } = 2.5;
    public double BreakevenR { get; set; } = 0.8;
    public int MaxHoldBars { get; set; } = 90;
    public double SlippageCents { get; set; } = 1.5;
    public double CommissionPerShare { get; set; } = 0.005;

    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;
}

/// <summary>
/// V3 "VWAP Reversion + BB Bounce + Keltner Squeeze + L2 Proxy".
/// Three sub-strategies targeting $10–$50 stocks.
/// </summary>
public sealed class StrategyV3 : BacktestStrategyBase
{
    private readonly V3Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public StrategyV3(V3Config? cfg = null)
    {
        _cfg = cfg ?? new V3Config();
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.5,
            UseFixedGivebackUsdCap = true,
            GivebackUsdCap = 30.0,
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
        string htfBias = HtfGuard(bars1h, bars1d);
        int squeezeCount = 0;

        for (int i = 50; i < triggerBars.Length; i++)
        {
            var row = triggerBars[i];
            double atrVal = row.Atr14;
            if (double.IsNaN(atrVal) || atrVal <= 0) continue;

            double price = row.Bar.Close;

            // Price filter
            if (price < _cfg.MinPrice || price > _cfg.MaxPrice) continue;

            // L2 proxy filters
            double l2Liq = double.IsNaN(row.L2Liquidity) ? 50.0 : row.L2Liquidity;
            if (l2Liq < _cfg.L2LiquidityMin) continue;

            double spreadZ = double.IsNaN(row.SpreadZ) ? 0.0 : row.SpreadZ;
            if (spreadZ > _cfg.SpreadZMax) continue;

            if (!double.IsNaN(row.Rvol) && row.Rvol < _cfg.RvolMin) continue;

            double ofiSig = double.IsNaN(row.OfiSignal) ? 0.0 : row.OfiSignal;

            // Read indicators
            double vwapVal = row.Vwap;
            double bbPctb = double.IsNaN(row.BbPctB) ? 0.5 : row.BbPctB;
            double rsiVal = double.IsNaN(row.Rsi14) ? 50.0 : row.Rsi14;
            double stochK = double.IsNaN(row.StochK) ? 50.0 : row.StochK;

            // Track squeeze (BB inside KC)
            if (!double.IsNaN(row.BbUpper) && !double.IsNaN(row.KcUpper) &&
                row.BbUpper < row.KcUpper && row.BbLower > row.KcLower)
            {
                squeezeCount++;
            }
            else
            {
                bool wasSqueezed = squeezeCount >= _cfg.SqueezeBars;
                squeezeCount = 0;

                // V3c: Squeeze breakout
                if (_cfg.SqueezeEnabled && wasSqueezed && !double.IsNaN(row.KcMid))
                {
                    if (price > row.KcMid && _cfg.AllowLong && htfBias != "STRONG_BEAR")
                    {
                        var sig = MakeSignal(i, triggerBars, TradeSide.Long, atrVal, "SQUEEZE");
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                    else if (price < row.KcMid && _cfg.AllowShort && htfBias != "STRONG_BULL")
                    {
                        var sig = MakeSignal(i, triggerBars, TradeSide.Short, atrVal, "SQUEEZE");
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                }
            }

            // V3a: VWAP Reversion
            if (_cfg.VwapEnabled && !double.IsNaN(vwapVal) && vwapVal > 0)
            {
                double distFromVwap = (price - vwapVal) / atrVal;

                if (distFromVwap < -_cfg.VwapStretchAtr && _cfg.AllowLong && htfBias != "STRONG_BEAR")
                {
                    if (rsiVal < _cfg.RsiOversold && ofiSig > 0)
                    {
                        var sig = MakeSignal(i, triggerBars, TradeSide.Long, atrVal, "VWAP");
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                }

                if (distFromVwap > _cfg.VwapStretchAtr && _cfg.AllowShort && htfBias != "STRONG_BULL")
                {
                    if (rsiVal > _cfg.RsiOverbought && ofiSig < 0)
                    {
                        var sig = MakeSignal(i, triggerBars, TradeSide.Short, atrVal, "VWAP");
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                }
            }

            // V3b: BB Bounce
            if (_cfg.BbEnabled)
            {
                if (bbPctb < _cfg.BbEntryPctbLow && _cfg.AllowLong && htfBias != "STRONG_BEAR")
                {
                    bool confirm = price > row.Bar.Open
                        || (stochK < 25 && !double.IsNaN(row.StochD) && stochK > row.StochD);
                    if (confirm)
                    {
                        var sig = MakeSignal(i, triggerBars, TradeSide.Long, atrVal, "BB");
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                }

                if (bbPctb > _cfg.BbEntryPctbHigh && _cfg.AllowShort && htfBias != "STRONG_BULL")
                {
                    bool confirm = price < row.Bar.Open
                        || (stochK > 75 && !double.IsNaN(row.StochD) && stochK < row.StochD);
                    if (confirm)
                    {
                        var sig = MakeSignal(i, triggerBars, TradeSide.Short, atrVal, "BB");
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                }
            }
        }

        return signals;
    }

    private BacktestSignal? MakeSignal(int i, EnrichedBar[] bars, TradeSide side, double atrVal, string subStrategy)
    {
        double price = bars[i].Bar.Close;
        double stopDist = _cfg.HardStopR * atrVal;
        double stopPrice = side == TradeSide.Long ? price - stopDist : price + stopDist;
        double riskPerShare = stopDist;
        if (riskPerShare <= 0) return null;

        int posSize = Math.Max(1, (int)(_cfg.RiskPerTradeDollars / riskPerShare));

        return new BacktestSignal(
            BarIndex: i,
            Timestamp: bars[i].Bar.Timestamp,
            Side: side,
            EntryPrice: price,
            StopPrice: stopPrice,
            RiskPerShare: riskPerShare,
            PositionSize: posSize,
            AtrValue: atrVal,
            HtfTrend: HtfBias.Neutral,
            MtfMomentum: "N/A",
            SubStrategy: subStrategy);
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);

    private static string HtfGuard(EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        var scores = new List<int>();
        foreach (var bars in new[] { bars1h, bars1d })
        {
            if (bars == null || bars.Length < 30) continue;
            var last = bars[^1];
            var prev = bars[^2];
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
        if (avg >= 2) return "STRONG_BULL";
        if (avg <= -2) return "STRONG_BEAR";
        return "NEUTRAL";
    }
}



