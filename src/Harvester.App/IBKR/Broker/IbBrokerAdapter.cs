using IBApi;

namespace Harvester.App.IBKR.Broker;

public sealed class IbBrokerAdapter : IBrokerAdapter
{
    private readonly IbContractNormalizationService _contracts = new();
    private readonly IbOrderTranslationService _orders = new();
    private Action<BrokerAdapterTrace>? _traceSink;

    public void SetTraceSink(Action<BrokerAdapterTrace>? traceSink)
    {
        _traceSink = traceSink;
        Emit("setTraceSink", null, traceSink is null ? "disabled" : "enabled");
    }

    private void Emit(string operation, int? requestId, string? metadata = null)
    {
        _traceSink?.Invoke(new BrokerAdapterTrace(DateTime.UtcNow, nameof(IbBrokerAdapter), operation, requestId, metadata));
    }

    public bool IsConnected(EClientSocket client)
    {
        Emit("isConnected", null);
        return client.IsConnected();
    }

    public Contract BuildContract(BrokerContractSpec spec)
    {
        Emit("buildContract", null, $"asset={spec.AssetType} symbol={spec.Symbol} exchange={spec.Exchange}");
        return _contracts.NormalizeAndBuild(spec);
    }

    public Order BuildOrder(BrokerOrderIntent intent)
    {
        Emit("buildOrder", null, $"action={intent.Action} type={intent.Type} qty={intent.Quantity}");
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
        Emit("translateOrderStatus", orderId, $"status={status} filled={filled} remaining={remaining}");
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
        Emit("translateOpenOrder", orderId, $"symbol={contract.Symbol} type={order.OrderType}");
        return _orders.FromOpenOrder(timestampUtc, orderId, contract, order, orderState);
    }

    public void RequestOpenOrders(EClientSocket client)
    {
        Emit("reqOpenOrders", null);
        client.reqOpenOrders();
    }

    public void RequestCurrentTime(EClientSocket client)
    {
        Emit("reqCurrentTime", null);
        client.reqCurrentTime();
    }

    public void RequestCompletedOrders(EClientSocket client, bool apiOnly)
    {
        Emit("reqCompletedOrders", null, $"apiOnly={apiOnly}");
        client.reqCompletedOrders(apiOnly);
    }

    public void RequestExecutions(EClientSocket client, int requestId, ExecutionFilter filter)
    {
        Emit("reqExecutions", requestId);
        client.reqExecutions(requestId, filter);
    }

    public void RequestContractDetails(EClientSocket client, int requestId, Contract contract)
    {
        Emit("reqContractDetails", requestId, $"symbol={contract.Symbol} secType={contract.SecType}");
        client.reqContractDetails(requestId, contract);
    }

    public void RequestOptionChainParameters(EClientSocket client, int requestId, string underlyingSymbol, string futFopExchange, string underlyingSecType, int underlyingConId)
    {
        Emit("reqSecDefOptParams", requestId, $"symbol={underlyingSymbol} secType={underlyingSecType} conId={underlyingConId}");
        client.reqSecDefOptParams(requestId, underlyingSymbol, futFopExchange, underlyingSecType, underlyingConId);
    }

    public void ExerciseOptions(EClientSocket client, int requestId, Contract contract, int exerciseAction, int exerciseQuantity, string account, int overrideOption)
    {
        Emit("exerciseOptions", requestId, $"symbol={contract.Symbol} action={exerciseAction} qty={exerciseQuantity} account={account}");
        client.exerciseOptions(requestId, contract, exerciseAction, exerciseQuantity, account, overrideOption);
    }

    public void PlaceOrder(EClientSocket client, int orderId, Contract contract, Order order)
    {
        Emit("placeOrder", orderId, $"symbol={contract.Symbol} action={order.Action} type={order.OrderType}");
        client.placeOrder(orderId, contract, order);
    }

    public void RequestMarketDataType(EClientSocket client, int marketDataType)
    {
        Emit("reqMarketDataType", null, $"type={marketDataType}");
        client.reqMarketDataType(marketDataType);
    }

