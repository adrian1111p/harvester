using Harvester.App.Backtest.Engine;

namespace Harvester.App.Backtest.Strategies;

public sealed class StrategyV1 : ConductStrategyAdapterBase
{
    public StrategyV1(StrategyConfig? cfg = null)
        : base(cfg, BuildDefaultConfig)
    {
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

}


