using Harvester.App.Backtest.Engine;
using Harvester.App.Backtest.Indicators;

namespace Harvester.App.Backtest.Strategies;

/// <summary>
/// V3 config: calibrated for $10-$50 stocks with L2 proxy.
/// "_1" version includes:
/// - Conservative missing-data policy (NaN fails critical filters)
/// - Optional next-bar-open entry modeling
/// - Uses VolAccelMin and RequireVolumeConfirm
/// - Position sizing caps using AccountSize
/// </summary>
public sealed class V3Config_1
{
    public double RiskPerTradeDollars { get; set; } = 50.0;
    public double AccountSize { get; set; } = 25_000.0;

    /// <summary>Cap position notional as % of AccountSize (0..1).</summary>
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.25;

    /// <summary>Absolute hard cap on shares.</summary>
    public int MaxShares { get; set; } = 10_000;

    /// <summary>Minimum risk per share to avoid unrealistic huge sizing when stop is too tight.</summary>
    public double MinRiskPerShare { get; set; } = 0.01;

    public double MinPrice { get; set; } = 8.0;
    public double MaxPrice { get; set; } = 50.0;

    /// <summary>
    /// If true: signal computed on bar i close, but entry assumed at bar i+1 open (more realistic).
    /// If false: entry at bar i close (legacy behavior).
    /// </summary>
    public bool UseNextBarOpenEntry { get; set; } = true;

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

    /// <summary>Minimum volume acceleration (conservative: NaN fails if used).</summary>
    public double VolAccelMin { get; set; } = -0.3;

    /// <summary>Minimum RVOL required when RequireVolumeConfirm=true.</summary>
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

    // Costs
    public double SlippageCents { get; set; } = 1.5;
    public double CommissionPerShare { get; set; } = 0.005;

    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;
}

/// <summary>
/// V3 "VWAP Reversion + BB Bounce + Keltner Squeeze + L2 Proxy".
/// Three sub-strategies targeting $10–$50 stocks.
/// </summary>
public sealed class StrategyV3_1 : BacktestStrategyBase
{
    private readonly V3Config_1 _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;

    public StrategyV3_1(V3Config_1? cfg = null)
    {
        _cfg = cfg ?? new V3Config_1();
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
        int squeezeCount = 0;

        for (int i = 50; i < triggerBars.Length; i++)
        {
            var row = triggerBars[i];
            double atrVal = row.Atr14;
            if (double.IsNaN(atrVal) || atrVal <= 0) continue;

            // Compute HTF bias per-bar (no lookahead)
            string htfBias = HtfGuardAtTime(row.Bar.Timestamp, bars1h, bars1d);

            // Use close for evaluation (signal bar), but entry may be next open.
            double evalPrice = row.Bar.Close;

            // Price filter
            if (evalPrice < _cfg.MinPrice || evalPrice > _cfg.MaxPrice) continue;

            // ---- Conservative missing-data policy for critical filters ----
            if (double.IsNaN(row.L2Liquidity) || row.L2Liquidity < _cfg.L2LiquidityMin) continue;
            if (double.IsNaN(row.SpreadZ) || row.SpreadZ > _cfg.SpreadZMax) continue;

            // Volume confirmation (explicitly used now)
            if (_cfg.RequireVolumeConfirm)
            {
                if (double.IsNaN(row.Rvol) || row.Rvol < _cfg.RvolMin) continue;
                if (double.IsNaN(row.VolAccel) || row.VolAccel < _cfg.VolAccelMin) continue;
            }
            else
            {
                // If volume confirm disabled, still allow but don't "pass" NaNs for RVOL/VolAccel
                if (!double.IsNaN(row.Rvol) && row.Rvol < _cfg.RvolMin) continue;
                if (!double.IsNaN(row.VolAccel) && row.VolAccel < _cfg.VolAccelMin) continue;
            }

            double ofiSig = double.IsNaN(row.OfiSignal) ? 0.0 : row.OfiSignal;

            // Read indicators
            double vwapVal = row.Vwap;
            double bbPctb = double.IsNaN(row.BbPctB) ? 0.5 : row.BbPctB;
            double rsiVal = double.IsNaN(row.Rsi14) ? 50.0 : row.Rsi14;
            double stochK = double.IsNaN(row.StochK) ? 50.0 : row.StochK;

            // ---- Squeeze tracking (BB inside KC) ----
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
                    // Basic breakout direction from KC mid.
                    // NOTE: If you want extra strictness, add magnitude confirmation (e.g., close beyond KC mid by X*ATR).
                    if (evalPrice > row.KcMid && _cfg.AllowLong && htfBias != "STRONG_BEAR")
                    {
                        var sig = MakeSignal(i, triggerBars, TradeSide.Long, atrVal, "SQUEEZE");
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                    else if (evalPrice < row.KcMid && _cfg.AllowShort && htfBias != "STRONG_BULL")
                    {
                        var sig = MakeSignal(i, triggerBars, TradeSide.Short, atrVal, "SQUEEZE");
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                }
            }

            // V3a: VWAP Reversion
            if (_cfg.VwapEnabled && !double.IsNaN(vwapVal) && vwapVal > 0)
            {
                double distFromVwap = (evalPrice - vwapVal) / atrVal;

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
                    bool confirm = evalPrice > row.Bar.Open
                        || (stochK < 25 && !double.IsNaN(row.StochD) && stochK > row.StochD);
                    if (confirm)
                    {
                        var sig = MakeSignal(i, triggerBars, TradeSide.Long, atrVal, "BB");
                        if (sig != null) { signals.Add(sig); continue; }
                    }
                }

                if (bbPctb > _cfg.BbEntryPctbHigh && _cfg.AllowShort && htfBias != "STRONG_BULL")
                {
                    bool confirm = evalPrice < row.Bar.Open
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
        // Entry model: next-bar open (preferred) vs same-bar close (legacy)
        int entryIndex = i;
        double entryPrice = bars[i].Bar.Close;
        DateTime entryTs = bars[i].Bar.Timestamp;

        if (_cfg.UseNextBarOpenEntry)
        {
            if (i + 1 >= bars.Length) return null; // cannot enter beyond available data
            entryIndex = i + 1;
            entryPrice = bars[entryIndex].Bar.Open;
            entryTs = bars[entryIndex].Bar.Timestamp;
        }

        double stopDist = _cfg.HardStopR * atrVal;
        double stopPrice = side == TradeSide.Long ? entryPrice - stopDist : entryPrice + stopDist;

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

        // Notional caps
        double maxNotional = Math.Max(0.0, _cfg.AccountSize * _cfg.MaxPositionNotionalPctOfAccount);
        int qtyByNotional = maxNotional > 0 && entryPrice > 0 ? Math.Max(1, (int)(maxNotional / entryPrice)) : _cfg.MaxShares;

        int qty = Math.Min(qtyByRisk, qtyByNotional);
        qty = Math.Min(qty, _cfg.MaxShares);
        return Math.Max(1, qty);
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);

    private static string HtfGuardAtTime(DateTime ts, EnrichedBar[]? bars1h, EnrichedBar[]? bars1d)
    {
        var scores = new List<int>();
        foreach (var bars in new[] { bars1h, bars1d })
        {
            if (bars == null || bars.Length < 2) continue;
            int idx = FindBarAtOrBefore(bars, ts);
            if (idx < 1) continue;
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
        if (avg >= 2) return "STRONG_BULL";
        if (avg <= -2) return "STRONG_BEAR";
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
}