    public void RequestMarketData(EClientSocket client, int requestId, Contract contract, string genericTickList = "")
    {
        Emit("reqMktData", requestId, $"symbol={contract.Symbol} ticks={genericTickList}");
        client.reqMktData(requestId, contract, genericTickList, false, false, new List<TagValue>());
    }

    public void CancelMarketData(EClientSocket client, int requestId)
    {
        Emit("cancelMktData", requestId);
        client.cancelMktData(requestId);
    }

    public void RequestMarketDepth(EClientSocket client, int requestId, Contract contract, int rows, bool isSmartDepth)
    {
        Emit("reqMarketDepth", requestId, $"symbol={contract.Symbol} rows={rows} smart={isSmartDepth}");
        client.reqMarketDepth(requestId, contract, rows, isSmartDepth, new List<TagValue>());
    }

    public void CancelMarketDepth(EClientSocket client, int requestId, bool isSmartDepth)
    {
        Emit("cancelMktDepth", requestId, $"smart={isSmartDepth}");
        client.cancelMktDepth(requestId, isSmartDepth);
    }

    public void RequestRealtimeBars(EClientSocket client, int requestId, Contract contract, string whatToShow, bool useRth)
    {
        Emit("reqRealTimeBars", requestId, $"symbol={contract.Symbol} what={whatToShow} useRth={useRth}");
        client.reqRealTimeBars(requestId, contract, 5, whatToShow, useRth, new List<TagValue>());
    }

    public void CancelRealtimeBars(EClientSocket client, int requestId)
    {
        Emit("cancelRealTimeBars", requestId);
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
        Emit("reqHistoricalData", requestId, $"symbol={contract.Symbol} duration={duration} barSize={barSize} keepUpToDate={keepUpToDate}");
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
        Emit("cancelHistoricalData", requestId);
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
        Emit("reqHistoricalTicks", requestId, $"symbol={contract.Symbol} count={numberOfTicks} what={whatToShow}");
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
        Emit("reqHeadTimestamp", requestId, $"symbol={contract.Symbol} what={whatToShow}");
        client.reqHeadTimestamp(requestId, contract, whatToShow, useRth, formatDate);
    }

    public void CancelHeadTimestamp(EClientSocket client, int requestId)
    {
        Emit("cancelHeadTimestamp", requestId);
        client.cancelHeadTimestamp(requestId);
    }

    public void RequestManagedAccounts(EClientSocket client)
    {
        Emit("reqManagedAccts", null);
        client.reqManagedAccts();
    }

    public void RequestFamilyCodes(EClientSocket client)
    {
        Emit("reqFamilyCodes", null);
        client.reqFamilyCodes();
    }

    public void RequestAccountUpdates(EClientSocket client, bool subscribe, string accountCode)
    {
        Emit("reqAccountUpdates", null, $"subscribe={subscribe} account={accountCode}");
        client.reqAccountUpdates(subscribe, accountCode);
    }

    public void RequestAccountUpdatesMulti(EClientSocket client, int requestId, string accountCode, string modelCode, bool ledgerAndNlv)
    {
        Emit("reqAccountUpdatesMulti", requestId, $"account={accountCode} model={modelCode} ledgerAndNlv={ledgerAndNlv}");
        client.reqAccountUpdatesMulti(requestId, accountCode, modelCode, ledgerAndNlv);
    }

    public void CancelAccountUpdatesMulti(EClientSocket client, int requestId)
    {
        Emit("cancelAccountUpdatesMulti", requestId);
        client.cancelAccountUpdatesMulti(requestId);
    }

    public void RequestAccountSummary(EClientSocket client, int requestId, string groupName, string tags)
    {
        Emit("reqAccountSummary", requestId, $"group={groupName} tags={tags}");
        client.reqAccountSummary(requestId, groupName, tags);
    }

    public void CancelAccountSummary(EClientSocket client, int requestId)
    {
        Emit("cancelAccountSummary", requestId);
        client.cancelAccountSummary(requestId);
    }

    public void RequestPositions(EClientSocket client)
    {
        Emit("reqPositions", null);
        client.reqPositions();
    }

    public void CancelPositions(EClientSocket client)
    {
        Emit("cancelPositions", null);
        client.cancelPositions();
    }

