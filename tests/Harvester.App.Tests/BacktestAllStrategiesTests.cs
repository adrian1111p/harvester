using Harvester.App.Backtest.Engine;
using Harvester.App.Backtest.Strategies;

namespace Harvester.App.Tests;

public sealed class BacktestAllStrategiesTests
{
    [Fact]
    public void AllStrategies_RunBacktestSmoke_DoesNotThrow()
    {
        var triggerBars = BuildEnrichedBars(count: 320, start: new DateTime(2025, 01, 03, 9, 30, 0), basePrice: 100.0, drift: 0.03);
        var bars5m = BuildEnrichedBars(count: 180, start: new DateTime(2025, 01, 03, 9, 30, 0), basePrice: 100.0, drift: 0.06);
        var bars15m = BuildEnrichedBars(count: 180, start: new DateTime(2025, 01, 03, 9, 30, 0), basePrice: 100.0, drift: 0.09);
        var bars1h = BuildEnrichedBars(count: 120, start: new DateTime(2024, 12, 01, 9, 30, 0), basePrice: 98.0, drift: 0.12);
        var bars1d = BuildEnrichedBars(count: 120, start: new DateTime(2024, 06, 01, 9, 30, 0), basePrice: 95.0, drift: 0.15);

        var strategies = new IBacktestStrategy[]
        {
            new ConductStrategyV2(),
            new StrategyV1(),
            new StrategyV2(),
            new StrategyV3(),
            new StrategyV4(),
            new StrategyV5(),
            new StrategyV6(),
            new StrategyV7(),
            new StrategyV8(),
            new StrategyV9(),
            new StrategyV10(),
            new StrategyV11(),
        };

        foreach (var strategy in strategies)
        {
            var result = BacktestEngine.RunBacktest(
                symbol: "TEST",
                strategy: strategy,
                triggerBars: triggerBars,
                triggerTf: "1m",
                bars5m: bars5m,
                bars15m: bars15m,
                bars1h: bars1h,
                bars1d: bars1d,
                initialCapital: 25_000.0);

            Assert.NotNull(result);
            Assert.NotNull(result.Stats);
            Assert.True(result.Stats.TotalTrades >= 0);
        }
    }

    private static EnrichedBar[] BuildEnrichedBars(int count, DateTime start, double basePrice, double drift)
    {
        var bars = new EnrichedBar[count];

        for (int i = 0; i < count; i++)
        {
            double wave = Math.Sin(i / 8.0) * 0.7 + Math.Cos(i / 19.0) * 0.5;
            double close = basePrice + (drift * i) + wave;
            double open = close - Math.Sin(i / 6.0) * 0.18;
            double high = Math.Max(open, close) + 0.22;
            double low = Math.Min(open, close) - 0.22;

            var enriched = new EnrichedBar(new BacktestBar(
                Timestamp: start.AddMinutes(i),
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: 120_000 + (i % 25) * 5_000));

            enriched.Atr14 = 0.9 + 0.15 * Math.Abs(Math.Sin(i / 14.0));
            enriched.Ema9 = close - 0.05;
            enriched.Ema21 = close - 0.15;
            enriched.Ema50 = close - 0.25;
            enriched.Sma20 = close - 0.10;
            enriched.Sma200 = close - 0.30;

            enriched.Rsi14 = 50 + 18 * Math.Sin(i / 11.0);
            enriched.Macd = 0.15 * Math.Sin(i / 7.0);
            enriched.MacdSignal = 0.10 * Math.Sin((i - 1) / 7.0);
            enriched.MacdHist = enriched.Macd - enriched.MacdSignal;

            enriched.BbMid = close;
            enriched.BbUpper = close + 1.0;
            enriched.BbLower = close - 1.0;
            enriched.BbPctB = 0.5 + 0.45 * Math.Sin(i / 10.0);
            enriched.BbBandwidth = 0.06 + 0.01 * Math.Abs(Math.Cos(i / 9.0));

            enriched.Adx = 24 + 6 * Math.Abs(Math.Sin(i / 13.0));
            enriched.PlusDi = 24 + 4 * Math.Sin(i / 9.0);
            enriched.MinusDi = 20 + 4 * Math.Cos(i / 9.0);

            enriched.Supertrend = close - 0.2;
            enriched.StDirection = (i / 17) % 2 == 0 ? 1 : -1;
            enriched.Rvol = 1.0 + 0.6 * Math.Abs(Math.Sin(i / 6.0));
            enriched.Vwap = close - 0.08 * Math.Sin(i / 8.0);

            enriched.StochK = 50 + 35 * Math.Sin(i / 5.0);
            enriched.StochD = 50 + 28 * Math.Sin((i - 1) / 5.0);

            enriched.KcMid = close;
            enriched.KcUpper = close + 0.9;
            enriched.KcLower = close - 0.9;

            enriched.Mfi14 = 50 + 20 * Math.Sin(i / 12.0);
            enriched.OfiRaw = Math.Sin(i / 4.0);
            enriched.OfiCum = Math.Sin(i / 9.0);
            enriched.OfiSignal = Math.Sin(i / 7.0);

            enriched.SpreadRatio = 0.0005 + 0.0001 * Math.Abs(Math.Sin(i / 5.0));
            enriched.SpreadZ = 0.3 + 0.8 * Math.Abs(Math.Sin(i / 13.0));
            enriched.VolAccel = 0.3 * Math.Sin(i / 6.0);
            enriched.L2Liquidity = 25 + 8 * Math.Abs(Math.Cos(i / 10.0));

            enriched.WillR14 = -50 + 40 * Math.Sin(i / 8.0);
            enriched.DcUpper = close + 1.1;
            enriched.DcLower = close - 1.1;
            enriched.DcMid = close;
            enriched.DcPct = 0.5 + 0.3 * Math.Sin(i / 9.0);
            enriched.Dpo20 = 0.3 * Math.Cos(i / 11.0);

            bars[i] = enriched;
        }

        return bars;
    }
}