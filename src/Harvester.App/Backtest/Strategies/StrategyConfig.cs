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
    public double MaxPositionNotionalPctOfAccount { get; set; } = 0.25;
    public int MaxShares { get; set; } = 10_000;
    public double MinRiskPerShare { get; set; } = 0.01;
    public int CooldownBars { get; set; } = 1;
    public bool UseNextBarOpenEntry { get; set; } = true;
    public bool StrictMissingDataChecks { get; set; } = true;

    // ── Entry Filters ──
    public double AdxThreshold { get; set; } = 20.0;
    public (double Low, double High) RsiLongRange { get; set; } = (35.0, 70.0);
    public (double Low, double High) RsiShortRange { get; set; } = (30.0, 65.0);
    public double RvolMin { get; set; } = 1.3;
    public int PullbackEmaPeriod { get; set; } = 9;
    public bool RequireSupertrend { get; set; } = true;
    public bool RequireMtfAlignment { get; set; } = false;

    // ── Price & Time Filters ──
    public double MinPrice { get; set; } = 10.0;
    public double MaxPrice { get; set; } = 500.0;
    public List<(int Start, int End)> EntryWindows { get; set; } = [(570, 780)]; // 9:30-13:00 ET
    public int SkipFirstNMinutes { get; set; } = 5;
    public int MarketOpenMinute { get; set; } = 570; // 9:30 ET

    // ── Alternate Entry Modes ──
    public bool VwapReversionEnabled { get; set; } = false;
    public double VwapStretchAtr { get; set; } = 1.0;
    public bool BbBounceEnabled { get; set; } = false;
    public double BbEntryPctbLow { get; set; } = 0.05;
    public double BbEntryPctbHigh { get; set; } = 0.95;

    // ── Exit Rules ──
    public double HardStopR { get; set; } = 1.0;
    public double BreakevenR { get; set; } = 1.0;
    public double TrailR { get; set; } = 0.5;
    public double GivebackPct { get; set; } = 0.50;
    public bool UseNotionalGivebackCap { get; set; } = false;
    public double GivebackPctOfNotional { get; set; } = 0.01;
    public double GivebackUsdCap { get; set; } = 30.0;
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
