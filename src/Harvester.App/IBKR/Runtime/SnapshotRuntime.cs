using System.Text.Json;
using System.Diagnostics;
using Harvester.App.IBKR.Connection;
using Harvester.App.IBKR.Contracts;
using Harvester.App.IBKR.Orders;
using IBApi;

namespace Harvester.App.IBKR.Runtime;

public sealed class SnapshotRuntime
{
    private readonly AppOptions _options;
    private readonly SnapshotEWrapper _wrapper;
    private readonly IbErrorPolicy _errorPolicy;
    private readonly RequestRegistry _requestRegistry;
    private bool _hasReconciliationQualityFailure;
    private bool _hasConnectivityFailure;
    private int _processedApiErrorCount;

    public SnapshotRuntime(AppOptions options)
    {
        _options = options;
        _wrapper = new SnapshotEWrapper();
        _errorPolicy = new IbErrorPolicy();
        _requestRegistry = new RequestRegistry();
    }

    public async Task<int> RunAsync()
    {
        Console.WriteLine($"[INFO] Mode={_options.Mode} host={_options.Host}:{_options.Port} clientId={_options.ClientId}");
        var runStartedUtc = DateTime.UtcNow;
        var runtimeStateStore = new RuntimeStateStore(_options.ExportDir);
        if (runtimeStateStore.TryLoadLatest(out var restoredCheckpoint, out var restoreMessage))
        {
            Console.WriteLine($"[OK] Restored runtime checkpoint: mode={restoredCheckpoint!.Snapshot.Mode} exitCode={restoredCheckpoint.Snapshot.ExitCode} state={restoredCheckpoint.Snapshot.ConnectionState} checkpoint={restoredCheckpoint.CheckpointUtc:O}");
        }
        else if (!string.IsNullOrWhiteSpace(restoreMessage))
        {
            Console.WriteLine($"[WARN] {restoreMessage}");
        }

        using var session = new IbkrSession(_wrapper);
        try
        {
            await session.ConnectAsync(_options.Host, _options.Port, _options.ClientId, _options.TimeoutSeconds);
            if (session.State != IbConnectionState.Connected)
            {
                throw new InvalidOperationException($"Session not in Connected state. Current state={session.State}");
            }
            Console.WriteLine($"[OK] nextValidId={await _wrapper.NextValidIdTask}");
            Console.WriteLine($"[OK] managedAccounts={await _wrapper.ManagedAccountsTask}");

            var client = session.Client;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            using var runtimeCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
            var monitorTask = MonitorConnectionHealthAsync(session, client, runtimeCts);

            client.reqCurrentTime();
            await AwaitTrackedWithTimeout(
                _wrapper.CurrentTimeTask,
                runtimeCts.Token,
                stage: "currentTime",
                requestId: null,
                requestType: "reqCurrentTime",
                origin: _options.Mode.ToString());
            Console.WriteLine("[OK] currentTime callback received");

            switch (_options.Mode)
            {
                case RunMode.Connect:
                    await RunConnectMode(client, runtimeCts.Token);
                    break;
                case RunMode.Orders:
                    await RunOrdersMode(client, runtimeCts.Token);
                    break;
                case RunMode.Positions:
                    await RunPositionsMode(client, runtimeCts.Token);
                    break;
                case RunMode.SnapshotAll:
                    await RunSnapshotAllMode(client, runtimeCts.Token);
                    break;
                case RunMode.ContractsValidate:
                    await RunContractsValidateMode(client, runtimeCts.Token);
                    break;
                case RunMode.OrdersDryRun:
                    await RunOrdersDryRunMode();
                    break;
                case RunMode.OrdersPlaceSim:
                    await RunOrdersPlaceSimMode(client, runtimeCts.Token);
                    break;
                case RunMode.OrdersWhatIf:
                    await RunOrdersWhatIfMode(client, runtimeCts.Token);
                    break;
                case RunMode.TopData:
                    await RunTopDataMode(client, runtimeCts.Token);
                    break;
                case RunMode.MarketDepth:
                    await RunMarketDepthMode(client, runtimeCts.Token);
                    break;
                case RunMode.RealtimeBars:
                    await RunRealtimeBarsMode(client, runtimeCts.Token);
                    break;
                case RunMode.MarketDataAll:
                    await RunMarketDataAllMode(client, runtimeCts.Token);
                    break;
                case RunMode.HistoricalBars:
                    await RunHistoricalBarsMode(client, runtimeCts.Token);
                    break;
                case RunMode.HistoricalBarsKeepUpToDate:
                    await RunHistoricalBarsKeepUpToDateMode(client, runtimeCts.Token);
                    break;
                case RunMode.Histogram:
                    await RunHistogramMode(client, runtimeCts.Token);
                    break;
                case RunMode.HistoricalTicks:
                    await RunHistoricalTicksMode(client, runtimeCts.Token);
                    break;
                case RunMode.HeadTimestamp:
                    await RunHeadTimestampMode(client, runtimeCts.Token);
                    break;
                case RunMode.ManagedAccounts:
                    await RunManagedAccountsMode(client, runtimeCts.Token);
                    break;
                case RunMode.FamilyCodes:
                    await RunFamilyCodesMode(client, runtimeCts.Token);
                    break;
                case RunMode.AccountUpdates:
                    await RunAccountUpdatesMode(client, runtimeCts.Token);
                    break;
                case RunMode.AccountUpdatesMulti:
                    await RunAccountUpdatesMultiMode(client, runtimeCts.Token);
                    break;
                case RunMode.AccountSummaryOnly:
                    await RunAccountSummaryOnlyMode(client, runtimeCts.Token);
                    break;
                case RunMode.PositionsMulti:
                    await RunPositionsMultiMode(client, runtimeCts.Token);
                    break;
                case RunMode.PnlAccount:
                    await RunPnlAccountMode(client, runtimeCts.Token);
                    break;
                case RunMode.PnlSingle:
                    await RunPnlSingleMode(client, runtimeCts.Token);
                    break;
                case RunMode.OptionChains:
                    await RunOptionChainsMode(client, runtimeCts.Token);
                    break;
                case RunMode.OptionExercise:
                    await RunOptionExerciseMode(client, runtimeCts.Token);
                    break;
                case RunMode.OptionGreeks:
                    await RunOptionGreeksMode(client, runtimeCts.Token);
                    break;
                case RunMode.CryptoPermissions:
                    await RunCryptoPermissionsMode(client, runtimeCts.Token);
                    break;
                case RunMode.CryptoContract:
                    await RunCryptoContractDefinitionMode(client, runtimeCts.Token);
                    break;
                case RunMode.CryptoStreaming:
                    await RunCryptoStreamingMode(client, runtimeCts.Token);
                    break;
                case RunMode.CryptoHistorical:
                    await RunCryptoHistoricalMode(client, runtimeCts.Token);
                    break;
                case RunMode.CryptoOrder:
                    await RunCryptoOrderPlacementMode(client, runtimeCts.Token);
                    break;
                case RunMode.FaAllocationGroups:
                    await RunFaAllocationMethodsAndGroupsMode(client, runtimeCts.Token);
                    break;
                case RunMode.FaGroupsProfiles:
                    await RunFaGroupsAndProfilesMode(client, runtimeCts.Token);
                    break;
                case RunMode.FaUnification:
                    await RunFaUnificationMode(client, runtimeCts.Token);
                    break;
                case RunMode.FaModelPortfolios:
                    await RunFaModelPortfoliosMode(client, runtimeCts.Token);
                    break;
                case RunMode.FaOrder:
                    await RunFaOrderPlacementMode(client, runtimeCts.Token);
                    break;
                case RunMode.FundamentalData:
                    await RunFundamentalDataMode(client, runtimeCts.Token);
                    break;
                case RunMode.WshFilters:
                    await RunWshFiltersMode(client, runtimeCts.Token);
                    break;
                case RunMode.ErrorCodes:
                    await RunErrorCodesMode(client, runtimeCts.Token);
                    break;
                case RunMode.ScannerExamples:
                    await RunScannerExamplesMode(client, runtimeCts.Token);
                    break;
                case RunMode.ScannerComplex:
                    await RunScannerComplexMode(client, runtimeCts.Token);
                    break;
                case RunMode.ScannerParameters:
                    await RunScannerParametersMode(client, runtimeCts.Token);
                    break;
                case RunMode.ScannerWorkbench:
                    await RunScannerWorkbenchMode(client, runtimeCts.Token);
                    break;
                case RunMode.DisplayGroupsQuery:
                    await RunDisplayGroupsQueryMode(client, runtimeCts.Token);
                    break;
                case RunMode.DisplayGroupsSubscribe:
                    await RunDisplayGroupsSubscribeMode(client, runtimeCts.Token);
                    break;
                case RunMode.DisplayGroupsUpdate:
                    await RunDisplayGroupsUpdateMode(client, runtimeCts.Token);
                    break;
                case RunMode.DisplayGroupsUnsubscribe:
                    await RunDisplayGroupsUnsubscribeMode(client, runtimeCts.Token);
                    break;
            }

            runtimeCts.Cancel();
            await monitorTask;

            var hasBlockingErrors = _wrapper.ApiErrors
                .Any(e => _errorPolicy.Evaluate(e, _options.Mode, _options.OptionGreeksAutoFallback).Action == IbErrorAction.HardFail);
            if (hasBlockingErrors || _hasReconciliationQualityFailure || _hasConnectivityFailure)
            {
                Console.WriteLine("[WARN] Completed with blocking API errors.");
                PrintErrors();
                PrintRequestDiagnostics();
                if (_hasReconciliationQualityFailure)
                {
                    Console.WriteLine("[WARN] Completed with reconciliation quality gate failure.");
                }
                if (_hasConnectivityFailure)
                {
                    Console.WriteLine("[WARN] Completed with connectivity halt escalation.");
                }
                PersistRuntimeState(runtimeStateStore, session, 1, runStartedUtc);
                return 1;
            }

            Console.WriteLine("[PASS] Completed successfully.");
            PersistRuntimeState(runtimeStateStore, session, 0, runStartedUtc);
            return 0;
        }
        catch (OperationCanceledException) when (_hasConnectivityFailure)
        {
            Console.WriteLine("[FAIL] Connectivity halt escalation triggered.");
            PrintErrors();
            PrintRequestDiagnostics();
            PersistRuntimeState(runtimeStateStore, session, 1, runStartedUtc);
            return 1;
        }
        catch (TimeoutException ex)
        {
            Console.WriteLine($"[FAIL] {ex.Message}");
            PrintErrors();
            PrintRequestDiagnostics();
            PersistRuntimeState(runtimeStateStore, session, 2, runStartedUtc);
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {ex.Message}");
            PrintErrors();
            PrintRequestDiagnostics();
            PersistRuntimeState(runtimeStateStore, session, 2, runStartedUtc);
            return 2;
        }
    }

    private void PersistRuntimeState(RuntimeStateStore runtimeStateStore, IbkrSession session, int exitCode, DateTime runStartedUtc)
    {
        var snapshot = RuntimeStateStore.BuildSnapshot(
            _options,
            session,
            _requestRegistry.Snapshot(),
            _wrapper.ApiErrors.Count,
            _hasConnectivityFailure,
            _hasReconciliationQualityFailure,
            exitCode,
            runStartedUtc);

        runtimeStateStore.Save(snapshot);
    }

    private async Task MonitorConnectionHealthAsync(IbkrSession session, EClientSocket client, CancellationTokenSource runtimeCts)
    {
        if (!_options.HeartbeatMonitorEnabled)
        {
            return;
        }

        var token = runtimeCts.Token;
        var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(2, _options.HeartbeatIntervalSeconds));
        var probeTimeout = TimeSpan.FromSeconds(Math.Max(2, _options.HeartbeatProbeTimeoutSeconds));

