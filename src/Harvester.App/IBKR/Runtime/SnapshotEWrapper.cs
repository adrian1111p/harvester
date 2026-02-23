using System.Collections.Concurrent;
using Harvester.App.IBKR.Wrapper;
using IBApi;

namespace Harvester.App.IBKR.Runtime;

public sealed class SnapshotEWrapper : HarvesterEWrapper
{
    private readonly TaskCompletionSource<bool> _currentTimeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _accountSummaryEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _openOrderEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _completedOrdersEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _execDetailsEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _positionEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _whatIfOpenOrderTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _topDataFirstTickTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _depthFirstUpdateTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _realtimeBarFirstTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConcurrentQueue<AccountSummaryRow> AccountSummaryRows { get; } = new();
    public ConcurrentQueue<OpenOrderRow> OpenOrders { get; } = new();
    public ConcurrentQueue<CompletedOrderRow> CompletedOrders { get; } = new();
    public ConcurrentQueue<ExecutionRow> Executions { get; } = new();
    public ConcurrentQueue<PositionRow> Positions { get; } = new();
    public ConcurrentQueue<WhatIfOrderStateRow> WhatIfOrderStates { get; } = new();
    public ConcurrentQueue<TopTickRow> TopTicks { get; } = new();
    public ConcurrentQueue<DepthRow> DepthRows { get; } = new();
    public ConcurrentQueue<RealtimeBarRow> RealtimeBars { get; } = new();
    public ConcurrentQueue<MarketDataTypeRow> MarketDataTypes { get; } = new();

    public Task<bool> CurrentTimeTask => _currentTimeTcs.Task;
    public Task<bool> AccountSummaryEndTask => _accountSummaryEndTcs.Task;
    public Task<bool> OpenOrderEndTask => _openOrderEndTcs.Task;
    public Task<bool> CompletedOrdersEndTask => _completedOrdersEndTcs.Task;
    public Task<bool> ExecDetailsEndTask => _execDetailsEndTcs.Task;
    public Task<bool> PositionEndTask => _positionEndTcs.Task;
    public Task<bool> WhatIfOpenOrderTask => _whatIfOpenOrderTcs.Task;
    public Task<bool> TopDataFirstTickTask => _topDataFirstTickTcs.Task;
    public Task<bool> DepthFirstUpdateTask => _depthFirstUpdateTcs.Task;
    public Task<bool> RealtimeBarFirstTask => _realtimeBarFirstTcs.Task;

    public override void currentTime(long time)
    {
        _currentTimeTcs.TrySetResult(true);
    }

    public override void accountSummary(int reqId, string account, string tag, string value, string currency)
    {
        AccountSummaryRows.Enqueue(new AccountSummaryRow(account, tag, value, currency));
    }

    public override void accountSummaryEnd(int reqId)
    {
        _accountSummaryEndTcs.TrySetResult(true);
    }

    public override void openOrder(int orderId, Contract contract, Order order, OrderState orderState)
    {
        base.openOrder(orderId, contract, order, orderState);

        OpenOrders.Enqueue(new OpenOrderRow(
            orderId,
            contract.Symbol,
            contract.SecType,
            contract.Exchange,
            order.Action,
            order.OrderType,
            order.TotalQuantity,
            order.LmtPrice,
            orderState.Status,
            order.Account
        ));

        if (order.WhatIf)
        {
            WhatIfOrderStates.Enqueue(new WhatIfOrderStateRow(
                orderId,
                contract.Symbol,
                order.Action,
                order.OrderType,
                order.TotalQuantity,
                order.LmtPrice,
                order.AuxPrice,
                orderState.Status,
                orderState.WarningText,
                orderState.InitMarginBefore,
                orderState.InitMarginChange,
                orderState.InitMarginAfter,
                orderState.MaintMarginBefore,
                orderState.MaintMarginChange,
                orderState.MaintMarginAfter,
                orderState.EquityWithLoanBefore,
                orderState.EquityWithLoanChange,
                orderState.EquityWithLoanAfter,
                orderState.Commission,
                orderState.MinCommission,
                orderState.MaxCommission,
                orderState.CommissionCurrency
            ));

            _whatIfOpenOrderTcs.TrySetResult(true);
        }
    }

    public override void openOrderEnd()
    {
        _openOrderEndTcs.TrySetResult(true);
    }

    public override void completedOrder(Contract contract, Order order, OrderState orderState)
    {
        CompletedOrders.Enqueue(new CompletedOrderRow(
            contract.Symbol,
            contract.SecType,
            contract.Exchange,
            order.Action,
            order.OrderType,
            order.TotalQuantity,
            order.LmtPrice,
            orderState.Status,
            order.Account,
            order.PermId
        ));
    }

    public override void completedOrdersEnd()
    {
        _completedOrdersEndTcs.TrySetResult(true);
    }

    public override void execDetails(int reqId, Contract contract, Execution execution)
    {
        Executions.Enqueue(new ExecutionRow(
            execution.ExecId,
            execution.OrderId,
            execution.PermId,
            execution.AcctNumber,
            contract.Symbol,
            contract.SecType,
            execution.Side,
            execution.Shares,
            execution.Price,
            execution.Time,
            execution.Exchange,
            execution.ClientId
        ));
    }

    public override void execDetailsEnd(int reqId)
    {
        _execDetailsEndTcs.TrySetResult(true);
    }

