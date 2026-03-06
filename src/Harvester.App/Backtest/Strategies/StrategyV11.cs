using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

public sealed class V11Config
{
    public double RiskPerTradeDollars { get; set; } = 22.0;
    public double AccountSize { get; set; } = 25_000.0;
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.22;
    public int MaxShares { get; set; } = 8000;
    public double MinRiskPerShare { get; set; } = 0.01;

    public bool UseNextBarOpenEntry { get; set; } = true;
    public int CooldownBars { get; set; } = 4;

    public double MinPrice { get; set; } = 5.0;
    public double MaxPrice { get; set; } = 600.0;
    public double RvolMin { get; set; } = 0.85;
    public double L2LiquidityMin { get; set; } = 20.0;
    public double SpreadZMax { get; set; } = 2.1;
    public double VolAccelMin { get; set; } = -0.20;

    public double BbLongThreshold { get; set; } = 0.12;
    public double BbShortThreshold { get; set; } = 0.88;
    public double VwapDeviationAtr { get; set; } = 0.60;
    public double RsiLongMax { get; set; } = 38.0;
    public double RsiShortMin { get; set; } = 62.0;
    public double AdxMin { get; set; } = 12.0;
    public double AdxMax { get; set; } = 36.0;
    public int MinScore { get; set; } = 5;

    public int SwingLookback { get; set; } = 4;
    public bool RequireHtfBiasFilter { get; set; } = true;
    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;

    public double HardStopR { get; set; } = 0.90;
    public double BreakevenR { get; set; } = 0.55;
    public double TrailR { get; set; } = 0.35;
    public double GivebackPct { get; set; } = 0.30;
    public bool UseFixedGivebackUsdCap { get; set; } = true;
    public bool UseVariableGivebackUsdCap { get; set; } = true;
    public double GivebackUsdCap { get; set; } = 38.0;
    public double Tp1R { get; set; } = 0.80;
    public double Tp2R { get; set; } = 1.45;
    public int MaxHoldBars { get; set; } = 30;

    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;
}

public sealed class StrategyV11 : BacktestStrategyBase
{
    private readonly V11Config _cfg;
    private readonly ExitEngine.ExitConfig _exitCfg;
    private readonly StrategyV3_1 _signalCore;

    public StrategyV11(V11Config? cfg = null)
    {
        _cfg = cfg ?? new V11Config();
        _signalCore = new StrategyV3_1(new V3Config_1
        {
            RiskPerTradeDollars = _cfg.RiskPerTradeDollars,
            AccountSize = _cfg.AccountSize,
            MaxPositionNotionalPctOfAccount = _cfg.MaxPositionNotionalPctOfAccount,
            MaxShares = _cfg.MaxShares,
            MinRiskPerShare = _cfg.MinRiskPerShare,
            MinPrice = _cfg.MinPrice,
            MaxPrice = _cfg.MaxPrice,
            UseNextBarOpenEntry = _cfg.UseNextBarOpenEntry,
            VwapStretchAtr = Math.Max(1.2, _cfg.VwapDeviationAtr * 2.0),
            VwapEnabled = true,
            BbEntryPctbLow = _cfg.BbLongThreshold,
            BbEntryPctbHigh = _cfg.BbShortThreshold,
            BbEnabled = true,
            SqueezeEnabled = true,
            SqueezeBars = 8,
            L2LiquidityMin = _cfg.L2LiquidityMin,
            SpreadZMax = _cfg.SpreadZMax,
            VolAccelMin = _cfg.VolAccelMin,
            RvolMin = _cfg.RvolMin,
            RsiOversold = _cfg.RsiLongMax,
            RsiOverbought = _cfg.RsiShortMin,
            RequireVolumeConfirm = true,
            // Phase 1 parity fix: use config values directly — no Math.Max() clamping.
            // This ensures backtest and live use identical exit parameters.
            HardStopR = _cfg.HardStopR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            Tp1R = _cfg.Tp1R,
            Tp2R = _cfg.Tp2R,
            BreakevenR = _cfg.BreakevenR,
            MaxHoldBars = _cfg.MaxHoldBars,
            SlippageCents = _cfg.SlippageCents,
            CommissionPerShare = _cfg.CommissionPerShare,
            AllowLong = _cfg.AllowLong,
            AllowShort = _cfg.AllowShort,
        });
        _exitCfg = new ExitEngine.ExitConfig
        {
            HardStopR = _cfg.HardStopR,
            BreakevenR = _cfg.BreakevenR,
            TrailR = _cfg.TrailR,
            GivebackPct = _cfg.GivebackPct,
            GivebackMinPeakR = 0.20,
            UseFixedGivebackUsdCap = _cfg.UseFixedGivebackUsdCap,
            UseVariableGivebackUsdCap = _cfg.UseVariableGivebackUsdCap,
            GivebackUsdCap = _cfg.GivebackUsdCap,
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
        var rawSignals = _signalCore.GenerateSignals(triggerBars, bars5m, bars15m, bars1h, bars1d);
        if (rawSignals.Count == 0) return rawSignals;

        var filtered = new List<BacktestSignal>(rawSignals.Count);
        int lastBar = -10_000;

        foreach (var signal in rawSignals.OrderBy(s => s.BarIndex))
        {
            if (signal.BarIndex - lastBar < _cfg.CooldownBars) continue;

            int evalIdx = Math.Max(0, signal.BarIndex - 1);
            if (evalIdx >= triggerBars.Length) continue;
            var row = triggerBars[evalIdx];

            if (!double.IsNaN(row.Adx) && (row.Adx < _cfg.AdxMin || row.Adx > _cfg.AdxMax)) continue;
            if (signal.Side == TradeSide.Long && !double.IsNaN(row.BbPctB) && row.BbPctB > 0.45) continue;
            if (signal.Side == TradeSide.Short && !double.IsNaN(row.BbPctB) && row.BbPctB < 0.55) continue;

            filtered.Add(signal);
            lastBar = signal.BarIndex;
        }

        return filtered;
    }

    public override BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => ExitEngine.SimulateTrade(signal, triggerBars, _exitCfg);
}