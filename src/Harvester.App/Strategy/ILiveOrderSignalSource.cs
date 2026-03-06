namespace Harvester.App.Strategy;

/// <summary>
/// Interface for strategy runtimes that produce live order signals
/// consumable by the host runtime (SnapshotRuntime).
/// Unlike <see cref="IReplayOrderSignalSource"/> which is for replay/backtest mode,
/// this interface carries proposed orders from a live strategy evaluation loop.
/// </summary>
public interface ILiveOrderSignalSource
{
    /// <summary>
    /// Drain all accepted order intents since the last call.
    /// Returns an empty list if no new intents are pending.
    /// The implementation MUST clear its internal queue after this call
    /// to avoid duplicate submissions.
    /// </summary>
    IReadOnlyList<LiveOrderIntent> ConsumeOrderIntents();

    /// <summary>
    /// Notify the strategy that an order intent was transmitted to the broker.
    /// Allows the strategy to update internal state (risk tracking, position tracking, etc.).
    /// </summary>
    void AcknowledgeOrderTransmitted(string intentId, string symbol, double filledQuantity, double fillPrice);

    /// <summary>
    /// Notify the strategy that a position has been fully or partially closed.
    /// Allows the strategy to release open risk and track realized PnL.
    /// </summary>
    void AcknowledgePositionClosed(string symbol, double closedQuantity, double realizedPnl);
}

/// <summary>
/// A live order intent ready for transmission to the broker.
/// Produced by strategy signal evaluation, consumed by the host runtime.
/// </summary>
public sealed record LiveOrderIntent(
    string IntentId,
    DateTime TimestampUtc,
    string Symbol,
    OrderSide Side,
    OrderType OrderType,
    OrderTimeInForce TimeInForce,
    int Quantity,
    double EntryPrice,
    double StopPrice,
    double TakeProfitPrice,
    double EstimatedRiskDollars,
    string Setup,
    string Source
) : IOrderIntent
{
    string IOrderIntent.SideLabel => Side.ToString();
    double IOrderIntent.OrderQuantity => Quantity;
}
