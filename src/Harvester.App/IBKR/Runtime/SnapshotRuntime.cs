using System.Text.Json;
using Harvester.App.IBKR.Connection;
using Harvester.App.IBKR.Contracts;
using Harvester.App.IBKR.Orders;
using IBApi;

namespace Harvester.App.IBKR.Runtime;

public sealed class SnapshotRuntime
{
    private readonly AppOptions _options;
    private readonly SnapshotEWrapper _wrapper;

    public SnapshotRuntime(AppOptions options)
    {
        _options = options;
        _wrapper = new SnapshotEWrapper();
    }

    public async Task<int> RunAsync()
    {
        Console.WriteLine($"[INFO] Mode={_options.Mode} host={_options.Host}:{_options.Port} clientId={_options.ClientId}");

        using var session = new IbkrSession(_wrapper);
        try
        {
            await session.ConnectAsync(_options.Host, _options.Port, _options.ClientId, _options.TimeoutSeconds);
            Console.WriteLine($"[OK] nextValidId={await _wrapper.NextValidIdTask}");
            Console.WriteLine($"[OK] managedAccounts={await _wrapper.ManagedAccountsTask}");

            var client = session.Client;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            client.reqCurrentTime();
            await AwaitWithTimeout(_wrapper.CurrentTimeTask, timeoutCts.Token, "currentTime");
            Console.WriteLine("[OK] currentTime callback received");

            switch (_options.Mode)
            {
                case RunMode.Connect:
                    await RunConnectMode(client, timeoutCts.Token);
                    break;
                case RunMode.Orders:
                    await RunOrdersMode(client, timeoutCts.Token);
                    break;
                case RunMode.Positions:
                    await RunPositionsMode(client, timeoutCts.Token);
                    break;
                case RunMode.SnapshotAll:
                    await RunSnapshotAllMode(client, timeoutCts.Token);
                    break;
                case RunMode.ContractsValidate:
                    await RunContractsValidateMode(client, timeoutCts.Token);
                    break;
                case RunMode.OrdersDryRun:
                    await RunOrdersDryRunMode();
                    break;
                case RunMode.OrdersPlaceSim:
                    await RunOrdersPlaceSimMode(client, timeoutCts.Token);
                    break;
                case RunMode.OrdersWhatIf:
                    await RunOrdersWhatIfMode(client, timeoutCts.Token);
                    break;
                case RunMode.TopData:
                    await RunTopDataMode(client, timeoutCts.Token);
                    break;
                case RunMode.MarketDepth:
                    await RunMarketDepthMode(client, timeoutCts.Token);
                    break;
                case RunMode.RealtimeBars:
                    await RunRealtimeBarsMode(client, timeoutCts.Token);
                    break;
                case RunMode.MarketDataAll:
                    await RunMarketDataAllMode(client, timeoutCts.Token);
                    break;
                case RunMode.HistoricalBars:
                    await RunHistoricalBarsMode(client, timeoutCts.Token);
                    break;
                case RunMode.HistoricalBarsKeepUpToDate:
                    await RunHistoricalBarsKeepUpToDateMode(client, timeoutCts.Token);
                    break;
                case RunMode.Histogram:
                    await RunHistogramMode(client, timeoutCts.Token);
                    break;
                case RunMode.HistoricalTicks:
                    await RunHistoricalTicksMode(client, timeoutCts.Token);
                    break;
                case RunMode.HeadTimestamp:
                    await RunHeadTimestampMode(client, timeoutCts.Token);
                    break;
            }

            var hasBlockingErrors = _wrapper.Errors.Any(IsBlockingError);
            if (hasBlockingErrors)
            {
                Console.WriteLine("[WARN] Completed with blocking API errors.");
                PrintErrors();
                return 1;
            }

            Console.WriteLine("[PASS] Completed successfully.");
            return 0;
        }
        catch (TimeoutException ex)
        {
            Console.WriteLine($"[FAIL] {ex.Message}");
            PrintErrors();
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {ex.Message}");
            PrintErrors();
            return 2;
        }
    }

    private async Task RunConnectMode(EClientSocket client, CancellationToken token)
    {
        const int summaryReqId = 9001;
        client.reqAccountSummary(summaryReqId, "All", "AccountType,NetLiquidation,TotalCashValue,BuyingPower");
        await AwaitWithTimeout(_wrapper.AccountSummaryEndTask, token, "accountSummaryEnd");
        client.cancelAccountSummary(summaryReqId);

        Console.WriteLine("\n=== Account Summary Rows ===");
        foreach (var row in _wrapper.AccountSummaryRows)
        {
            Console.WriteLine($"[ACCOUNT] {row.Account} {row.Tag}={row.Value} {row.Currency}");
        }
    }

    private async Task RunOrdersMode(EClientSocket client, CancellationToken token)
    {
        client.reqOpenOrders();
        await AwaitWithTimeout(_wrapper.OpenOrderEndTask, token, "openOrderEnd");

        client.reqCompletedOrders(true);
        await AwaitWithTimeout(_wrapper.CompletedOrdersEndTask, token, "completedOrdersEnd");

        client.reqExecutions(9201, new ExecutionFilter());
        await AwaitWithTimeout(_wrapper.ExecDetailsEndTask, token, "execDetailsEnd");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var openOrdersPath = Path.Combine(outputDir, $"open_orders_{timestamp}.json");
        var completedOrdersPath = Path.Combine(outputDir, $"completed_orders_{timestamp}.json");
        var executionsPath = Path.Combine(outputDir, $"executions_{timestamp}.json");

        WriteJson(openOrdersPath, _wrapper.OpenOrders.ToArray());
        WriteJson(completedOrdersPath, _wrapper.CompletedOrders.ToArray());
        WriteJson(executionsPath, _wrapper.Executions.ToArray());

        Console.WriteLine($"[OK] Open orders snapshot: {openOrdersPath} (rows={_wrapper.OpenOrders.Count})");
        Console.WriteLine($"[OK] Completed orders snapshot: {completedOrdersPath} (rows={_wrapper.CompletedOrders.Count})");
        Console.WriteLine($"[OK] Executions snapshot: {executionsPath} (rows={_wrapper.Executions.Count})");
    }

