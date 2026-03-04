using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

/// <summary>Common interface for all backtest strategies.</summary>
public interface IBacktestStrategy
{
    /// <summary>Scan trigger-timeframe bars and produce entry signals.</summary>
    IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null);

    /// <summary>Simulate a single trade from signal to exit.</summary>
    BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars);
}

/// <summary>
/// Base type for concrete strategy versions to enforce a common contract and enable shared evolution.
/// </summary>
public abstract class BacktestStrategyBase : IBacktestStrategy
{
    public abstract IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null);

    public abstract BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars);
}

/// <summary>
/// Shared adapter base for strategy versions that delegate to <see cref="ConductStrategyV2"/>.
/// </summary>
public abstract class ConductStrategyAdapterBase : IBacktestStrategy
{
    private readonly ConductStrategyV2 _inner;

    public StrategyConfig Config { get; }

    protected ConductStrategyAdapterBase(StrategyConfig? cfg, Func<StrategyConfig> defaultConfigFactory)
    {
        Config = cfg ?? defaultConfigFactory();
        _inner = new ConductStrategyV2(Config);
    }

    public IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
    {
        return _inner.GenerateSignals(triggerBars, bars5m, bars15m, bars1h, bars1d);
    }

    public BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
    {
        return _inner.SimulateTrade(signal, triggerBars);
    }
}

// ── Enriched Bar ─────────────────────────────────────────────────────────────

/// <summary>
/// A bar enriched with all computed indicator values.
/// Replaces the Python pattern of adding columns to a pandas DataFrame.
/// All indicator fields default to NaN until computed.
/// </summary>
public sealed class EnrichedBar
{
    public BacktestBar Bar { get; }

    // ── Moving Averages ──
    public double Ema9 { get; set; } = double.NaN;
    public double Ema21 { get; set; } = double.NaN;
    public double Ema50 { get; set; } = double.NaN;
    public double Sma20 { get; set; } = double.NaN;
    public double Sma200 { get; set; } = double.NaN;

    // ── ATR ──
    public double Atr14 { get; set; } = double.NaN;

    // ── RSI ──
    public double Rsi14 { get; set; } = double.NaN;

    // ── MACD ──
    public double Macd { get; set; } = double.NaN;
    public double MacdSignal { get; set; } = double.NaN;
    public double MacdHist { get; set; } = double.NaN;

    // ── Bollinger Bands ──
    public double BbMid { get; set; } = double.NaN;
    public double BbUpper { get; set; } = double.NaN;
    public double BbLower { get; set; } = double.NaN;
    public double BbPctB { get; set; } = double.NaN;
    public double BbBandwidth { get; set; } = double.NaN;

    // ── ADX ──
    public double Adx { get; set; } = double.NaN;
    public double PlusDi { get; set; } = double.NaN;
    public double MinusDi { get; set; } = double.NaN;

    // ── Supertrend ──
    public double Supertrend { get; set; } = double.NaN;
    public int StDirection { get; set; } = 1;

    // ── Relative Volume ──
    public double Rvol { get; set; } = double.NaN;

    // ── VWAP ──
    public double Vwap { get; set; } = double.NaN;

    // ── Stochastic ──
    public double StochK { get; set; } = double.NaN;
    public double StochD { get; set; } = double.NaN;

    // ── Keltner Channels ──
    public double KcMid { get; set; } = double.NaN;
    public double KcUpper { get; set; } = double.NaN;
    public double KcLower { get; set; } = double.NaN;

    // ── MFI ──
    public double Mfi14 { get; set; } = double.NaN;

    // ── Order Flow Imbalance ──
    public double OfiRaw { get; set; } = double.NaN;
    public double OfiCum { get; set; } = double.NaN;
    public double OfiSignal { get; set; } = double.NaN;

    // ── Spread Proxy ──
    public double SpreadRatio { get; set; } = double.NaN;
    public double SpreadZ { get; set; } = double.NaN;

    // ── Volume Acceleration ──
    public double VolAccel { get; set; } = double.NaN;

    // ── L2 Liquidity ──
    public double L2Liquidity { get; set; } = double.NaN;

    // ── Williams %R ──
    public double WillR14 { get; set; } = double.NaN;

    // ── Donchian Channels ──
    public double DcUpper { get; set; } = double.NaN;
    public double DcLower { get; set; } = double.NaN;
    public double DcMid { get; set; } = double.NaN;
    public double DcPct { get; set; } = double.NaN;

    // ── DPO ──
    public double Dpo20 { get; set; } = double.NaN;

    public EnrichedBar(BacktestBar bar)
    {
        Bar = bar;
    }
}
