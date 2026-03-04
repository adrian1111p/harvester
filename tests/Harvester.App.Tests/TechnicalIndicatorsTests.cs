using Harvester.App.Backtest.Indicators;

namespace Harvester.App.Tests;

public sealed class TechnicalIndicatorsTests
{
    [Fact]
    public void Ema_ReturnsExpectedLengthAndTracksSeries()
    {
        var series = new[] { 1.0, 2.0, 3.0, 4.0, 5.0 };

        var ema = TechnicalIndicators.Ema(series, period: 3);

        Assert.Equal(series.Length, ema.Length);
        Assert.Equal(series[0], ema[0]);
        Assert.True(ema[^1] > ema[0]);
    }

    [Fact]
    public void Sma_UsesWindowAndReturnsNaNBeforeWarmup()
    {
        var series = new[] { 1.0, 2.0, 3.0, 4.0 };

        var sma = TechnicalIndicators.Sma(series, period: 3);

        Assert.True(double.IsNaN(sma[0]));
        Assert.True(double.IsNaN(sma[1]));
        Assert.Equal(2.0, sma[2]);
        Assert.Equal(3.0, sma[3]);
    }
}
