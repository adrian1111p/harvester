using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Tests;

public sealed class ReconciliationAndCandlesTests
{
    [Fact]
    public void OrderReconciliation_ComputesFullCoverage_WhenExecutionAndCommissionPresent()
    {
        var open = new[]
        {
            new OpenOrderRow(1, "AAPL", "STK", "SMART", "BUY", "LMT", 10, 10.0, "Submitted", "DU123")
        };

        var completed = Array.Empty<CompletedOrderRow>();

        var executions = new[]
        {
            new ExecutionRow("E1", 1, 111, "DU123", "AAPL", "STK", "BOT", 10, 10.1, "20260304 13:30:00", "SMART", 1)
        };

        var commissions = new[]
        {
            new CommissionRow(DateTime.UtcNow, "E1", 0.35, "USD", 0)
        };

        var result = OrderReconciliation.Reconcile(open, completed, executions, commissions);

        Assert.Equal(1, result.Summary.CanonicalExecutionRows);
        Assert.Equal(1.0, result.Summary.ExecutionCommissionCoveragePct);
        Assert.Equal(1.0, result.Summary.ExecutionOrderMetadataCoveragePct);
    }

    [Fact]
    public void L2CandlestickBuilder_BuildsOneMinuteCandle()
    {
        var t0 = new DateTime(2026, 3, 4, 14, 0, 1, DateTimeKind.Utc);
        var rows = new[]
        {
            new DepthRow(t0, 1, 0, 0, 1, 99.9, 100, "MM1", false),
            new DepthRow(t0, 1, 0, 0, 0, 100.1, 100, "MM2", false),
            new DepthRow(t0.AddSeconds(10), 1, 0, 1, 1, 100.0, 100, "MM1", false),
            new DepthRow(t0.AddSeconds(10), 1, 0, 1, 0, 100.2, 100, "MM2", false)
        };

        var candles = L2CandlestickBuilder.BuildCandles(rows, new[] { TimeSpan.FromMinutes(1) });

        var candle = Assert.Single(candles);
        Assert.Equal("1m", candle.Timeframe);
        Assert.True(candle.Open > 0);
        Assert.True(candle.Close > 0);
        Assert.True(candle.High >= candle.Low);
        Assert.True(candle.Samples >= 2);
    }
}