    public override void position(string account, Contract contract, double pos, double avgCost)
    {
        Positions.Enqueue(new PositionRow(
            account,
            contract.Symbol,
            contract.SecType,
            contract.Currency,
            contract.Exchange,
            pos,
            avgCost
        ));
    }

    public override void positionEnd()
    {
        _positionEndTcs.TrySetResult(true);
    }

    public override void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
    {
        TopTicks.Enqueue(new TopTickRow(
            DateTime.UtcNow,
            tickerId,
            "tickPrice",
            field,
            price,
            0,
            string.Empty
        ));
        _topDataFirstTickTcs.TrySetResult(true);
    }

    public override void tickSize(int tickerId, int field, int size)
    {
        TopTicks.Enqueue(new TopTickRow(
            DateTime.UtcNow,
            tickerId,
            "tickSize",
            field,
            0,
            size,
            string.Empty
        ));
        _topDataFirstTickTcs.TrySetResult(true);
    }

    public override void tickString(int tickerId, int field, string value)
    {
        TopTicks.Enqueue(new TopTickRow(
            DateTime.UtcNow,
            tickerId,
            "tickString",
            field,
            0,
            0,
            value
        ));
        _topDataFirstTickTcs.TrySetResult(true);
    }

    public override void tickGeneric(int tickerId, int field, double value)
    {
        TopTicks.Enqueue(new TopTickRow(
            DateTime.UtcNow,
            tickerId,
            "tickGeneric",
            field,
            value,
            0,
            string.Empty
        ));
        _topDataFirstTickTcs.TrySetResult(true);
    }

    public override void marketDataType(int reqId, int marketDataType)
    {
        MarketDataTypes.Enqueue(new MarketDataTypeRow(DateTime.UtcNow, reqId, marketDataType));
    }

    public override void updateMktDepth(int tickerId, int position, int operation, int side, double price, int size)
    {
        DepthRows.Enqueue(new DepthRow(
            DateTime.UtcNow,
            tickerId,
            position,
            operation,
            side,
            price,
            size,
            string.Empty,
            false
        ));
        _depthFirstUpdateTcs.TrySetResult(true);
    }

    public override void updateMktDepthL2(int tickerId, int position, string marketMaker, int operation, int side, double price, int size, bool isSmartDepth)
    {
        DepthRows.Enqueue(new DepthRow(
            DateTime.UtcNow,
            tickerId,
            position,
            operation,
            side,
            price,
            size,
            marketMaker,
            isSmartDepth
        ));
        _depthFirstUpdateTcs.TrySetResult(true);
    }

    public override void realtimeBar(int reqId, long time, double open, double high, double low, double close, long volume, double wap, int count)
    {
        RealtimeBars.Enqueue(new RealtimeBarRow(
            DateTime.UtcNow,
            reqId,
            time,
            open,
            high,
            low,
            close,
            volume,
            wap,
            count
        ));
        _realtimeBarFirstTcs.TrySetResult(true);
    }
}

public sealed record AccountSummaryRow(string Account, string Tag, string Value, string Currency);

public sealed record OpenOrderRow(
    int OrderId,
    string Symbol,
    string SecurityType,
    string Exchange,
    string Action,
    string OrderType,
    double TotalQuantity,
    double LimitPrice,
    string Status,
    string Account
);

public sealed record CompletedOrderRow(
    string Symbol,
    string SecurityType,
    string Exchange,
    string Action,
    string OrderType,
    double TotalQuantity,
    double LimitPrice,
    string Status,
    string Account,
    int PermId
);

public sealed record PositionRow(
    string Account,
    string Symbol,
    string SecurityType,
    string Currency,
    string Exchange,
    double Quantity,
    double AverageCost
);

public sealed record ExecutionRow(
    string ExecId,
    int OrderId,
    int PermId,
    string Account,
    string Symbol,
    string SecurityType,
    string Side,
    double Shares,
    double Price,
    string Time,
    string Exchange,
    int ClientId
);

public sealed record WhatIfOrderStateRow(
    int OrderId,
    string Symbol,
    string Action,
    string OrderType,
    double Quantity,
    double LimitPrice,
    double StopPrice,
    string Status,
    string WarningText,
    string InitMarginBefore,
    string InitMarginChange,
    string InitMarginAfter,
    string MaintMarginBefore,
    string MaintMarginChange,
    string MaintMarginAfter,
    string EquityWithLoanBefore,
    string EquityWithLoanChange,
    string EquityWithLoanAfter,
    double Commission,
    double MinCommission,
    double MaxCommission,
    string CommissionCurrency
);

public sealed record TopTickRow(
    DateTime TimestampUtc,
    int TickerId,
    string Kind,
    int Field,
    double Price,
    int Size,
    string Value
);

public sealed record MarketDataTypeRow(
    DateTime TimestampUtc,
    int RequestId,
    int MarketDataType
);

public sealed record DepthRow(
    DateTime TimestampUtc,
    int TickerId,
    int Position,
    int Operation,
    int Side,
    double Price,
    int Size,
    string MarketMaker,
    bool IsSmartDepth
);

public sealed record RealtimeBarRow(
    DateTime TimestampUtc,
    int RequestId,
    long EpochSeconds,
    double Open,
    double High,
    double Low,
    double Close,
    long Volume,
    double Wap,
    int Count
);
