using IBApi;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.IBKR.Broker;

public interface IBrokerAdapter
{
    Contract BuildContract(BrokerContractSpec spec);
    Order BuildOrder(BrokerOrderIntent intent);
    CanonicalOrderEvent TranslateOrderStatus(
        DateTime timestampUtc,
        int orderId,
        string status,
        double filled,
        double remaining,
        double avgFillPrice,
        int permId,
        int parentId,
        double lastFillPrice,
        int clientId,
        string whyHeld,
        double mktCapPrice);

    CanonicalOrderEvent TranslateOpenOrder(
        DateTime timestampUtc,
        int orderId,
        Contract contract,
        Order order,
        OrderState orderState);

    void RequestOpenOrders(EClientSocket client);
    void RequestCompletedOrders(EClientSocket client, bool apiOnly);
    void RequestExecutions(EClientSocket client, int requestId, ExecutionFilter filter);
    void RequestContractDetails(EClientSocket client, int requestId, Contract contract);
    void PlaceOrder(EClientSocket client, int orderId, Contract contract, Order order);
    void RequestMarketDataType(EClientSocket client, int marketDataType);
    void RequestMarketData(EClientSocket client, int requestId, Contract contract, string genericTickList = "");
    void CancelMarketData(EClientSocket client, int requestId);
    void RequestMarketDepth(EClientSocket client, int requestId, Contract contract, int rows, bool isSmartDepth);
    void CancelMarketDepth(EClientSocket client, int requestId, bool isSmartDepth);
    void RequestRealtimeBars(EClientSocket client, int requestId, Contract contract, string whatToShow, bool useRth);
    void CancelRealtimeBars(EClientSocket client, int requestId);
    void RequestHistoricalData(
        EClientSocket client,
        int requestId,
        Contract contract,
        string endDateTime,
        string duration,
        string barSize,
        string whatToShow,
        int useRth,
        int formatDate,
        bool keepUpToDate);
    void CancelHistoricalData(EClientSocket client, int requestId);
}

public enum BrokerAssetType
{
    Stock,
    Option,
    Future,
    Forex,
    Crypto,
    Cfd,
    Index,
    Combo
}

public sealed record BrokerContractSpec(
    BrokerAssetType AssetType,
    string Symbol,
    string Exchange,
    string Currency,
    string? PrimaryExchange = null,
    string? Expiry = null,
    double? Strike = null,
    string? Right = null,
    string? Multiplier = null,
    string? UnderlyingSecType = null,
    string? FutFopExchange = null,
    IReadOnlyList<ComboLeg>? ComboLegs = null
);

public sealed record BrokerOrderIntent(
    string Action,
    string Type,
    double Quantity,
    double? LimitPrice = null,
    double? StopPrice = null,
    string TimeInForce = "DAY",
    bool WhatIf = false,
    bool Transmit = true,
    string? OrderRef = null,
    string? Account = null,
    string? FaGroup = null,
    string? FaProfile = null,
    string? FaMethod = null,
    string? FaPercentage = null
);

public sealed record CanonicalOrderEvent(
    DateTime TimestampUtc,
    string EventType,
    int OrderId,
    int PermId,
    string Symbol,
    string Action,
    string OrderType,
    string Status,
    double Filled,
    double Remaining,
    double AvgFillPrice,
    double LastFillPrice,
    int ClientId,
    string Account,
    string Reason
);
