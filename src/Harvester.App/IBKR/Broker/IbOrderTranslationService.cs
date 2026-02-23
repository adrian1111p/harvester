using Harvester.App.IBKR.Orders;
using IBApi;

namespace Harvester.App.IBKR.Broker;

public sealed class IbOrderTranslationService
{
    public Order ToIbOrder(BrokerOrderIntent intent)
    {
        var action = NormalizeAction(intent.Action);
        var type = NormalizeType(intent.Type);

        var order = type switch
        {
            "MKT" => OrderFactory.Market(action, intent.Quantity, intent.TimeInForce),
            "LMT" => OrderFactory.Limit(action, intent.Quantity, intent.LimitPrice ?? throw new ArgumentException("Limit price required for LMT order."), intent.TimeInForce),
            "STP" => OrderFactory.Stop(action, intent.Quantity, intent.StopPrice ?? throw new ArgumentException("Stop price required for STP order."), intent.TimeInForce),
            "STP LMT" => OrderFactory.StopLimit(
                action,
                intent.Quantity,
                intent.StopPrice ?? throw new ArgumentException("Stop price required for STP LMT order."),
                intent.LimitPrice ?? throw new ArgumentException("Limit price required for STP LMT order."),
                intent.TimeInForce),
            _ => throw new ArgumentException($"Unsupported order type '{intent.Type}'.")
        };

        order.WhatIf = intent.WhatIf;
        order.Transmit = intent.Transmit;
        order.OrderRef = intent.OrderRef ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(intent.Account))
        {
            order.Account = intent.Account;
        }

        if (!string.IsNullOrWhiteSpace(intent.FaGroup))
        {
            order.FaGroup = intent.FaGroup;
        }

        if (!string.IsNullOrWhiteSpace(intent.FaProfile))
        {
            order.FaProfile = intent.FaProfile;
        }

        if (!string.IsNullOrWhiteSpace(intent.FaMethod))
        {
            order.FaMethod = intent.FaMethod;
        }

        if (!string.IsNullOrWhiteSpace(intent.FaPercentage))
        {
            order.FaPercentage = intent.FaPercentage;
        }

        return order;
    }

    public CanonicalOrderEvent FromOrderStatus(
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
        return new CanonicalOrderEvent(
            timestampUtc,
            "OrderStatus",
            orderId,
            permId,
            string.Empty,
            string.Empty,
            string.Empty,
            status,
            filled,
            remaining,
            avgFillPrice,
            lastFillPrice,
            clientId,
            string.Empty,
            BuildReason(whyHeld, mktCapPrice, parentId));
    }

    public CanonicalOrderEvent FromOpenOrder(DateTime timestampUtc, int orderId, Contract contract, Order order, OrderState orderState)
    {
        return new CanonicalOrderEvent(
            timestampUtc,
            "OpenOrder",
            orderId,
            order.PermId,
            contract.Symbol ?? string.Empty,
            order.Action ?? string.Empty,
            order.OrderType ?? string.Empty,
            orderState.Status ?? string.Empty,
            0,
            order.TotalQuantity,
            0,
            0,
            order.ClientId,
            order.Account ?? string.Empty,
            string.Empty);
    }

    private static string NormalizeAction(string action)
    {
        var normalized = (action ?? string.Empty).Trim().ToUpperInvariant();
        return normalized is "BUY" or "SELL"
            ? normalized
            : throw new ArgumentException($"Unsupported action '{action}'. Use BUY|SELL.");
    }

    private static string NormalizeType(string type)
    {
        var normalized = (type ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "MKT" => "MKT",
            "LMT" => "LMT",
            "STP" => "STP",
            "STP LMT" or "STP_LMT" => "STP LMT",
            _ => throw new ArgumentException($"Unsupported order type '{type}'.")
        };
    }

    private static string BuildReason(string whyHeld, double mktCapPrice, int parentId)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(whyHeld))
        {
            parts.Add($"whyHeld={whyHeld}");
        }

        if (mktCapPrice > 0)
        {
            parts.Add($"mktCapPrice={mktCapPrice}");
        }

        if (parentId > 0)
        {
            parts.Add($"parentId={parentId}");
        }

        return string.Join(';', parts);
    }
}