    public void RequestPositionsMulti(EClientSocket client, int requestId, string accountCode, string modelCode)
    {
        Emit("reqPositionsMulti", requestId, $"account={accountCode} model={modelCode}");
        client.reqPositionsMulti(requestId, accountCode, modelCode);
    }

    public void CancelPositionsMulti(EClientSocket client, int requestId)
    {
        Emit("cancelPositionsMulti", requestId);
        client.cancelPositionsMulti(requestId);
    }

    public void RequestPnlAccount(EClientSocket client, int requestId, string accountCode, string modelCode)
    {
        Emit("reqPnL", requestId, $"account={accountCode} model={modelCode}");
        client.reqPnL(requestId, accountCode, modelCode);
    }

    public void CancelPnlAccount(EClientSocket client, int requestId)
    {
        Emit("cancelPnL", requestId);
        client.cancelPnL(requestId);
    }

    public void RequestPnlSingle(EClientSocket client, int requestId, string accountCode, string modelCode, int contractId)
    {
        Emit("reqPnLSingle", requestId, $"account={accountCode} model={modelCode} conId={contractId}");
        client.reqPnLSingle(requestId, accountCode, modelCode, contractId);
    }

    public void CancelPnlSingle(EClientSocket client, int requestId)
    {
        Emit("cancelPnLSingle", requestId);
        client.cancelPnLSingle(requestId);
    }

    public void RequestFaData(EClientSocket client, int faDataType)
    {
        Emit("requestFA", null, $"dataType={faDataType}");
        client.requestFA(faDataType);
    }

    public void RequestFundamentalData(EClientSocket client, int requestId, Contract contract, string reportType)
    {
        Emit("reqFundamentalData", requestId, $"symbol={contract.Symbol} report={reportType}");
        client.reqFundamentalData(requestId, contract, reportType, new List<TagValue>());
    }

    public void CancelFundamentalData(EClientSocket client, int requestId)
    {
        Emit("cancelFundamentalData", requestId);
        client.cancelFundamentalData(requestId);
    }

    public void RequestScannerSubscription(EClientSocket client, int requestId, ScannerSubscription subscription, IReadOnlyList<TagValue> subscriptionOptions, IReadOnlyList<TagValue> filterOptions)
    {
        Emit("reqScannerSubscription", requestId, $"instrument={subscription.Instrument} location={subscription.LocationCode} scan={subscription.ScanCode}");
        client.reqScannerSubscription(requestId, subscription, new List<TagValue>(subscriptionOptions), new List<TagValue>(filterOptions));
    }

    public void CancelScannerSubscription(EClientSocket client, int requestId)
    {
        Emit("cancelScannerSubscription", requestId);
        client.cancelScannerSubscription(requestId);
    }

    public void RequestScannerParameters(EClientSocket client)
    {
        Emit("reqScannerParameters", null);
        client.reqScannerParameters();
    }

    public void RequestHistogramData(EClientSocket client, int requestId, Contract contract, bool useRth, string period)
    {
        Emit("reqHistogramData", requestId, $"symbol={contract.Symbol} useRth={useRth} period={period}");
        client.reqHistogramData(requestId, contract, useRth, period);
    }

    public void QueryDisplayGroups(EClientSocket client, int requestId)
    {
        Emit("queryDisplayGroups", requestId);
        client.queryDisplayGroups(requestId);
    }

    public void SubscribeToDisplayGroupEvents(EClientSocket client, int requestId, int groupId)
    {
        Emit("subscribeToGroupEvents", requestId, $"groupId={groupId}");
        client.subscribeToGroupEvents(requestId, groupId);
    }

    public void UpdateDisplayGroup(EClientSocket client, int requestId, string contractInfo)
    {
        Emit("updateDisplayGroup", requestId, $"contractInfo={contractInfo}");
        client.updateDisplayGroup(requestId, contractInfo);
    }

    public void UnsubscribeFromDisplayGroupEvents(EClientSocket client, int requestId)
    {
        Emit("unsubscribeFromGroupEvents", requestId);
        client.unsubscribeFromGroupEvents(requestId);
    }
}