        while (!token.IsCancellationRequested)
        {
            await ProcessConnectivityApiErrorsAsync(session, runtimeCts, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (!client.IsConnected())
            {
                var recovered = await AttemptReconnectOrHaltAsync(session, runtimeCts, "socket disconnected");
                if (!recovered)
                {
                    return;
                }
            }

            var probeStartedUtc = DateTime.UtcNow;
            client.reqCurrentTime();

            try
            {
                await Task.Delay(probeTimeout, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_wrapper.LastCurrentTimeCallbackUtc < probeStartedUtc)
            {
                session.MarkDegraded($"heartbeat probe stale for {probeTimeout.TotalSeconds:0}s");
                var recovered = await AttemptReconnectOrHaltAsync(session, runtimeCts, "heartbeat probe timeout");
                if (!recovered)
                {
                    return;
                }
            }

            try
            {
                await Task.Delay(heartbeatInterval, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ProcessConnectivityApiErrorsAsync(IbkrSession session, CancellationTokenSource runtimeCts, CancellationToken token)
    {
        var errors = _wrapper.ApiErrors.ToArray();
        if (_processedApiErrorCount >= errors.Length)
        {
            return;
        }

        for (var i = _processedApiErrorCount; i < errors.Length; i++)
        {
            var error = errors[i];
            var decision = _errorPolicy.Evaluate(error, _options.Mode, _options.OptionGreeksAutoFallback);
            if (decision.Action != IbErrorAction.Retry || !IsReconnectTriggerCode(error.Code))
            {
                continue;
            }

            session.MarkDegraded($"connectivity code={error.Code} msg={error.Message}");
            var recovered = await AttemptReconnectOrHaltAsync(session, runtimeCts, $"connectivity code {error.Code}");
            if (!recovered || token.IsCancellationRequested)
            {
                _processedApiErrorCount = errors.Length;
                return;
            }
        }

        _processedApiErrorCount = errors.Length;
    }

    private async Task<bool> AttemptReconnectOrHaltAsync(IbkrSession session, CancellationTokenSource runtimeCts, string reason)
    {
        var restored = await session.TryReconnectAsync(
            _options.Host,
            _options.Port,
            _options.ClientId,
            _options.TimeoutSeconds,
            _options.ReconnectMaxAttempts,
            _options.ReconnectBackoffSeconds,
            runtimeCts.Token);

        if (restored)
        {
            Console.WriteLine($"[OK] Connectivity restored after {reason}.");
            return true;
        }

        _hasConnectivityFailure = true;
        session.MarkHalting($"connectivity halt escalation ({reason})");
        runtimeCts.Cancel();
        return false;
    }

    private static bool IsReconnectTriggerCode(int? code)
    {
        return code is 1100 or 1300 or 2110;
    }

    private async Task RunConnectMode(EClientSocket client, CancellationToken token)
    {
        const int summaryReqId = 9001;
        client.reqAccountSummary(summaryReqId, "All", "AccountType,NetLiquidation,TotalCashValue,BuyingPower");
        await AwaitTrackedWithTimeout(
            _wrapper.AccountSummaryEndTask,
            token,
            stage: "accountSummaryEnd",
            requestId: summaryReqId,
            requestType: "reqAccountSummary",
            origin: nameof(RunConnectMode));
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
        await AwaitTrackedWithTimeout(
            _wrapper.OpenOrderEndTask,
            token,
            stage: "openOrderEnd",
            requestId: null,
            requestType: "reqOpenOrders",
            origin: nameof(RunOrdersMode));

        client.reqCompletedOrders(true);
        await AwaitTrackedWithTimeout(
            _wrapper.CompletedOrdersEndTask,
            token,
            stage: "completedOrdersEnd",
            requestId: null,
            requestType: "reqCompletedOrders",
            origin: nameof(RunOrdersMode));

        const int executionsReqId = 9201;
        client.reqExecutions(executionsReqId, new ExecutionFilter());
        await AwaitTrackedWithTimeout(
            _wrapper.ExecDetailsEndTask,
            token,
            stage: "execDetailsEnd",
            requestId: executionsReqId,
            requestType: "reqExecutions",
            origin: nameof(RunOrdersMode));

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var openOrdersPath = Path.Combine(outputDir, $"open_orders_{timestamp}.json");
        var completedOrdersPath = Path.Combine(outputDir, $"completed_orders_{timestamp}.json");
        var executionsPath = Path.Combine(outputDir, $"executions_{timestamp}.json");
        var commissionsPath = Path.Combine(outputDir, $"commissions_{timestamp}.json");
        var reconciledOrdersPath = Path.Combine(outputDir, $"orders_reconciled_{timestamp}.json");
        var reconciliationDiagnosticsPath = Path.Combine(outputDir, $"orders_reconciliation_diagnostics_{timestamp}.json");
        var reconciliationSummaryPath = Path.Combine(outputDir, $"orders_reconciliation_summary_{timestamp}.json");

        var reconciliation = OrderReconciliation.Reconcile(
            _wrapper.OpenOrders.ToArray(),
            _wrapper.CompletedOrders.ToArray(),
            _wrapper.Executions.ToArray(),
            _wrapper.Commissions.ToArray());

        WriteJson(openOrdersPath, _wrapper.OpenOrders.ToArray());
        WriteJson(completedOrdersPath, _wrapper.CompletedOrders.ToArray());
        WriteJson(executionsPath, _wrapper.Executions.ToArray());
        WriteJson(commissionsPath, _wrapper.Commissions.ToArray());
        WriteJson(reconciledOrdersPath, reconciliation.Ledger);
        WriteJson(reconciliationDiagnosticsPath, reconciliation.Diagnostics);
        WriteJson(reconciliationSummaryPath, new[] { reconciliation.Summary });

        Console.WriteLine($"[OK] Open orders snapshot: {openOrdersPath} (rows={_wrapper.OpenOrders.Count})");
        Console.WriteLine($"[OK] Completed orders snapshot: {completedOrdersPath} (rows={_wrapper.CompletedOrders.Count})");
        Console.WriteLine($"[OK] Executions snapshot: {executionsPath} (rows={_wrapper.Executions.Count})");
        Console.WriteLine($"[OK] Commissions snapshot: {commissionsPath} (rows={_wrapper.Commissions.Count})");
        Console.WriteLine($"[OK] Reconciled orders: {reconciledOrdersPath} (rows={reconciliation.Ledger.Length})");
        Console.WriteLine($"[OK] Reconciliation diagnostics: {reconciliationDiagnosticsPath} (rows={reconciliation.Diagnostics.Length})");
        Console.WriteLine($"[OK] Reconciliation summary: {reconciliationSummaryPath}");
        Console.WriteLine($"[OK] Reconciliation coverage: execution->commission={reconciliation.Summary.ExecutionCommissionCoveragePct:P2} execution->order={reconciliation.Summary.ExecutionOrderMetadataCoveragePct:P2}");
        EvaluateReconciliationQualityGate(reconciliation.Summary, nameof(RunOrdersMode));
    }

    private async Task RunPositionsMode(EClientSocket client, CancellationToken token)
    {
        const int summaryReqId = 9001;
        client.reqAccountSummary(summaryReqId, "All", "AccountType,NetLiquidation,TotalCashValue,BuyingPower,MaintMarginReq,AvailableFunds");
        client.reqPositions();

        await AwaitTrackedWithTimeout(
            _wrapper.AccountSummaryEndTask,
            token,
            stage: "accountSummaryEnd",
            requestId: summaryReqId,
            requestType: "reqAccountSummary",
            origin: nameof(RunPositionsMode));
        await AwaitTrackedWithTimeout(
            _wrapper.PositionEndTask,
            token,
            stage: "positionEnd",
            requestId: null,
            requestType: "reqPositions",
            origin: nameof(RunPositionsMode));

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
        var commissionsPath = Path.Combine(outputDir, $"commissions_{timestamp}.json");
        var reconciledOrdersPath = Path.Combine(outputDir, $"orders_reconciled_{timestamp}.json");
        var reconciliationDiagnosticsPath = Path.Combine(outputDir, $"orders_reconciliation_diagnostics_{timestamp}.json");
        var reconciliationSummaryPath = Path.Combine(outputDir, $"orders_reconciliation_summary_{timestamp}.json");
        var accountSummaryPath = Path.Combine(outputDir, $"account_summary_{timestamp}.json");
        var positionsPath = Path.Combine(outputDir, $"positions_{timestamp}.json");
        var reportPath = Path.Combine(outputDir, $"snapshot_report_{timestamp}.md");

        var reconciliation = OrderReconciliation.Reconcile(
            _wrapper.OpenOrders.ToArray(),
            _wrapper.CompletedOrders.ToArray(),
            _wrapper.Executions.ToArray(),
            _wrapper.Commissions.ToArray());

        WriteJson(openOrdersPath, _wrapper.OpenOrders.ToArray());
        WriteJson(completedOrdersPath, _wrapper.CompletedOrders.ToArray());
        WriteJson(executionsPath, _wrapper.Executions.ToArray());
        WriteJson(commissionsPath, _wrapper.Commissions.ToArray());
        WriteJson(reconciledOrdersPath, reconciliation.Ledger);
        WriteJson(reconciliationDiagnosticsPath, reconciliation.Diagnostics);
        WriteJson(reconciliationSummaryPath, new[] { reconciliation.Summary });
        WriteJson(accountSummaryPath, _wrapper.AccountSummaryRows.ToArray());
        WriteJson(positionsPath, _wrapper.Positions.ToArray());
        File.WriteAllText(reportPath, BuildReport(timestamp));

        Console.WriteLine($"[OK] Open orders: {openOrdersPath} (rows={_wrapper.OpenOrders.Count})");
        Console.WriteLine($"[OK] Completed orders: {completedOrdersPath} (rows={_wrapper.CompletedOrders.Count})");
        Console.WriteLine($"[OK] Executions: {executionsPath} (rows={_wrapper.Executions.Count})");
        Console.WriteLine($"[OK] Commissions: {commissionsPath} (rows={_wrapper.Commissions.Count})");
        Console.WriteLine($"[OK] Reconciled orders: {reconciledOrdersPath} (rows={reconciliation.Ledger.Length})");
        Console.WriteLine($"[OK] Reconciliation diagnostics: {reconciliationDiagnosticsPath} (rows={reconciliation.Diagnostics.Length})");
        Console.WriteLine($"[OK] Reconciliation summary: {reconciliationSummaryPath}");
        Console.WriteLine($"[OK] Reconciliation coverage: execution->commission={reconciliation.Summary.ExecutionCommissionCoveragePct:P2} execution->order={reconciliation.Summary.ExecutionOrderMetadataCoveragePct:P2}");
        EvaluateReconciliationQualityGate(reconciliation.Summary, nameof(RunSnapshotAllMode));
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
        var sanitizationPath = Path.Combine(outputDir, $"top_data_sanitization_{_options.Symbol}_{timestamp}.json");

        WriteJson(ticksPath, _wrapper.TopTicks.ToArray());
        WriteJson(typesPath, _wrapper.MarketDataTypes.ToArray());
        WriteJson(sanitizationPath, _wrapper.MarketDataSanitizationRows.ToArray());

        Console.WriteLine($"[OK] Top data export: {ticksPath} (rows={_wrapper.TopTicks.Count})");
        Console.WriteLine($"[OK] Market data type export: {typesPath} (rows={_wrapper.MarketDataTypes.Count})");
        Console.WriteLine($"[OK] Top data sanitization export: {sanitizationPath} (rows={_wrapper.MarketDataSanitizationRows.Count})");
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
        var sanitizationPath = Path.Combine(outputDir, $"depth_data_sanitization_{_options.Symbol}_{timestamp}.json");

        WriteJson(depthPath, _wrapper.DepthRows.ToArray());
        WriteJson(sanitizationPath, _wrapper.MarketDataSanitizationRows.ToArray());
        Console.WriteLine($"[OK] Depth data export: {depthPath} (rows={_wrapper.DepthRows.Count})");
        Console.WriteLine($"[OK] Depth data sanitization export: {sanitizationPath} (rows={_wrapper.MarketDataSanitizationRows.Count})");
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
        var sanitizationPath = Path.Combine(outputDir, $"market_data_sanitization_{_options.Symbol}_{timestamp}.json");
        var reportPath = Path.Combine(outputDir, $"market_data_report_{_options.Symbol}_{timestamp}.md");

        WriteJson(topPath, _wrapper.TopTicks.ToArray());
        WriteJson(typePath, _wrapper.MarketDataTypes.ToArray());
        WriteJson(depthPath, _wrapper.DepthRows.ToArray());
        WriteJson(barsPath, _wrapper.RealtimeBars.ToArray());
        WriteJson(sanitizationPath, _wrapper.MarketDataSanitizationRows.ToArray());

        var report =
            "# Harvester Market Data Report\n\n"
            + $"- Timestamp (UTC): {timestamp}\n"
            + $"- Symbol: {_options.Symbol}\n"
            + $"- MarketDataType requested: {_options.MarketDataType}\n"
            + $"- Capture seconds: {_options.CaptureSeconds}\n"
            + $"- Top ticks: {_wrapper.TopTicks.Count}\n"
            + $"- Depth rows: {_wrapper.DepthRows.Count}\n"
            + $"- Realtime bars: {_wrapper.RealtimeBars.Count}\n"
            + $"- Sanitization events: {_wrapper.MarketDataSanitizationRows.Count}\n"
            + "\n"
            + "## Files\n"
            + $"- top: {topPath}\n"
            + $"- marketDataType: {typePath}\n"
            + $"- depth: {depthPath}\n"
            + $"- realtime bars: {barsPath}\n"
            + $"- sanitization: {sanitizationPath}\n";

        File.WriteAllText(reportPath, report);

        Console.WriteLine($"[OK] Top data export: {topPath} (rows={_wrapper.TopTicks.Count})");
        Console.WriteLine($"[OK] Market data type export: {typePath} (rows={_wrapper.MarketDataTypes.Count})");
        Console.WriteLine($"[OK] Depth data export: {depthPath} (rows={_wrapper.DepthRows.Count})");
        Console.WriteLine($"[OK] Realtime bars export: {barsPath} (rows={_wrapper.RealtimeBars.Count})");
        Console.WriteLine($"[OK] Market data sanitization export: {sanitizationPath} (rows={_wrapper.MarketDataSanitizationRows.Count})");
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

    private async Task RunManagedAccountsMode(EClientSocket client, CancellationToken token)
    {
        client.reqManagedAccts();
        var accountsList = await AwaitWithTimeout(_wrapper.ManagedAccountsTask, token, "managedAccounts");

        var accounts = accountsList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => new ManagedAccountRow(DateTime.UtcNow, a))
            .ToArray();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"managed_accounts_{timestamp}.json");
        WriteJson(path, accounts);

        Console.WriteLine($"[OK] Managed accounts export: {path} (rows={accounts.Length})");
    }

    private async Task RunFamilyCodesMode(EClientSocket client, CancellationToken token)
    {
        client.reqFamilyCodes();
        await AwaitWithTimeout(_wrapper.FamilyCodesTask, token, "familyCodes");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"family_codes_{timestamp}.json");
        WriteJson(path, _wrapper.FamilyCodesRows.ToArray());

        Console.WriteLine($"[OK] Family codes export: {path} (rows={_wrapper.FamilyCodesRows.Count})");
    }

    private async Task RunAccountUpdatesMode(EClientSocket client, CancellationToken token)
    {
        var subscriptionAccount = string.IsNullOrWhiteSpace(_options.UpdateAccount) ? _options.Account : _options.UpdateAccount;

        client.reqAccountUpdates(true, subscriptionAccount);
        await AwaitWithTimeout(_wrapper.AccountDownloadEndTask, token, "accountDownloadEnd");
        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        client.reqAccountUpdates(false, subscriptionAccount);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var valuesPath = Path.Combine(outputDir, $"account_updates_values_{timestamp}.json");
        var portfolioPath = Path.Combine(outputDir, $"account_updates_portfolio_{timestamp}.json");
        var timesPath = Path.Combine(outputDir, $"account_updates_time_{timestamp}.json");

        WriteJson(valuesPath, _wrapper.AccountValueUpdates.ToArray());
        WriteJson(portfolioPath, _wrapper.PortfolioUpdates.ToArray());
        WriteJson(timesPath, _wrapper.AccountUpdateTimes.ToArray());

        Console.WriteLine($"[OK] Account update values export: {valuesPath} (rows={_wrapper.AccountValueUpdates.Count})");
        Console.WriteLine($"[OK] Account update portfolio export: {portfolioPath} (rows={_wrapper.PortfolioUpdates.Count})");
        Console.WriteLine($"[OK] Account update times export: {timesPath} (rows={_wrapper.AccountUpdateTimes.Count})");
    }

    private async Task RunAccountUpdatesMultiMode(EClientSocket client, CancellationToken token)
    {
        const int reqId = 9701;
        client.reqAccountUpdatesMulti(reqId, _options.AccountUpdatesMultiAccount, _options.ModelCode, true);
        await AwaitWithTimeout(_wrapper.AccountUpdateMultiEndTask, token, "accountUpdateMultiEnd");
        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        client.cancelAccountUpdatesMulti(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"account_updates_multi_{timestamp}.json");
        WriteJson(path, _wrapper.AccountUpdateMultiRows.ToArray());

        Console.WriteLine($"[OK] Account updates multi export: {path} (rows={_wrapper.AccountUpdateMultiRows.Count})");
    }

    private async Task RunAccountSummaryOnlyMode(EClientSocket client, CancellationToken token)
    {
        const int reqId = 9702;
        client.reqAccountSummary(reqId, _options.AccountSummaryGroup, _options.AccountSummaryTags);
        await AwaitWithTimeout(_wrapper.AccountSummaryEndTask, token, "accountSummaryEnd");
        client.cancelAccountSummary(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"account_summary_subscription_{timestamp}.json");
        WriteJson(path, _wrapper.AccountSummaryRows.ToArray());

        Console.WriteLine($"[OK] Account summary export: {path} (rows={_wrapper.AccountSummaryRows.Count})");
    }

    private async Task RunPositionsMultiMode(EClientSocket client, CancellationToken token)
    {
        const int reqId = 9703;
        client.reqPositionsMulti(reqId, _options.PositionsMultiAccount, _options.ModelCode);
        await AwaitWithTimeout(_wrapper.PositionMultiEndTask, token, "positionMultiEnd");
        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        client.cancelPositionsMulti(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"positions_multi_{timestamp}.json");
        WriteJson(path, _wrapper.PositionMultiRows.ToArray());

        Console.WriteLine($"[OK] Positions multi export: {path} (rows={_wrapper.PositionMultiRows.Count})");
    }

    private async Task RunPnlAccountMode(EClientSocket client, CancellationToken token)
    {
        const int reqId = 9704;
        var pnlAccount = string.IsNullOrWhiteSpace(_options.PnlAccount) ? _options.Account : _options.PnlAccount;

        client.reqPnL(reqId, pnlAccount, _options.ModelCode);
        await AwaitWithTimeout(_wrapper.PnlFirstTask, token, "pnl");
        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        client.cancelPnL(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"pnl_account_{timestamp}.json");
        WriteJson(path, _wrapper.PnlRows.ToArray());

        Console.WriteLine($"[OK] PnL account export: {path} (rows={_wrapper.PnlRows.Count})");
    }

    private async Task RunPnlSingleMode(EClientSocket client, CancellationToken token)
    {
        if (_options.PnlConId <= 0)
        {
            throw new InvalidOperationException("pnl-single mode requires --pnl-conid > 0.");
        }

        const int reqId = 9705;
        var pnlAccount = string.IsNullOrWhiteSpace(_options.PnlAccount) ? _options.Account : _options.PnlAccount;

        client.reqPnLSingle(reqId, pnlAccount, _options.ModelCode, _options.PnlConId);
        var receivedFirstUpdate = true;
        try
        {
            await AwaitWithTimeout(_wrapper.PnlSingleFirstTask, token, "pnlSingle");
        }
        catch (TimeoutException)
        {
            receivedFirstUpdate = false;
            Console.WriteLine("[WARN] PnL single returned no update for the provided conId/account during capture window; exporting current rows.");
        }

        if (receivedFirstUpdate)
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        }

        client.cancelPnLSingle(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"pnl_single_{_options.PnlConId}_{timestamp}.json");
        WriteJson(path, _wrapper.PnlSingleRows.ToArray());

        Console.WriteLine($"[OK] PnL single export: {path} (rows={_wrapper.PnlSingleRows.Count})");
    }

    private async Task RunOptionChainsMode(EClientSocket client, CancellationToken token)
    {
        var underlying = ContractFactory.Stock(_options.Symbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        client.reqContractDetails(9801, underlying);
        await AwaitWithTimeout(_wrapper.ContractDetailsEndTask, token, "contractDetailsEnd");

        var underlyingDetails = _wrapper.ContractDetailsRows.FirstOrDefault();
        var underlyingConId = underlyingDetails?.Contract?.ConId ?? 0;
        if (underlyingConId <= 0)
        {
            throw new InvalidOperationException("Unable to resolve underlying conId for option chain request.");
        }

        client.reqSecDefOptParams(9802, _options.Symbol, _options.OptionFutFopExchange, _options.OptionUnderlyingSecType, underlyingConId);
        await AwaitWithTimeout(_wrapper.OptionChainEndTask, token, "securityDefinitionOptionParameterEnd");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var chainPath = Path.Combine(outputDir, $"option_chains_{_options.Symbol}_{timestamp}.json");

        WriteJson(chainPath, _wrapper.OptionChains.ToArray());
        Console.WriteLine($"[OK] Option chain export: {chainPath} (rows={_wrapper.OptionChains.Count})");
    }

    private async Task RunOptionExerciseMode(EClientSocket client, CancellationToken token)
    {
        if (!_options.OptionExerciseAllow)
        {
            throw new InvalidOperationException("Option exercise blocked: set --option-exercise-allow true to proceed.");
        }

        var option = ContractFactory.Option(
            _options.OptionSymbol,
            _options.OptionExpiry,
            _options.OptionStrike,
            _options.OptionRight,
            exchange: _options.OptionExchange,
            currency: _options.OptionCurrency,
            multiplier: _options.OptionMultiplier
        );

        client.exerciseOptions(
            9803,
            option,
            _options.OptionExerciseAction,
            _options.OptionExerciseQuantity,
            _options.Account,
            _options.OptionExerciseOverride
        );

        await Task.Delay(TimeSpan.FromSeconds(2), token);
        client.reqOpenOrders();
        await AwaitWithTimeout(_wrapper.OpenOrderEndTask, token, "openOrderEnd");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var requestPath = Path.Combine(outputDir, $"option_exercise_request_{timestamp}.json");
        var statusPath = Path.Combine(outputDir, $"option_exercise_status_{timestamp}.json");

        var request = new OptionExerciseRequestRow(
            DateTime.UtcNow,
            _options.OptionSymbol,
            _options.OptionExpiry,
            _options.OptionStrike,
            _options.OptionRight,
            _options.OptionExerciseAction,
            _options.OptionExerciseQuantity,
            _options.Account,
            _options.OptionExerciseOverride,
            _options.OptionExerciseManualTime
        );

        WriteJson(requestPath, new[] { request });
        WriteJson(statusPath, _wrapper.OrderStatusRows.ToArray());

        Console.WriteLine($"[OK] Option exercise request export: {requestPath}");
        Console.WriteLine($"[OK] Option exercise order-status export: {statusPath} (rows={_wrapper.OrderStatusRows.Count})");
    }

    private async Task RunOptionGreeksMode(EClientSocket client, CancellationToken token)
    {
        var option = ContractFactory.Option(
            _options.OptionSymbol,
            _options.OptionExpiry,
            _options.OptionStrike,
            _options.OptionRight,
            exchange: _options.OptionExchange,
            currency: _options.OptionCurrency,
            multiplier: _options.OptionMultiplier
        );

        while (_wrapper.ContractDetailsRows.TryDequeue(out _))
        {
        }

        client.reqContractDetails(98040, option);
        using (var resolveCts = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            resolveCts.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                await AwaitWithTimeout(_wrapper.ContractDetailsEndTask, resolveCts.Token, "contractDetailsEnd");
            }
            catch (TimeoutException) when (HasErrorCode("id=98040 code=200"))
            {
                if (_options.OptionGreeksAutoFallback)
                {
                    Console.WriteLine("[WARN] Exact option contract resolution failed with code=200; attempting option-chain fallback.");
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Unable to resolve option contract for {_options.OptionSymbol} {_options.OptionExpiry} {_options.OptionRight} {_options.OptionStrike}. "
                        + "IBKR returned code=200 (no security definition). Verify expiry/strike/right against option-chains output."
                    );
                }
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException("Timed out resolving option contract details (conId) for option-greeks request.");
            }
        }

        var candidates = _wrapper.ContractDetailsRows
            .Select(x => x.Contract)
            .Where(c => string.Equals(c.Symbol, _options.OptionSymbol, StringComparison.OrdinalIgnoreCase))
            .Where(c => string.Equals(c.Right, _options.OptionRight, StringComparison.OrdinalIgnoreCase))
            .Where(c => string.Equals(c.Currency, _options.OptionCurrency, StringComparison.OrdinalIgnoreCase))
            .Where(c => Math.Abs(c.Strike - _options.OptionStrike) < 0.0001)
            .Where(c => c.LastTradeDateOrContractMonth.StartsWith(_options.OptionExpiry, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Contract marketDataOption;
        if (candidates.Length == 0)
        {
            if (!_options.OptionGreeksAutoFallback)
            {
                throw new InvalidOperationException(
                    $"Unable to resolve option conId for {_options.OptionSymbol} {_options.OptionExpiry} {_options.OptionRight} {_options.OptionStrike}. "
                    + "Verify expiry/strike/right tuple and exchange/trading class from option-chains output, or set --option-greeks-auto-fallback true."
                );
            }

            var fallbackOption = await TryBuildOptionGreeksFallbackContractAsync(client, token);
            if (fallbackOption is null)
            {
                throw new InvalidOperationException(
                    $"Unable to resolve option conId for {_options.OptionSymbol} {_options.OptionExpiry} {_options.OptionRight} {_options.OptionStrike}, and no fallback contract was found from option-chains."
                );
            }

            marketDataOption = fallbackOption;
            Console.WriteLine(
                $"[WARN] Exact option tuple unavailable; fallback selected expiry={fallbackOption.LastTradeDateOrContractMonth} strike={fallbackOption.Strike} right={fallbackOption.Right} exchange={fallbackOption.Exchange}"
            );
        }
        else
        {
            var resolvedOption = candidates
                .OrderByDescending(c => c.ConId > 0)
                .ThenByDescending(c => string.Equals(c.Exchange, _options.OptionExchange, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => string.Equals(c.Exchange, "SMART", StringComparison.OrdinalIgnoreCase))
                .First();

            Console.WriteLine($"[OK] Resolved option conId={resolvedOption.ConId} localSymbol={resolvedOption.LocalSymbol} exchange={resolvedOption.Exchange}");
            marketDataOption = resolvedOption;
        }

        const int reqId = 9804;
        client.reqMarketDataType(_options.MarketDataType);
        client.reqMktData(reqId, marketDataOption, string.Empty, false, false, new List<TagValue>());

        var receivedFirstGreek = true;
        try
        {
            await AwaitWithTimeout(_wrapper.OptionGreeksFirstTask, token, "tickOptionComputation");
        }
        catch (TimeoutException)
        {
            receivedFirstGreek = false;
            Console.WriteLine("[WARN] Option greeks not received during capture window; exporting current rows.");
        }

        if (receivedFirstGreek)
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        }

        client.cancelMktData(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"option_greeks_{_options.OptionSymbol}_{timestamp}.json");
        WriteJson(path, _wrapper.OptionGreeks.ToArray());

        Console.WriteLine($"[OK] Option greeks export: {path} (rows={_wrapper.OptionGreeks.Count})");
    }

    private async Task<Contract?> TryBuildOptionGreeksFallbackContractAsync(EClientSocket client, CancellationToken token)
    {
        while (_wrapper.ContractDetailsRows.TryDequeue(out _))
        {
        }

        while (_wrapper.OptionChains.TryDequeue(out _))
        {
        }

        var underlying = ContractFactory.Stock(_options.OptionSymbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        client.reqContractDetails(98041, underlying);
        await Task.Delay(TimeSpan.FromSeconds(2), token);

        var underlyingConId = _wrapper.ContractDetailsRows
            .Select(x => x.Contract)
            .Where(c => string.Equals(c.Symbol, _options.OptionSymbol, StringComparison.OrdinalIgnoreCase))
            .Where(c => string.Equals(c.SecType, _options.OptionUnderlyingSecType, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ConId)
            .FirstOrDefault();

        if (underlyingConId <= 0)
        {
            return null;
        }

        client.reqSecDefOptParams(98042, _options.OptionSymbol, _options.OptionFutFopExchange, _options.OptionUnderlyingSecType, underlyingConId);
        try
        {
            await AwaitWithTimeout(_wrapper.OptionChainEndTask, token, "securityDefinitionOptionParameterEnd");
        }
        catch (TimeoutException)
        {
            return null;
        }

        var rows = _wrapper.OptionChains.ToArray();
        if (rows.Length == 0)
        {
            return null;
        }

        var expiryStrings = rows
            .SelectMany(r => r.Expirations)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (expiryStrings.Length == 0)
        {
            return null;
        }

        var requestedExpiry = DateOnly.TryParseExact(_options.OptionExpiry, "yyyyMMdd", out var parsedRequested)
            ? parsedRequested
            : DateOnly.FromDateTime(DateTime.UtcNow);

        var parsedExpiries = expiryStrings
            .Select(exp => new { Value = exp, Parsed = DateOnly.TryParseExact(exp, "yyyyMMdd", out var d) ? d : (DateOnly?)null })
            .Where(x => x.Parsed.HasValue)
            .Select(x => new { x.Value, Parsed = x.Parsed!.Value })
            .ToArray();

        if (parsedExpiries.Length == 0)
        {
            return null;
        }

        var nearestExpiry = parsedExpiries
            .OrderBy(x => Math.Abs(x.Parsed.DayNumber - requestedExpiry.DayNumber))
            .First()
            .Value;

        var strikeCandidates = rows
            .Where(r => r.Expirations.Contains(nearestExpiry, StringComparer.OrdinalIgnoreCase))
            .SelectMany(r => r.Strikes)
            .Distinct()
            .ToArray();

        if (strikeCandidates.Length == 0)
        {
            strikeCandidates = rows.SelectMany(r => r.Strikes).Distinct().ToArray();
        }

        if (strikeCandidates.Length == 0)
        {
            return null;
        }

        var nearestStrike = strikeCandidates
            .OrderBy(strike => Math.Abs(strike - _options.OptionStrike))
            .First();

        var preferredExchange = rows
            .OrderByDescending(r => string.Equals(r.Exchange, _options.OptionExchange, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => string.Equals(r.Exchange, "SMART", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Exchange)
            .FirstOrDefault() ?? _options.OptionExchange;

        return ContractFactory.Option(
            _options.OptionSymbol,
            nearestExpiry,
            nearestStrike,
            _options.OptionRight,
            exchange: preferredExchange,
            currency: _options.OptionCurrency,
            multiplier: _options.OptionMultiplier
        );
    }

    private async Task RunCryptoPermissionsMode(EClientSocket client, CancellationToken token)
    {
        var crypto = ContractFactory.Crypto(_options.CryptoSymbol, exchange: _options.CryptoExchange, currency: _options.CryptoCurrency);

        while (_wrapper.ContractDetailsRows.TryDequeue(out _))
        {
        }

        client.reqContractDetails(9901, crypto);
        var detailsResolved = true;
        using (var detailsCts = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            detailsCts.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                await AwaitWithTimeout(_wrapper.ContractDetailsEndTask, detailsCts.Token, "contractDetailsEnd");
            }
            catch (TimeoutException)
            {
                detailsResolved = false;
            }
        }

        client.reqMarketDataType(_options.MarketDataType);
        client.reqMktData(9902, crypto, string.Empty, false, false, new List<TagValue>());
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(2, Math.Min(_options.CaptureSeconds, 8))), token);
        client.cancelMktData(9902);

        var detailsCount = _wrapper.ContractDetailsRows.Count;
        var ticksCaptured = _wrapper.TopTicks.Count;
        var relatedErrors = _wrapper.Errors
            .Where(e => e.Contains("id=9901", StringComparison.OrdinalIgnoreCase) || e.Contains("id=9902", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"crypto_permissions_{_options.CryptoSymbol}_{timestamp}.json");

        var rows = new[]
        {
            new CryptoPermissionRow(
                DateTime.UtcNow,
                _options.CryptoSymbol,
                _options.CryptoExchange,
                _options.CryptoCurrency,
                detailsResolved,
                detailsCount,
                ticksCaptured,
                relatedErrors
            )
        };

        WriteJson(path, rows);
        Console.WriteLine($"[OK] Crypto permissions export: {path} (details={detailsCount}, topTicks={ticksCaptured}, errors={relatedErrors.Length})");
    }

    private async Task RunCryptoContractDefinitionMode(EClientSocket client, CancellationToken token)
    {
        var crypto = ContractFactory.Crypto(_options.CryptoSymbol, exchange: _options.CryptoExchange, currency: _options.CryptoCurrency);

        while (_wrapper.ContractDetailsRows.TryDequeue(out _))
        {
        }

        client.reqContractDetails(9903, crypto);
        using (var detailsCts = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            detailsCts.CancelAfter(TimeSpan.FromSeconds(10));
            await AwaitWithTimeout(_wrapper.ContractDetailsEndTask, detailsCts.Token, "contractDetailsEnd");
        }

        var details = _wrapper.ContractDetailsRows
            .Where(d => string.Equals(d.Contract.SecType, "CRYPTO", StringComparison.OrdinalIgnoreCase))
            .Select(d => new ContractDetailsRow(
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
            ))
            .ToArray();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"crypto_contract_details_{_options.CryptoSymbol}_{timestamp}.json");

        WriteJson(path, details);
        Console.WriteLine($"[OK] Crypto contract definition export: {path} (rows={details.Length})");
    }

    private async Task RunCryptoStreamingMode(EClientSocket client, CancellationToken token)
    {
        var crypto = ContractFactory.Crypto(_options.CryptoSymbol, exchange: _options.CryptoExchange, currency: _options.CryptoCurrency);

        client.reqMarketDataType(_options.MarketDataType);
        client.reqMktData(9904, crypto, string.Empty, false, false, new List<TagValue>());
        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        client.cancelMktData(9904);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var ticksPath = Path.Combine(outputDir, $"crypto_top_data_{_options.CryptoSymbol}_{timestamp}.json");
        var typesPath = Path.Combine(outputDir, $"crypto_top_data_type_{_options.CryptoSymbol}_{timestamp}.json");
        var sanitizationPath = Path.Combine(outputDir, $"crypto_top_data_sanitization_{_options.CryptoSymbol}_{timestamp}.json");

        WriteJson(ticksPath, _wrapper.TopTicks.ToArray());
        WriteJson(typesPath, _wrapper.MarketDataTypes.ToArray());
        WriteJson(sanitizationPath, _wrapper.MarketDataSanitizationRows.ToArray());

        Console.WriteLine($"[OK] Crypto streaming top data export: {ticksPath} (rows={_wrapper.TopTicks.Count})");
        Console.WriteLine($"[OK] Crypto market data type export: {typesPath} (rows={_wrapper.MarketDataTypes.Count})");
        Console.WriteLine($"[OK] Crypto market data sanitization export: {sanitizationPath} (rows={_wrapper.MarketDataSanitizationRows.Count})");
    }

    private async Task RunCryptoHistoricalMode(EClientSocket client, CancellationToken token)
    {
        ValidateHistoricalBarRequestLimitations(_options.HistoricalDuration, _options.HistoricalBarSize);

        var crypto = ContractFactory.Crypto(_options.CryptoSymbol, exchange: _options.CryptoExchange, currency: _options.CryptoCurrency);

        client.reqHistoricalData(
            9905,
            crypto,
            _options.HistoricalEndDateTime,
            _options.HistoricalDuration,
            _options.HistoricalBarSize,
            _options.HistoricalWhatToShow,
            _options.HistoricalUseRth,
            _options.HistoricalFormatDate,
            false,
            new List<TagValue>()
        );

        try
        {
            await AwaitWithTimeout(_wrapper.HistoricalDataEndTask, token, "historicalDataEnd");
        }
        catch (TimeoutException) when (HasErrorCode("id=9905 code=10285"))
        {
            Console.WriteLine("[WARN] Crypto historical request could not complete due API compatibility (code=10285). Exporting current rows.");
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"crypto_historical_bars_{_options.CryptoSymbol}_{timestamp}.json");

        WriteJson(path, _wrapper.HistoricalBars.ToArray());
        Console.WriteLine($"[OK] Crypto historical bars export: {path} (rows={_wrapper.HistoricalBars.Count})");
    }

    private async Task RunCryptoOrderPlacementMode(EClientSocket client, CancellationToken token)
    {
        if (!_options.CryptoOrderAllow)
        {
            throw new InvalidOperationException("Crypto order blocked: set --crypto-order-allow true to proceed.");
        }

        if (_options.CryptoOrderQuantity <= 0)
        {
            throw new InvalidOperationException("Crypto order blocked: --crypto-order-qty must be > 0.");
        }

        if (_options.CryptoOrderLimit <= 0)
        {
            throw new InvalidOperationException("Crypto order blocked: --crypto-order-limit must be > 0.");
        }

        var action = _options.CryptoOrderAction.ToUpperInvariant();
        if (action is not ("BUY" or "SELL"))
        {
            throw new InvalidOperationException("Crypto order blocked: --crypto-order-action must be BUY or SELL.");
        }

        var notional = _options.CryptoOrderQuantity * _options.CryptoOrderLimit;
        if (notional > _options.CryptoMaxNotional)
        {
            throw new InvalidOperationException($"Crypto order blocked: notional {notional:F2} exceeds --crypto-max-notional {_options.CryptoMaxNotional:F2}.");
        }

        var crypto = ContractFactory.Crypto(_options.CryptoSymbol, exchange: _options.CryptoExchange, currency: _options.CryptoCurrency);
        var order = OrderFactory.Limit(action, _options.CryptoOrderQuantity, _options.CryptoOrderLimit);
        var nextOrderId = await _wrapper.NextValidIdTask;

        order.OrderId = nextOrderId;
        order.OrderRef = $"HARVESTER_CRYPTO_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        order.Transmit = true;

        client.placeOrder(order.OrderId, crypto, order);
        Console.WriteLine($"[OK] Crypto order transmitted: orderId={order.OrderId} symbol={_options.CryptoSymbol} action={action} qty={_options.CryptoOrderQuantity} lmt={_options.CryptoOrderLimit}");

        await Task.Delay(TimeSpan.FromSeconds(4), token);
        client.reqOpenOrders();
        await AwaitWithTimeout(_wrapper.OpenOrderEndTask, token, "openOrderEnd");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var requestPath = Path.Combine(outputDir, $"crypto_order_request_{timestamp}.json");
        var statusPath = Path.Combine(outputDir, $"crypto_order_status_{timestamp}.json");

        var request = new[]
        {
            new CryptoOrderRequestRow(
                DateTime.UtcNow,
                order.OrderId,
                _options.CryptoSymbol,
                _options.CryptoExchange,
                _options.CryptoCurrency,
                action,
                _options.CryptoOrderQuantity,
                _options.CryptoOrderLimit,
                notional,
                _options.Account,
                order.OrderRef
            )
        };

        WriteJson(requestPath, request);
        WriteJson(statusPath, _wrapper.OrderStatusRows.ToArray());

        Console.WriteLine($"[OK] Crypto order request export: {requestPath}");
        Console.WriteLine($"[OK] Crypto order status export: {statusPath} (rows={_wrapper.OrderStatusRows.Count})");
    }

    private async Task RunFaAllocationMethodsAndGroupsMode(EClientSocket client, CancellationToken token)
    {
        while (_wrapper.FaDataRows.TryDequeue(out _))
        {
        }

        while (_wrapper.DisplayGroupListRows.TryDequeue(out _))
        {
        }

        client.requestFA(Constants.FaGroups);
        await Task.Delay(TimeSpan.FromSeconds(2), token);

        client.queryDisplayGroups(9951);
        await Task.Delay(TimeSpan.FromSeconds(2), token);

        var methods = new[]
        {
            new FaAllocationMethodRow("Group", "EqualQuantity", 0, "Requires order quantity; splits equally across accounts in group."),
            new FaAllocationMethodRow("Group", "NetLiq", 0, "Requires order quantity; allocates by net liquidation value."),
            new FaAllocationMethodRow("Group", "AvailableEquity", 0, "Requires order quantity; allocates by available equity."),
            new FaAllocationMethodRow("Group", "PctChange", 0, "No explicit total quantity; adjusts existing positions by percent."),
            new FaAllocationMethodRow("Profile", "Percentages", 1, "Allocates using explicit percentages per account."),
            new FaAllocationMethodRow("Profile", "Financial Ratios", 2, "Allocates using ratio values per account."),
            new FaAllocationMethodRow("Profile", "Shares", 3, "Allocates explicit shares per account; total is sum of profile shares.")
        };

        var groups = _wrapper.FaDataRows.Where(x => x.FaDataType == Constants.FaGroups).ToArray();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var methodsPath = Path.Combine(outputDir, $"fa_allocation_methods_{timestamp}.json");
        var groupsPath = Path.Combine(outputDir, $"fa_groups_{timestamp}.json");
        var displayPath = Path.Combine(outputDir, $"fa_display_groups_{timestamp}.json");

        WriteJson(methodsPath, methods);
        WriteJson(groupsPath, groups);
        WriteJson(displayPath, _wrapper.DisplayGroupListRows.ToArray());

        Console.WriteLine($"[OK] FA allocation methods export: {methodsPath} (rows={methods.Length})");
        Console.WriteLine($"[OK] FA groups export: {groupsPath} (rows={groups.Length})");
        Console.WriteLine($"[OK] FA display groups export: {displayPath} (rows={_wrapper.DisplayGroupListRows.Count})");
    }

    private async Task RunFaGroupsAndProfilesMode(EClientSocket client, CancellationToken token)
    {
        while (_wrapper.FaDataRows.TryDequeue(out _))
        {
        }

        client.requestFA(Constants.FaAliases);
        await Task.Delay(TimeSpan.FromSeconds(2), token);

        client.requestFA(Constants.FaGroups);
        await Task.Delay(TimeSpan.FromSeconds(2), token);

        client.requestFA(Constants.FaProfiles);
        await Task.Delay(TimeSpan.FromSeconds(2), token);

        var all = _wrapper.FaDataRows.ToArray();
        var aliases = all.Where(x => x.FaDataType == Constants.FaAliases).ToArray();
        var groups = all.Where(x => x.FaDataType == Constants.FaGroups).ToArray();
        var profiles = all.Where(x => x.FaDataType == Constants.FaProfiles).ToArray();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var allPath = Path.Combine(outputDir, $"fa_data_all_{timestamp}.json");
        var aliasesPath = Path.Combine(outputDir, $"fa_aliases_{timestamp}.json");
        var groupsPath = Path.Combine(outputDir, $"fa_groups_api_{timestamp}.json");
        var profilesPath = Path.Combine(outputDir, $"fa_profiles_api_{timestamp}.json");

        WriteJson(allPath, all);
        WriteJson(aliasesPath, aliases);
        WriteJson(groupsPath, groups);
        WriteJson(profilesPath, profiles);

        Console.WriteLine($"[OK] FA data export: {allPath} (rows={all.Length})");
        Console.WriteLine($"[OK] FA aliases export: {aliasesPath} (rows={aliases.Length})");
        Console.WriteLine($"[OK] FA groups export: {groupsPath} (rows={groups.Length})");
        Console.WriteLine($"[OK] FA profiles export: {profilesPath} (rows={profiles.Length})");
    }

    private async Task RunFaUnificationMode(EClientSocket client, CancellationToken token)
    {
        while (_wrapper.FaDataRows.TryDequeue(out _))
        {
        }

        var errorCountBefore = _wrapper.Errors.Count;

        client.requestFA(Constants.FaGroups);
        await Task.Delay(TimeSpan.FromSeconds(2), token);

        client.requestFA(Constants.FaProfiles);
        await Task.Delay(TimeSpan.FromSeconds(2), token);

        var all = _wrapper.FaDataRows.ToArray();
        var groups = all.Where(x => x.FaDataType == Constants.FaGroups).ToArray();
        var profiles = all.Where(x => x.FaDataType == Constants.FaProfiles).ToArray();
        var newErrors = _wrapper.Errors.Skip(errorCountBefore).ToArray();

        var profileRequestErrored = newErrors.Any(e => e.Contains("profile", StringComparison.OrdinalIgnoreCase))
            || (groups.Length > 0 && profiles.Length == 0 && newErrors.Length > 0);
        var likelyUnified = groups.Length > 0 && (profiles.Length == 0 || profileRequestErrored);

        var summary = new[]
        {
            new FaUnificationRow(
                DateTime.UtcNow,
                groups.Length,
                profiles.Length,
                profileRequestErrored,
                likelyUnified,
                "If likelyUnified=true, TWS/IBGW may be using merged groups/profiles mode (build 983+ setting)."
            )
        };

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var summaryPath = Path.Combine(outputDir, $"fa_unification_summary_{timestamp}.json");
        var dataPath = Path.Combine(outputDir, $"fa_unification_data_{timestamp}.json");

        WriteJson(summaryPath, summary);
        WriteJson(dataPath, all);

        Console.WriteLine($"[OK] FA unification summary export: {summaryPath}");
        Console.WriteLine($"[OK] FA unification raw data export: {dataPath} (rows={all.Length})");
    }

    private async Task RunFaModelPortfoliosMode(EClientSocket client, CancellationToken token)
    {
        var modelCode = string.IsNullOrWhiteSpace(_options.FaModelCode) ? _options.ModelCode : _options.FaModelCode;
        if (string.IsNullOrWhiteSpace(modelCode))
        {
            throw new InvalidOperationException("fa-model-portfolios mode requires --fa-model-code (or --model-code). ");
        }

        const int positionsReqId = 9952;
        const int updatesReqId = 9953;
        var account = string.IsNullOrWhiteSpace(_options.FaAccount) ? _options.Account : _options.FaAccount;

        client.reqPositionsMulti(positionsReqId, account, modelCode);
        try
        {
            await AwaitWithTimeout(_wrapper.PositionMultiEndTask, token, "positionMultiEnd");
        }
        catch (TimeoutException) when (HasErrorCode($"id={positionsReqId} code=321"))
        {
            Console.WriteLine("[WARN] FA model positions request did not complete due model/account validation (code=321). Exporting current rows.");
        }

        client.cancelPositionsMulti(positionsReqId);

        client.reqAccountUpdatesMulti(updatesReqId, account, modelCode, true);
        try
        {
            await AwaitWithTimeout(_wrapper.AccountUpdateMultiEndTask, token, "accountUpdateMultiEnd");
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(6, Math.Max(2, _options.CaptureSeconds))), token);
        }
        catch (TimeoutException) when (HasErrorCode($"id={updatesReqId} code=321"))
        {
            Console.WriteLine("[WARN] FA model account-updates request did not complete due model/account validation (code=321). Exporting current rows.");
        }
        catch (TimeoutException) when (HasErrorCode($"id={positionsReqId} code=321"))
        {
            Console.WriteLine("[WARN] FA model account-updates request timed out after positions-model validation failure; exporting current rows.");
        }

        client.cancelAccountUpdatesMulti(updatesReqId);

        var positions = _wrapper.PositionMultiRows.Where(x => x.RequestId == positionsReqId).ToArray();
        var updates = _wrapper.AccountUpdateMultiRows.Where(x => x.RequestId == updatesReqId).ToArray();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var positionsPath = Path.Combine(outputDir, $"fa_model_positions_{modelCode}_{timestamp}.json");
        var updatesPath = Path.Combine(outputDir, $"fa_model_account_updates_{modelCode}_{timestamp}.json");

        WriteJson(positionsPath, positions);
        WriteJson(updatesPath, updates);

        Console.WriteLine($"[OK] FA model positions export: {positionsPath} (rows={positions.Length})");
        Console.WriteLine($"[OK] FA model account-updates export: {updatesPath} (rows={updates.Length})");
    }

    private async Task RunFaOrderPlacementMode(EClientSocket client, CancellationToken token)
    {
        if (!_options.FaOrderAllow)
        {
            throw new InvalidOperationException("FA order blocked: set --fa-order-allow true to proceed.");
        }

        var action = _options.FaOrderAction.ToUpperInvariant();
        if (action is not ("BUY" or "SELL"))
        {
            throw new InvalidOperationException("FA order blocked: --fa-order-action must be BUY or SELL.");
        }

        if (_options.FaOrderQuantity <= 0)
        {
            throw new InvalidOperationException("FA order blocked: --fa-order-qty must be > 0.");
        }

        if (_options.FaOrderLimit <= 0)
        {
            throw new InvalidOperationException("FA order blocked: --fa-order-limit must be > 0.");
        }

        if (string.IsNullOrWhiteSpace(_options.FaOrderGroup) && string.IsNullOrWhiteSpace(_options.FaOrderProfile))
        {
            throw new InvalidOperationException("FA order blocked: provide --fa-order-group or --fa-order-profile.");
        }

        var notional = _options.FaOrderQuantity * _options.FaOrderLimit;
        if (notional > _options.FaMaxNotional)
        {
            throw new InvalidOperationException($"FA order blocked: notional {notional:F2} exceeds --fa-max-notional {_options.FaMaxNotional:F2}.");
        }

        var contract = ContractFactory.Stock(
            _options.FaOrderSymbol,
            exchange: _options.FaOrderExchange,
            currency: _options.FaOrderCurrency,
            primaryExchange: _options.FaOrderPrimaryExchange
        );

        var order = OrderFactory.Limit(action, _options.FaOrderQuantity, _options.FaOrderLimit);
        var nextOrderId = await _wrapper.NextValidIdTask;
        order.OrderId = nextOrderId;
        order.OrderRef = $"HARVESTER_FA_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        order.Transmit = true;
        order.Account = string.IsNullOrWhiteSpace(_options.FaOrderAccount) ? _options.Account : _options.FaOrderAccount;

        if (!string.IsNullOrWhiteSpace(_options.FaOrderGroup))
        {
            order.FaGroup = _options.FaOrderGroup;
        }

        if (!string.IsNullOrWhiteSpace(_options.FaOrderMethod))
        {
            order.FaMethod = _options.FaOrderMethod;
        }

        if (!string.IsNullOrWhiteSpace(_options.FaOrderPercentage))
        {
            order.FaPercentage = _options.FaOrderPercentage;
        }

        if (!string.IsNullOrWhiteSpace(_options.FaOrderProfile))
        {
            order.FaProfile = _options.FaOrderProfile;
        }

        client.placeOrder(order.OrderId, contract, order);
        Console.WriteLine($"[OK] FA order transmitted: orderId={order.OrderId} symbol={_options.FaOrderSymbol} action={action} qty={_options.FaOrderQuantity} lmt={_options.FaOrderLimit}");

        await Task.Delay(TimeSpan.FromSeconds(4), token);
        client.reqOpenOrders();
        await AwaitWithTimeout(_wrapper.OpenOrderEndTask, token, "openOrderEnd");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var requestPath = Path.Combine(outputDir, $"fa_order_request_{timestamp}.json");
        var statusPath = Path.Combine(outputDir, $"fa_order_status_{timestamp}.json");

        var request = new[]
        {
            new FaOrderRequestRow(
                DateTime.UtcNow,
                order.OrderId,
                _options.FaOrderSymbol,
                action,
                _options.FaOrderQuantity,
                _options.FaOrderLimit,
                notional,
                order.Account,
                order.FaGroup,
                order.FaProfile,
                order.FaMethod,
                order.FaPercentage,
                order.OrderRef
            )
        };

        WriteJson(requestPath, request);
        WriteJson(statusPath, _wrapper.OrderStatusRows.ToArray());

        Console.WriteLine($"[OK] FA order request export: {requestPath}");
        Console.WriteLine($"[OK] FA order status export: {statusPath} (rows={_wrapper.OrderStatusRows.Count})");
    }

    private async Task RunFundamentalDataMode(EClientSocket client, CancellationToken token)
    {
        while (_wrapper.FundamentalDataRows.TryDequeue(out _))
        {
        }

        var contract = ContractFactory.Stock(_options.Symbol, exchange: "SMART", primaryExchange: _options.PrimaryExchange);
        const int reqId = 9961;
        client.reqFundamentalData(reqId, contract, _options.FundamentalReportType, new List<TagValue>());

        try
        {
            await AwaitWithTimeout(_wrapper.FundamentalDataTask, token, "fundamentalData");
        }
        catch (TimeoutException) when (HasErrorCode($"id={reqId}"))
        {
            Console.WriteLine("[WARN] Fundamental data callback not received before timeout; request returned API errors. Exporting current rows.");
        }

        client.cancelFundamentalData(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var dataPath = Path.Combine(outputDir, $"fundamental_data_{_options.Symbol}_{_options.FundamentalReportType}_{timestamp}.json");

        WriteJson(dataPath, _wrapper.FundamentalDataRows.ToArray());
        Console.WriteLine($"[OK] Fundamental data export: {dataPath} (rows={_wrapper.FundamentalDataRows.Count})");
    }

    private Task RunWshFiltersMode(EClientSocket client, CancellationToken token)
    {
        var hasWshMetaRequest = typeof(EClientSocket).GetMethod("reqWshMetaData") is not null;
        var hasWshEventRequest = typeof(EClientSocket).GetMethod("reqWshEventData") is not null;
        var hasWshMetaCallback = typeof(EWrapper).GetMethod("wshMetaData") is not null;
        var hasWshEventCallback = typeof(EWrapper).GetMethod("wshEventData") is not null;

        var supported = hasWshMetaRequest && hasWshEventRequest && hasWshMetaCallback && hasWshEventCallback;
        var rows = new[]
        {
            new WshFilterSupportRow(
                DateTime.UtcNow,
                supported,
                hasWshMetaRequest,
                hasWshEventRequest,
                hasWshMetaCallback,
                hasWshEventCallback,
                _options.WshFilterJson,
                supported
                    ? "WSH APIs detected in current assembly; runtime scaffolding can be expanded to execute live WSH filter requests."
                    : "Pinned IBApi assembly does not expose WSH APIs (reqWshMetaData/reqWshEventData callbacks). Upgrade IBApi to implement live WSH filter requests."
            )
        };

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"wsh_filters_support_{timestamp}.json");
        WriteJson(path, rows);

        Console.WriteLine($"[OK] WSH filters compatibility export: {path}");
        if (!supported)
        {
            Console.WriteLine("[WARN] WSH filters are not callable with installed IBApi package; exported upgrade-readiness diagnostics.");
        }

        return Task.CompletedTask;
    }

    private Task RunErrorCodesMode(EClientSocket client, CancellationToken token)
    {
        var observed = _wrapper.Errors
            .Select(ParseObservedError)
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();

        var observedCounts = observed
            .GroupBy(x => x.Code)
            .ToDictionary(g => g.Key, g => g.Count());

        var systemRows = BuildSystemMessageCodes()
            .Select(x => new ErrorCodeRow(x.Code, x.Name, x.Description, observedCounts.TryGetValue(x.Code, out var count) ? count : 0))
            .ToArray();

        var warningRows = BuildWarningMessageCodes()
            .Select(x => new ErrorCodeRow(x.Code, x.Name, x.Description, observedCounts.TryGetValue(x.Code, out var count) ? count : 0))
            .ToArray();

        var twsRows = BuildTwsErrorCodes()
            .Select(x => new ErrorCodeRow(x.Code, x.Name, x.Description, observedCounts.TryGetValue(x.Code, out var count) ? count : 0))
            .ToArray();

        var clientRows = BuildClientErrorCodes()
            .Select(x => new ErrorCodeRow(x.Code, x.Name, x.Description, 0))
            .ToArray();

        var uncataloguedObserved = observed
            .Where(x => !systemRows.Any(r => r.Code == x.Code)
                && !warningRows.Any(r => r.Code == x.Code)
                && !twsRows.Any(r => r.Code == x.Code)
                && !clientRows.Any(r => r.Code == x.Code))
            .ToArray();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var systemPath = Path.Combine(outputDir, $"error_codes_system_{timestamp}.json");
        var warningPath = Path.Combine(outputDir, $"error_codes_warning_{timestamp}.json");
        var clientPath = Path.Combine(outputDir, $"error_codes_client_{timestamp}.json");
        var twsPath = Path.Combine(outputDir, $"error_codes_tws_{timestamp}.json");
        var observedPath = Path.Combine(outputDir, $"error_codes_observed_{timestamp}.json");
        var uncataloguedPath = Path.Combine(outputDir, $"error_codes_uncatalogued_{timestamp}.json");

        WriteJson(systemPath, systemRows);
        WriteJson(warningPath, warningRows);
        WriteJson(clientPath, clientRows);
        WriteJson(twsPath, twsRows);
        WriteJson(observedPath, observed);
        WriteJson(uncataloguedPath, uncataloguedObserved);

        Console.WriteLine($"[OK] System message codes export: {systemPath} (rows={systemRows.Length})");
        Console.WriteLine($"[OK] Warning message codes export: {warningPath} (rows={warningRows.Length})");
        Console.WriteLine($"[OK] Client error codes export: {clientPath} (rows={clientRows.Length})");
        Console.WriteLine($"[OK] TWS error codes export: {twsPath} (rows={twsRows.Length})");
        Console.WriteLine($"[OK] Observed error codes export: {observedPath} (rows={observed.Length})");
        Console.WriteLine($"[OK] Uncatalogued observed export: {uncataloguedPath} (rows={uncataloguedObserved.Length})");

        return Task.CompletedTask;
    }

    private async Task RunScannerExamplesMode(EClientSocket client, CancellationToken token)
    {
        while (_wrapper.ScannerDataRows.TryDequeue(out _))
        {
        }

        const int reqId = 9971;
        var subscription = BuildScannerSubscriptionFromOptions();
        client.reqScannerSubscription(reqId, subscription, new List<TagValue>(), new List<TagValue>());

        try
        {
            await AwaitWithTimeout(_wrapper.ScannerDataEndTask, token, "scannerDataEnd");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("[WARN] Scanner examples timed out waiting for scannerDataEnd; exporting current rows.");
        }

        client.cancelScannerSubscription(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var rowsPath = Path.Combine(outputDir, $"scanner_examples_{timestamp}.json");
        var requestPath = Path.Combine(outputDir, $"scanner_examples_request_{timestamp}.json");

        var requestRow = new[]
        {
            new ScannerRequestRow(
                reqId,
                subscription.Instrument,
                subscription.LocationCode,
                subscription.ScanCode,
                subscription.NumberOfRows,
                _options.ScannerScannerSettingPairs,
                _options.ScannerFilterTagValues,
                _options.ScannerOptionsTagValues
            )
        };

        WriteJson(rowsPath, _wrapper.ScannerDataRows.Where(x => x.RequestId == reqId).ToArray());
        WriteJson(requestPath, requestRow);

        Console.WriteLine($"[OK] Scanner examples export: {rowsPath} (rows={_wrapper.ScannerDataRows.Count(x => x.RequestId == reqId)})");
        Console.WriteLine($"[OK] Scanner examples request export: {requestPath}");
    }

    private async Task RunScannerComplexMode(EClientSocket client, CancellationToken token)
    {
        while (_wrapper.ScannerDataRows.TryDequeue(out _))
        {
        }

        const int reqId = 9972;
        var subscription = BuildScannerSubscriptionFromOptions();
        if (!string.IsNullOrWhiteSpace(_options.ScannerScannerSettingPairs))
        {
            subscription.ScannerSettingPairs = _options.ScannerScannerSettingPairs;
        }

        var filterOptions = ParseTagValuePairs(_options.ScannerFilterTagValues);
        var scannerOptions = ParseTagValuePairs(_options.ScannerOptionsTagValues);

        client.reqScannerSubscription(reqId, subscription, filterOptions, scannerOptions);

        try
        {
            await AwaitWithTimeout(_wrapper.ScannerDataEndTask, token, "scannerDataEnd");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("[WARN] Scanner complex run timed out waiting for scannerDataEnd; exporting current rows.");
        }

        client.cancelScannerSubscription(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var rowsPath = Path.Combine(outputDir, $"scanner_complex_{timestamp}.json");
        var requestPath = Path.Combine(outputDir, $"scanner_complex_request_{timestamp}.json");

        var requestRow = new[]
        {
            new ScannerRequestRow(
                reqId,
                subscription.Instrument,
                subscription.LocationCode,
                subscription.ScanCode,
                subscription.NumberOfRows,
                subscription.ScannerSettingPairs,
                string.Join(';', filterOptions.Select(x => $"{x.Tag}={x.Value}")),
                string.Join(';', scannerOptions.Select(x => $"{x.Tag}={x.Value}"))
            )
        };

        WriteJson(rowsPath, _wrapper.ScannerDataRows.Where(x => x.RequestId == reqId).ToArray());
        WriteJson(requestPath, requestRow);

        Console.WriteLine($"[OK] Scanner complex export: {rowsPath} (rows={_wrapper.ScannerDataRows.Count(x => x.RequestId == reqId)})");
        Console.WriteLine($"[OK] Scanner complex request export: {requestPath}");
    }

    private async Task RunScannerParametersMode(EClientSocket client, CancellationToken token)
    {
        while (_wrapper.ScannerParametersRows.TryDequeue(out _))
        {
        }

        client.reqScannerParameters();
        try
        {
            await AwaitWithTimeout(_wrapper.ScannerParametersTask, token, "scannerParameters");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("[WARN] Scanner parameters callback not received before timeout; exporting current rows.");
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var jsonPath = Path.Combine(outputDir, $"scanner_parameters_{timestamp}.json");
        var xmlPath = Path.Combine(outputDir, $"scanner_parameters_{timestamp}.xml");

        var rows = _wrapper.ScannerParametersRows.ToArray();
        WriteJson(jsonPath, rows);

        var xml = rows.LastOrDefault()?.Xml ?? string.Empty;
        File.WriteAllText(xmlPath, xml);

        Console.WriteLine($"[OK] Scanner parameters JSON export: {jsonPath} (rows={rows.Length})");
        Console.WriteLine($"[OK] Scanner parameters XML export: {xmlPath} (chars={xml.Length})");
    }

    private async Task RunScannerWorkbenchMode(EClientSocket client, CancellationToken token)
    {
        var scanCodes = _options.ScannerWorkbenchCodes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant())
            .Distinct()
            .ToArray();

        if (scanCodes.Length == 0)
        {
            throw new InvalidOperationException("Scanner workbench requires at least one scan code in --scanner-workbench-codes.");
        }

        var runs = Math.Max(1, _options.ScannerWorkbenchRuns);
        var captureSeconds = Math.Max(1, _options.ScannerWorkbenchCaptureSeconds);
        var minRows = Math.Max(0, _options.ScannerWorkbenchMinRows);
        var filterOptions = ParseTagValuePairs(_options.ScannerFilterTagValues);
        var scannerOptions = ParseTagValuePairs(_options.ScannerOptionsTagValues);

        var runRows = new List<ScannerWorkbenchRunRow>();
        var scoreRows = new List<ScannerWorkbenchScoreRow>();
        var baseReqId = 9980;

        for (var codeIndex = 0; codeIndex < scanCodes.Length; codeIndex++)
        {
            var scanCode = scanCodes[codeIndex];

            for (var runIndex = 1; runIndex <= runs; runIndex++)
            {
                var reqId = baseReqId + (codeIndex * 100) + runIndex;
                var subscription = BuildScannerSubscriptionFromOptions();
                subscription.ScanCode = scanCode;

                var errorCountBefore = _wrapper.Errors.Count;
                var startedUtc = DateTime.UtcNow;
                var stopwatch = Stopwatch.StartNew();

                client.reqScannerSubscription(reqId, subscription, filterOptions, scannerOptions);
                await Task.Delay(TimeSpan.FromSeconds(captureSeconds), token);
                client.cancelScannerSubscription(reqId);
                await Task.Delay(TimeSpan.FromMilliseconds(400), token);

                stopwatch.Stop();

                var rowsForReq = _wrapper.ScannerDataRows
                    .Where(x => x.RequestId == reqId)
                    .OrderBy(x => x.TimestampUtc)
                    .ToArray();

                var newErrors = _wrapper.Errors.Skip(errorCountBefore).ToArray();
                var reqErrors = newErrors.Where(x => x.Contains($"id={reqId}", StringComparison.OrdinalIgnoreCase)).ToArray();
                var reqErrorCodes = reqErrors
                    .Select(ParseObservedError)
                    .Where(x => x is not null)
                    .Select(x => x!.Code)
                    .Distinct()
                    .ToArray();

                var firstRowSeconds = rowsForReq.Length == 0
                    ? (double?)null
                    : Math.Max(0, (rowsForReq[0].TimestampUtc - startedUtc).TotalSeconds);

                runRows.Add(new ScannerWorkbenchRunRow(
                    DateTime.UtcNow,
                    reqId,
                    scanCode,
                    runIndex,
                    rowsForReq.Length,
                    Math.Round(stopwatch.Elapsed.TotalSeconds, 3),
                    firstRowSeconds,
                    reqErrors.Length,
                    string.Join(',', reqErrorCodes)
                ));
            }

            var grouped = runRows.Where(x => x.ScanCode == scanCode).ToArray();
            var averageRows = grouped.Length == 0 ? 0 : grouped.Average(x => x.Rows);
            var averageFirstRowSeconds = grouped
                .Where(x => x.FirstRowSeconds is not null)
                .Select(x => x.FirstRowSeconds!.Value)
                .DefaultIfEmpty(captureSeconds)
                .Average();
            var averageErrors = grouped.Length == 0 ? 0 : grouped.Average(x => x.ErrorCount);

            var nonBlockingCodes = new[] { 162, 365, 420 };
            var successfulRuns = grouped.Count(x => x.Rows >= minRows
                && (string.IsNullOrWhiteSpace(x.ErrorCodes) || x.ErrorCodes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .All(c => int.TryParse(c, out var code) && nonBlockingCodes.Contains(code))));

            var coverage = _options.ScannerRows <= 0
                ? 0
                : Math.Min(100, (averageRows / _options.ScannerRows) * 100);
            var speed = Math.Clamp((1 - (averageFirstRowSeconds / captureSeconds)) * 100, 0, 100);
            var stability = grouped.Length == 0 ? 0 : (successfulRuns * 100.0 / grouped.Length);
            var cleanliness = Math.Clamp(100 - (averageErrors * 25), 0, 100);

            var hardFail = averageRows < minRows
                || grouped.Any(x => x.ErrorCodes.Contains("10337", StringComparison.OrdinalIgnoreCase)
                    || x.ErrorCodes.Contains("321", StringComparison.OrdinalIgnoreCase));

            var weighted = (coverage * 0.40) + (speed * 0.20) + (stability * 0.30) + (cleanliness * 0.10);

            scoreRows.Add(new ScannerWorkbenchScoreRow(
                scanCode,
                runs,
                Math.Round(averageRows, 3),
                Math.Round(averageFirstRowSeconds, 3),
                Math.Round(averageErrors, 3),
                Math.Round(coverage, 3),
                Math.Round(speed, 3),
                Math.Round(stability, 3),
                Math.Round(cleanliness, 3),
                Math.Round(weighted, 3),
                hardFail
            ));
        }

        var ranked = scoreRows
            .OrderBy(x => x.HardFail)
            .ThenByDescending(x => x.WeightedScore)
            .ThenByDescending(x => x.CoverageScore)
            .ToArray();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var runsPath = Path.Combine(outputDir, $"scanner_workbench_runs_{timestamp}.json");
        var rankingPath = Path.Combine(outputDir, $"scanner_workbench_ranking_{timestamp}.json");

        WriteJson(runsPath, runRows);
        WriteJson(rankingPath, ranked);

        Console.WriteLine($"[OK] Scanner workbench runs export: {runsPath} (rows={runRows.Count})");
        Console.WriteLine($"[OK] Scanner workbench ranking export: {rankingPath} (rows={ranked.Length})");
    }

    private async Task RunDisplayGroupsQueryMode(EClientSocket client, CancellationToken token)
    {
        while (_wrapper.DisplayGroupListRows.TryDequeue(out _))
        {
        }

        const int reqId = 9961;
        client.queryDisplayGroups(reqId);

        try
        {
            await AwaitWithTimeout(_wrapper.DisplayGroupListTask, token, "displayGroupList");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("[WARN] displayGroupList callback not received before timeout; exporting current rows.");
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"display_groups_query_{timestamp}.json");

        WriteJson(path, _wrapper.DisplayGroupListRows.Where(x => x.RequestId == reqId).ToArray());
        Console.WriteLine($"[OK] Display groups query export: {path} (rows={_wrapper.DisplayGroupListRows.Count(x => x.RequestId == reqId)})");
    }

    private async Task RunDisplayGroupsSubscribeMode(EClientSocket client, CancellationToken token)
    {
        while (_wrapper.DisplayGroupUpdatedRows.TryDequeue(out _))
        {
        }

        var reqId = _options.DisplayGroupId;
        client.subscribeToGroupEvents(reqId, _options.DisplayGroupId);

        try
        {
            await AwaitWithTimeout(_wrapper.DisplayGroupUpdatedTask, token, "displayGroupUpdated");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("[WARN] displayGroupUpdated callback not received before timeout; exporting current rows.");
        }

        await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.DisplayGroupCaptureSeconds)), token);
        client.unsubscribeFromGroupEvents(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var updatesPath = Path.Combine(outputDir, $"display_groups_subscribe_updates_{timestamp}.json");
        var requestPath = Path.Combine(outputDir, $"display_groups_subscribe_request_{timestamp}.json");

        var request = new[]
        {
            new DisplayGroupActionRow(DateTime.UtcNow, reqId, _options.DisplayGroupId, _options.DisplayGroupContractInfo, "subscribe")
        };

        WriteJson(updatesPath, _wrapper.DisplayGroupUpdatedRows.Where(x => x.RequestId == reqId).ToArray());
        WriteJson(requestPath, request);

        Console.WriteLine($"[OK] Display groups subscribe updates export: {updatesPath} (rows={_wrapper.DisplayGroupUpdatedRows.Count(x => x.RequestId == reqId)})");
        Console.WriteLine($"[OK] Display groups subscribe request export: {requestPath}");
    }

    private async Task RunDisplayGroupsUpdateMode(EClientSocket client, CancellationToken token)
    {
        while (_wrapper.DisplayGroupUpdatedRows.TryDequeue(out _))
        {
        }

        var reqId = _options.DisplayGroupId;
        client.subscribeToGroupEvents(reqId, _options.DisplayGroupId);
        await Task.Delay(TimeSpan.FromMilliseconds(500), token);

        client.updateDisplayGroup(reqId, _options.DisplayGroupContractInfo);
        try
        {
            await AwaitWithTimeout(_wrapper.DisplayGroupUpdatedTask, token, "displayGroupUpdated");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("[WARN] displayGroupUpdated callback not received before timeout after update; exporting current rows.");
        }

        client.unsubscribeFromGroupEvents(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var updatesPath = Path.Combine(outputDir, $"display_groups_update_updates_{timestamp}.json");
        var requestPath = Path.Combine(outputDir, $"display_groups_update_request_{timestamp}.json");

        var request = new[]
        {
            new DisplayGroupActionRow(DateTime.UtcNow, reqId, _options.DisplayGroupId, _options.DisplayGroupContractInfo, "update")
        };

        WriteJson(updatesPath, _wrapper.DisplayGroupUpdatedRows.Where(x => x.RequestId == reqId).ToArray());
        WriteJson(requestPath, request);

        Console.WriteLine($"[OK] Display groups update export: {updatesPath} (rows={_wrapper.DisplayGroupUpdatedRows.Count(x => x.RequestId == reqId)})");
        Console.WriteLine($"[OK] Display groups update request export: {requestPath}");
    }

    private async Task RunDisplayGroupsUnsubscribeMode(EClientSocket client, CancellationToken token)
    {
        var reqId = _options.DisplayGroupId;
        client.subscribeToGroupEvents(reqId, _options.DisplayGroupId);
        await Task.Delay(TimeSpan.FromMilliseconds(500), token);
        client.unsubscribeFromGroupEvents(reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"display_groups_unsubscribe_{timestamp}.json");

        var row = new[]
        {
            new DisplayGroupActionRow(DateTime.UtcNow, reqId, _options.DisplayGroupId, _options.DisplayGroupContractInfo, "unsubscribe")
        };

        WriteJson(path, row);
        Console.WriteLine($"[OK] Display groups unsubscribe export: {path}");
    }

    private ScannerSubscription BuildScannerSubscriptionFromOptions()
    {
        return new ScannerSubscription
        {
            Instrument = _options.ScannerInstrument,
            LocationCode = _options.ScannerLocationCode,
            ScanCode = _options.ScannerScanCode,
            NumberOfRows = _options.ScannerRows,
            AbovePrice = _options.ScannerAbovePrice,
            BelowPrice = _options.ScannerBelowPrice,
            AboveVolume = _options.ScannerAboveVolume,
            MarketCapAbove = _options.ScannerMarketCapAbove,
            MarketCapBelow = _options.ScannerMarketCapBelow,
            StockTypeFilter = _options.ScannerStockTypeFilter,
            ScannerSettingPairs = _options.ScannerScannerSettingPairs
        };
    }

    private static List<TagValue> ParseTagValuePairs(string input)
    {
        var list = new List<TagValue>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return list;
        }

        var pairs = input
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var pair in pairs)
        {
            var split = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (split.Length != 2 || string.IsNullOrWhiteSpace(split[0]))
            {
                continue;
            }

            list.Add(new TagValue(split[0], split[1]));
        }

        return list;
    }

    private static ErrorCodeSeedRow[] BuildSystemMessageCodes()
    {
        return
        [
            new ErrorCodeSeedRow(1100, "ConnectivityLost", "Connectivity between IB and TWS has been lost."),
            new ErrorCodeSeedRow(1101, "ConnectivityRestoredDataLost", "Connectivity restored; market data requests need re-submission."),
            new ErrorCodeSeedRow(1102, "ConnectivityRestoredDataMaintained", "Connectivity restored; market data maintained."),
            new ErrorCodeSeedRow(1300, "SocketPortReset", "Socket port changed; reconnect using the new port.")
        ];
    }

    private static ErrorCodeSeedRow[] BuildWarningMessageCodes()
    {
        return
        [
            new ErrorCodeSeedRow(2100, "AccountDataOverride", "New account subscription overrides previous one."),
            new ErrorCodeSeedRow(2101, "AccountDataSubscriptionRejected", "Different client has active account subscription."),
            new ErrorCodeSeedRow(2102, "OrderStillProcessing", "Order cannot be modified while still processing."),
            new ErrorCodeSeedRow(2103, "MarketDataFarmDisconnected", "Market data farm disconnected."),
            new ErrorCodeSeedRow(2104, "MarketDataFarmOk", "Market data farm connection is OK."),
            new ErrorCodeSeedRow(2105, "HistoricalFarmDisconnected", "Historical data farm disconnected."),
            new ErrorCodeSeedRow(2106, "HistoricalFarmOk", "Historical data farm connection is OK."),
            new ErrorCodeSeedRow(2107, "HistoricalFarmInactive", "Historical data farm is inactive on-demand."),
            new ErrorCodeSeedRow(2108, "MarketDataFarmInactive", "Market data farm is inactive on-demand."),
            new ErrorCodeSeedRow(2109, "OutsideRthIgnored", "Outside regular trading hours flag ignored for order."),
            new ErrorCodeSeedRow(2110, "TwsServerConnectivityBroken", "Connectivity between TWS and server is temporarily broken."),
            new ErrorCodeSeedRow(2158, "SecDefFarmOk", "Security definition farm connection is OK.")
        ];
    }

    private static ErrorCodeSeedRow[] BuildTwsErrorCodes()
    {
        return
        [
            new ErrorCodeSeedRow(100, "MaxRateExceeded", "Max rate of messages per second exceeded."),
            new ErrorCodeSeedRow(101, "MaxTickersReached", "Max number of active tickers reached."),
            new ErrorCodeSeedRow(102, "DuplicateTickerId", "Duplicate ticker ID."),
            new ErrorCodeSeedRow(103, "DuplicateOrderId", "Duplicate order ID."),
            new ErrorCodeSeedRow(200, "NoSecurityDefinition", "No security definition found for request."),
            new ErrorCodeSeedRow(201, "OrderRejected", "Order rejected by IB server."),
            new ErrorCodeSeedRow(202, "OrderCancelled", "Order cancelled by IB server."),
            new ErrorCodeSeedRow(300, "TickerIdNotFound", "Ticker ID not found for cancel or lookup."),
            new ErrorCodeSeedRow(321, "ServerValidationError", "Server error while validating API request."),
            new ErrorCodeSeedRow(322, "ServerProcessingError", "Server error while processing API request."),
            new ErrorCodeSeedRow(326, "ClientIdInUse", "Client ID already in use."),
            new ErrorCodeSeedRow(330, "ManagedAccountsFaStlOnly", "Managed accounts list request is FA/STL only."),
            new ErrorCodeSeedRow(331, "NoManagedAccounts", "FA/STL has no managed accounts configured."),
            new ErrorCodeSeedRow(344, "NotFaAccount", "Action requires an FA account."),
            new ErrorCodeSeedRow(354, "NoMarketDataSubscription", "Not subscribed to requested market data."),
            new ErrorCodeSeedRow(420, "InvalidRealtimeQuery", "Invalid real-time query / pacing context."),
            new ErrorCodeSeedRow(430, "FundamentalsUnavailable", "Fundamental data unavailable for requested security."),
            new ErrorCodeSeedRow(10090, "PartialMarketDataSubscription", "Part of requested market data is not subscribed."),
            new ErrorCodeSeedRow(10148, "CancelRejectedState", "Order cannot be cancelled in current state."),
            new ErrorCodeSeedRow(10230, "FaUnsavedChanges", "Unsaved FA changes; request FA later."),
            new ErrorCodeSeedRow(10231, "FaInvalidGroupsProfiles", "Invalid accounts in FA groups/profiles."),
            new ErrorCodeSeedRow(10276, "WshNewsNotAllowed", "WSH news feed not allowed."),
            new ErrorCodeSeedRow(10277, "WshPermissionsRequired", "WSH news feed permissions required."),
            new ErrorCodeSeedRow(10278, "WshDuplicateMetaRequest", "Duplicate WSH metadata request."),
            new ErrorCodeSeedRow(10279, "WshMetaRequestFailed", "WSH metadata request failed."),
            new ErrorCodeSeedRow(10280, "WshMetaCancelFailed", "WSH metadata cancel failed."),
            new ErrorCodeSeedRow(10281, "WshDuplicateEventRequest", "Duplicate WSH event data request."),
            new ErrorCodeSeedRow(10282, "WshMetaNotRequested", "WSH metadata must be requested first."),
            new ErrorCodeSeedRow(10283, "WshEventRequestFailed", "WSH event data request failed."),
            new ErrorCodeSeedRow(10284, "WshEventCancelFailed", "WSH event data cancel failed."),
            new ErrorCodeSeedRow(10285, "FractionalRuleApiCompatibility", "API version does not support fractional size rules."),
            new ErrorCodeSeedRow(10358, "FundamentalsNotAllowed", "Fundamentals data is not allowed for this account/session.")
        ];
    }

    private static ErrorCodeSeedRow[] BuildClientErrorCodes()
    {
        return typeof(EClientErrors)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(CodeMsgPair))
            .Select(f => (Name: f.Name, Pair: f.GetValue(null) as CodeMsgPair))
            .Where(x => x.Pair is not null)
            .Select(x => new ErrorCodeSeedRow(x.Pair!.Code, x.Name, x.Pair.Message))
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToArray();
    }

