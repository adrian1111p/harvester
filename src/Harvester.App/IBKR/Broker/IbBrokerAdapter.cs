using IBApi;

namespace Harvester.App.IBKR.Broker;

public sealed class IbBrokerAdapter : IBrokerAdapter
{
    private readonly IbContractNormalizationService _contracts = new();
    private readonly IbOrderTranslationService _orders = new();

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
}
