using IBApi;

namespace Harvester.App.IBKR.Orders;

public static class OrderFactory
{
    public static Order Market(string action, double quantity, string tif = "DAY")
    {
        return new Order
        {
            Action = action,
            OrderType = "MKT",
            TotalQuantity = quantity,
            Tif = tif
        };
    }

    public static Order Limit(string action, double quantity, double limitPrice, string tif = "DAY")
    {
        return new Order
        {
            Action = action,
            OrderType = "LMT",
            TotalQuantity = quantity,
            LmtPrice = limitPrice,
            Tif = tif
        };
    }

    public static Order Stop(string action, double quantity, double stopPrice, string tif = "DAY")
    {
        return new Order
        {
            Action = action,
            OrderType = "STP",
            TotalQuantity = quantity,
            AuxPrice = stopPrice,
            Tif = tif
        };
    }

    public static Order StopLimit(string action, double quantity, double stopPrice, double limitPrice, string tif = "DAY")
    {
        return new Order
        {
            Action = action,
            OrderType = "STP LMT",
            TotalQuantity = quantity,
            AuxPrice = stopPrice,
            LmtPrice = limitPrice,
            Tif = tif
        };
    }

    public static IReadOnlyList<Order> Bracket(
        int parentOrderId,
        string action,
        double quantity,
        double entryLimitPrice,
        double takeProfitLimitPrice,
        double stopLossPrice,
        string tif = "DAY")
    {
        var reverseAction = string.Equals(action, "BUY", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";

        var parent = new Order
        {
            OrderId = parentOrderId,
            Action = action,
            OrderType = "LMT",
            TotalQuantity = quantity,
            LmtPrice = entryLimitPrice,
            Tif = tif,
            Transmit = false
        };

        var takeProfit = new Order
        {
            OrderId = parentOrderId + 1,
            Action = reverseAction,
            OrderType = "LMT",
            TotalQuantity = quantity,
            LmtPrice = takeProfitLimitPrice,
            ParentId = parentOrderId,
            Tif = tif,
            Transmit = false
        };

        var stopLoss = new Order
        {
            OrderId = parentOrderId + 2,
            Action = reverseAction,
            OrderType = "STP",
            TotalQuantity = quantity,
            AuxPrice = stopLossPrice,
            ParentId = parentOrderId,
            Tif = tif,
            Transmit = true
        };

        return new[] { parent, takeProfit, stopLoss };
    }

    public static Order MarketOnClose(string action, double quantity)
    {
        return new Order
        {
            Action = action,
            OrderType = "MOC",
            TotalQuantity = quantity,
            Tif = "DAY"
        };
    }

    public static Order LimitOnClose(string action, double quantity, double limitPrice)
    {
        return new Order
        {
            Action = action,
            OrderType = "LOC",
            TotalQuantity = quantity,
            LmtPrice = limitPrice,
            Tif = "DAY"
        };
    }

    public static Order TrailingStop(string action, double quantity, double trailingAmount, double? initialStopPrice = null, string tif = "DAY")
    {
        return new Order
        {
            Action = action,
            OrderType = "TRAIL",
            TotalQuantity = quantity,
            AuxPrice = trailingAmount,
            TrailStopPrice = initialStopPrice ?? 0,
            Tif = tif
        };
    }

    public static Order TrailingStopLimit(
        string action,
        double quantity,
        double trailingAmount,
        double limitOffset,
        double? initialStopPrice = null,
        string tif = "DAY")
    {
        return new Order
        {
            Action = action,
            OrderType = "TRAIL LIMIT",
            TotalQuantity = quantity,
            AuxPrice = trailingAmount,
            LmtPriceOffset = limitOffset,
            TrailStopPrice = initialStopPrice ?? 0,
            Tif = tif
        };
    }

    public static IReadOnlyList<Order> ApplyOcaGroup(IEnumerable<Order> orders, string ocaGroup, int ocaType = 1)
    {
        var result = orders.ToList();
        foreach (var order in result)
        {
            order.OcaGroup = ocaGroup;
            order.OcaType = ocaType;
        }

        return result;
    }

    public static Order MarketIfTouched(string action, double quantity, double triggerPrice, string tif = "DAY")
    {
        return new Order
        {
            Action = action,
            OrderType = "MIT",
            TotalQuantity = quantity,
            AuxPrice = triggerPrice,
            Tif = tif
        };
    }

    public static Order PeggedToMarket(string action, double quantity, double marketOffset, string tif = "DAY")
    {
        return new Order
        {
            Action = action,
            OrderType = "PEG MKT",
            TotalQuantity = quantity,
            AuxPrice = marketOffset,
            Tif = tif
        };
    }

    public static Order PeggedToMidpoint(string action, double quantity, double offset, double? limitPriceCap = null, string tif = "DAY")
    {
        return new Order
        {
            Action = action,
            OrderType = "PEG MID",
            TotalQuantity = quantity,
            AuxPrice = offset,
            LmtPrice = limitPriceCap ?? 0,
            Tif = tif
        };
    }

    public static Order Relative(string action, double quantity, double offset, double? limitPriceCap = null, string tif = "DAY")
    {
        return new Order
        {
            Action = action,
            OrderType = "REL",
            TotalQuantity = quantity,
            AuxPrice = offset,
            LmtPrice = limitPriceCap ?? 0,
            Tif = tif
        };
    }

    public static Order ScaleLimit(
        string action,
        double quantity,
        double limitPrice,
        int initLevelSize,
        int subLevelSize,
        double priceIncrement,
        string tif = "DAY")
    {
        return new Order
        {
            Action = action,
            OrderType = "LMT",
            TotalQuantity = quantity,
            LmtPrice = limitPrice,
            Tif = tif,
            ScaleInitLevelSize = initLevelSize,
            ScaleSubsLevelSize = subLevelSize,
            ScalePriceIncrement = priceIncrement
        };
    }

    public static Order Algo(Order baseOrder, string strategy, params (string Key, string Value)[] parameters)
    {
        baseOrder.AlgoStrategy = strategy;
        baseOrder.AlgoParams = parameters.Select(x => new TagValue(x.Key, x.Value)).ToList();
        return baseOrder;
    }

    public static Order Twap(
        Order baseOrder,
        string startTime,
        string endTime,
        bool allowPastEndTime = false,
        bool noTakeLiq = false,
        string strategyType = "Marketable")
    {
        return Algo(
            baseOrder,
            "Twap",
            ("startTime", startTime),
            ("endTime", endTime),
            ("allowPastEndTime", allowPastEndTime ? "1" : "0"),
            ("noTakeLiq", noTakeLiq ? "1" : "0"),
            ("strategyType", strategyType)
        );
    }

    public static Order Vwap(
        Order baseOrder,
        string startTime,
        string endTime,
        bool allowPastEndTime = false,
        bool noTakeLiq = false,
        double maxPctVol = 0.2)
    {
        return Algo(
            baseOrder,
            "Vwap",
            ("startTime", startTime),
            ("endTime", endTime),
            ("allowPastEndTime", allowPastEndTime ? "1" : "0"),
            ("noTakeLiq", noTakeLiq ? "1" : "0"),
            ("maxPctVol", maxPctVol.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))
        );
    }

    public static Order Adaptive(Order baseOrder, string priority = "Normal")
    {
        baseOrder.AlgoStrategy = "Adaptive";
        baseOrder.AlgoParams =
        [
            new TagValue("adaptivePriority", NormalizeAdaptivePriority(priority))
        ];
        return baseOrder;
    }

    private static string NormalizeAdaptivePriority(string priority)
    {
        return priority.Trim().ToLowerInvariant() switch
        {
            "urgent" => "Urgent",
            "patient" => "Patient",
            _ => "Normal"
        };
    }
}
