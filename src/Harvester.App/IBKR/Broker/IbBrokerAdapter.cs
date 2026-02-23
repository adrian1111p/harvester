using IBApi;

namespace Harvester.App.IBKR.Broker;

public sealed class IbBrokerAdapter : IBrokerAdapter
{
    private readonly IbContractNormalizationService _contracts = new();
    private readonly IbOrderTranslationService _orders = new();

    public bool IsConnected(EClientSocket client)
    {
        return client.IsConnected();
    }

    public Contract BuildContract(BrokerContractSpec spec)
    {
        return _contracts.NormalizeAndBuild(spec);
    }

    public Order BuildOrder(BrokerOrderIntent intent)
    {
        return _orders.ToIbOrder(intent);
    }

    public CanonicalOrderEvent TranslateOrderStatus(
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
        double mktCapPrice)
    {
        return _orders.FromOrderStatus(
            timestampUtc,
            orderId,
            status,
            filled,
            remaining,
            avgFillPrice,
            permId,
            parentId,
            lastFillPrice,
            clientId,
            whyHeld,
            mktCapPrice);
    }

    public CanonicalOrderEvent TranslateOpenOrder(DateTime timestampUtc, int orderId, Contract contract, Order order, OrderState orderState)
    {
        return _orders.FromOpenOrder(timestampUtc, orderId, contract, order, orderState);
    }

    public void RequestOpenOrders(EClientSocket client)
    {
        client.reqOpenOrders();
    }

    public void RequestCurrentTime(EClientSocket client)
    {
        client.reqCurrentTime();
    }

    public void RequestCompletedOrders(EClientSocket client, bool apiOnly)
    {
        client.reqCompletedOrders(apiOnly);
    }

    public void RequestExecutions(EClientSocket client, int requestId, ExecutionFilter filter)
    {
        client.reqExecutions(requestId, filter);
    }

    public void RequestContractDetails(EClientSocket client, int requestId, Contract contract)
    {
        client.reqContractDetails(requestId, contract);
    }

    public void RequestOptionChainParameters(EClientSocket client, int requestId, string underlyingSymbol, string futFopExchange, string underlyingSecType, int underlyingConId)
    {
        client.reqSecDefOptParams(requestId, underlyingSymbol, futFopExchange, underlyingSecType, underlyingConId);
    }

    public void ExerciseOptions(EClientSocket client, int requestId, Contract contract, int exerciseAction, int exerciseQuantity, string account, int overrideOption)
    {
        client.exerciseOptions(requestId, contract, exerciseAction, exerciseQuantity, account, overrideOption);
    }

    public void PlaceOrder(EClientSocket client, int orderId, Contract contract, Order order)
    {
        client.placeOrder(orderId, contract, order);
    }

    public void RequestMarketDataType(EClientSocket client, int marketDataType)
    {
        client.reqMarketDataType(marketDataType);
    }

    public void RequestMarketData(EClientSocket client, int requestId, Contract contract, string genericTickList = "")
    {
        client.reqMktData(requestId, contract, genericTickList, false, false, new List<TagValue>());
    }

    public void CancelMarketData(EClientSocket client, int requestId)
    {
        client.cancelMktData(requestId);
    }

    public void RequestMarketDepth(EClientSocket client, int requestId, Contract contract, int rows, bool isSmartDepth)
    {
        client.reqMarketDepth(requestId, contract, rows, isSmartDepth, new List<TagValue>());
    }

    public void CancelMarketDepth(EClientSocket client, int requestId, bool isSmartDepth)
    {
        client.cancelMktDepth(requestId, isSmartDepth);
    }

    public void RequestRealtimeBars(EClientSocket client, int requestId, Contract contract, string whatToShow, bool useRth)
    {
        client.reqRealTimeBars(requestId, contract, 5, whatToShow, useRth, new List<TagValue>());
    }

    public void CancelRealtimeBars(EClientSocket client, int requestId)
    {
        client.cancelRealTimeBars(requestId);
    }

    public void RequestHistoricalData(
        EClientSocket client,
        int requestId,
        Contract contract,
        string endDateTime,
        string duration,
        string barSize,
        string whatToShow,
        int useRth,
        int formatDate,
        bool keepUpToDate)
    {
        client.reqHistoricalData(
            requestId,
            contract,
            endDateTime,
            duration,
            barSize,
            whatToShow,
            useRth,
            formatDate,
            keepUpToDate,
            new List<TagValue>());
    }

    public void CancelHistoricalData(EClientSocket client, int requestId)
    {
        client.cancelHistoricalData(requestId);
    }

