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
}