    private async Task RunPositionsMode(EClientSocket client, CancellationToken token)
    {
        const int summaryReqId = 9001;
        client.reqAccountSummary(summaryReqId, "All", "AccountType,NetLiquidation,TotalCashValue,BuyingPower,MaintMarginReq,AvailableFunds");
        client.reqPositions();

        await AwaitWithTimeout(_wrapper.AccountSummaryEndTask, token, "accountSummaryEnd");
        await AwaitWithTimeout(_wrapper.PositionEndTask, token, "positionEnd");

        client.cancelAccountSummary(summaryReqId);
        client.cancelPositions();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var accountSummaryPath = Path.Combine(outputDir, $"account_summary_{timestamp}.json");
        var positionsPath = Path.Combine(outputDir, $"positions_{timestamp}.json");

        WriteJson(accountSummaryPath, _wrapper.AccountSummaryRows.ToArray());
        WriteJson(positionsPath, _wrapper.Positions.ToArray());

        Console.WriteLine($"[OK] Account summary export: {accountSummaryPath} (rows={_wrapper.AccountSummaryRows.Count})");
        Console.WriteLine($"[OK] Positions export: {positionsPath} (rows={_wrapper.Positions.Count})");
    }

    private async Task RunSnapshotAllMode(EClientSocket client, CancellationToken token)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();

        client.reqOpenOrders();
        await AwaitWithTimeout(_wrapper.OpenOrderEndTask, token, "openOrderEnd");

        client.reqCompletedOrders(true);
        await AwaitWithTimeout(_wrapper.CompletedOrdersEndTask, token, "completedOrdersEnd");

        client.reqExecutions(9201, new ExecutionFilter());
        await AwaitWithTimeout(_wrapper.ExecDetailsEndTask, token, "execDetailsEnd");

        const int summaryReqId = 9001;
        client.reqAccountSummary(summaryReqId, "All", "AccountType,NetLiquidation,TotalCashValue,BuyingPower,MaintMarginReq,AvailableFunds");
        client.reqPositions();
        await AwaitWithTimeout(_wrapper.AccountSummaryEndTask, token, "accountSummaryEnd");
        await AwaitWithTimeout(_wrapper.PositionEndTask, token, "positionEnd");
        client.cancelAccountSummary(summaryReqId);
        client.cancelPositions();

        var openOrdersPath = Path.Combine(outputDir, $"open_orders_{timestamp}.json");
        var completedOrdersPath = Path.Combine(outputDir, $"completed_orders_{timestamp}.json");
        var executionsPath = Path.Combine(outputDir, $"executions_{timestamp}.json");
        var accountSummaryPath = Path.Combine(outputDir, $"account_summary_{timestamp}.json");
        var positionsPath = Path.Combine(outputDir, $"positions_{timestamp}.json");
        var reportPath = Path.Combine(outputDir, $"snapshot_report_{timestamp}.md");

        WriteJson(openOrdersPath, _wrapper.OpenOrders.ToArray());
        WriteJson(completedOrdersPath, _wrapper.CompletedOrders.ToArray());
        WriteJson(executionsPath, _wrapper.Executions.ToArray());
        WriteJson(accountSummaryPath, _wrapper.AccountSummaryRows.ToArray());
        WriteJson(positionsPath, _wrapper.Positions.ToArray());
        File.WriteAllText(reportPath, BuildReport(timestamp));

