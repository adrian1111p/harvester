using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

public sealed class StrategyV1 : IBacktestStrategy
{
    private readonly ConductStrategyV2 _inner;

    public StrategyConfig Config { get; }

    public StrategyV1(StrategyConfig? cfg = null)
    {
        Config = cfg ?? BuildDefaultConfig();
        _inner = new ConductStrategyV2(Config);
    }

    public static StrategyConfig BuildDefaultConfig() => new()
    {
        TrailR = 1.5,
        GivebackPct = 0.70,
        Tp1R = 2.0,
        Tp2R = 4.0,
        HardStopR = 1.5,
        BreakevenR = 1.2,
        RvolMin = 1.3,
        AdxThreshold = 20.0,
        RiskPerTradeDollars = 50.0,
        AccountSize = 25_000.0,
        UseNotionalGivebackCap = true,
        GivebackPctOfNotional = 0.01,
        GivebackUsdCap = 30.0,
    };

    public IReadOnlyList<BacktestSignal> GenerateSignals(
        EnrichedBar[] triggerBars,
        EnrichedBar[]? bars5m = null,
        EnrichedBar[]? bars15m = null,
        EnrichedBar[]? bars1h = null,
        EnrichedBar[]? bars1d = null)
        => _inner.GenerateSignals(triggerBars, bars5m, bars15m, bars1h, bars1d);

    public BacktestTradeResult? SimulateTrade(BacktestSignal signal, EnrichedBar[] triggerBars)
        => _inner.SimulateTrade(signal, triggerBars);
}
