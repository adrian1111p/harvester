using IBApi;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.IBKR.Broker;

public interface IBrokerAdapter
{
    void SetTraceSink(Action<BrokerAdapterTrace>? traceSink);
    bool IsConnected(EClientSocket client);
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
    void RequestAllOpenOrders(EClientSocket client);
    void RequestCurrentTime(EClientSocket client);
    void RequestCompletedOrders(EClientSocket client, bool apiOnly);
    void RequestExecutions(EClientSocket client, int requestId, ExecutionFilter filter);
    void RequestContractDetails(EClientSocket client, int requestId, Contract contract);
    void RequestOptionChainParameters(EClientSocket client, int requestId, string underlyingSymbol, string futFopExchange, string underlyingSecType, int underlyingConId);
    void ExerciseOptions(EClientSocket client, int requestId, Contract contract, int exerciseAction, int exerciseQuantity, string account, int overrideOption);
    void PlaceOrder(EClientSocket client, int orderId, Contract contract, Order order);
    void CancelOrder(EClientSocket client, int orderId, string manualOrderCancelTime);
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
    void RequestHistoricalTicks(
        EClientSocket client,
        int requestId,
        Contract contract,
        string startDateTime,
        string endDateTime,
        int numberOfTicks,
        string whatToShow,
        int useRth,
        bool ignoreSize);
    void RequestHeadTimestamp(EClientSocket client, int requestId, Contract contract, string whatToShow, int useRth, int formatDate);
    void CancelHeadTimestamp(EClientSocket client, int requestId);
    void RequestManagedAccounts(EClientSocket client);
    void RequestFamilyCodes(EClientSocket client);
    void RequestAccountUpdates(EClientSocket client, bool subscribe, string accountCode);
    void RequestAccountUpdatesMulti(EClientSocket client, int requestId, string accountCode, string modelCode, bool ledgerAndNlv);
    void CancelAccountUpdatesMulti(EClientSocket client, int requestId);
    void RequestAccountSummary(EClientSocket client, int requestId, string groupName, string tags);
    void CancelAccountSummary(EClientSocket client, int requestId);
    void RequestPositions(EClientSocket client);
    void CancelPositions(EClientSocket client);
    void RequestPositionsMulti(EClientSocket client, int requestId, string accountCode, string modelCode);
    void CancelPositionsMulti(EClientSocket client, int requestId);
    void RequestPnlAccount(EClientSocket client, int requestId, string accountCode, string modelCode);
    void CancelPnlAccount(EClientSocket client, int requestId);
    void RequestPnlSingle(EClientSocket client, int requestId, string accountCode, string modelCode, int contractId);
    void CancelPnlSingle(EClientSocket client, int requestId);
    void RequestFaData(EClientSocket client, int faDataType);
    void RequestFundamentalData(EClientSocket client, int requestId, Contract contract, string reportType);
    void CancelFundamentalData(EClientSocket client, int requestId);
    void RequestScannerSubscription(EClientSocket client, int requestId, ScannerSubscription subscription, IReadOnlyList<TagValue> subscriptionOptions, IReadOnlyList<TagValue> filterOptions);
    void CancelScannerSubscription(EClientSocket client, int requestId);
    void RequestScannerParameters(EClientSocket client);
    void RequestHistogramData(EClientSocket client, int requestId, Contract contract, bool useRth, string period);
    void QueryDisplayGroups(EClientSocket client, int requestId);
    void SubscribeToDisplayGroupEvents(EClientSocket client, int requestId, int groupId);
    void UpdateDisplayGroup(EClientSocket client, int requestId, string contractInfo);
    void UnsubscribeFromDisplayGroupEvents(EClientSocket client, int requestId);
}

public sealed record BrokerAdapterTrace(
    DateTime TimestampUtc,
    string Adapter,
    string Operation,
    int? RequestId,
    string? Metadata
);

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
    string? FaPercentage = null,
    IReadOnlyList<double>? ComboLegLimitPrices = null
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
