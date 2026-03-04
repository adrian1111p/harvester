namespace Harvester.Contracts;

public sealed record AccountSummaryRow(string Account, string Tag, string Value, string Currency);

public sealed record PositionRow(
    string Account,
    string Symbol,
    string SecurityType,
    string Currency,
    string Exchange,
    double Quantity,
    double AverageCost
);

public sealed record TopTickRow(
    DateTime TimestampUtc,
    int TickerId,
    string Kind,
    int Field,
    double Price,
    int Size,
    string Value
);

public sealed record DepthRow(
    DateTime TimestampUtc,
    int TickerId,
    int Position,
    int Operation,
    int Side,
    double Price,
    int Size,
    string MarketMaker,
    bool IsSmartDepth
);

public sealed record HistoricalBarRow(
    DateTime TimestampUtc,
    int RequestId,
    string Time,
    double Open,
    double High,
    double Low,
    double Close,
    decimal Volume,
    double Wap,
    int Count
);
