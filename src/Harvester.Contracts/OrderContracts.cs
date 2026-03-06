namespace Harvester.Contracts;

/// <summary>IBKR contract details row — resolved instrument metadata.</summary>
public sealed record ContractDetailsRow(
    int ConId,
    string Symbol,
    string SecType,
    string Exchange,
    string PrimaryExchange,
    string Currency,
    string LocalSymbol,
    string TradingClass,
    string MarketName,
    string LongName,
    double MinTick
);

/// <summary>Order template for placing or simulating IBKR orders.</summary>
public sealed record OrderTemplateRow(
    string Name,
    int OrderId,
    int ParentId,
    string Action,
    string OrderType,
    double Quantity,
    double LimitPrice,
    double StopPrice,
    string Tif,
    bool Transmit
);

/// <summary>Recorded live order placement event.</summary>
public sealed record LiveOrderPlacementRow(
    string TimestampUtc,
    int OrderId,
    string Symbol,
    string Action,
    double Quantity,
    double LimitPrice,
    double Notional,
    string Account,
    string OrderRef
);

/// <summary>Sim order cancellation event record.</summary>
public sealed record SimOrderCancellationRow(
    string TimestampUtc,
    int OrderId,
    string Account
);

/// <summary>IBKR managed account identity.</summary>
public sealed record ManagedAccountRow(
    DateTime TimestampUtc,
    string AccountId
);
