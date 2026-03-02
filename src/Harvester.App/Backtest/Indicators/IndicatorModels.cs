namespace Harvester.App.Backtest.Indicators;

/// <summary>Multi-column MACD result per bar.</summary>
public sealed record MacdResult(double Macd, double Signal, double Histogram);

/// <summary>Multi-column Bollinger Bands result per bar.</summary>
public sealed record BollingerResult(double Mid, double Upper, double Lower, double PctB, double Bandwidth);

/// <summary>Multi-column ADX result per bar.</summary>
public sealed record AdxResult(double Adx, double PlusDi, double MinusDi);

/// <summary>Multi-column Supertrend result per bar.</summary>
public sealed record SupertrendResult(double Value, int Direction);

/// <summary>Multi-column Stochastic result per bar.</summary>
public sealed record StochasticResult(double K, double D);

/// <summary>Multi-column Keltner Channels result per bar.</summary>
public sealed record KeltnerResult(double Mid, double Upper, double Lower);

/// <summary>Multi-column Donchian Channels result per bar.</summary>
public sealed record DonchianResult(double Upper, double Lower, double Mid, double Pct);

/// <summary>Multi-column Order Flow Imbalance result per bar.</summary>
public sealed record OrderFlowResult(double Raw, double Cumulative, double Signal);

/// <summary>Multi-column Spread proxy result per bar.</summary>
public sealed record SpreadResult(double Ratio, double ZScore);
