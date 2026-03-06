namespace Harvester.App.Strategy;

/// <summary>
/// Minimal shared contract for order intents across live and replay modes.
/// Enables cross-mode analytics and comparison tooling.
/// </summary>
public interface IOrderIntent
{
    DateTime TimestampUtc { get; }
    string Symbol { get; }
    /// <summary>Canonical side label: "Buy" or "Sell".</summary>
    string SideLabel { get; }
    /// <summary>Order quantity (widened to double for replay compatibility).</summary>
    double OrderQuantity { get; }
    string Source { get; }
}

/// <summary>
/// Minimal shared contract for computed feature snapshots.
/// <see cref="V3LiveFeatureSnapshot"/> implements this; replay strategies
/// can adopt it when a dedicated feature record is introduced.
/// </summary>
public interface IFeatureSnapshot
{
    DateTime TimestampUtc { get; }
    bool IsReady { get; }
    double Price { get; }
    double Atr14 { get; }
}

/// <summary>
/// Minimal shared contract for multi-timeframe alignment state.
/// Both <see cref="V3LiveMtfAlignment"/> and <see cref="ReplayMtfSignalSnapshot"/>
/// expose these core directional flags.
/// </summary>
public interface IMtfAlignment
{
    bool HasAllTimeframes { get; }
    bool IsBullish { get; }
    bool IsBearish { get; }
}
