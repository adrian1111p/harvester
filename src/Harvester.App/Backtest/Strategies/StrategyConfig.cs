namespace Harvester.App.Backtest.Strategies;

/// <summary>
/// Tunable parameters for the Conduct strategy family.
/// Mirrors Python's StrategyConfig dataclass.
/// </summary>
public sealed class StrategyConfig
{
    // ── Risk Sizing ──
    public double RiskPerTradeDollars { get; set; } = 50.0;
    public double AccountSize { get; set; } = 25_000.0;

    // ── Entry Filters ──
    public double AdxThreshold { get; set; } = 20.0;
    public (double Low, double High) RsiLongRange { get; set; } = (35.0, 70.0);
    public (double Low, double High) RsiShortRange { get; set; } = (30.0, 65.0);
    public double RvolMin { get; set; } = 1.3;
    public int PullbackEmaPeriod { get; set; } = 9;
    public bool RequireSupertrend { get; set; } = true;

    // ── Exit Rules ──
    public double HardStopR { get; set; } = 1.0;
    public double BreakevenR { get; set; } = 1.0;
    public double TrailR { get; set; } = 0.5;
    public double GivebackPct { get; set; } = 0.50;
    public double Tp1R { get; set; } = 1.5;
    public double Tp1ScalePct { get; set; } = 0.50;
    public double Tp2R { get; set; } = 3.0;
    public int MaxHoldBars { get; set; } = 180;      // 180 × 30s = 90 min
    public int EodBarMinute { get; set; } = 955;      // 15:55 ET

    // ── 20MA Exhaustion Filter (V2.0) ──
    public double MaxMaDistAtr { get; set; } = 0.5;

    // ── Slippage & Commission ──
    public double SlippageCents { get; set; } = 1.0;
    public double CommissionPerShare { get; set; } = 0.005;
}
