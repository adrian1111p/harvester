using Harvester.App.IBKR.Broker;
using Harvester.App.IBKR.Runtime;
using IBApi;
using Moq;

namespace Harvester.App.Tests;

public sealed class SnapshotEWrapperIntegrationTests
{
    [Fact]
    public void OpenOrder_EnqueuesCanonicalEventFromBrokerAdapter()
    {
        var expected = new CanonicalOrderEvent(
            DateTime.UtcNow,
            "openOrder",
            42,
            777,
            "AAPL",
            "BUY",
            "LMT",
            "Submitted",
            0,
            100,
            0,
            0,
            1,
            "DU123",
            "ok");

        var adapter = new Mock<IBrokerAdapter>(MockBehavior.Strict);
        adapter.Setup(a => a.TranslateOpenOrder(
                It.IsAny<DateTime>(),
                42,
                It.IsAny<Contract>(),
                It.IsAny<Order>(),
                It.IsAny<OrderState>()))
            .Returns(expected);

        var wrapper = new SnapshotEWrapper(adapter.Object);
        var contract = new Contract { Symbol = "AAPL", SecType = "STK", Exchange = "SMART" };
        var order = new Order { Action = "BUY", OrderType = "LMT", TotalQuantity = 100, LmtPrice = 190.25, Account = "DU123" };
        var orderState = new OrderState { Status = "Submitted" };

        wrapper.openOrder(42, contract, order, orderState);

        Assert.True(wrapper.CanonicalOrderEvents.TryDequeue(out var actual));
        Assert.Equal(expected, actual);
        adapter.VerifyAll();
    }

    [Fact]
    public void OrderStatus_EnqueuesCanonicalEventFromBrokerAdapter()
    {
        var expected = new CanonicalOrderEvent(
            DateTime.UtcNow,
            "orderStatus",
            51,
            800,
            "AAPL",
            "BUY",
            "LMT",
            "Filled",
            100,
            0,
            191,
            191,
            1,
            "DU123",
            "ok");

        var adapter = new Mock<IBrokerAdapter>(MockBehavior.Strict);
        adapter.Setup(a => a.TranslateOrderStatus(
                It.IsAny<DateTime>(),
                51,
                "Filled",
                100,
                0,
                191,
                800,
                0,
                191,
                1,
                string.Empty,
                0))
            .Returns(expected);

        var wrapper = new SnapshotEWrapper(adapter.Object);

        wrapper.orderStatus(51, "Filled", 100, 0, 191, 800, 0, 191, 1, string.Empty, 0);

        Assert.True(wrapper.CanonicalOrderEvents.TryDequeue(out var actual));
        Assert.Equal(expected, actual);
        adapter.VerifyAll();
    }
}