    public void RequestHistoricalTicks(
        EClientSocket client,
        int requestId,
        Contract contract,
        string startDateTime,
        string endDateTime,
        int numberOfTicks,
        string whatToShow,
        int useRth,
        bool ignoreSize)
    {
        client.reqHistoricalTicks(
            requestId,
            contract,
            startDateTime,
            endDateTime,
            numberOfTicks,
            whatToShow,
            useRth,
            ignoreSize,
            new List<TagValue>());
    }

    public void RequestHeadTimestamp(EClientSocket client, int requestId, Contract contract, string whatToShow, int useRth, int formatDate)
    {
        client.reqHeadTimestamp(requestId, contract, whatToShow, useRth, formatDate);
    }

    public void CancelHeadTimestamp(EClientSocket client, int requestId)
    {
        client.cancelHeadTimestamp(requestId);
    }

    public void RequestManagedAccounts(EClientSocket client)
    {
        client.reqManagedAccts();
    }

    public void RequestFamilyCodes(EClientSocket client)
    {
        client.reqFamilyCodes();
    }

    public void RequestAccountUpdates(EClientSocket client, bool subscribe, string accountCode)
    {
        client.reqAccountUpdates(subscribe, accountCode);
    }

    public void RequestAccountUpdatesMulti(EClientSocket client, int requestId, string accountCode, string modelCode, bool ledgerAndNlv)
    {
        client.reqAccountUpdatesMulti(requestId, accountCode, modelCode, ledgerAndNlv);
    }

    public void CancelAccountUpdatesMulti(EClientSocket client, int requestId)
    {
        client.cancelAccountUpdatesMulti(requestId);
    }

    public void RequestAccountSummary(EClientSocket client, int requestId, string groupName, string tags)
    {
        client.reqAccountSummary(requestId, groupName, tags);
    }

    public void CancelAccountSummary(EClientSocket client, int requestId)
    {
        client.cancelAccountSummary(requestId);
    }

    public void RequestPositions(EClientSocket client)
    {
        client.reqPositions();
    }

    public void CancelPositions(EClientSocket client)
    {
        client.cancelPositions();
    }

    public void RequestPositionsMulti(EClientSocket client, int requestId, string accountCode, string modelCode)
    {
        client.reqPositionsMulti(requestId, accountCode, modelCode);
    }

    public void CancelPositionsMulti(EClientSocket client, int requestId)
    {
        client.cancelPositionsMulti(requestId);
    }

    public void RequestPnlAccount(EClientSocket client, int requestId, string accountCode, string modelCode)
    {
        client.reqPnL(requestId, accountCode, modelCode);
    }

    public void CancelPnlAccount(EClientSocket client, int requestId)
    {
        client.cancelPnL(requestId);
    }

    public void RequestPnlSingle(EClientSocket client, int requestId, string accountCode, string modelCode, int contractId)
    {
        client.reqPnLSingle(requestId, accountCode, modelCode, contractId);
    }

    public void CancelPnlSingle(EClientSocket client, int requestId)
    {
        client.cancelPnLSingle(requestId);
    }

    public void RequestFaData(EClientSocket client, int faDataType)
    {
        client.requestFA(faDataType);
    }

    public void RequestFundamentalData(EClientSocket client, int requestId, Contract contract, string reportType)
    {
        client.reqFundamentalData(requestId, contract, reportType, new List<TagValue>());
    }

    public void CancelFundamentalData(EClientSocket client, int requestId)
    {
        client.cancelFundamentalData(requestId);
    }

    public void RequestScannerSubscription(EClientSocket client, int requestId, ScannerSubscription subscription, IReadOnlyList<TagValue> subscriptionOptions, IReadOnlyList<TagValue> filterOptions)
    {
        client.reqScannerSubscription(requestId, subscription, new List<TagValue>(subscriptionOptions), new List<TagValue>(filterOptions));
    }

    public void CancelScannerSubscription(EClientSocket client, int requestId)
    {
        client.cancelScannerSubscription(requestId);
    }

    public void RequestScannerParameters(EClientSocket client)
    {
        client.reqScannerParameters();
    }

    public void RequestHistogramData(EClientSocket client, int requestId, Contract contract, bool useRth, string period)
    {
        client.reqHistogramData(requestId, contract, useRth, period);
    }

    public void QueryDisplayGroups(EClientSocket client, int requestId)
    {
        client.queryDisplayGroups(requestId);
    }

    public void SubscribeToDisplayGroupEvents(EClientSocket client, int requestId, int groupId)
    {
        client.subscribeToGroupEvents(requestId, groupId);
    }

    public void UpdateDisplayGroup(EClientSocket client, int requestId, string contractInfo)
    {
        client.updateDisplayGroup(requestId, contractInfo);
    }

    public void UnsubscribeFromDisplayGroupEvents(EClientSocket client, int requestId)
    {
        client.unsubscribeFromGroupEvents(requestId);
    }
}