    private static ObservedErrorRow? ParseObservedError(string line)
    {
        var codeIndex = line.IndexOf("code=", StringComparison.OrdinalIgnoreCase);
        var msgIndex = line.IndexOf(" msg=", StringComparison.OrdinalIgnoreCase);
        if (codeIndex < 0 || msgIndex < 0 || msgIndex <= codeIndex + 5)
        {
            return null;
        }

        var codeToken = line.Substring(codeIndex + 5, msgIndex - (codeIndex + 5));
        if (!int.TryParse(codeToken, out var code))
        {
            return null;
        }

        var message = line[(msgIndex + 5)..].Trim();
        return new ObservedErrorRow(code, message, line);
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

    private Task<T> AwaitWithTimeout<T>(Task<T> task, CancellationToken cancellationToken, string stage)
    {
        return AwaitTrackedWithTimeout(
            task,
            cancellationToken,
            stage,
            requestId: null,
            requestType: stage,
            origin: _options.Mode.ToString());
    }

    private static async Task<T> AwaitWithTimeoutCore<T>(Task<T> task, CancellationToken cancellationToken, string stage)
    {
        var delayTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var winner = await Task.WhenAny(task, delayTask);
        if (winner == task)
        {
            return await task;
        }

        throw new TimeoutException($"Timed out waiting for {stage}.");
    }

    private async Task<T> AwaitTrackedWithTimeout<T>(
        Task<T> task,
        CancellationToken cancellationToken,
        string stage,
        int? requestId,
        string requestType,
        string origin)
    {
        var correlationId = _requestRegistry.Register(
            requestId,
            requestType,
            origin,
            DateTime.UtcNow.AddSeconds(_options.TimeoutSeconds));

        try
        {
            var result = await AwaitWithTimeoutCore(task, cancellationToken, stage);
            _requestRegistry.Complete(correlationId, details: stage);
            return result;
        }
        catch (TimeoutException ex)
        {
            _requestRegistry.Timeout(correlationId, ex.Message);
            var context = _requestRegistry.Describe(correlationId);
            throw new TimeoutException($"Timed out waiting for {stage}. {context}", ex);
        }
        catch (Exception ex)
        {
            _requestRegistry.Fail(correlationId, ex.Message);
            throw;
        }
    }

    private void PrintErrors()
    {
        if (_wrapper.ApiErrors.IsEmpty)
        {
            return;
        }

        var throttledCounts = new Dictionary<int, int>();
        var emittedKeys = new HashSet<string>();

        Console.WriteLine("\n=== API Errors ===");
        foreach (var error in _wrapper.ApiErrors)
        {
            var decision = _errorPolicy.Evaluate(error, _options.Mode, _options.OptionGreeksAutoFallback);
            if (decision.Action == IbErrorAction.Ignore)
            {
                continue;
            }

            if (decision.Action is IbErrorAction.Warn or IbErrorAction.Retry)
            {
                var key = $"{decision.Action}:{error.Code?.ToString() ?? "none"}";
                if (!emittedKeys.Add(key))
                {
                    if (error.Code is int code)
                    {
                        throttledCounts[code] = throttledCounts.GetValueOrDefault(code) + 1;
                    }
                    continue;
                }
            }

            Console.WriteLine($"[{decision.Action}] ts={error.UtcTimestamp:O} id={error.Id?.ToString() ?? "n/a"} code={error.Code?.ToString() ?? "n/a"} msg={error.Message} reason={decision.Reason}");
        }

        foreach (var item in throttledCounts.OrderBy(kvp => kvp.Key))
        {
            Console.WriteLine($"[THROTTLED] code={item.Key} suppressed={item.Value}");
        }
    }

    private void PrintRequestDiagnostics()
    {
        var rows = _requestRegistry
            .Snapshot()
            .Where(r => r.Status is RequestStatus.TimedOut or RequestStatus.Failed or RequestStatus.Started)
            .ToArray();

        if (rows.Length == 0)
        {
            return;
        }

        Console.WriteLine("\n=== Request Diagnostics ===");
        foreach (var row in rows)
        {
            Console.WriteLine($"[REQ] corr={row.CorrelationId} reqId={row.RequestId?.ToString() ?? "n/a"} type={row.Type} origin={row.Origin} status={row.Status} started={row.StartedAtUtc:O} deadline={row.DeadlineUtc:O} details={row.Details ?? "n/a"}");
        }
    }

    private void EvaluateReconciliationQualityGate(ReconciliationSummaryRow summary, string context)
    {
        if (_options.ReconciliationGateAction == ReconciliationGateAction.Off)
        {
            return;
        }

        var violations = new List<string>();
        if (summary.ExecutionCommissionCoveragePct < _options.ReconciliationMinCommissionCoverage)
        {
            violations.Add(
                $"execution->commission coverage {summary.ExecutionCommissionCoveragePct:P2} < threshold {_options.ReconciliationMinCommissionCoverage:P2}");
        }

        if (summary.ExecutionOrderMetadataCoveragePct < _options.ReconciliationMinOrderCoverage)
        {
            violations.Add(
                $"execution->order coverage {summary.ExecutionOrderMetadataCoveragePct:P2} < threshold {_options.ReconciliationMinOrderCoverage:P2}");
        }

        if (violations.Count == 0)
        {
            return;
        }

        var message = $"[{context}] reconciliation quality gate violations: {string.Join("; ", violations)}";
        if (_options.ReconciliationGateAction == ReconciliationGateAction.Warn)
        {
            Console.WriteLine($"[WARN] {message}");
            return;
        }

        Console.WriteLine($"[FAIL] {message}");
        _hasReconciliationQualityFailure = true;
    }

    private bool IsBlockingErrorForCurrentMode(string error)
    {
        if (_options.Mode == RunMode.OptionGreeks && _options.OptionGreeksAutoFallback)
        {
            var isExpectedProbeError =
                (error.Contains("id=98040", StringComparison.OrdinalIgnoreCase) || error.Contains("id=9804", StringComparison.OrdinalIgnoreCase))
                && (error.Contains("code=200", StringComparison.OrdinalIgnoreCase) || error.Contains("code=300", StringComparison.OrdinalIgnoreCase));

            if (isExpectedProbeError)
            {
                return false;
            }
        }

        if ((_options.Mode == RunMode.FaAllocationGroups
            || _options.Mode == RunMode.FaGroupsProfiles
            || _options.Mode == RunMode.FaUnification
            || _options.Mode == RunMode.FaModelPortfolios
            || _options.Mode == RunMode.FaOrder)
            && error.Contains("code=321", StringComparison.OrdinalIgnoreCase))
        {
            var expectedFaValidation =
                error.Contains("FA data operations ignored for non FA customers", StringComparison.OrdinalIgnoreCase)
                || error.Contains("Model name", StringComparison.OrdinalIgnoreCase)
                || error.Contains("cause - Model", StringComparison.OrdinalIgnoreCase);

            if (expectedFaValidation)
            {
                return false;
            }
        }

        if (_options.Mode == RunMode.FundamentalData && error.Contains("code=10358", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if ((_options.Mode == RunMode.ScannerExamples || _options.Mode == RunMode.ScannerComplex || _options.Mode == RunMode.ScannerParameters || _options.Mode == RunMode.ScannerWorkbench)
            && (error.Contains("code=162", StringComparison.OrdinalIgnoreCase)
                || error.Contains("code=200", StringComparison.OrdinalIgnoreCase)
                || error.Contains("code=300", StringComparison.OrdinalIgnoreCase)
                || error.Contains("code=365", StringComparison.OrdinalIgnoreCase)
                || error.Contains("code=420", StringComparison.OrdinalIgnoreCase)
                || error.Contains("code=321", StringComparison.OrdinalIgnoreCase)
                || error.Contains("code=354", StringComparison.OrdinalIgnoreCase)
                || error.Contains("code=10186", StringComparison.OrdinalIgnoreCase)
                || error.Contains("code=10337", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if ((_options.Mode == RunMode.DisplayGroupsQuery
            || _options.Mode == RunMode.DisplayGroupsSubscribe
            || _options.Mode == RunMode.DisplayGroupsUpdate
            || _options.Mode == RunMode.DisplayGroupsUnsubscribe)
            && (error.Contains("code=321", StringComparison.OrdinalIgnoreCase)
                || error.Contains("code=344", StringComparison.OrdinalIgnoreCase)
                || error.Contains("code=365", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return IsBlockingError(error);
    }

    private static bool IsBlockingError(string error)
    {
        var nonBlockingCodes = new[] { "code=2100", "code=2104", "code=2106", "code=2158", "code=10089", "code=10167", "code=10168", "code=10187", "code=10285", "code=354", "code=322", "code=300", "code=310", "code=420" };

        if (error.Contains("code=162") && error.Contains("query cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !nonBlockingCodes.Any(error.Contains);
    }

    private bool HasErrorCode(string code)
    {
        if (code.StartsWith("code=", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(code[5..], out var parsedCode))
        {
            if (_wrapper.ApiErrors.Any(e => e.Code == parsedCode))
            {
                return true;
            }
        }

        if (code.StartsWith("id=", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(code[3..], out var parsedId))
        {
            if (_wrapper.ApiErrors.Any(e => e.Id == parsedId))
            {
                return true;
            }
        }

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
    string HeadTimestampWhatToShow,
    string UpdateAccount,
    string AccountSummaryGroup,
    string AccountSummaryTags,
    string AccountUpdatesMultiAccount,
    string PositionsMultiAccount,
    string ModelCode,
    string PnlAccount,
    int PnlConId,
    string OptionSymbol,
    string OptionExpiry,
    double OptionStrike,
    string OptionRight,
    string OptionExchange,
    string OptionCurrency,
    string OptionMultiplier,
    string OptionUnderlyingSecType,
    string OptionFutFopExchange,
    bool OptionExerciseAllow,
    int OptionExerciseAction,
    int OptionExerciseQuantity,
    int OptionExerciseOverride,
    string OptionExerciseManualTime,
    bool OptionGreeksAutoFallback,
    string CryptoSymbol,
    string CryptoExchange,
    string CryptoCurrency,
    bool CryptoOrderAllow,
    string CryptoOrderAction,
    double CryptoOrderQuantity,
    double CryptoOrderLimit,
    double CryptoMaxNotional,
    string FaAccount,
    string FaModelCode,
    bool FaOrderAllow,
    string FaOrderAccount,
    string FaOrderSymbol,
    string FaOrderAction,
    double FaOrderQuantity,
    double FaOrderLimit,
    double FaMaxNotional,
    string FaOrderGroup,
    string FaOrderMethod,
    string FaOrderPercentage,
    string FaOrderProfile,
    string FaOrderExchange,
    string FaOrderPrimaryExchange,
    string FaOrderCurrency,
    string FundamentalReportType,
    string WshFilterJson,
    string ScannerInstrument,
    string ScannerLocationCode,
    string ScannerScanCode,
    int ScannerRows,
    double ScannerAbovePrice,
    double ScannerBelowPrice,
    int ScannerAboveVolume,
    double ScannerMarketCapAbove,
    double ScannerMarketCapBelow,
    string ScannerStockTypeFilter,
    string ScannerScannerSettingPairs,
    string ScannerFilterTagValues,
    string ScannerOptionsTagValues,
    string ScannerWorkbenchCodes,
    int ScannerWorkbenchRuns,
    int ScannerWorkbenchCaptureSeconds,
    int ScannerWorkbenchMinRows,
    int DisplayGroupId,
    string DisplayGroupContractInfo,
    int DisplayGroupCaptureSeconds,
    bool HeartbeatMonitorEnabled,
    int HeartbeatIntervalSeconds,
    int HeartbeatProbeTimeoutSeconds,
    int ReconnectMaxAttempts,
    int ReconnectBackoffSeconds,
    ReconciliationGateAction ReconciliationGateAction,
    double ReconciliationMinCommissionCoverage,
    double ReconciliationMinOrderCoverage
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
        var updateAccount = account;
        var accountSummaryGroup = "All";
        var accountSummaryTags = "AccountType,NetLiquidation,TotalCashValue,BuyingPower,MaintMarginReq,AvailableFunds";
        var accountUpdatesMultiAccount = account;
        var positionsMultiAccount = account;
        var modelCode = string.Empty;
        var pnlAccount = account;
        var pnlConId = 0;
        var optionSymbol = "SIRI";
        var optionExpiry = DateTime.UtcNow.AddMonths(1).ToString("yyyyMMdd");
        var optionStrike = 5.0;
        var optionRight = "C";
        var optionExchange = "SMART";
        var optionCurrency = "USD";
        var optionMultiplier = "100";
        var optionUnderlyingSecType = "STK";
        var optionFutFopExchange = string.Empty;
        var optionExerciseAllow = false;
        var optionExerciseAction = 1;
        var optionExerciseQuantity = 1;
        var optionExerciseOverride = 0;
        var optionExerciseManualTime = string.Empty;
        var optionGreeksAutoFallback = false;
        var cryptoSymbol = "BTC";
        var cryptoExchange = "PAXOS";
        var cryptoCurrency = "USD";
        var cryptoOrderAllow = false;
        var cryptoOrderAction = "BUY";
        var cryptoOrderQuantity = 0.001;
        var cryptoOrderLimit = 30000.0;
        var cryptoMaxNotional = 100.0;
        var faAccount = account;
        var faModelCode = string.Empty;
        var faOrderAllow = false;
        var faOrderAccount = account;
        var faOrderSymbol = "SIRI";
        var faOrderAction = "BUY";
        var faOrderQuantity = 1.0;
        var faOrderLimit = 5.0;
        var faMaxNotional = 100.0;
        var faOrderGroup = string.Empty;
        var faOrderMethod = string.Empty;
        var faOrderPercentage = string.Empty;
        var faOrderProfile = string.Empty;
        var faOrderExchange = "SMART";
        var faOrderPrimaryExchange = "NASDAQ";
        var faOrderCurrency = "USD";
        var fundamentalReportType = "ReportSnapshot";
        var wshFilterJson = "{}";
        var scannerInstrument = "STK";
        var scannerLocationCode = "STK.US.MAJOR";
        var scannerScanCode = "TOP_PERC_GAIN";
        var scannerRows = 10;
        var scannerAbovePrice = 1.0;
        var scannerBelowPrice = 0.0;
        var scannerAboveVolume = 100000;
        var scannerMarketCapAbove = 0.0;
        var scannerMarketCapBelow = 0.0;
        var scannerStockTypeFilter = "ALL";
        var scannerScannerSettingPairs = string.Empty;
        var scannerFilterTagValues = string.Empty;
        var scannerOptionsTagValues = string.Empty;
        var scannerWorkbenchCodes = "TOP_PERC_GAIN,HOT_BY_VOLUME,MOST_ACTIVE";
        var scannerWorkbenchRuns = 2;
        var scannerWorkbenchCaptureSeconds = 6;
        var scannerWorkbenchMinRows = 1;
        var displayGroupId = 1;
        var displayGroupContractInfo = "265598@SMART";
        var displayGroupCaptureSeconds = 4;
        var heartbeatMonitorEnabled = true;
        var heartbeatIntervalSeconds = 6;
        var heartbeatProbeTimeoutSeconds = 4;
        var reconnectMaxAttempts = 3;
        var reconnectBackoffSeconds = 2;
        var reconciliationGateAction = ReconciliationGateAction.Warn;
        var reconciliationMinCommissionCoverage = 0.80;
        var reconciliationMinOrderCoverage = 0.95;

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
                case "--update-account" when i + 1 < args.Length:
                    updateAccount = args[++i];
                    break;
                case "--summary-group" when i + 1 < args.Length:
                    accountSummaryGroup = args[++i];
                    break;
                case "--summary-tags" when i + 1 < args.Length:
                    accountSummaryTags = args[++i];
                    break;
                case "--updates-multi-account" when i + 1 < args.Length:
                    accountUpdatesMultiAccount = args[++i];
                    break;
                case "--positions-multi-account" when i + 1 < args.Length:
                    positionsMultiAccount = args[++i];
                    break;
                case "--model-code":
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        modelCode = args[++i];
                    }
                    else
                    {
                        modelCode = string.Empty;
                    }
                    break;
                case "--pnl-account" when i + 1 < args.Length:
                    pnlAccount = args[++i];
                    break;
                case "--pnl-conid" when i + 1 < args.Length && int.TryParse(args[i + 1], out var pcon):
                    pnlConId = pcon;
                    i++;
                    break;
                case "--opt-symbol" when i + 1 < args.Length:
                    optionSymbol = args[++i].ToUpperInvariant();
                    break;
                case "--opt-expiry" when i + 1 < args.Length:
                    optionExpiry = args[++i];
                    break;
                case "--opt-strike" when i + 1 < args.Length && double.TryParse(args[i + 1], out var os):
                    optionStrike = os;
                    i++;
                    break;
                case "--opt-right" when i + 1 < args.Length:
                    optionRight = args[++i].ToUpperInvariant();
                    break;
                case "--opt-exchange" when i + 1 < args.Length:
                    optionExchange = args[++i].ToUpperInvariant();
                    break;
                case "--opt-currency" when i + 1 < args.Length:
                    optionCurrency = args[++i].ToUpperInvariant();
                    break;
                case "--opt-multiplier" when i + 1 < args.Length:
                    optionMultiplier = args[++i];
                    break;
                case "--opt-underlying-sec-type" when i + 1 < args.Length:
                    optionUnderlyingSecType = args[++i].ToUpperInvariant();
                    break;
                case "--opt-futfop-exchange" when i + 1 < args.Length:
                    optionFutFopExchange = args[++i].ToUpperInvariant();
                    break;
                case "--option-exercise-allow" when i + 1 < args.Length:
                    optionExerciseAllow = bool.TryParse(args[++i], out var oea) && oea;
                    break;
                case "--option-exercise-action" when i + 1 < args.Length && int.TryParse(args[i + 1], out var oaction):
                    optionExerciseAction = oaction;
                    i++;
                    break;
                case "--option-exercise-qty" when i + 1 < args.Length && int.TryParse(args[i + 1], out var oqty):
                    optionExerciseQuantity = oqty;
                    i++;
                    break;
                case "--option-exercise-override" when i + 1 < args.Length && int.TryParse(args[i + 1], out var oovr):
                    optionExerciseOverride = oovr;
                    i++;
                    break;
                case "--option-exercise-time" when i + 1 < args.Length:
                    optionExerciseManualTime = args[++i];
                    break;
                case "--option-greeks-auto-fallback" when i + 1 < args.Length:
                    optionGreeksAutoFallback = bool.TryParse(args[++i], out var ogf) && ogf;
                    break;
                case "--crypto-symbol" when i + 1 < args.Length:
                    cryptoSymbol = args[++i].ToUpperInvariant();
                    break;
                case "--crypto-exchange" when i + 1 < args.Length:
                    cryptoExchange = args[++i].ToUpperInvariant();
                    break;
                case "--crypto-currency" when i + 1 < args.Length:
                    cryptoCurrency = args[++i].ToUpperInvariant();
                    break;
                case "--crypto-order-allow" when i + 1 < args.Length:
                    cryptoOrderAllow = bool.TryParse(args[++i], out var coa) && coa;
                    break;
                case "--crypto-order-action" when i + 1 < args.Length:
                    cryptoOrderAction = args[++i].ToUpperInvariant();
                    break;
                case "--crypto-order-qty" when i + 1 < args.Length && double.TryParse(args[i + 1], out var coq):
                    cryptoOrderQuantity = coq;
                    i++;
                    break;
                case "--crypto-order-limit" when i + 1 < args.Length && double.TryParse(args[i + 1], out var col):
                    cryptoOrderLimit = col;
                    i++;
                    break;
                case "--crypto-max-notional" when i + 1 < args.Length && double.TryParse(args[i + 1], out var cmn):
                    cryptoMaxNotional = cmn;
                    i++;
                    break;
                case "--fa-account" when i + 1 < args.Length:
                    faAccount = args[++i];
                    break;
                case "--fa-model-code" when i + 1 < args.Length:
                    faModelCode = args[++i];
                    break;
                case "--fa-order-allow" when i + 1 < args.Length:
                    faOrderAllow = bool.TryParse(args[++i], out var foa) && foa;
                    break;
                case "--fa-order-account" when i + 1 < args.Length:
                    faOrderAccount = args[++i];
                    break;
                case "--fa-order-symbol" when i + 1 < args.Length:
                    faOrderSymbol = args[++i].ToUpperInvariant();
                    break;
                case "--fa-order-action" when i + 1 < args.Length:
                    faOrderAction = args[++i].ToUpperInvariant();
                    break;
                case "--fa-order-qty" when i + 1 < args.Length && double.TryParse(args[i + 1], out var foq):
                    faOrderQuantity = foq;
                    i++;
                    break;
                case "--fa-order-limit" when i + 1 < args.Length && double.TryParse(args[i + 1], out var fol):
                    faOrderLimit = fol;
                    i++;
                    break;
                case "--fa-max-notional" when i + 1 < args.Length && double.TryParse(args[i + 1], out var fmn):
                    faMaxNotional = fmn;
                    i++;
                    break;
                case "--fa-order-group" when i + 1 < args.Length:
                    faOrderGroup = args[++i];
                    break;
                case "--fa-order-method" when i + 1 < args.Length:
                    faOrderMethod = args[++i];
                    break;
                case "--fa-order-percentage" when i + 1 < args.Length:
                    faOrderPercentage = args[++i];
                    break;
                case "--fa-order-profile" when i + 1 < args.Length:
                    faOrderProfile = args[++i];
                    break;
                case "--fa-order-exchange" when i + 1 < args.Length:
                    faOrderExchange = args[++i].ToUpperInvariant();
                    break;
                case "--fa-order-primary-exchange" when i + 1 < args.Length:
                    faOrderPrimaryExchange = args[++i].ToUpperInvariant();
                    break;
                case "--fa-order-currency" when i + 1 < args.Length:
                    faOrderCurrency = args[++i].ToUpperInvariant();
                    break;
                case "--fund-report-type" when i + 1 < args.Length:
                    fundamentalReportType = args[++i];
                    break;
                case "--wsh-filter-json" when i + 1 < args.Length:
                    wshFilterJson = args[++i];
                    break;
                case "--scanner-instrument" when i + 1 < args.Length:
                    scannerInstrument = args[++i].ToUpperInvariant();
                    break;
                case "--scanner-location" when i + 1 < args.Length:
                    scannerLocationCode = args[++i].ToUpperInvariant();
                    break;
                case "--scanner-code" when i + 1 < args.Length:
                    scannerScanCode = args[++i].ToUpperInvariant();
                    break;
                case "--scanner-rows" when i + 1 < args.Length && int.TryParse(args[i + 1], out var srows):
                    scannerRows = srows;
                    i++;
                    break;
                case "--scanner-above-price" when i + 1 < args.Length && double.TryParse(args[i + 1], out var sap):
                    scannerAbovePrice = sap;
                    i++;
                    break;
                case "--scanner-below-price" when i + 1 < args.Length && double.TryParse(args[i + 1], out var sbp):
                    scannerBelowPrice = sbp;
                    i++;
                    break;
                case "--scanner-above-volume" when i + 1 < args.Length && int.TryParse(args[i + 1], out var sav):
                    scannerAboveVolume = sav;
                    i++;
                    break;
                case "--scanner-mcap-above" when i + 1 < args.Length && double.TryParse(args[i + 1], out var smca):
                    scannerMarketCapAbove = smca;
                    i++;
                    break;
                case "--scanner-mcap-below" when i + 1 < args.Length && double.TryParse(args[i + 1], out var smcb):
                    scannerMarketCapBelow = smcb;
                    i++;
                    break;
                case "--scanner-stock-type" when i + 1 < args.Length:
                    scannerStockTypeFilter = args[++i].ToUpperInvariant();
                    break;
                case "--scanner-setting-pairs" when i + 1 < args.Length:
                    scannerScannerSettingPairs = args[++i];
                    break;
                case "--scanner-filter-tags" when i + 1 < args.Length:
                    scannerFilterTagValues = args[++i];
                    break;
                case "--scanner-options-tags" when i + 1 < args.Length:
                    scannerOptionsTagValues = args[++i];
                    break;
                case "--scanner-workbench-codes" when i + 1 < args.Length:
                    scannerWorkbenchCodes = args[++i];
                    break;
                case "--scanner-workbench-runs" when i + 1 < args.Length && int.TryParse(args[i + 1], out var swr):
                    scannerWorkbenchRuns = swr;
                    i++;
                    break;
                case "--scanner-workbench-capture-seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var swc):
                    scannerWorkbenchCaptureSeconds = swc;
                    i++;
                    break;
                case "--scanner-workbench-min-rows" when i + 1 < args.Length && int.TryParse(args[i + 1], out var swm):
                    scannerWorkbenchMinRows = swm;
                    i++;
                    break;
                case "--display-group-id" when i + 1 < args.Length && int.TryParse(args[i + 1], out var dgid):
                    displayGroupId = dgid;
                    i++;
                    break;
                case "--display-group-contract-info" when i + 1 < args.Length:
                    displayGroupContractInfo = args[++i];
                    break;
                case "--display-group-capture-seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var dgcs):
                    displayGroupCaptureSeconds = dgcs;
                    i++;
                    break;
                case "--heartbeat-monitor" when i + 1 < args.Length:
                    heartbeatMonitorEnabled = bool.TryParse(args[++i], out var hm) && hm;
                    break;
                case "--heartbeat-interval" when i + 1 < args.Length && int.TryParse(args[i + 1], out var hi):
                    heartbeatIntervalSeconds = hi;
                    i++;
                    break;
                case "--heartbeat-probe-timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out var hpt):
                    heartbeatProbeTimeoutSeconds = hpt;
                    i++;
                    break;
                case "--reconnect-max-attempts" when i + 1 < args.Length && int.TryParse(args[i + 1], out var rma):
                    reconnectMaxAttempts = rma;
                    i++;
                    break;
                case "--reconnect-backoff" when i + 1 < args.Length && int.TryParse(args[i + 1], out var rbs):
                    reconnectBackoffSeconds = rbs;
                    i++;
                    break;
                case "--recon-gate-action" when i + 1 < args.Length:
                    reconciliationGateAction = ParseReconciliationGateAction(args[++i]);
                    break;
                case "--recon-min-commission-coverage" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rmcc):
                    reconciliationMinCommissionCoverage = rmcc;
                    i++;
                    break;
                case "--recon-min-order-coverage" when i + 1 < args.Length && double.TryParse(args[i + 1], out var rmoc):
                    reconciliationMinOrderCoverage = rmoc;
                    i++;
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
            headTimestampWhatToShow,
            updateAccount,
            accountSummaryGroup,
            accountSummaryTags,
            accountUpdatesMultiAccount,
            positionsMultiAccount,
            modelCode,
            pnlAccount,
            pnlConId,
            optionSymbol,
            optionExpiry,
            optionStrike,
            optionRight,
            optionExchange,
            optionCurrency,
            optionMultiplier,
            optionUnderlyingSecType,
            optionFutFopExchange,
            optionExerciseAllow,
            optionExerciseAction,
            optionExerciseQuantity,
            optionExerciseOverride,
            optionExerciseManualTime,
            optionGreeksAutoFallback,
            cryptoSymbol,
            cryptoExchange,
            cryptoCurrency,
            cryptoOrderAllow,
            cryptoOrderAction,
            cryptoOrderQuantity,
            cryptoOrderLimit,
            cryptoMaxNotional,
            faAccount,
            faModelCode,
            faOrderAllow,
            faOrderAccount,
            faOrderSymbol,
            faOrderAction,
            faOrderQuantity,
            faOrderLimit,
            faMaxNotional,
            faOrderGroup,
            faOrderMethod,
            faOrderPercentage,
            faOrderProfile,
            faOrderExchange,
            faOrderPrimaryExchange,
            faOrderCurrency,
            fundamentalReportType,
            wshFilterJson,
            scannerInstrument,
            scannerLocationCode,
            scannerScanCode,
            scannerRows,
            scannerAbovePrice,
            scannerBelowPrice,
            scannerAboveVolume,
            scannerMarketCapAbove,
            scannerMarketCapBelow,
            scannerStockTypeFilter,
            scannerScannerSettingPairs,
            scannerFilterTagValues,
            scannerOptionsTagValues,
            scannerWorkbenchCodes,
            scannerWorkbenchRuns,
            scannerWorkbenchCaptureSeconds,
            scannerWorkbenchMinRows,
            displayGroupId,
            displayGroupContractInfo,
            displayGroupCaptureSeconds,
            heartbeatMonitorEnabled,
            heartbeatIntervalSeconds,
            heartbeatProbeTimeoutSeconds,
            reconnectMaxAttempts,
            reconnectBackoffSeconds,
            reconciliationGateAction,
            reconciliationMinCommissionCoverage,
            reconciliationMinOrderCoverage
        );
    }

    private static ReconciliationGateAction ParseReconciliationGateAction(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "off" => ReconciliationGateAction.Off,
            "warn" => ReconciliationGateAction.Warn,
            "fail" => ReconciliationGateAction.Fail,
            _ => throw new ArgumentException($"Unknown reconciliation gate action '{value}'. Use off|warn|fail.")
        };
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
            "managed-accounts" => RunMode.ManagedAccounts,
            "family-codes" => RunMode.FamilyCodes,
            "account-updates" => RunMode.AccountUpdates,
            "account-updates-multi" => RunMode.AccountUpdatesMulti,
            "account-summary" => RunMode.AccountSummaryOnly,
            "positions-multi" => RunMode.PositionsMulti,
            "pnl-account" => RunMode.PnlAccount,
            "pnl-single" => RunMode.PnlSingle,
            "option-chains" => RunMode.OptionChains,
            "option-exercise" => RunMode.OptionExercise,
            "option-greeks" => RunMode.OptionGreeks,
            "crypto-permissions" => RunMode.CryptoPermissions,
            "crypto-contract" => RunMode.CryptoContract,
            "crypto-streaming" => RunMode.CryptoStreaming,
            "crypto-historical" => RunMode.CryptoHistorical,
            "crypto-order" => RunMode.CryptoOrder,
            "fa-allocation-groups" => RunMode.FaAllocationGroups,
            "fa-groups-profiles" => RunMode.FaGroupsProfiles,
            "fa-unification" => RunMode.FaUnification,
            "fa-model-portfolios" => RunMode.FaModelPortfolios,
            "fa-order" => RunMode.FaOrder,
            "fundamental-data" => RunMode.FundamentalData,
            "wsh-filters" => RunMode.WshFilters,
            "error-codes" => RunMode.ErrorCodes,
            "scanner-examples" => RunMode.ScannerExamples,
            "scanner-complex" => RunMode.ScannerComplex,
            "scanner-parameters" => RunMode.ScannerParameters,
            "scanner-workbench" => RunMode.ScannerWorkbench,
            "display-groups-query" => RunMode.DisplayGroupsQuery,
            "display-groups-subscribe" => RunMode.DisplayGroupsSubscribe,
            "display-groups-update" => RunMode.DisplayGroupsUpdate,
            "display-groups-unsubscribe" => RunMode.DisplayGroupsUnsubscribe,
            _ => throw new ArgumentException($"Unknown mode '{value}'. Use connect|orders|positions|snapshot-all|contracts-validate|orders-dryrun|orders-place-sim|orders-whatif|top-data|market-depth|realtime-bars|market-data-all|historical-bars|historical-bars-live|histogram|historical-ticks|head-timestamp|managed-accounts|family-codes|account-updates|account-updates-multi|account-summary|positions-multi|pnl-account|pnl-single|option-chains|option-exercise|option-greeks|crypto-permissions|crypto-contract|crypto-streaming|crypto-historical|crypto-order|fa-allocation-groups|fa-groups-profiles|fa-unification|fa-model-portfolios|fa-order|fundamental-data|wsh-filters|error-codes|scanner-examples|scanner-complex|scanner-parameters|scanner-workbench|display-groups-query|display-groups-subscribe|display-groups-update|display-groups-unsubscribe.")
        };
    }
}

public enum ReconciliationGateAction
{
    Off,
    Warn,
    Fail
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
    HeadTimestamp,
    ManagedAccounts,
    FamilyCodes,
    AccountUpdates,
    AccountUpdatesMulti,
    AccountSummaryOnly,
    PositionsMulti,
    PnlAccount,
    PnlSingle,
    OptionChains,
    OptionExercise,
    OptionGreeks,
    CryptoPermissions,
    CryptoContract,
    CryptoStreaming,
    CryptoHistorical,
    CryptoOrder,
    FaAllocationGroups,
    FaGroupsProfiles,
    FaUnification,
    FaModelPortfolios,
    FaOrder,
    FundamentalData,
    WshFilters,
    ErrorCodes,
    ScannerExamples,
    ScannerComplex,
    ScannerParameters,
    ScannerWorkbench,
    DisplayGroupsQuery,
    DisplayGroupsSubscribe,
    DisplayGroupsUpdate,
    DisplayGroupsUnsubscribe
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

public sealed record ManagedAccountRow(
    DateTime TimestampUtc,
    string AccountId
);

public sealed record OptionExerciseRequestRow(
    DateTime TimestampUtc,
    string Symbol,
    string Expiry,
    double Strike,
    string Right,
    int Action,
    int Quantity,
    string Account,
    int Override,
    string ManualTime
);

public sealed record CryptoPermissionRow(
    DateTime TimestampUtc,
    string Symbol,
    string Exchange,
    string Currency,
    bool ContractDetailsResolved,
    int ContractDetailsCount,
    int TopTicksCaptured,
    string[] RelatedErrors
);

public sealed record CryptoOrderRequestRow(
    DateTime TimestampUtc,
    int OrderId,
    string Symbol,
    string Exchange,
    string Currency,
    string Action,
    double Quantity,
    double LimitPrice,
    double Notional,
    string Account,
    string OrderRef
);

public sealed record FaAllocationMethodRow(
    string Category,
    string Method,
    int TypeNumber,
    string Notes
);

public sealed record FaUnificationRow(
    DateTime TimestampUtc,
    int GroupPayloadCount,
    int ProfilePayloadCount,
    bool ProfileRequestErrored,
    bool LikelyUnified,
    string Note
);

public sealed record FaOrderRequestRow(
    DateTime TimestampUtc,
    int OrderId,
    string Symbol,
    string Action,
    double Quantity,
    double LimitPrice,
    double Notional,
    string Account,
    string FaGroup,
    string FaProfile,
    string FaMethod,
    string FaPercentage,
    string OrderRef
);

public sealed record WshFilterSupportRow(
    DateTime TimestampUtc,
    bool IsWshSupported,
    bool HasWshMetaRequest,
    bool HasWshEventRequest,
    bool HasWshMetaCallback,
    bool HasWshEventCallback,
    string RequestedFilterJson,
    string Note
);

public sealed record ErrorCodeSeedRow(
    int Code,
    string Name,
    string Description
);

public sealed record ErrorCodeRow(
    int Code,
    string Name,
    string Description,
    int ObservedCount
);

public sealed record ObservedErrorRow(
    int Code,
    string Message,
    string Raw
);

public sealed record ScannerRequestRow(
    int RequestId,
    string Instrument,
    string LocationCode,
    string ScanCode,
    int NumberOfRows,
    string ScannerSettingPairs,
    string FilterTagPairs,
    string OptionTagPairs
);

public sealed record ScannerWorkbenchRunRow(
    DateTime TimestampUtc,
    int RequestId,
    string ScanCode,
    int RunIndex,
    int Rows,
    double DurationSeconds,
    double? FirstRowSeconds,
    int ErrorCount,
    string ErrorCodes
);

public sealed record ScannerWorkbenchScoreRow(
    string ScanCode,
    int Runs,
    double AverageRows,
    double AverageFirstRowSeconds,
    double AverageErrors,
    double CoverageScore,
    double SpeedScore,
    double StabilityScore,
    double CleanlinessScore,
    double WeightedScore,
    bool HardFail
);

public sealed record DisplayGroupActionRow(
    DateTime TimestampUtc,
    int RequestId,
    int GroupId,
    string ContractInfo,
    string Action
);
