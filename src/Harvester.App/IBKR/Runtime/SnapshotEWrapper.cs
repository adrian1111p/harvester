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
    private readonly TaskCompletionSource<bool> _historicalDataEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _historicalDataUpdateTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _histogramDataTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _historicalTicksDoneTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _headTimestampTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _familyCodesTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _accountDownloadEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _accountUpdateMultiEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _positionMultiEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _pnlFirstTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _pnlSingleFirstTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _optionChainEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _optionGreeksFirstTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _faDataFirstTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _displayGroupListTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _displayGroupUpdatedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _fundamentalDataTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _scannerDataEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _scannerParametersTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
    public ConcurrentQueue<HistoricalBarRow> HistoricalBars { get; } = new();
    public ConcurrentQueue<HistoricalBarUpdateRow> HistoricalBarUpdates { get; } = new();
    public ConcurrentQueue<HistogramRow> Histograms { get; } = new();
    public ConcurrentQueue<HistoricalTickRow> HistoricalTicks { get; } = new();
    public ConcurrentQueue<HistoricalTickBidAskRow> HistoricalTicksBidAsk { get; } = new();
    public ConcurrentQueue<HistoricalTickLastRow> HistoricalTicksLast { get; } = new();
    public ConcurrentQueue<HeadTimestampRow> HeadTimestamps { get; } = new();
    public ConcurrentQueue<FamilyCodeRow> FamilyCodesRows { get; } = new();
    public ConcurrentQueue<AccountValueUpdateRow> AccountValueUpdates { get; } = new();
    public ConcurrentQueue<PortfolioUpdateRow> PortfolioUpdates { get; } = new();
    public ConcurrentQueue<AccountUpdateTimeRow> AccountUpdateTimes { get; } = new();
    public ConcurrentQueue<AccountUpdateMultiRow> AccountUpdateMultiRows { get; } = new();
    public ConcurrentQueue<PositionMultiRow> PositionMultiRows { get; } = new();
    public ConcurrentQueue<PnlRow> PnlRows { get; } = new();
    public ConcurrentQueue<PnlSingleRow> PnlSingleRows { get; } = new();
    public ConcurrentQueue<OptionChainRow> OptionChains { get; } = new();
    public ConcurrentQueue<OptionGreekRow> OptionGreeks { get; } = new();
    public ConcurrentQueue<FaDataRow> FaDataRows { get; } = new();
    public ConcurrentQueue<DisplayGroupListRow> DisplayGroupListRows { get; } = new();
    public ConcurrentQueue<DisplayGroupUpdatedRow> DisplayGroupUpdatedRows { get; } = new();
    public ConcurrentQueue<FundamentalDataRow> FundamentalDataRows { get; } = new();
    public ConcurrentQueue<ScannerDataRow> ScannerDataRows { get; } = new();
    public ConcurrentQueue<ScannerParametersRow> ScannerParametersRows { get; } = new();

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
    public Task<bool> HistoricalDataEndTask => _historicalDataEndTcs.Task;
    public Task<bool> HistoricalDataUpdateTask => _historicalDataUpdateTcs.Task;
    public Task<bool> HistogramDataTask => _histogramDataTcs.Task;
    public Task<bool> HistoricalTicksDoneTask => _historicalTicksDoneTcs.Task;
    public Task<bool> HeadTimestampTask => _headTimestampTcs.Task;
    public Task<bool> FamilyCodesTask => _familyCodesTcs.Task;
    public Task<bool> AccountDownloadEndTask => _accountDownloadEndTcs.Task;
    public Task<bool> AccountUpdateMultiEndTask => _accountUpdateMultiEndTcs.Task;
    public Task<bool> PositionMultiEndTask => _positionMultiEndTcs.Task;
    public Task<bool> PnlFirstTask => _pnlFirstTcs.Task;
    public Task<bool> PnlSingleFirstTask => _pnlSingleFirstTcs.Task;
    public Task<bool> OptionChainEndTask => _optionChainEndTcs.Task;
    public Task<bool> OptionGreeksFirstTask => _optionGreeksFirstTcs.Task;
    public Task<bool> FaDataFirstTask => _faDataFirstTcs.Task;
    public Task<bool> DisplayGroupListTask => _displayGroupListTcs.Task;
    public Task<bool> DisplayGroupUpdatedTask => _displayGroupUpdatedTcs.Task;
    public Task<bool> FundamentalDataTask => _fundamentalDataTcs.Task;
    public Task<bool> ScannerDataEndTask => _scannerDataEndTcs.Task;
    public Task<bool> ScannerParametersTask => _scannerParametersTcs.Task;

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

    public override void historicalData(int reqId, Bar bar)
    {
        HistoricalBars.Enqueue(new HistoricalBarRow(
            DateTime.UtcNow,
            reqId,
            bar.Time,
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            bar.Volume,
            bar.WAP,
            bar.Count
        ));
    }

    public override void historicalDataEnd(int reqId, string startDateStr, string endDateStr)
    {
        _historicalDataEndTcs.TrySetResult(true);
    }

    public override void historicalDataUpdate(int reqId, Bar bar)
    {
        HistoricalBarUpdates.Enqueue(new HistoricalBarUpdateRow(
            DateTime.UtcNow,
            reqId,
            bar.Time,
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            bar.Volume,
            bar.WAP,
            bar.Count
        ));
        _historicalDataUpdateTcs.TrySetResult(true);
    }

    public override void histogramData(int reqId, HistogramEntry[] data)
    {
        foreach (var item in data)
        {
            Histograms.Enqueue(new HistogramRow(DateTime.UtcNow, reqId, item.Price, item.Size));
        }

        _histogramDataTcs.TrySetResult(true);
    }

    public override void historicalTicks(int reqId, HistoricalTick[] ticks, bool done)
    {
        foreach (var item in ticks)
        {
            HistoricalTicks.Enqueue(new HistoricalTickRow(
                DateTime.UtcNow,
                reqId,
                item.Time,
                item.Price,
                item.Size
            ));
        }

        if (done)
        {
            _historicalTicksDoneTcs.TrySetResult(true);
        }
    }

    public override void historicalTicksBidAsk(int reqId, HistoricalTickBidAsk[] ticks, bool done)
    {
        foreach (var item in ticks)
        {
            HistoricalTicksBidAsk.Enqueue(new HistoricalTickBidAskRow(
                DateTime.UtcNow,
                reqId,
                item.Time,
                item.PriceBid,
                item.PriceAsk,
                item.SizeBid,
                item.SizeAsk,
                item.TickAttribBidAsk.BidPastLow,
                item.TickAttribBidAsk.AskPastHigh
            ));
        }

        if (done)
        {
            _historicalTicksDoneTcs.TrySetResult(true);
        }
    }

    public override void historicalTicksLast(int reqId, HistoricalTickLast[] ticks, bool done)
    {
        foreach (var item in ticks)
        {
            HistoricalTicksLast.Enqueue(new HistoricalTickLastRow(
                DateTime.UtcNow,
                reqId,
                item.Time,
                item.Price,
                item.Size,
                item.Exchange,
                item.SpecialConditions,
                item.TickAttribLast.PastLimit,
                item.TickAttribLast.Unreported
            ));
        }

        if (done)
        {
            _historicalTicksDoneTcs.TrySetResult(true);
        }
    }

    public override void headTimestamp(int reqId, string headTimestamp)
    {
        HeadTimestamps.Enqueue(new HeadTimestampRow(DateTime.UtcNow, reqId, headTimestamp));
        _headTimestampTcs.TrySetResult(true);
    }

    public override void familyCodes(FamilyCode[] familyCodes)
    {
        foreach (var familyCode in familyCodes)
        {
            FamilyCodesRows.Enqueue(new FamilyCodeRow(
                DateTime.UtcNow,
                familyCode.AccountID,
                familyCode.FamilyCodeStr
            ));
        }

        _familyCodesTcs.TrySetResult(true);
    }

    public override void updateAccountValue(string key, string value, string currency, string accountName)
    {
        AccountValueUpdates.Enqueue(new AccountValueUpdateRow(
            DateTime.UtcNow,
            accountName,
            key,
            value,
            currency
        ));
    }

    public override void updatePortfolio(Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName)
    {
        PortfolioUpdates.Enqueue(new PortfolioUpdateRow(
            DateTime.UtcNow,
            accountName,
            contract.ConId,
            contract.Symbol,
            contract.SecType,
            contract.Currency,
            contract.Exchange,
            position,
            marketPrice,
            marketValue,
            averageCost,
            unrealizedPNL,
            realizedPNL
        ));
    }

    public override void updateAccountTime(string timestamp)
    {
        AccountUpdateTimes.Enqueue(new AccountUpdateTimeRow(DateTime.UtcNow, timestamp));
    }

    public override void accountDownloadEnd(string account)
    {
        _accountDownloadEndTcs.TrySetResult(true);
    }

    public override void accountUpdateMulti(int reqId, string account, string modelCode, string key, string value, string currency)
    {
        AccountUpdateMultiRows.Enqueue(new AccountUpdateMultiRow(
            DateTime.UtcNow,
            reqId,
            account,
            modelCode,
            key,
            value,
            currency
        ));
    }

    public override void accountUpdateMultiEnd(int reqId)
    {
        _accountUpdateMultiEndTcs.TrySetResult(true);
    }

    public override void positionMulti(int reqId, string account, string modelCode, Contract contract, double pos, double avgCost)
    {
        PositionMultiRows.Enqueue(new PositionMultiRow(
            DateTime.UtcNow,
            reqId,
            account,
            modelCode,
            contract.ConId,
            contract.Symbol,
            contract.SecType,
            contract.Currency,
            contract.Exchange,
            pos,
            avgCost
        ));
    }

    public override void positionMultiEnd(int reqId)
    {
        _positionMultiEndTcs.TrySetResult(true);
    }

    public override void pnl(int reqId, double dailyPnL, double unrealizedPnL, double realizedPnL)
    {
        PnlRows.Enqueue(new PnlRow(
            DateTime.UtcNow,
            reqId,
            dailyPnL,
            unrealizedPnL,
            realizedPnL
        ));
        _pnlFirstTcs.TrySetResult(true);
    }

    public override void pnlSingle(int reqId, int pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value)
    {
        PnlSingleRows.Enqueue(new PnlSingleRow(
            DateTime.UtcNow,
            reqId,
            pos,
            dailyPnL,
            unrealizedPnL,
            realizedPnL,
            value
        ));
        _pnlSingleFirstTcs.TrySetResult(true);
    }

    public override void securityDefinitionOptionParameter(int reqId, string exchange, int underlyingConId, string tradingClass, string multiplier, HashSet<string> expirations, HashSet<double> strikes)
    {
        OptionChains.Enqueue(new OptionChainRow(
            DateTime.UtcNow,
            reqId,
            exchange,
            underlyingConId,
            tradingClass,
            multiplier,
            expirations.OrderBy(x => x).ToArray(),
            strikes.OrderBy(x => x).ToArray()
        ));
    }

    public override void securityDefinitionOptionParameterEnd(int reqId)
    {
        _optionChainEndTcs.TrySetResult(true);
    }

    public override void tickOptionComputation(int tickerId, int field, double impliedVolatility, double delta, double optPrice, double pvDividend, double gamma, double vega, double theta, double undPrice)
    {
        OptionGreeks.Enqueue(new OptionGreekRow(
            DateTime.UtcNow,
            tickerId,
            field,
            0,
            impliedVolatility,
            delta,
            optPrice,
            pvDividend,
            gamma,
            vega,
            theta,
            undPrice
        ));

        _optionGreeksFirstTcs.TrySetResult(true);
    }

    public override void receiveFA(int faDataType, string faXmlData)
    {
        FaDataRows.Enqueue(new FaDataRow(
            DateTime.UtcNow,
            faDataType,
            faXmlData
        ));

        _faDataFirstTcs.TrySetResult(true);
    }

    public override void displayGroupList(int reqId, string groups)
    {
        DisplayGroupListRows.Enqueue(new DisplayGroupListRow(
            DateTime.UtcNow,
            reqId,
            groups
        ));

        _displayGroupListTcs.TrySetResult(true);
    }

    public override void displayGroupUpdated(int reqId, string contractInfo)
    {
        DisplayGroupUpdatedRows.Enqueue(new DisplayGroupUpdatedRow(
            DateTime.UtcNow,
            reqId,
            contractInfo
        ));

        _displayGroupUpdatedTcs.TrySetResult(true);
    }

    public override void fundamentalData(int reqId, string data)
    {
        FundamentalDataRows.Enqueue(new FundamentalDataRow(
            DateTime.UtcNow,
            reqId,
            data
        ));

        _fundamentalDataTcs.TrySetResult(true);
    }

    public override void scannerData(int reqId, int rank, ContractDetails contractDetails, string distance, string benchmark, string projection, string legsStr)
    {
        ScannerDataRows.Enqueue(new ScannerDataRow(
            DateTime.UtcNow,
            reqId,
            rank,
            contractDetails.Contract.ConId,
            contractDetails.Contract.Symbol,
            contractDetails.Contract.SecType,
            contractDetails.Contract.Exchange,
            contractDetails.Contract.PrimaryExch,
            contractDetails.Contract.Currency,
            contractDetails.Contract.LocalSymbol,
            contractDetails.Contract.TradingClass,
            distance,
            benchmark,
            projection,
            legsStr
        ));
    }

    public override void scannerDataEnd(int reqId)
    {
        _scannerDataEndTcs.TrySetResult(true);
    }

    public override void scannerParameters(string xml)
    {
        ScannerParametersRows.Enqueue(new ScannerParametersRow(
            DateTime.UtcNow,
            xml
        ));

        _scannerParametersTcs.TrySetResult(true);
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

public sealed record HistoricalBarRow(
    DateTime TimestampUtc,
    int RequestId,
    string Time,
    double Open,
    double High,
    double Low,
    double Close,
    decimal Volume,
    double Wap,
    int Count
);

public sealed record HistoricalBarUpdateRow(
    DateTime TimestampUtc,
    int RequestId,
    string Time,
    double Open,
    double High,
    double Low,
    double Close,
    decimal Volume,
    double Wap,
    int Count
);

public sealed record HistogramRow(
    DateTime TimestampUtc,
    int RequestId,
    double Price,
    decimal Size
);

public sealed record HistoricalTickRow(
    DateTime TimestampUtc,
    int RequestId,
    long EpochSeconds,
    double Price,
    decimal Size
);

public sealed record HistoricalTickBidAskRow(
    DateTime TimestampUtc,
    int RequestId,
    long EpochSeconds,
    double PriceBid,
    double PriceAsk,
    decimal SizeBid,
    decimal SizeAsk,
    bool BidPastLow,
    bool AskPastHigh
);

public sealed record HistoricalTickLastRow(
    DateTime TimestampUtc,
    int RequestId,
    long EpochSeconds,
    double Price,
    decimal Size,
    string Exchange,
    string SpecialConditions,
    bool PastLimit,
    bool Unreported
);

public sealed record HeadTimestampRow(
    DateTime TimestampUtc,
    int RequestId,
    string HeadTimestamp
);

public sealed record FamilyCodeRow(
    DateTime TimestampUtc,
    string AccountId,
    string FamilyCode
);

public sealed record AccountValueUpdateRow(
    DateTime TimestampUtc,
    string Account,
    string Key,
    string Value,
    string Currency
);

public sealed record PortfolioUpdateRow(
    DateTime TimestampUtc,
    string Account,
    int ConId,
    string Symbol,
    string SecurityType,
    string Currency,
    string Exchange,
    double Position,
    double MarketPrice,
    double MarketValue,
    double AverageCost,
    double UnrealizedPnl,
    double RealizedPnl
);

public sealed record AccountUpdateTimeRow(
    DateTime TimestampUtc,
    string AccountTimestamp
);

public sealed record AccountUpdateMultiRow(
    DateTime TimestampUtc,
    int RequestId,
    string Account,
    string ModelCode,
    string Key,
    string Value,
    string Currency
);

public sealed record PositionMultiRow(
    DateTime TimestampUtc,
    int RequestId,
    string Account,
    string ModelCode,
    int ConId,
    string Symbol,
    string SecurityType,
    string Currency,
    string Exchange,
    double Position,
    double AverageCost
);

public sealed record PnlRow(
    DateTime TimestampUtc,
    int RequestId,
    double DailyPnl,
    double UnrealizedPnl,
    double RealizedPnl
);

public sealed record PnlSingleRow(
    DateTime TimestampUtc,
    int RequestId,
    int Position,
    double DailyPnl,
    double UnrealizedPnl,
    double RealizedPnl,
    double Value
);

public sealed record OptionChainRow(
    DateTime TimestampUtc,
    int RequestId,
    string Exchange,
    int UnderlyingConId,
    string TradingClass,
    string Multiplier,
    string[] Expirations,
    double[] Strikes
);

public sealed record OptionGreekRow(
    DateTime TimestampUtc,
    int RequestId,
    int Field,
    int TickAttrib,
    double ImpliedVolatility,
    double Delta,
    double OptionPrice,
    double PvDividend,
    double Gamma,
    double Vega,
    double Theta,
    double UnderlyingPrice
);

public sealed record FaDataRow(
    DateTime TimestampUtc,
    int FaDataType,
    string XmlData
);

public sealed record DisplayGroupListRow(
    DateTime TimestampUtc,
    int RequestId,
    string Groups
);

public sealed record DisplayGroupUpdatedRow(
    DateTime TimestampUtc,
    int RequestId,
    string ContractInfo
);

public sealed record FundamentalDataRow(
    DateTime TimestampUtc,
    int RequestId,
    string Data
);

public sealed record ScannerDataRow(
    DateTime TimestampUtc,
    int RequestId,
    int Rank,
    int ConId,
    string Symbol,
    string SecurityType,
    string Exchange,
    string PrimaryExchange,
    string Currency,
    string LocalSymbol,
    string TradingClass,
    string Distance,
    string Benchmark,
    string Projection,
    string Legs
);

public sealed record ScannerParametersRow(
    DateTime TimestampUtc,
    string Xml
);
