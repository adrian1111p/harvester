using Harvester.App.IBKR.Contracts;
using Harvester.App.IBKR.Orders;

namespace Harvester.App.Tests;

public sealed class FactoryTests
{
    [Fact]
    public void ContractFactory_Forex_NormalizesPair()
    {
        var contract = ContractFactory.Forex("eur/usd");

        Assert.Equal("EUR", contract.Symbol);
        Assert.Equal("USD", contract.Currency);
        Assert.Equal("CASH", contract.SecType);
    }

    [Fact]
    public void ContractFactory_Forex_RejectsInvalidPair()
    {
        Assert.Throws<ArgumentException>(() => ContractFactory.Forex("EURUSDX"));
    }

    [Fact]
    public void OrderFactory_Bracket_BuildsParentTakeProfitStop()
    {
        var orders = OrderFactory.Bracket(
            parentOrderId: 100,
            action: "BUY",
            quantity: 10,
            entryLimitPrice: 10.5,
            takeProfitLimitPrice: 11.0,
            stopLossPrice: 10.0);

        Assert.Equal(3, orders.Count);
        Assert.Equal(100, orders[0].OrderId);
        Assert.Equal("BUY", orders[0].Action);
        Assert.Equal("SELL", orders[1].Action);
        Assert.Equal("SELL", orders[2].Action);
        Assert.True(orders[2].Transmit);
    }
}