        Console.WriteLine($"[OK] Open orders: {openOrdersPath} (rows={_wrapper.OpenOrders.Count})");
        Console.WriteLine($"[OK] Completed orders: {completedOrdersPath} (rows={_wrapper.CompletedOrders.Count})");
        Console.WriteLine($"[OK] Executions: {executionsPath} (rows={_wrapper.Executions.Count})");
        Console.WriteLine($"[OK] Account summary: {accountSummaryPath} (rows={_wrapper.AccountSummaryRows.Count})");
        Console.WriteLine($"[OK] Positions: {positionsPath} (rows={_wrapper.Positions.Count})");
        Console.WriteLine($"[OK] Snapshot report: {reportPath}");
    }

    private async Task RunContractsValidateMode(EClientSocket client, CancellationToken token)
    {
        var contract = ContractFactory.Stock(_options.Symbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        client.reqContractDetails(9301, contract);
        await AwaitWithTimeout(_wrapper.ContractDetailsEndTask, token, "contractDetailsEnd");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var detailsPath = Path.Combine(outputDir, $"contract_details_{_options.Symbol}_{timestamp}.json");

        var details = _wrapper.ContractDetailsRows.Select(d => new ContractDetailsRow(
            d.Contract.ConId,
            d.Contract.Symbol,
            d.Contract.SecType,
            d.Contract.Exchange,
            d.Contract.PrimaryExch,
            d.Contract.Currency,
            d.Contract.LocalSymbol,
            d.Contract.TradingClass,
            d.MarketName,
            d.LongName,
            d.MinTick
        )).ToArray();

        WriteJson(detailsPath, details);
        Console.WriteLine($"[OK] Contract details export: {detailsPath} (rows={details.Length})");
    }

    private Task RunOrdersDryRunMode()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"order_templates_{timestamp}.json");

        const double qty = 10;
        const double limitPrice = 20.50;
        const double stopPrice = 20.10;

        var templates = new List<OrderTemplateRow>
        {
            ToTemplate("MKT_BUY", OrderFactory.Market("BUY", qty)),
            ToTemplate("LMT_BUY", OrderFactory.Limit("BUY", qty, limitPrice)),
            ToTemplate("STP_SELL", OrderFactory.Stop("SELL", qty, stopPrice)),
            ToTemplate("STP_LMT_BUY", OrderFactory.StopLimit("BUY", qty, stopPrice, limitPrice)),
            ToTemplate("MIT_BUY", OrderFactory.MarketIfTouched("BUY", qty, triggerPrice: 20.20)),
            ToTemplate("PEG_MKT_BUY", OrderFactory.PeggedToMarket("BUY", qty, marketOffset: 0.02)),
            ToTemplate("PEG_MID_BUY", OrderFactory.PeggedToMidpoint("BUY", qty, offset: 0.01, limitPriceCap: 20.70)),
            ToTemplate("REL_BUY", OrderFactory.Relative("BUY", qty, offset: 0.02, limitPriceCap: 20.80)),
            ToTemplate("TRAIL_SELL", OrderFactory.TrailingStop("SELL", qty, trailingAmount: 0.25, initialStopPrice: 20.25)),
            ToTemplate("TRAIL_LMT_SELL", OrderFactory.TrailingStopLimit("SELL", qty, trailingAmount: 0.25, limitOffset: 0.05, initialStopPrice: 20.25)),
            ToTemplate("MOC_SELL", OrderFactory.MarketOnClose("SELL", qty)),
            ToTemplate("LOC_SELL", OrderFactory.LimitOnClose("SELL", qty, 20.60)),
            ToTemplate("SCALE_LMT_BUY", OrderFactory.ScaleLimit("BUY", qty * 5, limitPrice: 20.40, initLevelSize: 10, subLevelSize: 5, priceIncrement: 0.01))
        };

        var bracket = OrderFactory.Bracket(
            parentOrderId: 1000,
            action: "BUY",
            quantity: qty,
            entryLimitPrice: limitPrice,
            takeProfitLimitPrice: 21.00,
            stopLossPrice: 19.90
        );

        templates.AddRange(bracket.Select((o, i) => ToTemplate($"BRACKET_{i}", o)));

        var ocaOrders = OrderFactory.ApplyOcaGroup(new[]
        {
            OrderFactory.Limit("SELL", qty, 21.00),
            OrderFactory.Stop("SELL", qty, 19.90)
        }, ocaGroup: "HARVESTER_OCA_1");
        templates.AddRange(ocaOrders.Select((o, i) => ToTemplate($"OCA_{i}", o)));

        var adaptive = OrderFactory.Adaptive(OrderFactory.Limit("BUY", qty, 20.55), priority: "Normal");
        templates.Add(ToTemplate("ADAPTIVE_LMT_BUY", adaptive));

        var twap = OrderFactory.Twap(
            OrderFactory.Limit("BUY", qty * 2, 20.45),
            startTime: "09:35:00 US/Eastern",
            endTime: "15:45:00 US/Eastern",
            allowPastEndTime: false,
            noTakeLiq: false,
            strategyType: "Marketable"
        );
        templates.Add(ToTemplate("TWAP_LMT_BUY", twap));

        var vwap = OrderFactory.Vwap(
            OrderFactory.Limit("BUY", qty * 2, 20.45),
            startTime: "09:35:00 US/Eastern",
            endTime: "15:45:00 US/Eastern",
            allowPastEndTime: false,
            noTakeLiq: false,
            maxPctVol: 0.2
        );
        templates.Add(ToTemplate("VWAP_LMT_BUY", vwap));

        WriteJson(path, templates);
        Console.WriteLine($"[OK] Order templates dry-run export: {path} (rows={templates.Count})");
        Console.WriteLine("[INFO] Dry-run only: no orders transmitted to IBKR.");
        return Task.CompletedTask;
    }

    private async Task RunOrdersPlaceSimMode(EClientSocket client, CancellationToken token)
    {
        ValidateLiveSafetyInputs();

        var notional = _options.LiveQuantity * _options.LiveLimitPrice;
        if (notional > _options.MaxNotional)
        {
            throw new InvalidOperationException($"Live order blocked: notional {notional:F2} exceeds max-notional {_options.MaxNotional:F2}.");
        }

        if (!_options.EnableLive)
        {
            throw new InvalidOperationException("Live order blocked: set --enable-live true to allow transmission.");
        }

        var contract = ContractFactory.Stock(_options.LiveSymbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        var order = OrderFactory.Limit(_options.LiveAction, _options.LiveQuantity, _options.LiveLimitPrice);
        var nextOrderId = await _wrapper.NextValidIdTask;
        order.OrderId = nextOrderId;
        order.OrderRef = $"HARVESTER_SIM_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        order.Transmit = true;

        client.placeOrder(order.OrderId, contract, order);
        Console.WriteLine($"[OK] Sim order transmitted: orderId={order.OrderId} symbol={_options.LiveSymbol} action={_options.LiveAction} qty={_options.LiveQuantity} lmt={_options.LiveLimitPrice}");

        await Task.Delay(TimeSpan.FromSeconds(4), token);
        client.reqOpenOrders();
        await AwaitWithTimeout(_wrapper.OpenOrderEndTask, token, "openOrderEnd");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var placementPath = Path.Combine(outputDir, $"sim_order_{timestamp}.json");
        var statusPath = Path.Combine(outputDir, $"sim_order_status_{timestamp}.json");

        var placement = new LiveOrderPlacementRow(
            timestamp,
            order.OrderId,
            _options.LiveSymbol,
            _options.LiveAction,
            _options.LiveQuantity,
            _options.LiveLimitPrice,
            notional,
            _options.Account,
            order.OrderRef
        );

        WriteJson(placementPath, new[] { placement });
        WriteJson(statusPath, _wrapper.OrderStatusRows.ToArray());

        Console.WriteLine($"[OK] Sim placement export: {placementPath}");
        Console.WriteLine($"[OK] Sim status export: {statusPath} (rows={_wrapper.OrderStatusRows.Count})");
    }

    private async Task RunOrdersWhatIfMode(EClientSocket client, CancellationToken token)
    {
        var nextOrderId = await _wrapper.NextValidIdTask;
        var contract = ContractFactory.Stock(_options.LiveSymbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        var order = BuildWhatIfOrderTemplate(_options.WhatIfTemplate, _options.LiveAction, _options.LiveQuantity, _options.LiveLimitPrice);

        order.OrderId = nextOrderId;
        order.WhatIf = true;
        order.Transmit = true;
        order.OrderRef = $"HARVESTER_WHATIF_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

        client.placeOrder(order.OrderId, contract, order);
        client.reqOpenOrders();

        var whatIfTask = _wrapper.WhatIfOpenOrderTask;
        var openEndTask = _wrapper.OpenOrderEndTask;
        var waitTask = Task.WhenAny(whatIfTask, openEndTask, Task.Delay(TimeSpan.FromSeconds(12), token));
        await waitTask;

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"whatif_{_options.WhatIfTemplate}_{timestamp}.json");
        var statusPath = Path.Combine(outputDir, $"whatif_status_{_options.WhatIfTemplate}_{timestamp}.json");
        var errorPath = Path.Combine(outputDir, $"whatif_errors_{_options.WhatIfTemplate}_{timestamp}.json");

        WriteJson(path, _wrapper.WhatIfOrderStates.ToArray());
        WriteJson(statusPath, _wrapper.OrderStatusRows.ToArray());
        WriteJson(errorPath, _wrapper.Errors.ToArray());

        Console.WriteLine($"[OK] What-if export: {path} (rows={_wrapper.WhatIfOrderStates.Count})");
        Console.WriteLine($"[OK] What-if status export: {statusPath} (rows={_wrapper.OrderStatusRows.Count})");
        Console.WriteLine($"[OK] What-if errors export: {errorPath} (rows={_wrapper.Errors.Count})");

        if (_wrapper.WhatIfOrderStates.IsEmpty)
        {
            throw new InvalidOperationException("What-if response not returned by TWS for this request. Check TWS/API permissions and account route settings.");
        }

        Console.WriteLine("[INFO] What-if only: no live transmission.");
    }

    private static Order BuildWhatIfOrderTemplate(string template, string action, double quantity, double limitPrice)
    {
        return template.ToLowerInvariant() switch
        {
            "mkt" => OrderFactory.Market(action, quantity),
            "lmt" => OrderFactory.Limit(action, quantity, limitPrice),
            "stp" => OrderFactory.Stop(action, quantity, stopPrice: Math.Max(0.01, limitPrice - 0.2)),
            "stp_lmt" => OrderFactory.StopLimit(action, quantity, stopPrice: Math.Max(0.01, limitPrice - 0.2), limitPrice),
            "mit" => OrderFactory.MarketIfTouched(action, quantity, triggerPrice: limitPrice),
            "trailing" => OrderFactory.TrailingStop(action, quantity, trailingAmount: 0.25),
            "adaptive" => OrderFactory.Adaptive(OrderFactory.Limit(action, quantity, limitPrice), "Normal"),
            _ => throw new InvalidOperationException("Unsupported --whatif-template. Use mkt|lmt|stp|stp_lmt|mit|trailing|adaptive")
        };
    }

    private async Task RunTopDataMode(EClientSocket client, CancellationToken token)
    {
        var contract = ContractFactory.Stock(_options.Symbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        const int reqId = 9401;

        client.reqMarketDataType(_options.MarketDataType);
        client.reqMktData(reqId, contract, string.Empty, false, false, new List<TagValue>());

        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        client.cancelMktData(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var ticksPath = Path.Combine(outputDir, $"top_data_{_options.Symbol}_{timestamp}.json");
        var typesPath = Path.Combine(outputDir, $"top_data_type_{_options.Symbol}_{timestamp}.json");

        WriteJson(ticksPath, _wrapper.TopTicks.ToArray());
        WriteJson(typesPath, _wrapper.MarketDataTypes.ToArray());

        Console.WriteLine($"[OK] Top data export: {ticksPath} (rows={_wrapper.TopTicks.Count})");
        Console.WriteLine($"[OK] Market data type export: {typesPath} (rows={_wrapper.MarketDataTypes.Count})");
    }

    private async Task RunMarketDepthMode(EClientSocket client, CancellationToken token)
    {
        var contract = ContractFactory.Stock(_options.Symbol, exchange: _options.DepthExchange, primaryExchange: _options.PrimaryExchange);
        const int reqId = 9402;

        client.reqMarketDataType(_options.MarketDataType);
        client.reqMarketDepth(reqId, contract, _options.DepthRows, false, new List<TagValue>());

        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        client.cancelMktDepth(reqId, false);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var depthPath = Path.Combine(outputDir, $"depth_data_{_options.Symbol}_{timestamp}.json");

        WriteJson(depthPath, _wrapper.DepthRows.ToArray());
        Console.WriteLine($"[OK] Depth data export: {depthPath} (rows={_wrapper.DepthRows.Count})");
    }

    private async Task RunRealtimeBarsMode(EClientSocket client, CancellationToken token)
    {
        var contract = ContractFactory.Stock(_options.Symbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        const int reqId = 9403;

        client.reqMarketDataType(_options.MarketDataType);
        client.reqRealTimeBars(reqId, contract, 5, _options.RealTimeBarsWhatToShow, false, new List<TagValue>());

        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        client.cancelRealTimeBars(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var barsPath = Path.Combine(outputDir, $"realtime_bars_{_options.Symbol}_{timestamp}.json");

        WriteJson(barsPath, _wrapper.RealtimeBars.ToArray());
        Console.WriteLine($"[OK] Realtime bars export: {barsPath} (rows={_wrapper.RealtimeBars.Count})");
    }

    private async Task RunMarketDataAllMode(EClientSocket client, CancellationToken token)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();

        var topContract = ContractFactory.Stock(_options.Symbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        var depthContract = ContractFactory.Stock(_options.Symbol, exchange: _options.DepthExchange, primaryExchange: _options.PrimaryExchange);

        const int topReqId = 9501;
        const int depthReqId = 9502;
        const int barsReqId = 9503;

        client.reqMarketDataType(_options.MarketDataType);
        client.reqMktData(topReqId, topContract, string.Empty, false, false, new List<TagValue>());
        client.reqMarketDepth(depthReqId, depthContract, _options.DepthRows, false, new List<TagValue>());
        client.reqRealTimeBars(barsReqId, topContract, 5, _options.RealTimeBarsWhatToShow, false, new List<TagValue>());

        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);

        client.cancelMktData(topReqId);
        client.cancelMktDepth(depthReqId, false);
        client.cancelRealTimeBars(barsReqId);

        var topPath = Path.Combine(outputDir, $"top_data_{_options.Symbol}_{timestamp}.json");
        var typePath = Path.Combine(outputDir, $"top_data_type_{_options.Symbol}_{timestamp}.json");
        var depthPath = Path.Combine(outputDir, $"depth_data_{_options.Symbol}_{timestamp}.json");
        var barsPath = Path.Combine(outputDir, $"realtime_bars_{_options.Symbol}_{timestamp}.json");
        var reportPath = Path.Combine(outputDir, $"market_data_report_{_options.Symbol}_{timestamp}.md");

        WriteJson(topPath, _wrapper.TopTicks.ToArray());
        WriteJson(typePath, _wrapper.MarketDataTypes.ToArray());
        WriteJson(depthPath, _wrapper.DepthRows.ToArray());
        WriteJson(barsPath, _wrapper.RealtimeBars.ToArray());

        var report =
            "# Harvester Market Data Report\n\n"
            + $"- Timestamp (UTC): {timestamp}\n"
            + $"- Symbol: {_options.Symbol}\n"
            + $"- MarketDataType requested: {_options.MarketDataType}\n"
            + $"- Capture seconds: {_options.CaptureSeconds}\n"
            + $"- Top ticks: {_wrapper.TopTicks.Count}\n"
            + $"- Depth rows: {_wrapper.DepthRows.Count}\n"
            + $"- Realtime bars: {_wrapper.RealtimeBars.Count}\n"
            + "\n"
            + "## Files\n"
            + $"- top: {topPath}\n"
            + $"- marketDataType: {typePath}\n"
            + $"- depth: {depthPath}\n"
            + $"- realtime bars: {barsPath}\n";

        File.WriteAllText(reportPath, report);

        Console.WriteLine($"[OK] Top data export: {topPath} (rows={_wrapper.TopTicks.Count})");
        Console.WriteLine($"[OK] Market data type export: {typePath} (rows={_wrapper.MarketDataTypes.Count})");
        Console.WriteLine($"[OK] Depth data export: {depthPath} (rows={_wrapper.DepthRows.Count})");
        Console.WriteLine($"[OK] Realtime bars export: {barsPath} (rows={_wrapper.RealtimeBars.Count})");
        Console.WriteLine($"[OK] Market data report: {reportPath}");
    }

    private async Task RunHistoricalBarsMode(EClientSocket client, CancellationToken token)
    {
        ValidateHistoricalBarRequestLimitations(_options.HistoricalDuration, _options.HistoricalBarSize);

        var contract = ContractFactory.Stock(_options.Symbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        const int reqId = 9601;

        client.reqHistoricalData(
            reqId,
            contract,
            _options.HistoricalEndDateTime,
            _options.HistoricalDuration,
            _options.HistoricalBarSize,
            _options.HistoricalWhatToShow,
            _options.HistoricalUseRth,
            _options.HistoricalFormatDate,
            false,
            new List<TagValue>()
        );

        await AwaitWithTimeout(_wrapper.HistoricalDataEndTask, token, "historicalDataEnd");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var barsPath = Path.Combine(outputDir, $"historical_bars_{_options.Symbol}_{timestamp}.json");

        WriteJson(barsPath, _wrapper.HistoricalBars.ToArray());
        Console.WriteLine($"[OK] Historical bars export: {barsPath} (rows={_wrapper.HistoricalBars.Count})");
    }

    private async Task RunHistoricalBarsKeepUpToDateMode(EClientSocket client, CancellationToken token)
    {
        ValidateHistoricalBarRequestLimitations(_options.HistoricalDuration, _options.HistoricalBarSize);

        if (!string.IsNullOrWhiteSpace(_options.HistoricalEndDateTime))
        {
            throw new InvalidOperationException("Historical keepUpToDate requires empty --hist-end.");
        }

        if (BarSizeToSeconds(_options.HistoricalBarSize) < 5)
        {
            throw new InvalidOperationException("Historical keepUpToDate requires bar size >= 5 secs.");
        }

        var contract = ContractFactory.Stock(_options.Symbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        const int reqId = 9602;

        client.reqHistoricalData(
            reqId,
            contract,
            string.Empty,
            _options.HistoricalDuration,
            _options.HistoricalBarSize,
            _options.HistoricalWhatToShow,
            _options.HistoricalUseRth,
            _options.HistoricalFormatDate,
            true,
            new List<TagValue>()
        );

        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        client.cancelHistoricalData(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var barsPath = Path.Combine(outputDir, $"historical_bars_keepup_{_options.Symbol}_{timestamp}.json");
        var updatesPath = Path.Combine(outputDir, $"historical_bars_updates_{_options.Symbol}_{timestamp}.json");

        WriteJson(barsPath, _wrapper.HistoricalBars.ToArray());
        WriteJson(updatesPath, _wrapper.HistoricalBarUpdates.ToArray());

        Console.WriteLine($"[OK] Historical bars export: {barsPath} (rows={_wrapper.HistoricalBars.Count})");
        Console.WriteLine($"[OK] Historical bar updates export: {updatesPath} (rows={_wrapper.HistoricalBarUpdates.Count})");
    }

    private async Task RunHistogramMode(EClientSocket client, CancellationToken token)
    {
        var contract = ContractFactory.Stock(_options.Symbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        const int reqId = 9603;

        client.reqHistogramData(reqId, contract, _options.HistoricalUseRth == 1, _options.HistogramPeriod);
        await AwaitWithTimeout(_wrapper.HistogramDataTask, token, "histogramData");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var histogramPath = Path.Combine(outputDir, $"histogram_{_options.Symbol}_{timestamp}.json");

        WriteJson(histogramPath, _wrapper.Histograms.ToArray());
        Console.WriteLine($"[OK] Histogram export: {histogramPath} (rows={_wrapper.Histograms.Count})");
    }

    private async Task RunHistoricalTicksMode(EClientSocket client, CancellationToken token)
    {
        var contract = ContractFactory.Stock(_options.Symbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        const int reqId = 9604;

        var startValue = NormalizeMaybeEmpty(_options.HistoricalTickStart);
        var endValue = NormalizeMaybeEmpty(_options.HistoricalTickEnd);

        var hasStart = !string.IsNullOrWhiteSpace(startValue);
        var hasEnd = !string.IsNullOrWhiteSpace(endValue);
        if (!hasStart && !hasEnd)
        {
            throw new InvalidOperationException("Historical ticks requires one of --hist-tick-start or --hist-tick-end.");
        }

        if (hasStart && hasEnd)
        {
            Console.WriteLine("[WARN] Both --hist-tick-start and --hist-tick-end provided. Using endDateTime and clearing startDateTime.");
            startValue = string.Empty;
        }

        client.reqHistoricalTicks(
            reqId,
            contract,
            startValue,
            endValue,
            _options.HistoricalTicksNumber,
            _options.HistoricalTicksWhatToShow,
            _options.HistoricalUseRth,
            _options.HistoricalTickIgnoreSize,
            new List<TagValue>()
        );

        try
        {
            await AwaitWithTimeout(_wrapper.HistoricalTicksDoneTask, token, "historicalTicksDone");
        }
        catch (TimeoutException) when (HasErrorCode("code=10187"))
        {
            Console.WriteLine("[WARN] Historical ticks timed out due to market data permissions for this route; exporting empty result set.");
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var ticksPath = Path.Combine(outputDir, $"historical_ticks_{_options.Symbol}_{_options.HistoricalTicksWhatToShow}_{timestamp}.json");

        if (string.Equals(_options.HistoricalTicksWhatToShow, "BID_ASK", StringComparison.OrdinalIgnoreCase))
        {
            WriteJson(ticksPath, _wrapper.HistoricalTicksBidAsk.ToArray());
            Console.WriteLine($"[OK] Historical BID_ASK ticks export: {ticksPath} (rows={_wrapper.HistoricalTicksBidAsk.Count})");
            return;
        }

        if (string.Equals(_options.HistoricalTicksWhatToShow, "TRADES", StringComparison.OrdinalIgnoreCase))
        {
            WriteJson(ticksPath, _wrapper.HistoricalTicksLast.ToArray());
            Console.WriteLine($"[OK] Historical TRADES ticks export: {ticksPath} (rows={_wrapper.HistoricalTicksLast.Count})");
            return;
        }

        WriteJson(ticksPath, _wrapper.HistoricalTicks.ToArray());
        Console.WriteLine($"[OK] Historical MIDPOINT ticks export: {ticksPath} (rows={_wrapper.HistoricalTicks.Count})");
    }

    private async Task RunHeadTimestampMode(EClientSocket client, CancellationToken token)
    {
        var contract = ContractFactory.Stock(_options.Symbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        const int reqId = 9605;

        client.reqHeadTimestamp(reqId, contract, _options.HeadTimestampWhatToShow, _options.HistoricalUseRth, _options.HistoricalFormatDate);
        await AwaitWithTimeout(_wrapper.HeadTimestampTask, token, "headTimestamp");
        client.cancelHeadTimestamp(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var headPath = Path.Combine(outputDir, $"head_timestamp_{_options.Symbol}_{timestamp}.json");

        WriteJson(headPath, _wrapper.HeadTimestamps.ToArray());
        Console.WriteLine($"[OK] Head timestamp export: {headPath} (rows={_wrapper.HeadTimestamps.Count})");
    }

    private static void ValidateHistoricalBarRequestLimitations(string duration, string barSize)
    {
        if (!TryParseDurationToSeconds(duration, out var durationSeconds))
        {
            Console.WriteLine($"[WARN] Unable to parse duration '{duration}' for limitations precheck.");
            return;
        }

        var barSeconds = BarSizeToSeconds(barSize);
        if (barSeconds <= 0)
        {
            Console.WriteLine($"[WARN] Unable to parse bar size '{barSize}' for limitations precheck.");
            return;
        }

        var maxBarSeconds = durationSeconds switch
        {
            <= 60 => 60,
            <= 120 => 120,
            <= 1800 => 1800,
            <= 3600 => 3600,
            <= 14400 => 10800,
            <= 28800 => 28800,
            <= 86400 => 86400,
            <= 172800 => 86400,
            <= 604800 => 604800,
            <= 2678400 => 2678400,
            _ => 2678400
        };

        if (barSeconds > maxBarSeconds)
        {
            Console.WriteLine("[WARN] Request may violate IBKR duration/bar-size step guidance and may be throttled.");
        }

        if (barSeconds <= 30)
        {
            Console.WriteLine("[INFO] Small bars pacing rules apply (identical request<15s, 6+ similar requests/2s, >60 requests/10m).");
        }
    }

    private static bool TryParseDurationToSeconds(string duration, out int seconds)
    {
        seconds = 0;
        var parts = duration.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var value))
        {
            return false;
        }

        seconds = parts[1].ToUpperInvariant() switch
        {
            "S" => value,
            "D" => value * 86400,
            "W" => value * 7 * 86400,
            "M" => value * 31 * 86400,
            "Y" => value * 365 * 86400,
            _ => 0
        };

        return seconds > 0;
    }

    private static string NormalizeMaybeEmpty(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed is "\"\"" or "''")
        {
            return string.Empty;
        }

        return trimmed;
    }

    private static int BarSizeToSeconds(string barSize)
    {
        return barSize.Trim().ToLowerInvariant() switch
        {
            "1 secs" => 1,
            "5 secs" => 5,
            "10 secs" => 10,
            "15 secs" => 15,
            "30 secs" => 30,
            "1 min" => 60,
            "2 mins" => 120,
            "3 mins" => 180,
            "5 mins" => 300,
            "10 mins" => 600,
            "15 mins" => 900,
            "20 mins" => 1200,
            "30 mins" => 1800,
            "1 hour" => 3600,
            "2 hours" => 7200,
            "3 hours" => 10800,
            "4 hours" => 14400,
            "8 hours" => 28800,
            "1 day" => 86400,
            "1 week" => 604800,
            "1 month" => 2678400,
            _ => 0
        };
    }

    private void ValidateLiveSafetyInputs()
    {
        var normalizedAction = _options.LiveAction.ToUpperInvariant();
        if (normalizedAction is not ("BUY" or "SELL"))
        {
            throw new InvalidOperationException("Live order blocked: --live-action must be BUY or SELL.");
        }

        if (_options.LiveQuantity <= 0 || _options.LiveQuantity > _options.MaxShares)
        {
            throw new InvalidOperationException($"Live order blocked: qty must be >0 and <= max-shares ({_options.MaxShares}).");
        }

        if (_options.LiveLimitPrice <= 0 || _options.LiveLimitPrice > _options.MaxPrice)
        {
            throw new InvalidOperationException($"Live order blocked: limit must be >0 and <= max-price ({_options.MaxPrice}).");
        }

        var symbolAllowed = _options.AllowedSymbols.Any(s => string.Equals(s, _options.LiveSymbol, StringComparison.OrdinalIgnoreCase));
        if (!symbolAllowed)
        {
            throw new InvalidOperationException($"Live order blocked: symbol '{_options.LiveSymbol}' is not in allow-list.");
        }
    }

    private string BuildReport(string timestamp)
    {
        var netLiq = _wrapper.AccountSummaryRows.FirstOrDefault(x => x.Account == _options.Account && x.Tag == "NetLiquidation")?.Value
            ?? _wrapper.AccountSummaryRows.FirstOrDefault(x => x.Tag == "NetLiquidation")?.Value
            ?? "n/a";
        var buyingPower = _wrapper.AccountSummaryRows.FirstOrDefault(x => x.Account == _options.Account && x.Tag == "BuyingPower")?.Value
            ?? _wrapper.AccountSummaryRows.FirstOrDefault(x => x.Tag == "BuyingPower")?.Value
            ?? "n/a";

        return $"# Harvester Snapshot Report\\n\\n"
            + $"- Timestamp (UTC): {timestamp}\\n"
            + $"- Account: {_options.Account}\\n"
            + $"- Open Orders: {_wrapper.OpenOrders.Count}\\n"
            + $"- Completed Orders: {_wrapper.CompletedOrders.Count}\\n"
            + $"- Executions: {_wrapper.Executions.Count}\\n"
            + $"- Positions: {_wrapper.Positions.Count}\\n"
            + $"- Net Liquidation: {netLiq}\\n"
            + $"- Buying Power: {buyingPower}\\n";
    }

    private string EnsureOutputDir()
    {
        var full = Path.GetFullPath(_options.ExportDir);
        Directory.CreateDirectory(full);
        return full;
    }

    private static void WriteJson<T>(string path, IReadOnlyCollection<T> rows)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(rows, options));
    }

    private static async Task<T> AwaitWithTimeout<T>(Task<T> task, CancellationToken cancellationToken, string stage)
    {
        var delayTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var winner = await Task.WhenAny(task, delayTask);
        if (winner == task)
        {
            return await task;
        }

        throw new TimeoutException($"Timed out waiting for {stage}.");
    }

    private void PrintErrors()
    {
        if (_wrapper.Errors.IsEmpty)
        {
            return;
        }

        Console.WriteLine("\n=== API Errors ===");
        foreach (var line in _wrapper.Errors)
        {
            Console.WriteLine(line);
        }
    }

    private static bool IsBlockingError(string error)
    {
        var nonBlockingCodes = new[] { "code=2104", "code=2106", "code=2158", "code=10089", "code=10167", "code=10168", "code=10187", "code=354", "code=300", "code=310", "code=420" };

        if (error.Contains("code=162") && error.Contains("query cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !nonBlockingCodes.Any(error.Contains);
    }

    private bool HasErrorCode(string code)
    {
        return _wrapper.Errors.Any(e => e.Contains(code, StringComparison.OrdinalIgnoreCase));
    }

    private static OrderTemplateRow ToTemplate(string name, Order order)
    {
        return new OrderTemplateRow(
            name,
            order.OrderId,
            order.ParentId,
            order.Action,
            order.OrderType,
            order.TotalQuantity,
            order.LmtPrice,
            order.AuxPrice,
            order.Tif,
            order.Transmit
        );
    }
}

public sealed record AppOptions(
    RunMode Mode,
    string Host,
    int Port,
    int ClientId,
    string Account,
    int TimeoutSeconds,
    string ExportDir,
    string Symbol,
    string PrimaryExchange,
    bool EnableLive,
    string LiveSymbol,
    string LiveAction,
    double LiveQuantity,
    double LiveLimitPrice,
    double MaxNotional,
    double MaxShares,
    double MaxPrice,
    string[] AllowedSymbols
    ,
    string WhatIfTemplate,
    int MarketDataType,
    int CaptureSeconds,
    int DepthRows,
    string DepthExchange,
    string RealTimeBarsWhatToShow,
    string HistoricalEndDateTime,
    string HistoricalDuration,
    string HistoricalBarSize,
    string HistoricalWhatToShow,
    int HistoricalUseRth,
    int HistoricalFormatDate,
    string HistogramPeriod,
    string HistoricalTickStart,
    string HistoricalTickEnd,
    int HistoricalTicksNumber,
    string HistoricalTicksWhatToShow,
    bool HistoricalTickIgnoreSize,
    string HeadTimestampWhatToShow
)
{
    public static AppOptions Parse(string[] args)
    {
        var mode = RunMode.Connect;
        var host = "127.0.0.1";
        var port = 7496;
        var clientId = 9100;
        var account = "U22462030";
        var timeoutSeconds = 25;
        var exportDir = "exports";
        var symbol = "SIRI";
        var primaryExchange = "NASDAQ";
        var enableLive = false;
        var liveSymbol = "SIRI";
        var liveAction = "BUY";
        var liveQuantity = 1.0;
        var liveLimitPrice = 5.00;
        var maxNotional = 100.00;
        var maxShares = 10.0;
        var maxPrice = 10.0;
        var allowedSymbols = new[] { "SIRI", "SOFI", "F", "PLTR" };
        var whatIfTemplate = "lmt";
        var marketDataType = 3;
        var captureSeconds = 12;
        var depthRows = 5;
        var depthExchange = "NASDAQ";
        var realTimeBarsWhatToShow = "TRADES";
        var historicalEndDateTime = string.Empty;
        var historicalDuration = "1 D";
        var historicalBarSize = "5 mins";
        var historicalWhatToShow = "TRADES";
        var historicalUseRth = 1;
        var historicalFormatDate = 1;
        var histogramPeriod = "1 week";
        var historicalTickStart = string.Empty;
        var historicalTickEnd = DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss");
        var historicalTicksNumber = 200;
        var historicalTicksWhatToShow = "TRADES";
        var historicalTickIgnoreSize = true;
        var headTimestampWhatToShow = "TRADES";

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode" when i + 1 < args.Length:
                    mode = ParseMode(args[++i]);
                    break;
                case "--host" when i + 1 < args.Length:
                    host = args[++i];
                    break;
                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var p):
                    port = p;
                    i++;
                    break;
                case "--client-id" when i + 1 < args.Length && int.TryParse(args[i + 1], out var c):
                    clientId = c;
                    i++;
                    break;
                case "--account" when i + 1 < args.Length:
                    account = args[++i];
                    break;
                case "--timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out var t):
                    timeoutSeconds = t;
                    i++;
                    break;
                case "--export-dir" when i + 1 < args.Length:
                    exportDir = args[++i];
                    break;
                case "--symbol" when i + 1 < args.Length:
                    symbol = args[++i].ToUpperInvariant();
                    break;
                case "--primary-exchange" when i + 1 < args.Length:
                    primaryExchange = args[++i].ToUpperInvariant();
                    break;
                case "--enable-live" when i + 1 < args.Length:
                    enableLive = bool.TryParse(args[++i], out var flag) && flag;
                    break;
                case "--live-symbol" when i + 1 < args.Length:
                    liveSymbol = args[++i].ToUpperInvariant();
                    break;
                case "--live-action" when i + 1 < args.Length:
                    liveAction = args[++i].ToUpperInvariant();
                    break;
                case "--live-qty" when i + 1 < args.Length && double.TryParse(args[i + 1], out var q):
                    liveQuantity = q;
                    i++;
                    break;
                case "--live-limit" when i + 1 < args.Length && double.TryParse(args[i + 1], out var lp):
                    liveLimitPrice = lp;
                    i++;
                    break;
                case "--max-notional" when i + 1 < args.Length && double.TryParse(args[i + 1], out var mn):
                    maxNotional = mn;
                    i++;
                    break;
                case "--max-shares" when i + 1 < args.Length && double.TryParse(args[i + 1], out var ms):
                    maxShares = ms;
                    i++;
                    break;
                case "--max-price" when i + 1 < args.Length && double.TryParse(args[i + 1], out var mp):
                    maxPrice = mp;
                    i++;
                    break;
                case "--allowed-symbols" when i + 1 < args.Length:
                    allowedSymbols = args[++i]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(x => x.ToUpperInvariant())
                        .ToArray();
                    break;
                case "--whatif-template" when i + 1 < args.Length:
                    whatIfTemplate = args[++i].ToLowerInvariant();
                    break;
                case "--market-data-type" when i + 1 < args.Length && int.TryParse(args[i + 1], out var mdt):
                    marketDataType = mdt;
                    i++;
                    break;
                case "--capture-seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var cs):
                    captureSeconds = cs;
                    i++;
                    break;
                case "--depth-rows" when i + 1 < args.Length && int.TryParse(args[i + 1], out var dr):
                    depthRows = dr;
                    i++;
                    break;
                case "--depth-exchange" when i + 1 < args.Length:
                    depthExchange = args[++i].ToUpperInvariant();
                    break;
                case "--rtb-what" when i + 1 < args.Length:
                    realTimeBarsWhatToShow = args[++i].ToUpperInvariant();
                    break;
                case "--hist-end" when i + 1 < args.Length:
                    historicalEndDateTime = args[++i];
                    break;
                case "--hist-duration" when i + 1 < args.Length:
                    historicalDuration = args[++i];
                    break;
                case "--hist-barsize" when i + 1 < args.Length:
                    historicalBarSize = args[++i].ToLowerInvariant();
                    break;
                case "--hist-what" when i + 1 < args.Length:
                    historicalWhatToShow = args[++i].ToUpperInvariant();
                    break;
                case "--hist-use-rth" when i + 1 < args.Length && int.TryParse(args[i + 1], out var hu):
                    historicalUseRth = hu;
                    i++;
                    break;
                case "--hist-format-date" when i + 1 < args.Length && int.TryParse(args[i + 1], out var hf):
                    historicalFormatDate = hf;
                    i++;
                    break;
                case "--histogram-period" when i + 1 < args.Length:
                    histogramPeriod = args[++i];
                    break;
                case "--hist-tick-start" when i + 1 < args.Length:
                    historicalTickStart = args[++i];
                    break;
                case "--hist-tick-end" when i + 1 < args.Length:
                    historicalTickEnd = args[++i];
                    break;
                case "--hist-ticks-num" when i + 1 < args.Length && int.TryParse(args[i + 1], out var htn):
                    historicalTicksNumber = htn;
                    i++;
                    break;
                case "--hist-ticks-what" when i + 1 < args.Length:
                    historicalTicksWhatToShow = args[++i].ToUpperInvariant();
                    break;
                case "--hist-ignore-size" when i + 1 < args.Length:
                    historicalTickIgnoreSize = bool.TryParse(args[++i], out var his) && his;
                    break;
                case "--head-what" when i + 1 < args.Length:
                    headTimestampWhatToShow = args[++i].ToUpperInvariant();
                    break;
            }
        }

        return new AppOptions(
            mode,
            host,
            port,
            clientId,
            account,
            timeoutSeconds,
            exportDir,
            symbol,
            primaryExchange,
            enableLive,
            liveSymbol,
            liveAction,
            liveQuantity,
            liveLimitPrice,
            maxNotional,
            maxShares,
            maxPrice,
            allowedSymbols,
            whatIfTemplate,
            marketDataType,
            captureSeconds,
            depthRows,
            depthExchange,
            realTimeBarsWhatToShow,
            historicalEndDateTime,
            historicalDuration,
            historicalBarSize,
            historicalWhatToShow,
            historicalUseRth,
            historicalFormatDate,
            histogramPeriod,
            historicalTickStart,
            historicalTickEnd,
            historicalTicksNumber,
            historicalTicksWhatToShow,
            historicalTickIgnoreSize,
            headTimestampWhatToShow
        );
    }

    private static RunMode ParseMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "connect" => RunMode.Connect,
            "orders" => RunMode.Orders,
            "positions" => RunMode.Positions,
            "snapshot-all" => RunMode.SnapshotAll,
            "contracts-validate" => RunMode.ContractsValidate,
            "orders-dryrun" => RunMode.OrdersDryRun,
            "orders-place-sim" => RunMode.OrdersPlaceSim,
            "orders-whatif" => RunMode.OrdersWhatIf,
            "top-data" => RunMode.TopData,
            "market-depth" => RunMode.MarketDepth,
            "realtime-bars" => RunMode.RealtimeBars,
            "market-data-all" => RunMode.MarketDataAll,
            "historical-bars" => RunMode.HistoricalBars,
            "historical-bars-live" => RunMode.HistoricalBarsKeepUpToDate,
            "histogram" => RunMode.Histogram,
            "historical-ticks" => RunMode.HistoricalTicks,
            "head-timestamp" => RunMode.HeadTimestamp,
            _ => throw new ArgumentException($"Unknown mode '{value}'. Use connect|orders|positions|snapshot-all|contracts-validate|orders-dryrun|orders-place-sim|orders-whatif|top-data|market-depth|realtime-bars|market-data-all|historical-bars|historical-bars-live|histogram|historical-ticks|head-timestamp.")
        };
    }
}

public enum RunMode
{
    Connect,
    Orders,
    Positions,
    SnapshotAll,
    ContractsValidate,
    OrdersDryRun,
    OrdersPlaceSim,
    OrdersWhatIf,
    TopData,
    MarketDepth,
    RealtimeBars,
    MarketDataAll,
    HistoricalBars,
    HistoricalBarsKeepUpToDate,
    Histogram,
    HistoricalTicks,
    HeadTimestamp
}

public sealed record ContractDetailsRow(
    int ConId,
    string Symbol,
    string SecType,
    string Exchange,
    string PrimaryExchange,
    string Currency,
    string LocalSymbol,
    string TradingClass,
    string MarketName,
    string LongName,
    double MinTick
);

public sealed record OrderTemplateRow(
    string Name,
    int OrderId,
    int ParentId,
    string Action,
    string OrderType,
    double Quantity,
    double LimitPrice,
    double StopPrice,
    string Tif,
    bool Transmit
);

public sealed record LiveOrderPlacementRow(
    string TimestampUtc,
    int OrderId,
    string Symbol,
    string Action,
    double Quantity,
    double LimitPrice,
    double Notional,
    string Account,
    string OrderRef
);
