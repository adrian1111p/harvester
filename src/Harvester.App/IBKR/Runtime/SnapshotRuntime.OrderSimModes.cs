using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Globalization;
using ClosedXML.Excel;
using Harvester.App.IBKR.Broker;
using Harvester.App.IBKR.Contracts;
using Harvester.App.IBKR.Orders;
using Harvester.App.IBKR.Risk;
using Harvester.App.IBKR.Wrapper;
using Harvester.App.Strategy;
using Harvester.Contracts;
using IBApi;

namespace Harvester.App.IBKR.Runtime;

// Phase 3 #10: Extracted from SnapshotRuntime.cs — Order simulation modes
public sealed partial class SnapshotRuntime
{
    private async Task RunOrdersPlaceSimMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        EnsureSteadyStateForOrderRoute(nameof(RunOrdersPlaceSimMode));
        var livePlan = await ResolveLiveOrderPlacementPlanAsync(client, brokerAdapter, _options.LiveAction, excludedSymbols: null, quantityOverride: null, token);
        await ExecuteLiveOrderPlacementPlanAsync(client, brokerAdapter, livePlan, nameof(RunOrdersPlaceSimMode), token, quoteRequestSeed: 9917);
    }

    private async Task<LiveOrderExecutionResult> ExecuteLiveOrderPlacementPlanAsync(
        EClientSocket client,
        IBrokerAdapter brokerAdapter,
        LiveOrderPlacementPlan requestedPlan,
        string route,
        CancellationToken token,
        int quoteRequestSeed)
    {
        var livePlan = requestedPlan;
        var liveOrderType = NormalizeLiveOrderType(_options.LiveOrderType);
        var quoteSnapshot = await FetchLiveQuoteSnapshotAsync(client, brokerAdapter, livePlan.Symbol, quoteRequestSeed, token);
        if (liveOrderType == "LMT")
        {
            livePlan = ApplyLiveDefaultLimitFromQuote(livePlan, quoteSnapshot);
        }

        ValidateLiveSafetyInputs(livePlan.Action, livePlan.Quantity, livePlan.LimitPrice, livePlan.Symbol, liveOrderType);
        ValidateLivePriceSanity(livePlan, quoteSnapshot, liveOrderType);

        var notionalReferencePrice = liveOrderType == "LMT"
            ? livePlan.LimitPrice
            : ResolveObservedPriceForExit(livePlan.Symbol, quoteSnapshot, livePlan.Action);
        if (notionalReferencePrice <= 0)
        {
            notionalReferencePrice = _options.MaxPrice;
        }

        var notional = livePlan.Quantity * notionalReferencePrice;
        if (notional > _options.MaxNotional)
        {
            throw new InvalidOperationException($"Live order blocked: notional {notional:F2} exceeds max-notional {_options.MaxNotional:F2}.");
        }

        if (!_options.EnableLive)
        {
            throw new InvalidOperationException("Live order blocked: set --enable-live true to allow transmission.");
        }

        var currentPositionQty = await GetCurrentPositionQuantityAsync(client, brokerAdapter, livePlan.Symbol, token);
        var closesExistingPosition =
            (string.Equals(livePlan.Action, "SELL", StringComparison.OrdinalIgnoreCase) && currentPositionQty > 0)
            || (string.Equals(livePlan.Action, "BUY", StringComparison.OrdinalIgnoreCase) && currentPositionQty < 0);

        if (string.Equals(livePlan.Action, "BUY", StringComparison.OrdinalIgnoreCase) && currentPositionQty > 0)
        {
            throw new InvalidOperationException(
                $"Live order blocked: existing long position detected for {livePlan.Symbol} (qty={currentPositionQty:F4}). Close/reduce before placing another BUY.");
        }

        EvaluatePreTradeControls(
            route: route,
            symbol: livePlan.Symbol,
            action: livePlan.Action,
            quantity: livePlan.Quantity,
            limitPrice: livePlan.LimitPrice,
            notional: notional);

        var contract = brokerAdapter.BuildContract(new BrokerContractSpec(
            BrokerAssetType.Stock,
            livePlan.Symbol,
            "SMART",
            "USD",
            _options.PrimaryExchange));
        var order = brokerAdapter.BuildOrder(new BrokerOrderIntent(
            livePlan.Action,
            liveOrderType,
            livePlan.Quantity,
            LimitPrice: liveOrderType == "LMT" ? livePlan.LimitPrice : null));
        var nextOrderId = await _wrapper.NextValidIdTask;
        order.OrderId = nextOrderId;
        order.OrderRef = $"HARVESTER_SIM_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        order.Transmit = true;
        RegisterPreTradeCostEstimate(order.OrderId, route, livePlan.Symbol, livePlan.Action, livePlan.Quantity, livePlan.LimitPrice, order.OrderRef);

        brokerAdapter.PlaceOrder(client, order.OrderId, contract, order);
        MarkOrderTransmitted();
        Console.WriteLine($"[OK] Sim order transmitted: orderId={order.OrderId} symbol={livePlan.Symbol} action={livePlan.Action} type={liveOrderType} qty={livePlan.Quantity} lmt={(liveOrderType == "LMT" ? livePlan.LimitPrice.ToString(CultureInfo.InvariantCulture) : "n/a")} source={livePlan.Source} route={route}");

        await Task.Delay(TimeSpan.FromSeconds(4), token);
        brokerAdapter.RequestOpenOrders(client);
        await AwaitWithTimeout(_wrapper.OpenOrderEndTask, token, "openOrderEnd");

        var filledQuantity = ResolveFilledQuantityForOrder(order.OrderId);
        if (filledQuantity > 0)
        {
            await TryApplyPeakDrawdownExitAsync(client, brokerAdapter, livePlan, filledQuantity, token, entryOrderId: order.OrderId);
            brokerAdapter.RequestOpenOrders(client);
            await AwaitWithTimeout(_wrapper.OpenOrderEndTask, token, "openOrderEnd");
        }
        else
        {
            if (string.Equals(liveOrderType, "MKT", StringComparison.OrdinalIgnoreCase) && closesExistingPosition)
            {
                Console.WriteLine($"[INFO] Unfilled close MKT order left working intentionally: orderId={order.OrderId} symbol={livePlan.Symbol} action={livePlan.Action} status=awaiting-market.");
            }
            else
            {
                await TryRepriceOrCancelUnfilledLiveOrderAsync(client, brokerAdapter, livePlan, contract, order.OrderId, order.OrderRef, token);
            }

            brokerAdapter.RequestOpenOrders(client);
            await AwaitWithTimeout(_wrapper.OpenOrderEndTask, token, "openOrderEnd");
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var routeTag = string.Equals(route, nameof(RunOrdersPlaceSimMode), StringComparison.Ordinal)
            ? "sim_order"
            : $"sim_order_{route.ToLowerInvariant()}";
        var placementPath = Path.Combine(outputDir, $"{routeTag}_{timestamp}.json");
        var statusPath = string.Equals(route, nameof(RunOrdersPlaceSimMode), StringComparison.Ordinal)
            ? Path.Combine(outputDir, $"sim_order_status_{timestamp}.json")
            : Path.Combine(outputDir, $"sim_order_status_{route.ToLowerInvariant()}_{timestamp}.json");

        var placement = new LiveOrderPlacementRow(
            timestamp,
            order.OrderId,
            livePlan.Symbol,
            livePlan.Action,
            livePlan.Quantity,
            livePlan.LimitPrice,
            notional,
            _options.Account,
            order.OrderRef
        );

        WriteJson(placementPath, new[] { placement });
        WriteJson(statusPath, _wrapper.OrderStatusRows.ToArray());
        UpdatePreTradeTelemetryFromCallbacks();
        ExportPreTradeTelemetry(outputDir, timestamp);

        Console.WriteLine($"[OK] Sim placement export: {placementPath}");
        Console.WriteLine($"[OK] Sim status export: {statusPath} (rows={_wrapper.OrderStatusRows.Count})");

        return new LiveOrderExecutionResult(livePlan.Symbol, livePlan.Quantity, filledQuantity, true);
    }

    private async Task<double> GetCurrentPositionQuantityAsync(EClientSocket client, IBrokerAdapter brokerAdapter, string symbol, CancellationToken token)
    {
        brokerAdapter.RequestPositions(client);
        await AwaitWithTimeout(_wrapper.PositionEndTask, token, "positionEnd");
        brokerAdapter.CancelPositions(client);

        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return 0.0;
        }

        return _wrapper.Positions
            .Where(p => string.Equals(p.Symbol, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Quantity)
            .DefaultIfEmpty(0.0)
            .Sum();
    }

    private double ResolveFilledQuantityForOrder(int orderId)
    {
        var filledFromCanonical = _wrapper.CanonicalOrderEvents
            .Where(e => e.OrderId == orderId)
            .Select(e => e.Filled)
            .DefaultIfEmpty(0.0)
            .Max();
        if (filledFromCanonical > 0)
        {
            return filledFromCanonical;
        }

        var filledFromExecutions = _wrapper.Executions
            .Where(e => e.OrderId == orderId)
            .Select(e => e.Shares)
            .DefaultIfEmpty(0.0)
            .Sum();

        return Math.Max(0.0, filledFromExecutions);
    }

    private async Task TryRepriceOrCancelUnfilledLiveOrderAsync(EClientSocket client, IBrokerAdapter brokerAdapter, LiveOrderPlacementPlan livePlan, Contract contract, int orderId, string? orderRef, CancellationToken token)
    {
        var latestEvent = _wrapper.CanonicalOrderEvents
            .Where(e => e.OrderId == orderId)
            .OrderByDescending(e => e.TimestampUtc)
            .FirstOrDefault();

        if (latestEvent is null)
        {
            return;
        }

        if (IsTerminalOrderStatus(latestEvent.Status))
        {
            return;
        }

        var remainingQuantity = latestEvent.Remaining > 0
            ? latestEvent.Remaining
            : Math.Max(0.0, livePlan.Quantity - latestEvent.Filled);
        if (remainingQuantity <= 0)
        {
            return;
        }

        var requestId = 9970 + Math.Abs(orderId % 1000);
        var refreshedQuote = await FetchLiveQuoteSnapshotAsync(client, brokerAdapter, livePlan.Symbol, requestId, token);
        if (refreshedQuote.Bid <= 0 && refreshedQuote.Ask <= 0 && refreshedQuote.Last <= 0)
        {
            brokerAdapter.CancelOrder(client, orderId, string.Empty);
            Console.WriteLine($"[WARN] Unfilled order canceled due to missing refreshed quote context: orderId={orderId} symbol={livePlan.Symbol} status={latestEvent.Status}.");
            return;
        }

        var updatedPlan = ApplyLiveDefaultLimitFromQuote(livePlan with { Quantity = remainingQuantity }, refreshedQuote);

        try
        {
            ValidateLiveSafetyInputs(updatedPlan.Action, updatedPlan.Quantity, updatedPlan.LimitPrice, updatedPlan.Symbol, "LMT");
            ValidateLivePriceSanity(updatedPlan, refreshedQuote, "LMT");
        }
        catch (Exception ex)
        {
            brokerAdapter.CancelOrder(client, orderId, string.Empty);
            Console.WriteLine($"[WARN] Unfilled order canceled due to invalid refreshed context: orderId={orderId} symbol={livePlan.Symbol} status={latestEvent.Status} reason={ex.Message}");
            return;
        }

        var currentOpenLimit = _wrapper.OpenOrders
            .Where(o => o.OrderId == orderId)
            .Select(o => o.LimitPrice)
            .DefaultIfEmpty(livePlan.LimitPrice)
            .Last();

        if (Math.Abs(updatedPlan.LimitPrice - currentOpenLimit) < 0.0001)
        {
            Console.WriteLine($"[INFO] Unfilled order remains unchanged: orderId={orderId} symbol={livePlan.Symbol} status={latestEvent.Status} remaining={remainingQuantity:F4} lmt={currentOpenLimit:F4}.");
            return;
        }

        var amendedOrder = brokerAdapter.BuildOrder(new BrokerOrderIntent(
            updatedPlan.Action,
            "LMT",
            updatedPlan.Quantity,
            LimitPrice: updatedPlan.LimitPrice));
        amendedOrder.OrderId = orderId;
        amendedOrder.OrderRef = string.IsNullOrWhiteSpace(orderRef)
            ? $"HARVESTER_SIM_ADJUST_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
            : orderRef;
        amendedOrder.Transmit = true;

        brokerAdapter.PlaceOrder(client, orderId, contract, amendedOrder);
        MarkOrderTransmitted();

        Console.WriteLine($"[OK] Unfilled order repriced: orderId={orderId} symbol={updatedPlan.Symbol} action={updatedPlan.Action} qty={updatedPlan.Quantity:F4} oldLmt={currentOpenLimit:F4} newLmt={updatedPlan.LimitPrice:F4} status={latestEvent.Status}.");
    }

    private static bool IsTerminalOrderStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Equals("Filled", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("ApiCancelled", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Inactive", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// DT_V1.1_CONDUCT exit engine. Manages an open position from entry-fill to close
    /// with layered exit rules checked every second in strict priority order.
    ///
    /// V1.1 improvements over V1.0:
    ///   - Hard stop (absolute max-loss at 2Ã— giveback cap from entry)
    ///   - Break-even ratchet (floorâ†’entry after peak PnL â‰¥ 1R)
    ///   - Profit-lock floor (guarantee â‰¥ 0.5R after peak PnL â‰¥ 2R)
    ///   - Cancel open orders before flatten (avoid OCA conflicts)
    ///   - Position qty refresh every 15s (detect external closes)
    ///   - EMA-based ATR proxy (faster volatility adaptation)
    ///   - ATR warmup guard (noise floor disabled until â‰¥ 10 samples)
    ///   - Unified exit priority (removed redundant GIVEBACK_REVERSAL)
    ///   - Structured state tracking with diagnostics snapshot
    ///
    /// V1.2 additions:
    ///   - SAFETY overlay (kill switch file, daily loss limit, disconnect flatten)
    ///   - ENSURE_EXITS (broker-managed STP order + repair)
    ///   - Take-profit scaling (TP1/TP2 with OCA reconciliation)
    ///   - 1-minute candle maintenance with real ATR(14)
    ///   - TradeEpisode journal output on every close
    /// </summary>
    private async Task TryApplyPeakDrawdownExitAsync(EClientSocket client, IBrokerAdapter brokerAdapter, LiveOrderPlacementPlan livePlan, double filledQuantity, CancellationToken token, int requestIdSeed = 9960, int? entryOrderId = null)
    {
        // â”€â”€ V1.2 Config â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var cfg = _options.ConductL1StaleSec >= 0
            ? new ConductExitConfig() with { L1StaleSec = _options.ConductL1StaleSec }
            : new ConductExitConfig();
        const int monitorSafetyBufferSeconds = 5;
        var monitorSeconds = Math.Max(0, _options.TimeoutSeconds - monitorSafetyBufferSeconds);
        if (monitorSeconds <= 0 || filledQuantity <= 0)
        {
            return;
        }

        var isLong = string.Equals(livePlan.Action, "BUY", StringComparison.OrdinalIgnoreCase);
        var isShort = string.Equals(livePlan.Action, "SELL", StringComparison.OrdinalIgnoreCase);
        if (!isLong && !isShort)
        {
            return;
        }

        // â”€â”€ Derived risk parameters â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var entryPrice = Math.Max(0.0001, Math.Abs(livePlan.LimitPrice));
        var entryCommission = entryOrderId.HasValue
            ? ResolveEntryCommissionForOrder(entryOrderId.Value, filledQuantity)
            : ResolveEntryCommissionForPosition(livePlan.Symbol, livePlan.Action, filledQuantity);
        var estimatedExitCommission = EstimateCommissionPerOrder(filledQuantity);
        var roundTripCommission = entryCommission + estimatedExitCommission;
        var entryNotional = entryPrice * filledQuantity;
        var givebackLimitUsd = Math.Min(cfg.GivebackPctOfNotional * entryNotional, cfg.GivebackUsdCap);
        var riskPerTradeUsd = Math.Max(0.01, givebackLimitUsd);
        var trailingActivationUsd = Math.Max(cfg.TrailingProfitMinUsd, cfg.TrailingProfitMinRMultiple * riskPerTradeUsd);
        var givebackPerShare = givebackLimitUsd / filledQuantity;
        var immediateAdverseUsdDynamic = Math.Min(cfg.ImmediateAdverseMoveUsd, Math.Max(1.0, 0.35 * riskPerTradeUsd));

        // Hard stop: absolute max-loss distance (V1.1)
        var hardStopDistance = Math.Max(givebackPerShare * cfg.HardStopDistanceMultiplier, entryPrice * 0.03);
        var hardStopPrice = isLong
            ? entryPrice - hardStopDistance
            : entryPrice + hardStopDistance;

        // Break-even and profit-lock thresholds (V1.1)
        var breakEvenActivationUsd = cfg.BreakEvenActivationR * riskPerTradeUsd;
        var profitLockActivationUsd = cfg.ProfitLockActivationR * riskPerTradeUsd;
        var profitLockGuaranteeUsd = cfg.ProfitLockGuaranteeR * riskPerTradeUsd;

        // Take-profit prices (V1.2)
        var tp1Price = isLong
            ? entryPrice + (cfg.Tp1RMultiple * riskPerTradeUsd / filledQuantity)
            : entryPrice - (cfg.Tp1RMultiple * riskPerTradeUsd / filledQuantity);
        var tp2Price = isLong
            ? entryPrice + (cfg.Tp2RMultiple * riskPerTradeUsd / filledQuantity)
            : entryPrice - (cfg.Tp2RMultiple * riskPerTradeUsd / filledQuantity);
        var tp1Qty = Math.Max(1, Math.Floor(filledQuantity * cfg.Tp1ScaleOutPct));
        var tp2Qty = Math.Max(0, filledQuantity - tp1Qty);

        // â”€â”€ Initialize state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var state = new ConductPositionState
        {
            Symbol = livePlan.Symbol,
            IsLong = isLong,
            FilledQuantity = filledQuantity,
            EntryPrice = entryPrice,
            LoopStartUtc = DateTime.UtcNow,
            PeakPrice = entryPrice,
            TroughPrice = entryPrice,
            ActiveMaxDrawdownPct = cfg.InitialMaxDrawdownPct,
            FloorPricePerShare = isLong ? 0.0 : double.MaxValue,
            LastFreshL1Utc = DateTime.UtcNow,
            TimeStopDeadlineUtc = DateTime.UtcNow.AddSeconds(cfg.TimeStopSec),
            OriginalFilledQuantity = filledQuantity,
            RiskPerTradeUsd = riskPerTradeUsd,
            RoundTripCommission = roundTripCommission,
        };

        Console.WriteLine($"[INFO] Conduct V1.2 monitor armed: symbol={state.Symbol} side={livePlan.Action} qty={filledQuantity:F4} entry={entryPrice:F4} hardStop={hardStopPrice:F4} maxDrawdown={state.ActiveMaxDrawdownPct:P2} window={monitorSeconds}s riskUsd={riskPerTradeUsd:F2} trailingActivation={trailingActivationUsd:F2} breakEvenAt={breakEvenActivationUsd:F2} profitLockAt={profitLockActivationUsd:F2} safety={cfg.SafetyOverlayEnabled} ensureExits={cfg.EnsureExitsEnabled} tp={cfg.TakeProfitEnabled} candles={cfg.CandleMaintenanceEnabled} journal={cfg.TradeEpisodeJournalEnabled}.");

        // â”€â”€ Step 3.2: ENSURE_EXITS â€” place protective bracket (V1.2) â”€â”€â”€â”€
        if (cfg.EnsureExitsEnabled)
        {
            await ConductEnsureExitsAsync(client, brokerAdapter, livePlan, state, cfg, hardStopPrice, tp1Price, tp2Price, tp1Qty, tp2Qty, token);
        }

        var deadlineUtc = DateTime.UtcNow.AddSeconds(monitorSeconds);
        var requestId = requestIdSeed;
        var loopIteration = 0;

        try
        {
            while (DateTime.UtcNow < deadlineUtc && !token.IsCancellationRequested)
            {
                loopIteration++;

                // â”€â”€ E0: SAFETY OVERLAY (V1.2) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if (cfg.SafetyOverlayEnabled)
                {
                    state.LastConnectedUtc = _hasConnectivityFailure ? state.LastConnectedUtc : DateTime.UtcNow;
                    var safetyReason = ConductCheckSafetyOverlay(state, cfg);
                    if (safetyReason != null)
                    {
                        var safetyRefPrice = state.PeakPrice > 0 ? state.PeakPrice : entryPrice;
                        await ConductFlattenAsync(client, brokerAdapter, livePlan, state, safetyRefPrice, safetyReason, token, loopIteration);
                        return;
                    }
                }

                // â”€â”€ Periodic position refresh (V1.1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if (cfg.PositionRecheckIntervalSec > 0
                    && state.TicksSincePositionRecheck >= cfg.PositionRecheckIntervalSec)
                {
                    state.TicksSincePositionRecheck = 0;
                    var currentQty = await GetCurrentPositionQuantityAsync(client, brokerAdapter, livePlan.Symbol, token);
                    if (Math.Abs(currentQty) < 0.0001)
                    {
                        Console.WriteLine($"[INFO] Conduct V1.1: position externally closed. symbol={state.Symbol} reason=EXTERNAL_CLOSE.");
                        return;
                    }

                    if (Math.Abs(currentQty) < state.FilledQuantity * 0.99)
                    {
                        Console.WriteLine($"[INFO] Conduct V1.1: position partially reduced externally. symbol={state.Symbol} prev={state.FilledQuantity:F4} now={Math.Abs(currentQty):F4}.");
                        state.FilledQuantity = Math.Abs(currentQty);
                    }
                }

                state.TicksSincePositionRecheck++;

                // â”€â”€ Fetch market data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                var quoteSnapshot = await FetchLiveQuoteSnapshotAsync(client, brokerAdapter, livePlan.Symbol, requestId++, token);
                var observed = ResolveObservedPriceForExit(livePlan.Symbol, quoteSnapshot, livePlan.Action);
                var hasFreshL1 = observed > 0 && (quoteSnapshot.Bid > 0 || quoteSnapshot.Ask > 0 || quoteSnapshot.Last > 0);
                if (hasFreshL1)
                {
                    state.LastFreshL1Utc = DateTime.UtcNow;
                }

                // â”€â”€ E1: L1 STALE GUARD â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if ((DateTime.UtcNow - state.LastFreshL1Utc).TotalSeconds > cfg.L1StaleSec)
                {
                    var staleRef = observed > 0 ? observed : entryPrice;
                    await ConductFlattenAsync(client, brokerAdapter, livePlan, state, staleRef, "L1_STALE", token, loopIteration);
                    return;
                }

                if (observed <= 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(cfg.MonitorPollSeconds), token);
                    continue;
                }

                // â”€â”€ Update market metrics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                var spread = quoteSnapshot.Bid > 0 && quoteSnapshot.Ask > 0
                    ? Math.Max(0, quoteSnapshot.Ask - quoteSnapshot.Bid)
                    : 0.0;
                var tickSize = observed < 1 ? 0.0001 : 0.01;

                state.MarkHistory.Enqueue(observed);
                while (state.MarkHistory.Count > cfg.AtrWindowSize)
                {
                    _ = state.MarkHistory.Dequeue();
                }

                var atrProxy = ComputeEmaAtrProxy(state.MarkHistory, cfg.AtrEmaAlpha);

                // â”€â”€ Candle maintenance (V1.2) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if (cfg.CandleMaintenanceEnabled && observed > 0)
                {
                    ConductUpdateCandle(state, observed, DateTime.UtcNow);
                    if (state.CandleBars1M.Count >= cfg.AtrCandlePeriod)
                    {
                        state.Atr1M = ConductComputeAtr1M(state.CandleBars1M, cfg.AtrCandlePeriod);
                    }
                }

                // â”€â”€ Update peak/trough + MFE/MAE â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                var unrealUsd = isLong
                    ? (observed - entryPrice) * state.FilledQuantity
                    : (entryPrice - observed) * state.FilledQuantity;

                state.MfeUsd = Math.Max(state.MfeUsd, unrealUsd);
                state.MaeUsd = Math.Min(state.MaeUsd, unrealUsd);

                if (isLong)
                {
                    state.PeakPrice = Math.Max(state.PeakPrice, observed);
                    state.PeakUnrealPnlUsd = Math.Max(state.PeakUnrealPnlUsd, (state.PeakPrice - entryPrice) * state.FilledQuantity);
                }
                else
                {
                    state.TroughPrice = Math.Min(state.TroughPrice, observed);
                    state.PeakUnrealPnlUsd = Math.Max(state.PeakUnrealPnlUsd, (entryPrice - state.TroughPrice) * state.FilledQuantity);
                }

                // â”€â”€ E2: IMMEDIATE REVERSAL (first N seconds) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if ((DateTime.UtcNow - state.LoopStartUtc).TotalSeconds <= cfg.ImmediateWindowSec)
                {
                    var adversePct = Math.Abs(observed - entryPrice) / Math.Max(0.0001, entryPrice);
                    var adverseForLong = isLong && observed < entryPrice;
                    var adverseForShort = isShort && observed > entryPrice;
                    if ((adverseForLong || adverseForShort)
                        && (adversePct >= cfg.ImmediateAdverseMovePct || unrealUsd <= -immediateAdverseUsdDynamic))
                    {
                        await ConductFlattenAsync(client, brokerAdapter, livePlan, state, observed, "IMMEDIATE_REVERSAL", token, loopIteration);
                        return;
                    }
                }

                // â”€â”€ E3: HARD STOP (absolute price floor/ceiling) V1.1 â”€â”€â”€
                var hardStopHit = isLong
                    ? observed <= hardStopPrice
                    : observed >= hardStopPrice;
                if (hardStopHit)
                {
                    await ConductFlattenAsync(client, brokerAdapter, livePlan, state, observed, "HARD_STOP", token, loopIteration);
                    return;
                }

                // â”€â”€ Adaptive drawdown tightening â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if (!state.TightenedRuleActive)
                {
                    var favorableWinPct = isLong
                        ? (observed * state.FilledQuantity - entryNotional) / entryNotional
                        : (entryNotional - observed * state.FilledQuantity) / entryNotional;
                    if (favorableWinPct >= cfg.TightenTriggerWinPct)
                    {
                        state.TightenedRuleActive = true;
                        state.ActiveMaxDrawdownPct = cfg.TightenedMaxDrawdownPct;
                        Console.WriteLine($"[INFO] Conduct V1.1 drawdown tightened: symbol={state.Symbol} winPct={favorableWinPct:P2} newMaxDD={state.ActiveMaxDrawdownPct:P2}.");
                    }
                }

                // â”€â”€ Break-even ratchet (V1.1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if (!state.BreakEvenActive && state.PeakUnrealPnlUsd >= breakEvenActivationUsd)
                {
                    state.BreakEvenActive = true;
                    if (isLong)
                    {
                        state.FloorPricePerShare = Math.Max(state.FloorPricePerShare, entryPrice);
                    }
                    else
                    {
                        state.FloorPricePerShare = Math.Min(state.FloorPricePerShare, entryPrice);
                    }

                    Console.WriteLine($"[INFO] Conduct V1.1 break-even ratchet: symbol={state.Symbol} peakPnl={state.PeakUnrealPnlUsd:F2} floor={state.FloorPricePerShare:F4}.");
                }

                // â”€â”€ Profit-lock floor (V1.1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if (!state.ProfitLocked && state.PeakUnrealPnlUsd >= profitLockActivationUsd)
                {
                    state.ProfitLocked = true;
                    var lockPerShare = profitLockGuaranteeUsd / state.FilledQuantity;
                    if (isLong)
                    {
                        state.FloorPricePerShare = Math.Max(state.FloorPricePerShare, entryPrice + lockPerShare);
                    }
                    else
                    {
                        state.FloorPricePerShare = Math.Min(state.FloorPricePerShare, entryPrice - lockPerShare);
                    }

                    Console.WriteLine($"[INFO] Conduct V1.1 profit locked: symbol={state.Symbol} peakPnl={state.PeakUnrealPnlUsd:F2} guaranteedFloor={state.FloorPricePerShare:F4} lockPerShare={lockPerShare:F4}.");
                }

                // â”€â”€ E4: FLOOR BREACH (break-even / profit-lock) V1.1 â”€â”€â”€â”€
                if (state.BreakEvenActive || state.ProfitLocked)
                {
                    var floorBreached = isLong
                        ? observed <= state.FloorPricePerShare
                        : observed >= state.FloorPricePerShare;
                    if (floorBreached)
                    {
                        var reason = state.ProfitLocked ? "PROFIT_LOCK_FLOOR" : "BREAKEVEN_FLOOR";
                        await ConductFlattenAsync(client, brokerAdapter, livePlan, state, observed, reason, token, loopIteration);
                        return;
                    }
                }

                // â”€â”€ E5: TRAILING STOP â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if (!state.TrailingActive && state.PeakUnrealPnlUsd >= trailingActivationUsd)
                {
                    state.TrailingActive = true;
                    Console.WriteLine($"[INFO] Conduct V1.1 trailing activated: symbol={state.Symbol} peakUnreal={state.PeakUnrealPnlUsd:F2} activation={trailingActivationUsd:F2}.");
                }

                if (state.TrailingActive)
                {
                    var noiseFloorPerShare = state.MarkHistory.Count >= cfg.AtrWarmupMinSamples
                        ? Math.Max(
                            Math.Max(cfg.TrailKSpread * spread, cfg.TrailKTicks * tickSize),
                            cfg.TrailKAtr * atrProxy)
                        : Math.Max(cfg.TrailKSpread * spread, cfg.TrailKTicks * tickSize);
                    var trailPerShare = Math.Max(givebackPerShare, noiseFloorPerShare);

                    var trailTriggered = isLong
                        ? observed <= state.PeakPrice - trailPerShare
                        : observed >= state.TroughPrice + trailPerShare;

                    if (trailTriggered)
                    {
                        await ConductFlattenAsync(client, brokerAdapter, livePlan, state, observed, "TRAIL_STOP", token, loopIteration);
                        return;
                    }
                }

                // â”€â”€ ENSURE_EXITS RECHECK (V1.2) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if (cfg.EnsureExitsEnabled)
                {
                    state.TicksSinceExitRecheck++;
                    if (state.TicksSinceExitRecheck >= (int)(cfg.ExitRecheckIntervalSec / cfg.MonitorPollSeconds))
                    {
                        state.TicksSinceExitRecheck = 0;
                        await ConductReconcileExitsAsync(client, brokerAdapter, livePlan, state, cfg, hardStopPrice, token);
                    }
                }

                // â”€â”€ E6: MAX DRAWDOWN CAP (pre-trailing fallback) â”€â”€â”€â”€â”€â”€â”€â”€â”€
                // When trailing is NOT yet active, enforce percentage-based max drawdown
                // from peak position value (subsumes the old GIVEBACK_REVERSAL).
                if (!state.TrailingActive)
                {
                    var peakPositionValue = isLong
                        ? state.PeakPrice * state.FilledQuantity
                        : entryPrice * state.FilledQuantity;
                    var currentPositionValue = observed * state.FilledQuantity;
                    var drawdownFromPeak = isLong
                        ? (peakPositionValue - currentPositionValue) / Math.Max(0.0001, peakPositionValue)
                        : (currentPositionValue - peakPositionValue) / Math.Max(0.0001, peakPositionValue);

                    if (drawdownFromPeak >= state.ActiveMaxDrawdownPct)
                    {
                        await ConductFlattenAsync(client, brokerAdapter, livePlan, state, observed, "MAX_DRAWDOWN", token, loopIteration);
                        return;
                    }
                }

                // â”€â”€ E7: TIME STOP â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if (DateTime.UtcNow >= state.TimeStopDeadlineUtc && state.MfeUsd < (cfg.MinProgressR * riskPerTradeUsd))
                {
                    await ConductFlattenAsync(client, brokerAdapter, livePlan, state, observed, "TIME_STOP", token, loopIteration);
                    return;
                }

                // â”€â”€ E8: EOD FLATTEN â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                var nyTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Eastern Standard Time");
                if (nyTime.TimeOfDay >= TimeSpan.Parse(cfg.EodFlattenTimeET))
                {
                    await ConductFlattenAsync(client, brokerAdapter, livePlan, state, observed, "EOD", token, loopIteration);
                    return;
                }

                // â”€â”€ Periodic diagnostics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                if (loopIteration % 30 == 0)
                {
                    Console.WriteLine($"[DIAG] Conduct V1.2: symbol={state.Symbol} tick={loopIteration} mark={observed:F4} peak={state.PeakPrice:F4} trough={state.TroughPrice:F4} unreal={unrealUsd:F2} mfe={state.MfeUsd:F2} mae={state.MaeUsd:F2} peakPnl={state.PeakUnrealPnlUsd:F2} trailing={state.TrailingActive} breakeven={state.BreakEvenActive} locked={state.ProfitLocked} floor={state.FloorPricePerShare:F4} atr={atrProxy:F6} atr1m={state.Atr1M:F6} candles={state.CandleBars1M.Count} spread={spread:F4} stopOrd={state.StopOrderId} tp1Ord={state.Tp1OrderId} tp1Done={state.Tp1Done}.");
                }

                await Task.Delay(TimeSpan.FromSeconds(cfg.MonitorPollSeconds), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Console.WriteLine($"[WARN] Conduct V1.2 monitor ended (cancellation): symbol={state.Symbol} ticks={loopIteration} mfe={state.MfeUsd:F2} mae={state.MaeUsd:F2}.");
            return;
        }

        Console.WriteLine($"[INFO] Conduct V1.2 monitor window elapsed: symbol={state.Symbol} ticks={loopIteration} mfe={state.MfeUsd:F2} mae={state.MaeUsd:F2} peakPnl={state.PeakUnrealPnlUsd:F2} trailing={state.TrailingActive} breakeven={state.BreakEvenActive} locked={state.ProfitLocked}.");
    }

    /// <summary>
    /// V1.2 flatten procedure: cancel broker-managed exits, cancel conflicting orders,
    /// submit MKT exit, verify, and write TradeEpisode journal.
    /// </summary>
    private async Task ConductFlattenAsync(EClientSocket client, IBrokerAdapter brokerAdapter, LiveOrderPlacementPlan plan, ConductPositionState state, double referencePrice, string reasonCode, CancellationToken token, int loopIteration = 0)
    {
        Console.WriteLine($"[INFO] Conduct V1.2 flatten triggered: symbol={state.Symbol} reason={reasonCode} mark={referencePrice:F4} mfe={state.MfeUsd:F2} mae={state.MaeUsd:F2} peakPnl={state.PeakUnrealPnlUsd:F2} trailing={state.TrailingActive} breakeven={state.BreakEvenActive} locked={state.ProfitLocked}.");

        // Step 1: Cancel any existing open orders for this symbol (including broker-managed exits)
        try
        {
            brokerAdapter.RequestOpenOrders(client);
            await AwaitWithTimeout(_wrapper.OpenOrderEndTask, token, "openOrderEnd");
            var symbolOrders = _wrapper.OpenOrders
                .Where(o => string.Equals(o.Symbol, state.Symbol, StringComparison.OrdinalIgnoreCase))
                .Where(o => !IsTerminalOrderStatus(o.Status))
                .ToArray();
            foreach (var existingOrder in symbolOrders)
            {
                brokerAdapter.CancelOrder(client, existingOrder.OrderId, string.Empty);
                Console.WriteLine($"[INFO] Conduct V1.2: canceled order before flatten: orderId={existingOrder.OrderId} symbol={state.Symbol} status={existingOrder.Status}.");
            }

            if (symbolOrders.Length > 0)
            {
                await Task.Delay(500, token);
            }

            // Clear tracked exit order IDs
            state.StopOrderId = null;
            state.Tp1OrderId = null;
            state.Tp2OrderId = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Conduct V1.2: failed to cancel orders for {state.Symbol}: {ex.Message}");
        }

        // Step 2: Submit MKT flatten order
        var exitAction = state.IsLong ? "SELL" : "BUY";
        var exitContract = brokerAdapter.BuildContract(new BrokerContractSpec(
            BrokerAssetType.Stock,
            plan.Symbol,
            "SMART",
            "USD",
            _options.PrimaryExchange));

        var exitOrder = brokerAdapter.BuildOrder(new BrokerOrderIntent(
            exitAction,
            "MKT",
            state.FilledQuantity,
            LimitPrice: null));

        var nextOrderId = await _wrapper.NextValidIdTask;
        exitOrder.OrderId = nextOrderId;
        exitOrder.OrderRef = $"CONDUCT_V1.2_{reasonCode}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        exitOrder.Transmit = true;

        brokerAdapter.PlaceOrder(client, exitOrder.OrderId, exitContract, exitOrder);
        MarkOrderTransmitted();

        Console.WriteLine($"[OK] Conduct V1.2 exit transmitted: orderId={exitOrder.OrderId} symbol={plan.Symbol} action={exitAction} type=MKT qty={state.FilledQuantity:F4} reason={reasonCode} refPx={referencePrice:F4}.");

        // Step 3: Verify flatten â€” wait briefly and check position
        double exitPrice = referencePrice;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
            var remainingQty = await GetCurrentPositionQuantityAsync(client, brokerAdapter, plan.Symbol, token);
            if (Math.Abs(remainingQty) > 0.0001)
            {
                Console.WriteLine($"[WARN] Conduct V1.2: position not fully closed. symbol={plan.Symbol} reason={reasonCode} remaining={remainingQty:F4}. Will retry.");
                var retryOrder = brokerAdapter.BuildOrder(new BrokerOrderIntent(
                    exitAction,
                    "MKT",
                    Math.Abs(remainingQty),
                    LimitPrice: null));
                var retryOrderId = await _wrapper.NextValidIdTask;
                retryOrder.OrderId = retryOrderId;
                retryOrder.OrderRef = $"CONDUCT_V1.2_{reasonCode}_RETRY_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                retryOrder.Transmit = true;
                brokerAdapter.PlaceOrder(client, retryOrder.OrderId, exitContract, retryOrder);
                MarkOrderTransmitted();
                Console.WriteLine($"[OK] Conduct V1.2 retry exit transmitted: orderId={retryOrder.OrderId} symbol={plan.Symbol} remaining={Math.Abs(remainingQty):F4} reason={reasonCode}_RETRY.");
            }
            else
            {
                Console.WriteLine($"[OK] Conduct V1.2: position fully closed. symbol={plan.Symbol} reason={reasonCode}.");
            }

            // Try to get actual fill price from executions
            var exitExec = _wrapper.Executions
                .Where(e => e.OrderId == exitOrder.OrderId && e.Price > 0)
                .OrderByDescending(e => e.Time)
                .FirstOrDefault();
            if (exitExec != null && exitExec.Price > 0)
            {
                exitPrice = exitExec.Price;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Conduct V1.2: flatten verification failed for {plan.Symbol}: {ex.Message}");
        }

        // Step 4: Write TradeEpisode journal (V1.2)
        if (new ConductExitConfig().TradeEpisodeJournalEnabled)
        {
            try
            {
                WriteConductTradeEpisode(state, exitPrice, reasonCode, loopIteration);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Conduct V1.2: failed to write trade episode for {plan.Symbol}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Compute ATR proxy using exponential moving average of absolute price changes.
    /// More responsive to recent volatility than simple average. (V1.1)
    /// </summary>
    private static double ComputeEmaAtrProxy(Queue<double> markHistory, double alpha)
    {
        if (markHistory.Count < 2)
        {
            return 0.0;
        }

        var marks = markHistory.ToArray();
        var ema = Math.Abs(marks[1] - marks[0]);
        for (var i = 2; i < marks.Length; i++)
        {
            var absDiff = Math.Abs(marks[i] - marks[i - 1]);
            ema = alpha * absDiff + (1 - alpha) * ema;
        }

        return Math.Max(0.0, ema);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // V1.2 HELPER METHODS
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// SAFETY OVERLAY (V1.2): Check kill switch file, daily loss limit, and disconnect timeout.
    /// Returns a reason code string if we should flatten, or null if safe.
    /// </summary>
    private string? ConductCheckSafetyOverlay(ConductPositionState state, ConductExitConfig cfg)
    {
        // Kill switch: if a specific file exists on disk, flatten everything
        if (!string.IsNullOrWhiteSpace(cfg.KillSwitchFilePath))
        {
            try
            {
                if (File.Exists(cfg.KillSwitchFilePath))
                {
                    Console.WriteLine($"[ALERT] Conduct V1.2 SAFETY: kill switch file detected: {cfg.KillSwitchFilePath}. Flattening {state.Symbol}.");
                    return "SAFETY_KILL_SWITCH";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Conduct V1.2 SAFETY: kill switch file check failed: {ex.Message}");
            }
        }

        // Daily loss limit: if session realized PnL exceeds maximum, flatten
        if (cfg.DailyMaxLossUsd > 0 && state.SessionRealizedPnlUsd <= -cfg.DailyMaxLossUsd)
        {
            Console.WriteLine($"[ALERT] Conduct V1.2 SAFETY: daily loss limit breached: realizedPnl={state.SessionRealizedPnlUsd:F2} limit=-{cfg.DailyMaxLossUsd:F2}. Flattening {state.Symbol}.");
            return "SAFETY_DAILY_LOSS";
        }

        // Disconnect detection: if we haven't had connectivity for N seconds, flatten
        if (cfg.DisconnectFlattenSec > 0 && _hasConnectivityFailure)
        {
            var disconnectedSec = (DateTime.UtcNow - state.LastConnectedUtc).TotalSeconds;
            if (disconnectedSec >= cfg.DisconnectFlattenSec)
            {
                Console.WriteLine($"[ALERT] Conduct V1.2 SAFETY: disconnect timeout: disconnectedSec={disconnectedSec:F1} limit={cfg.DisconnectFlattenSec}. Flattening {state.Symbol}.");
                return "SAFETY_DISCONNECT";
            }
        }

        return null;
    }

    /// <summary>
    /// CANDLE MAINTENANCE (V1.2): Update the building 1-minute bar with the latest tick.
    /// When a new minute starts, finalize the previous bar and start a new one.
    /// </summary>
    private static void ConductUpdateCandle(ConductPositionState state, double price, DateTime utcNow)
    {
        // Truncate to minute boundary
        var minuteUtc = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, utcNow.Minute, 0, DateTimeKind.Utc);

        if (state.CurrentCandle == null || state.CurrentCandle.MinuteUtc != minuteUtc)
        {
            // Finalize previous candle if exists
            if (state.CurrentCandle != null)
            {
                var completed = state.CurrentCandle.Finalize();
                state.CandleBars1M.Add(completed);

                // Keep at most 120 bars (2 hours of 1m data)
                while (state.CandleBars1M.Count > 120)
                {
                    state.CandleBars1M.RemoveAt(0);
                }
            }

            // Start new building bar
            state.CurrentCandle = new ConductCandleBarBuilder
            {
                MinuteUtc = minuteUtc,
                Open = price,
                High = price,
                Low = price,
                Close = price,
                Volume = 1,
                TickCount = 1,
            };
        }
        else
        {
            // Update existing building bar
            state.CurrentCandle.High = Math.Max(state.CurrentCandle.High, price);
            state.CurrentCandle.Low = Math.Min(state.CurrentCandle.Low, price);
            state.CurrentCandle.Close = price;
            state.CurrentCandle.Volume++;
            state.CurrentCandle.TickCount++;
        }
    }

    /// <summary>
    /// ATR(N) from completed 1-minute bars using True Range (V1.2).
    /// Returns 0 if insufficient bars.
    /// </summary>
    private static double ConductComputeAtr1M(List<ConductCandleBar> bars, int period)
    {
        if (bars.Count < period + 1)
        {
            return 0.0;
        }

        // Use the last `period` bars (skip the oldest for previous close reference)
        var startIndex = bars.Count - period;
        double atr = 0;
        for (var i = startIndex; i < bars.Count; i++)
        {
            var current = bars[i];
            var prevClose = bars[i - 1].Close;
            var trueRange = Math.Max(
                current.High - current.Low,
                Math.Max(
                    Math.Abs(current.High - prevClose),
                    Math.Abs(current.Low - prevClose)));
            atr += trueRange;
        }

        return Math.Max(0.0, atr / period);
    }

    /// <summary>
    /// ENSURE_EXITS (V1.2): Place initial protective bracket â€” STP stop-loss order
    /// and optionally TP1/TP2 limit take-profit orders.
    /// </summary>
    private async Task ConductEnsureExitsAsync(
        EClientSocket client,
        IBrokerAdapter brokerAdapter,
        LiveOrderPlacementPlan plan,
        ConductPositionState state,
        ConductExitConfig cfg,
        double stopPrice,
        double tp1Price,
        double tp2Price,
        double tp1Qty,
        double tp2Qty,
        CancellationToken token)
    {
        var exitAction = state.IsLong ? "SELL" : "BUY";
        var exitContract = brokerAdapter.BuildContract(new BrokerContractSpec(
            BrokerAssetType.Stock,
            plan.Symbol,
            "SMART",
            "USD",
            _options.PrimaryExchange));

        // Place STP (stop-loss) order
        try
        {
            var stopOrder = brokerAdapter.BuildOrder(new BrokerOrderIntent(
                exitAction,
                "STP",
                state.FilledQuantity,
                LimitPrice: null));
            stopOrder.AuxPrice = Math.Round(stopPrice, 2);
            var stopOrderId = await _wrapper.NextValidIdTask;
            stopOrder.OrderId = stopOrderId;
            stopOrder.OrderRef = $"CONDUCT_V1.2_STOP_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            stopOrder.Transmit = true;
            stopOrder.OutsideRth = true;

            brokerAdapter.PlaceOrder(client, stopOrder.OrderId, exitContract, stopOrder);
            MarkOrderTransmitted();
            state.StopOrderId = stopOrderId;
            Console.WriteLine($"[OK] Conduct V1.2 ENSURE_EXITS: STP placed: orderId={stopOrderId} symbol={plan.Symbol} action={exitAction} stopPx={stopPrice:F4} qty={state.FilledQuantity:F4}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Conduct V1.2 ENSURE_EXITS: failed to place STP for {plan.Symbol}: {ex.Message}");
        }

        // Place TP1 (take-profit 1) if enabled
        if (cfg.TakeProfitEnabled && tp1Qty > 0)
        {
            try
            {
                var tp1Order = brokerAdapter.BuildOrder(new BrokerOrderIntent(
                    exitAction,
                    "LMT",
                    tp1Qty,
                    LimitPrice: Math.Round(tp1Price, 2)));
                var tp1OrderId = await _wrapper.NextValidIdTask;
                tp1Order.OrderId = tp1OrderId;
                tp1Order.OrderRef = $"CONDUCT_V1.2_TP1_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                tp1Order.Transmit = true;
                tp1Order.OutsideRth = true;

                brokerAdapter.PlaceOrder(client, tp1Order.OrderId, exitContract, tp1Order);
                MarkOrderTransmitted();
                state.Tp1OrderId = tp1OrderId;
                Console.WriteLine($"[OK] Conduct V1.2 ENSURE_EXITS: TP1 placed: orderId={tp1OrderId} symbol={plan.Symbol} action={exitAction} limitPx={tp1Price:F4} qty={tp1Qty:F0}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Conduct V1.2 ENSURE_EXITS: failed to place TP1 for {plan.Symbol}: {ex.Message}");
            }
        }

        // Place TP2 (take-profit 2) if enabled
        if (cfg.TakeProfitEnabled && tp2Qty > 0)
        {
            try
            {
                var tp2Order = brokerAdapter.BuildOrder(new BrokerOrderIntent(
                    exitAction,
                    "LMT",
                    tp2Qty,
                    LimitPrice: Math.Round(tp2Price, 2)));
                var tp2OrderId = await _wrapper.NextValidIdTask;
                tp2Order.OrderId = tp2OrderId;
                tp2Order.OrderRef = $"CONDUCT_V1.2_TP2_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                tp2Order.Transmit = true;
                tp2Order.OutsideRth = true;

                brokerAdapter.PlaceOrder(client, tp2Order.OrderId, exitContract, tp2Order);
                MarkOrderTransmitted();
                state.Tp2OrderId = tp2OrderId;
                Console.WriteLine($"[OK] Conduct V1.2 ENSURE_EXITS: TP2 placed: orderId={tp2OrderId} symbol={plan.Symbol} action={exitAction} limitPx={tp2Price:F4} qty={tp2Qty:F0}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Conduct V1.2 ENSURE_EXITS: failed to place TP2 for {plan.Symbol}: {ex.Message}");
            }
        }

        await Task.Delay(500, token); // Brief pause for order acknowledgment
    }

    /// <summary>
    /// ENSURE_EXITS RECONCILIATION (V1.2): Periodically verify broker-managed exit orders
    /// still exist (haven't been cancelled externally). Repair if missing.
    /// Also detect TP1/TP2 fills and adjust position tracking.
    /// </summary>
    private async Task ConductReconcileExitsAsync(
        EClientSocket client,
        IBrokerAdapter brokerAdapter,
        LiveOrderPlacementPlan plan,
        ConductPositionState state,
        ConductExitConfig cfg,
        double stopPrice,
        CancellationToken token)
    {
        try
        {
            brokerAdapter.RequestOpenOrders(client);
            await AwaitWithTimeout(_wrapper.OpenOrderEndTask, token, "openOrderEnd");

            var symbolOrders = _wrapper.OpenOrders
                .Where(o => string.Equals(o.Symbol, state.Symbol, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // Check if TP1 has filled (order no longer open but was tracked)
            if (state.Tp1OrderId.HasValue && !state.Tp1Done)
            {
                var tp1Alive = symbolOrders.Any(o => o.OrderId == state.Tp1OrderId.Value && !IsTerminalOrderStatus(o.Status));
                if (!tp1Alive)
                {
                    // Check if it actually filled (vs cancelled)
                    var tp1Filled = _wrapper.Executions.Any(e => e.OrderId == state.Tp1OrderId.Value);
                    var tp1Status = symbolOrders.FirstOrDefault(o => o.OrderId == state.Tp1OrderId.Value)?.Status;
                    var isFilled = tp1Filled || string.Equals(tp1Status, "Filled", StringComparison.OrdinalIgnoreCase);

                    if (isFilled)
                    {
                        state.Tp1Done = true;
                        // Reduce tracked position quantity
                        var tp1ScaledQty = Math.Max(1, Math.Floor(state.OriginalFilledQuantity * cfg.Tp1ScaleOutPct));
                        state.FilledQuantity = Math.Max(0, state.FilledQuantity - tp1ScaledQty);
                        Console.WriteLine($"[INFO] Conduct V1.2 RECONCILE: TP1 filled for {state.Symbol}. Reduced qty by {tp1ScaledQty:F0}, remaining={state.FilledQuantity:F4}.");

                        // If position fully closed by TPs, we're done
                        if (state.FilledQuantity < 0.0001)
                        {
                            Console.WriteLine($"[INFO] Conduct V1.2 RECONCILE: position fully closed by TPs for {state.Symbol}.");
                            return;
                        }

                        // Adjust stop order quantity if stop is still alive
                        if (state.StopOrderId.HasValue)
                        {
                            var stopAlive = symbolOrders.Any(o => o.OrderId == state.StopOrderId.Value && !IsTerminalOrderStatus(o.Status));
                            if (stopAlive && state.FilledQuantity > 0)
                            {
                                // Cancel and replace stop with new quantity
                                brokerAdapter.CancelOrder(client, state.StopOrderId.Value, string.Empty);
                                await Task.Delay(300, token);

                                var exitAction = state.IsLong ? "SELL" : "BUY";
                                var exitContract = brokerAdapter.BuildContract(new BrokerContractSpec(
                                    BrokerAssetType.Stock, plan.Symbol, "SMART", "USD", _options.PrimaryExchange));
                                var newStopOrder = brokerAdapter.BuildOrder(new BrokerOrderIntent(
                                    exitAction, "STP", state.FilledQuantity, LimitPrice: null));
                                newStopOrder.AuxPrice = Math.Round(stopPrice, 2);
                                var newStopId = await _wrapper.NextValidIdTask;
                                newStopOrder.OrderId = newStopId;
                                newStopOrder.OrderRef = $"CONDUCT_V1.2_STOP_RESIZE_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                                newStopOrder.Transmit = true;
                                newStopOrder.OutsideRth = true;
                                brokerAdapter.PlaceOrder(client, newStopOrder.OrderId, exitContract, newStopOrder);
                                MarkOrderTransmitted();
                                state.StopOrderId = newStopId;
                                Console.WriteLine($"[OK] Conduct V1.2 RECONCILE: stop resized for {state.Symbol}: newOrderId={newStopId} qty={state.FilledQuantity:F4}.");
                            }
                        }
                    }
                    else
                    {
                        // TP1 was cancelled, not filled â€” clear tracking
                        Console.WriteLine($"[WARN] Conduct V1.2 RECONCILE: TP1 order {state.Tp1OrderId} for {state.Symbol} is gone (cancelled, not filled).");
                        state.Tp1OrderId = null;
                    }
                }
            }

            // Check if STP order is still alive â€” repair if missing
            if (state.StopOrderId.HasValue)
            {
                var stopAlive = symbolOrders.Any(o => o.OrderId == state.StopOrderId.Value && !IsTerminalOrderStatus(o.Status));
                if (!stopAlive)
                {
                    // Check if stop was filled (position gone)
                    var stopFilled = _wrapper.Executions.Any(e => e.OrderId == state.StopOrderId.Value);
                    if (stopFilled)
                    {
                        Console.WriteLine($"[INFO] Conduct V1.2 RECONCILE: STP order filled for {state.Symbol}. Position should be flat.");
                        return;
                    }

                    // Stop disappeared â€” repair
                    if (state.ExitsRepairAttempts < cfg.ExitRepairRetries)
                    {
                        state.ExitsRepairAttempts++;
                        Console.WriteLine($"[WARN] Conduct V1.2 RECONCILE: STP order {state.StopOrderId} missing for {state.Symbol}. Repairing (attempt {state.ExitsRepairAttempts}/{cfg.ExitRepairRetries}).");

                        var exitAction = state.IsLong ? "SELL" : "BUY";
                        var exitContract = brokerAdapter.BuildContract(new BrokerContractSpec(
                            BrokerAssetType.Stock, plan.Symbol, "SMART", "USD", _options.PrimaryExchange));
                        var repairStop = brokerAdapter.BuildOrder(new BrokerOrderIntent(
                            exitAction, "STP", state.FilledQuantity, LimitPrice: null));
                        repairStop.AuxPrice = Math.Round(stopPrice, 2);
                        var repairId = await _wrapper.NextValidIdTask;
                        repairStop.OrderId = repairId;
                        repairStop.OrderRef = $"CONDUCT_V1.2_STOP_REPAIR_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                        repairStop.Transmit = true;
                        repairStop.OutsideRth = true;
                        brokerAdapter.PlaceOrder(client, repairStop.OrderId, exitContract, repairStop);
                        MarkOrderTransmitted();
                        state.StopOrderId = repairId;
                        Console.WriteLine($"[OK] Conduct V1.2 RECONCILE: STP repaired for {state.Symbol}: newOrderId={repairId}.");
                    }
                    else
                    {
                        Console.WriteLine($"[ERROR] Conduct V1.2 RECONCILE: STP repair exhausted for {state.Symbol}. Max retries={cfg.ExitRepairRetries}. Position is UNPROTECTED.");
                    }
                }
                else
                {
                    state.ExitsVerifiedCount++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Conduct V1.2 RECONCILE: failed for {state.Symbol}: {ex.Message}");
        }
    }

    /// <summary>
    /// TRADE EPISODE JOURNAL (V1.2): Write a structured JSON record capturing
    /// all details of a completed trade for post-session analysis.
    /// </summary>
    private void WriteConductTradeEpisode(ConductPositionState state, double exitPrice, string reasonCode, int loopIteration)
    {
        var elapsed = DateTime.UtcNow - state.LoopStartUtc;
        var realizedPnlPerShare = state.IsLong
            ? exitPrice - state.EntryPrice
            : state.EntryPrice - exitPrice;
        var realizedPnlUsd = realizedPnlPerShare * state.OriginalFilledQuantity;
        var realizedPnlR = state.RiskPerTradeUsd > 0
            ? realizedPnlUsd / state.RiskPerTradeUsd
            : 0;

        var episode = new ConductTradeEpisode
        {
            TradeId = $"{state.Symbol}_{state.LoopStartUtc:yyyyMMdd_HHmmss}",
            Symbol = state.Symbol,
            Side = state.IsLong ? "LONG" : "SHORT",
            EntryPrice = state.EntryPrice,
            ExitPrice = exitPrice,
            Quantity = state.OriginalFilledQuantity,
            EntryUtc = state.LoopStartUtc,
            ExitUtc = DateTime.UtcNow,
            HoldDurationSec = elapsed.TotalSeconds,
            ExitReason = reasonCode,
            RealizedPnlUsd = realizedPnlUsd,
            RealizedPnlR = realizedPnlR,
            MfeUsd = state.MfeUsd,
            MaeUsd = state.MaeUsd,
            PeakUnrealPnlUsd = state.PeakUnrealPnlUsd,
            RiskPerTradeUsd = state.RiskPerTradeUsd,
            CommissionUsd = state.RoundTripCommission,
            PeakPrice = state.PeakPrice,
            TroughPrice = state.TroughPrice,
            TrailingActivated = state.TrailingActive,
            BreakEvenActivated = state.BreakEvenActive,
            ProfitLocked = state.ProfitLocked,
            Tp1Done = state.Tp1Done,
            FinalFloorPrice = state.FloorPricePerShare,
            Atr1MAtExit = state.Atr1M,
            CandleBarCount = state.CandleBars1M.Count,
            LoopIterations = loopIteration,
            EngineVersion = "V1.2",
        };

        var dir = Path.Combine(EnsureOutputDir(), "trade_episodes");
        Directory.CreateDirectory(dir);
        var fileName = $"{episode.TradeId}.json";
        var filePath = Path.Combine(dir, fileName);
        var jsonOpts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(filePath, System.Text.Json.JsonSerializer.Serialize(episode, jsonOpts));
        Console.WriteLine($"[OK] Conduct V1.2 JOURNAL: trade episode written: {filePath} pnl={realizedPnlUsd:F2} r={realizedPnlR:F2} reason={reasonCode}.");
    }

    private double ResolveEntryCommissionForOrder(int orderId, double quantity)
    {
        var execIds = _wrapper.Executions
            .Where(e => e.OrderId == orderId)
            .Select(e => e.ExecId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (execIds.Count > 0)
        {
            var commission = _wrapper.Commissions
                .Where(c => execIds.Contains(c.ExecId))
                .Select(c => Math.Abs(c.Commission))
                .Sum();
            if (commission > 0)
            {
                return commission;
            }
        }

        return EstimateCommissionPerOrder(quantity);
    }

    private double ResolveEntryCommissionForPosition(string symbol, string entryAction, double quantity)
    {
        var normalizedSymbol = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSymbol) || quantity <= 0)
        {
            return EstimateCommissionPerOrder(quantity);
        }

        var targetQuantity = Math.Abs(quantity);
        var executions = _wrapper.Executions
            .Where(e => string.Equals(e.Symbol, normalizedSymbol, StringComparison.OrdinalIgnoreCase))
            .Where(e => IsExecutionMatchingEntrySide(e.Side, entryAction))
            .Reverse()
            .ToArray();

        if (executions.Length == 0)
        {
            return EstimateCommissionPerOrder(quantity);
        }

        var covered = 0.0;
        var execIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var execution in executions)
        {
            var shares = Math.Abs(execution.Shares);
            if (shares <= 0)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(execution.ExecId))
            {
                execIds.Add(execution.ExecId);
            }

            covered += shares;
            if (covered >= targetQuantity)
            {
                break;
            }
        }

        if (execIds.Count == 0)
        {
            return EstimateCommissionPerOrder(quantity);
        }

        var commission = _wrapper.Commissions
            .Where(c => execIds.Contains(c.ExecId))
            .Select(c => Math.Abs(c.Commission))
            .Sum();

        return commission > 0
            ? commission
            : EstimateCommissionPerOrder(quantity);
    }

    private static bool IsExecutionMatchingEntrySide(string executionSide, string entryAction)
    {
        var side = (executionSide ?? string.Empty).Trim().ToUpperInvariant();
        var action = (entryAction ?? string.Empty).Trim().ToUpperInvariant();

        if (action == "BUY")
        {
            return side is "BOT" or "BUY";
        }

        if (action == "SELL")
        {
            return side is "SLD" or "SELL";
        }

        return false;
    }

    private double EstimateCommissionPerOrder(double quantity)
    {
        var normalizedQuantity = Math.Max(0, quantity);
        var perUnit = Math.Max(0, _options.PreTradeCommissionPerUnit);
        var minPerOrder = Math.Max(0, _options.PreTradeMinCommissionPerOrder);
        var estimate = normalizedQuantity * perUnit;
        if (minPerOrder > 0)
        {
            estimate = Math.Max(estimate, minPerOrder);
        }

        return estimate;
    }

    private double ResolveObservedPriceForExit(string symbol, LiveQuoteSnapshot quoteSnapshot, string action)
    {
        var isShortEntry = string.Equals(action, "SELL", StringComparison.OrdinalIgnoreCase);

        if (!isShortEntry && quoteSnapshot.Bid > 0)
        {
            return quoteSnapshot.Bid;
        }

        if (isShortEntry && quoteSnapshot.Ask > 0)
        {
            return quoteSnapshot.Ask;
        }

        if (quoteSnapshot.Last > 0)
        {
            return quoteSnapshot.Last;
        }

        if (!isShortEntry && quoteSnapshot.Ask > 0)
        {
            return quoteSnapshot.Ask;
        }

        if (isShortEntry && quoteSnapshot.Bid > 0)
        {
            return quoteSnapshot.Bid;
        }

        var scannerInputPath = ResolveLiveScannerInputPath(action);
        if (string.IsNullOrWhiteSpace(scannerInputPath))
        {
            return 0;
        }

        try
        {
            var fullPath = Path.GetFullPath(scannerInputPath);
            if (!File.Exists(fullPath))
            {
                return 0;
            }

            var row = LoadLiveScannerCandidateRows(fullPath)
                .FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                return 0;
            }

            if (row.Bid > 0)
            {
                return row.Bid;
            }

            if (isShortEntry && row.Ask > 0)
            {
                return row.Ask;
            }

            if (row.Mark > 0)
            {
                return row.Mark;
            }

            if (!isShortEntry && row.Ask > 0)
            {
                return row.Ask;
            }

            if (isShortEntry && row.Bid > 0)
            {
                return row.Bid;
            }
        }
        catch
        {
            return 0;
        }

        return 0;
    }

    private async Task RunOrdersCancelSimMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        EnsureSteadyStateForOrderRoute(nameof(RunOrdersCancelSimMode));

        if (!_options.EnableLive)
        {
            throw new InvalidOperationException("Cancel order blocked: set --enable-live true to allow transmission.");
        }

        if (_options.CancelOrderId <= 0)
        {
            throw new InvalidOperationException("Cancel order blocked: set --cancel-order-id to a positive IBKR order id.");
        }

        brokerAdapter.CancelOrder(client, _options.CancelOrderId, string.Empty);
        Console.WriteLine($"[OK] Sim cancel transmitted: orderId={_options.CancelOrderId}");

        await Task.Delay(TimeSpan.FromSeconds(4), token);
        brokerAdapter.RequestOpenOrders(client);
        await AwaitWithTimeout(_wrapper.OpenOrderEndTask, token, "openOrderEnd");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var cancelPath = Path.Combine(outputDir, $"sim_order_cancel_{timestamp}.json");
        var statusPath = Path.Combine(outputDir, $"sim_order_cancel_status_{timestamp}.json");

        var cancellation = new SimOrderCancellationRow(
            timestamp,
            _options.CancelOrderId,
            _options.Account);

        WriteJson(cancelPath, new[] { cancellation });
        WriteJson(statusPath, _wrapper.OrderStatusRows.ToArray());

        Console.WriteLine($"[OK] Sim cancel export: {cancelPath}");
        Console.WriteLine($"[OK] Sim cancel status export: {statusPath} (rows={_wrapper.OrderStatusRows.Count})");
    }

    private LiveOrderPlacementPlan ApplyLiveDefaultLimitFromQuote(LiveOrderPlacementPlan livePlan, LiveQuoteSnapshot quoteSnapshot)
    {
        if (!string.Equals(livePlan.Action, "BUY", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(livePlan.Action, "SELL", StringComparison.OrdinalIgnoreCase))
        {
            return livePlan;
        }

        if (quoteSnapshot.Bid > 0)
        {
            Console.WriteLine($"[INFO] Live default limit set from current bid: {quoteSnapshot.Bid:F4} (action={livePlan.Action}).");
            return livePlan with { LimitPrice = quoteSnapshot.Bid };
        }

        if (string.Equals(livePlan.Action, "BUY", StringComparison.OrdinalIgnoreCase))
        {
            var ask = quoteSnapshot.Ask;
            var last = quoteSnapshot.Last;

            if (ask > 0 && last > 0)
            {
                var syntheticBid = Math.Max(0.0001, Math.Min(last, (2 * last) - ask));
                if (syntheticBid > 0)
                {
                    Console.WriteLine($"[WARN] Live default limit: bid unavailable; using synthetic bid {syntheticBid:F4} from ask={ask:F4} and last={last:F4}.");
                    return livePlan with { LimitPrice = syntheticBid };
                }
            }

            if (last > 0)
            {
                Console.WriteLine($"[WARN] Live default limit: bid unavailable; using last trade {last:F4}.");
                return livePlan with { LimitPrice = last };
            }

            if (ask > 0)
            {
                Console.WriteLine($"[WARN] Live default limit: bid unavailable; using ask fallback {ask:F4}.");
                return livePlan with { LimitPrice = ask };
            }
        }

        Console.WriteLine($"[WARN] Live default limit: no bid quote available; falling back to --live-limit value {livePlan.LimitPrice:F4}.");
        return livePlan;
    }

    private void ValidateLivePriceSanity(LiveOrderPlacementPlan livePlan, LiveQuoteSnapshot quoteSnapshot, string liveOrderType)
    {
        if (!string.Equals(livePlan.Action, "BUY", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var temporaryNoL1L2BypassActive = IsTemporaryNoL1L2BypassActiveUtc(DateTime.UtcNow);

        var ticks = quoteSnapshot.Ticks;
        var bid = quoteSnapshot.Bid;
        var ask = quoteSnapshot.Ask;
        var last = quoteSnapshot.Last;
        var reference = ask > 0 ? ask : last;
        if (reference <= 0)
        {
            if (temporaryNoL1L2BypassActive)
            {
                Console.WriteLine("[WARN] Live price sanity: no L1/L2 reference (ask/last unavailable). Temporarily bypassing quote requirement for 2026-02-26..2026-02-27 UTC.");
                return;
            }

            if (!_options.LivePriceSanityRequireQuote)
            {
                Console.WriteLine("[WARN] Live price sanity: no ask/last reference price returned; continuing because --live-price-sanity-require-quote=false.");
                return;
            }

            throw new InvalidOperationException(
                "Live order blocked: no ask/last reference price returned for market sanity check.");
        }

        if (reference > _options.MaxPrice)
        {
            throw new InvalidOperationException(
                $"Live order blocked: current reference price {reference:F2} exceeds configured max-price {_options.MaxPrice:F2}.");
        }

        if (string.Equals(liveOrderType, "LMT", StringComparison.OrdinalIgnoreCase))
        {
            var minReasonableBuyLimit = Math.Round(reference * 0.8, 4);
            if (livePlan.LimitPrice < minReasonableBuyLimit)
            {
                throw new InvalidOperationException(
                    $"Live order blocked: buy limit {livePlan.LimitPrice:F2} is too far below market reference {reference:F2}. Minimum allowed by sanity gate is {minReasonableBuyLimit:F2}.");
            }
        }

        if (!_options.LiveMomentumGuardEnabled)
        {
            return;
        }

        var lastTradeSeries = ticks
            .Where(t => t.Field == 4 && t.Price > 0)
            .Select(t => t.Price)
            .ToArray();

        if (lastTradeSeries.Length >= 2 && lastTradeSeries[0] > 0)
        {
            var openingLast = lastTradeSeries[0];
            var currentLast = lastTradeSeries[^1];
            var adverseBps = ((openingLast - currentLast) / openingLast) * 10000.0;

            if (adverseBps >= _options.LiveMomentumMaxAdverseBps)
            {
                throw new InvalidOperationException(
                    $"Live order blocked: momentum guard detected short-horizon downside ({adverseBps:F1} bps >= {_options.LiveMomentumMaxAdverseBps:F1} bps). Disable with --live-momentum-guard false if you intentionally want to buy into weakness.");
            }
        }
        else if (bid > 0 && last > 0 && last <= bid)
        {
            throw new InvalidOperationException(
                "Live order blocked: momentum guard detected weak tape (last trade at or below bid). Disable with --live-momentum-guard false if intentional.");
        }
        else
        {
            Console.WriteLine("[WARN] Live momentum guard: insufficient last-trade samples; skipping momentum trend test.");
        }
    }

    private static bool IsTemporaryNoL1L2BypassActiveUtc(DateTime utcNow)
    {
        var date = DateOnly.FromDateTime(utcNow);
        var start = new DateOnly(2026, 2, 26);
        var end = new DateOnly(2026, 2, 27);
        return date >= start && date <= end;
    }

    private async Task<LiveQuoteSnapshot> FetchLiveQuoteSnapshotAsync(EClientSocket client, IBrokerAdapter brokerAdapter, string symbol, int requestId, CancellationToken token)
    {
        var contract = brokerAdapter.BuildContract(new BrokerContractSpec(
            BrokerAssetType.Stock,
            symbol,
            "SMART",
            "USD",
            _options.PrimaryExchange));

        brokerAdapter.RequestMarketDataType(client, _options.MarketDataType);
        brokerAdapter.RequestMarketData(client, requestId, contract);

        try
        {
            var firstTickTask = _wrapper.TopDataFirstTickTask;
            await Task.WhenAny(firstTickTask, Task.Delay(TimeSpan.FromSeconds(3), token));
        }
        finally
        {
            brokerAdapter.CancelMarketData(client, requestId);
        }

        var ticks = _wrapper.TopTicks
            .Where(t => t.TickerId == requestId && t.Kind == "tickPrice")
            .OrderBy(t => t.TimestampUtc)
            .ToArray();

        var bid = ticks.LastOrDefault(t => t.Field == 1)?.Price ?? 0;
        var ask = ticks.LastOrDefault(t => t.Field == 2)?.Price ?? 0;
        var last = ticks.LastOrDefault(t => t.Field == 4)?.Price ?? 0;
        return new LiveQuoteSnapshot(bid, ask, last, ticks);
    }

    private Task<LiveOrderPlacementPlan> ResolveLiveOrderPlacementPlanAsync(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        return ResolveLiveOrderPlacementPlanAsync(client, brokerAdapter, _options.LiveAction, excludedSymbols: null, quantityOverride: null, token);
    }

    private async Task<LiveOrderPlacementPlan> ResolveLiveOrderPlacementPlanAsync(
        EClientSocket client,
        IBrokerAdapter brokerAdapter,
        string actionOverride,
        IReadOnlySet<string>? excludedSymbols,
        double? quantityOverride,
        CancellationToken token)
    {
        var symbol = _options.LiveSymbol;
        var action = actionOverride;
        var quantity = _options.LiveQuantity;
        var limitPrice = _options.LiveLimitPrice;
        var source = "manual";

        if (quantityOverride is > 0)
        {
            quantity = quantityOverride.Value;
        }

        var excluded = excludedSymbols ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var scannerInputPath = ResolveLiveScannerInputPath(action);
        if (string.IsNullOrWhiteSpace(scannerInputPath))
        {
            if (excluded.Contains(symbol))
            {
                throw new InvalidOperationException($"Live handoff blocked: manual symbol '{symbol}' is excluded for replacement.");
            }

            return new LiveOrderPlacementPlan(symbol, action, quantity, limitPrice, source);
        }

        var fullPath = Path.GetFullPath(scannerInputPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Live scanner candidates input not found: {fullPath}");
        }

        var fileName = Path.GetFileName(fullPath);
        if (fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Live handoff blocked: '{fileName}' looks like an Excel lock/temp file. Use the real workbook file, not ~$*.xlsx.");
        }

        if (_options.LiveScannerKillSwitchMaxFileAgeMinutes > 0)
        {
            var ageMinutes = (DateTime.UtcNow - File.GetLastWriteTimeUtc(fullPath)).TotalMinutes;
            if (ageMinutes > _options.LiveScannerKillSwitchMaxFileAgeMinutes)
            {
                _hasPreTradeHalt = true;
                throw new InvalidOperationException(
                    $"Live handoff blocked by kill-switch: scanner candidates file is stale ({ageMinutes:F1}m > {_options.LiveScannerKillSwitchMaxFileAgeMinutes}m).");
            }
        }

        var rows = LoadLiveScannerCandidateRows(fullPath);

        // â”€â”€ V2 scanner selection engine (optional, alongside V1) â”€â”€â”€â”€
        var useV2Scanner = string.Equals(
            Environment.GetEnvironmentVariable("SCN_V2_ENABLED") ?? "false",
            "true", StringComparison.OrdinalIgnoreCase);

        LiveScannerCandidateRow[] selected;
        if (useV2Scanner)
        {
            var v2Config = new Strategy.ScannerSelectionV2Config
            {
                TopN = Math.Max(1, _options.LiveScannerTopN),
                MinFileScore = _options.LiveScannerMinScore,
                MinPrice = 0.50,
                MaxPrice = _options.MaxPrice
            };
            var v2Engine = new Strategy.ScannerSelectionEngineV2(v2Config);
            var fileCandidates = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
                .Select(x => new Strategy.ScannerV2CandidateFileRow
                {
                    Symbol = x.Symbol.Trim().ToUpperInvariant(),
                    WeightedScore = x.WeightedScore,
                    Eligible = x.Eligible is not false,
                    AverageRank = x.AverageRank,
                    Bid = x.Bid,
                    Ask = x.Ask,
                    Mark = x.Mark
                })
                .Where(x => _options.AllowedSymbols.Any(s => string.Equals(s, x.Symbol, StringComparison.OrdinalIgnoreCase)))
                .Where(x => !excluded.Contains(x.Symbol))
                .ToList();

            var marketDataProbeCandidates = fileCandidates
                .OrderByDescending(x => x.WeightedScore)
                .ThenBy(x => x.AverageRank)
                .Take(Math.Max(_options.LiveScannerTopN, _options.LiveScannerTopN * 3))
                .ToList();

            var (sessionOpenUtc, sessionCloseUtc) = ResolveUsEquitiesSessionWindowUtc(DateTime.UtcNow);
            var snapshots = await CaptureScannerV2MarketSnapshotsAsync(client, brokerAdapter, marketDataProbeCandidates, token);
            var biasEntries = LoadLiveScannerV2BiasEntries();
            var v2Snapshot = v2Engine.Evaluate(
                fileCandidates,
                snapshots.L1BySymbol,
                snapshots.L2BySymbol,
                biasEntries,
                sessionOpenUtc,
                sessionCloseUtc,
                DateTime.UtcNow,
                fullPath);

            Console.WriteLine($"[INFO] Live Scanner V2.0: {v2Snapshot.SelectedCount} selected " +
                $"from {v2Snapshot.TotalCandidates} candidates " +
                $"({v2Snapshot.EligibleCandidates} eligible)");

            // Map V2 ranked output back to LiveScannerCandidateRow for downstream compatibility
            var v2SymbolOrder = v2Snapshot.SelectedSymbols
                .Select((sym, idx) => (sym, idx))
                .ToDictionary(x => x.sym, x => x.idx, StringComparer.OrdinalIgnoreCase);
            selected = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
                .Where(x => v2SymbolOrder.ContainsKey(x.Symbol.Trim().ToUpperInvariant()))
                .Select(x => new LiveScannerCandidateRow
                {
                    Symbol = x.Symbol.Trim().ToUpperInvariant(),
                    WeightedScore = x.WeightedScore,
                    Eligible = x.Eligible,
                    AverageRank = x.AverageRank,
                    Bid = x.Bid,
                    Ask = x.Ask,
                    Mark = x.Mark
                })
                .OrderBy(x => v2SymbolOrder.GetValueOrDefault(x.Symbol, int.MaxValue))
                .ToArray();
        }
        else
        {
            selected = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
                .Where(x => x.Eligible is not false)
                .Where(x => x.WeightedScore >= _options.LiveScannerMinScore)
                .Select(x => new LiveScannerCandidateRow
                {
                    Symbol = x.Symbol.Trim().ToUpperInvariant(),
                    WeightedScore = x.WeightedScore,
                    Eligible = x.Eligible,
                    AverageRank = x.AverageRank,
                    Bid = x.Bid,
                    Ask = x.Ask,
                    Mark = x.Mark
                })
                .Where(x => _options.AllowedSymbols.Any(s => string.Equals(s, x.Symbol, StringComparison.OrdinalIgnoreCase)))
                .Where(x => !excluded.Contains(x.Symbol))
                .OrderByDescending(x => x.WeightedScore)
                .ThenBy(x => x.AverageRank)
                .Take(Math.Max(1, _options.LiveScannerTopN))
                .ToArray();
        }

        if (selected.Length == 0)
        {
            throw new InvalidOperationException("Live handoff blocked: no eligible scanner candidates matched allow-list and score threshold.");
        }

        if (selected.Length < Math.Max(1, _options.LiveScannerKillSwitchMinCandidates))
        {
            _hasPreTradeHalt = true;
            throw new InvalidOperationException(
                $"Live handoff blocked by kill-switch: only {selected.Length} eligible candidate(s), below minimum {_options.LiveScannerKillSwitchMinCandidates}.");
        }

        var chosen = selected[0];
        symbol = chosen.Symbol;
        source = "scanner-candidate";

        if (string.Equals(action, "BUY", StringComparison.OrdinalIgnoreCase))
        {
            var scannerLimitHint = 0.0;
            if (chosen.Bid > 0)
            {
                scannerLimitHint = chosen.Bid;
                Console.WriteLine($"[INFO] Live scanner hint: using scanner bid {scannerLimitHint:F4} for BUY limit seed.");
            }
            else if (chosen.Ask > 0 && chosen.Mark > 0)
            {
                scannerLimitHint = Math.Max(0.0001, Math.Min(chosen.Mark, (2 * chosen.Mark) - chosen.Ask));
                Console.WriteLine($"[WARN] Live scanner hint: bid missing; using synthetic scanner bid {scannerLimitHint:F4} from ask={chosen.Ask:F4} mark={chosen.Mark:F4}.");
            }
            else if (chosen.Mark > 0)
            {
                scannerLimitHint = chosen.Mark;
                Console.WriteLine($"[WARN] Live scanner hint: bid missing; using scanner mark {scannerLimitHint:F4} for BUY limit seed.");
            }

            if (scannerLimitHint > 0)
            {
                limitPrice = scannerLimitHint;
            }
        }

        if (_options.LiveAllocationBudget > 0)
        {
            var targetNotional = ResolveTargetNotional(_options.LiveAllocationMode, _options.LiveAllocationBudget, chosen.WeightedScore, selected);

            if (_options.LiveScannerKillSwitchMaxBudgetConcentrationPct > 0)
            {
                var concentration = (_options.LiveAllocationBudget <= 0)
                    ? 0
                    : (targetNotional / _options.LiveAllocationBudget) * 100.0;
                if (concentration > _options.LiveScannerKillSwitchMaxBudgetConcentrationPct)
                {
                    _hasPreTradeHalt = true;
                    throw new InvalidOperationException(
                        $"Live handoff blocked by kill-switch: concentration {concentration:F1}% exceeds {_options.LiveScannerKillSwitchMaxBudgetConcentrationPct:F1}%.");
                }
            }

            if (targetNotional > 0)
            {
                if (limitPrice <= 0)
                {
                    throw new InvalidOperationException("Live handoff blocked: --live-limit must be > 0 when live allocation budget sizing is enabled.");
                }

                quantity = Math.Floor(targetNotional / limitPrice);
                if (quantity <= 0)
                {
                    quantity = 1;
                }
            }
        }

        return new LiveOrderPlacementPlan(symbol, action, quantity, limitPrice, source);
    }

    private string ResolveLiveScannerInputPath(string action)
    {
        if (!string.IsNullOrWhiteSpace(_options.LiveScannerCandidatesInputPath))
        {
            return _options.LiveScannerCandidatesInputPath;
        }

        if (string.IsNullOrWhiteSpace(_options.LiveScannerOpenPhaseInputPath)
            && string.IsNullOrWhiteSpace(_options.LiveScannerPostOpenGainersInputPath)
            && string.IsNullOrWhiteSpace(_options.LiveScannerPostOpenLosersInputPath))
        {
            return string.Empty;
        }

        var calendar = new UsEquitiesExchangeCalendarService();
        if (!calendar.TryGetSessionWindowUtc("US-EQUITIES", DateTime.UtcNow, out var session)
            || !session.IsTradingDay)
        {
            return string.IsNullOrWhiteSpace(_options.LiveScannerOpenPhaseInputPath)
                ? _options.LiveScannerPostOpenGainersInputPath
                : _options.LiveScannerOpenPhaseInputPath;
        }

        var nowUtc = DateTime.UtcNow;
        var openPhaseEnd = session.SessionOpenUtc.AddMinutes(_options.LiveScannerOpenPhaseMinutes);
        var postOpenEnd = openPhaseEnd.AddMinutes(_options.LiveScannerPostOpenMinutes);

        if (nowUtc >= session.SessionOpenUtc && nowUtc < openPhaseEnd)
        {
            return _options.LiveScannerOpenPhaseInputPath;
        }

        if (nowUtc >= openPhaseEnd && nowUtc < postOpenEnd)
        {
            if (string.Equals(action, "SELL", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(_options.LiveScannerPostOpenLosersInputPath)
                    ? _options.LiveScannerPostOpenLosersInputPath
                    : _options.LiveScannerPostOpenGainersInputPath;
            }

            return !string.IsNullOrWhiteSpace(_options.LiveScannerPostOpenGainersInputPath)
                ? _options.LiveScannerPostOpenGainersInputPath
                : _options.LiveScannerPostOpenLosersInputPath;
        }

        return string.Empty;
    }

    private static LiveScannerCandidateRow[] LoadLiveScannerCandidateRows(string fullPath)
    {
        var extension = Path.GetExtension(fullPath);
        if (string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return LoadLiveScannerCandidateRowsFromExcel(fullPath);
        }

        return JsonSerializer.Deserialize<LiveScannerCandidateRow[]>(File.ReadAllText(fullPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
    }

    private static LiveScannerCandidateRow[] LoadLiveScannerCandidateRowsFromExcel(string fullPath)
    {
        using var sourceStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memoryStream = new MemoryStream();
        sourceStream.CopyTo(memoryStream);
        memoryStream.Position = 0;
        using var workbook = new XLWorkbook(memoryStream);
        var worksheet = workbook.Worksheets.First();
        var usedRange = worksheet.RangeUsed();
        if (usedRange is null)
        {
            return [];
        }

        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headerRow = usedRange.RowsUsed()
            .FirstOrDefault(r => r.CellsUsed().Any(c => string.Equals(c.GetString().Trim(), "Symbol", StringComparison.OrdinalIgnoreCase)))
            ?? usedRange.FirstRowUsed();
        foreach (var cell in headerRow.CellsUsed())
        {
            var key = cell.GetString().Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                headerMap[key] = cell.Address.ColumnNumber;
            }
        }

        var symbolColumn = ResolveHeaderColumn(headerMap, "Symbol", fallbackColumn: 1);
        var scoreColumn = ResolveHeaderColumn(headerMap, "WeightedScore", fallbackColumn: 2, alternateHeaders: ["Score", "Weighted Score"]);
        var eligibleColumn = ResolveHeaderColumn(headerMap, "Eligible", fallbackColumn: 3);
        var rankColumn = ResolveHeaderColumn(headerMap, "AverageRank", fallbackColumn: 4, alternateHeaders: ["AvgRank", "Average Rank"]);
        var bidColumn = ResolveHeaderColumn(headerMap, "Bid", fallbackColumn: 7, alternateHeaders: ["Bid Price"]);
        var askColumn = ResolveHeaderColumn(headerMap, "Ask", fallbackColumn: 8, alternateHeaders: ["Ask Price"]);
        var markColumn = ResolveHeaderColumn(headerMap, "Mark", fallbackColumn: 3, alternateHeaders: ["Last", "Price", "Mark Price", "Mark %Change"]);

        var rows = new List<LiveScannerCandidateRow>();
        foreach (var row in usedRange.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()))
        {
            var symbol = row.Cell(symbolColumn).GetString().Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            var weightedScore = TryReadDouble(row.Cell(scoreColumn));
            var eligible = TryReadBoolean(row.Cell(eligibleColumn));
            var averageRank = TryReadDouble(row.Cell(rankColumn));
            var bid = TryReadDouble(row.Cell(bidColumn));
            var ask = TryReadDouble(row.Cell(askColumn));
            var mark = TryReadDouble(row.Cell(markColumn));

            rows.Add(new LiveScannerCandidateRow
            {
                Symbol = symbol,
                WeightedScore = weightedScore,
                Eligible = eligible,
                AverageRank = averageRank,
                Bid = bid,
                Ask = ask,
                Mark = mark
            });
        }

        return rows.ToArray();
    }

    private static int ResolveHeaderColumn(Dictionary<string, int> headerMap, string primaryHeader, int fallbackColumn, string[]? alternateHeaders = null)
    {
        if (headerMap.TryGetValue(primaryHeader, out var column))
        {
            return column;
        }

        if (alternateHeaders is not null)
        {
            foreach (var alternate in alternateHeaders)
            {
                if (headerMap.TryGetValue(alternate, out column))
                {
                    return column;
                }
            }
        }

        return fallbackColumn;
    }

    private static double TryReadDouble(IXLCell cell)
    {
        if (cell.TryGetValue<double>(out var value))
        {
            return value;
        }

        var text = cell.GetString().Trim();
        return double.TryParse(text, out value) ? value : 0;
    }

    private static bool? TryReadBoolean(IXLCell cell)
    {
        if (cell.TryGetValue<bool>(out var boolValue))
        {
            return boolValue;
        }

        var text = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (bool.TryParse(text, out var parsed))
        {
            return parsed;
        }

        return text.ToUpperInvariant() switch
        {
            "1" or "Y" or "YES" or "TRUE" => true,
            "0" or "N" or "NO" or "FALSE" => false,
            _ => null
        };
    }

    private (DateTime SessionOpenUtc, DateTime SessionCloseUtc) ResolveUsEquitiesSessionWindowUtc(DateTime nowUtc)
    {
        var calendar = new UsEquitiesExchangeCalendarService();
        if (!calendar.TryGetSessionWindowUtc("US-EQUITIES", nowUtc, out var session)
            || !session.IsTradingDay)
        {
            return (DateTime.MinValue, DateTime.MinValue);
        }

        return (session.SessionOpenUtc, session.SessionCloseUtc);
    }

    private IReadOnlyList<Strategy.ScannerV2SymbolBias> LoadLiveScannerV2BiasEntries()
    {
        var configuredPath = Environment.GetEnvironmentVariable("SCN_V2_BIAS_STORE_PATH") ?? string.Empty;
        var outputPath = Path.Combine(EnsureOutputDir(), "strategy_replay_self_learning_v2_bias_store.json");
        var fallbackPath = Path.Combine(Directory.GetCurrentDirectory(), "temp", "scanner_v2_bias_store.json");
        var pathCandidates = new[] { configuredPath, outputPath, fallbackPath }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var candidate in pathCandidates)
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                var store = LoadSelfLearningV2BiasStore(fullPath);
                if (store is null || store.Count == 0)
                {
                    continue;
                }

                return store.Values
                    .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
                    .Select(x => new Strategy.ScannerV2SymbolBias(
                        Symbol: x.Symbol.Trim().ToUpperInvariant(),
                        Bias: x.Bias,
                        Confidence: x.Confidence,
                        ScannerScoreShift: x.Bias * x.Confidence * 10.0))
                    .ToArray();
            }
            catch
            {
            }
        }

        return Array.Empty<Strategy.ScannerV2SymbolBias>();
    }

    private async Task<(Dictionary<string, Strategy.ScannerV2L1Snapshot> L1BySymbol, Dictionary<string, Strategy.ScannerV2L2DepthSnapshot> L2BySymbol)> CaptureScannerV2MarketSnapshotsAsync(
        EClientSocket client,
        IBrokerAdapter brokerAdapter,
        IReadOnlyList<Strategy.ScannerV2CandidateFileRow> candidates,
        CancellationToken token)
    {
        var l1BySymbol = new Dictionary<string, Strategy.ScannerV2L1Snapshot>(StringComparer.OrdinalIgnoreCase);
        var l2BySymbol = new Dictionary<string, Strategy.ScannerV2L2DepthSnapshot>(StringComparer.OrdinalIgnoreCase);
        if (candidates.Count == 0)
        {
            return (l1BySymbol, l2BySymbol);
        }

        var reqBase = 45000 + (int)(DateTime.UtcNow.Ticks % 10000);
        brokerAdapter.RequestMarketDataType(client, _options.MarketDataType);

        for (var i = 0; i < candidates.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var symbol = candidates[i].Symbol;
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            var topReqId = reqBase + (i * 2);
            var depthReqId = topReqId + 1;
            var topContract = brokerAdapter.BuildContract(new BrokerContractSpec(
                BrokerAssetType.Stock,
                symbol,
                "SMART",
                "USD",
                _options.PrimaryExchange));
            var depthContract = brokerAdapter.BuildContract(new BrokerContractSpec(
                BrokerAssetType.Stock,
                symbol,
                _options.DepthExchange,
                "USD",
                _options.PrimaryExchange));

            brokerAdapter.RequestMarketData(client, topReqId, topContract);
            brokerAdapter.RequestMarketDepth(client, depthReqId, depthContract, _options.DepthRows, isSmartDepth: false);

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(700), token);
            }
            finally
            {
                brokerAdapter.CancelMarketData(client, topReqId);
                brokerAdapter.CancelMarketDepth(client, depthReqId, isSmartDepth: false);
            }

            var ticks = _wrapper.TopTicks
                .Where(t => t.TickerId == topReqId)
                .OrderBy(t => t.TimestampUtc)
                .ToArray();
            var bid = ticks.LastOrDefault(t => t.Kind == "tickPrice" && t.Field == 1)?.Price ?? 0;
            var ask = ticks.LastOrDefault(t => t.Kind == "tickPrice" && t.Field == 2)?.Price ?? 0;
            var last = ticks.LastOrDefault(t => t.Kind == "tickPrice" && t.Field == 4)?.Price ?? 0;
            var volume = ticks
                .Where(t => t.Kind == "tickSize" && t.Field == 8)
                .Select(t => (double)Math.Max(0, t.Size))
                .LastOrDefault();

            l1BySymbol[symbol] = new Strategy.ScannerV2L1Snapshot(
                Symbol: symbol,
                Bid: bid,
                Ask: ask,
                Last: last,
                Volume: volume,
                TimestampUtc: DateTime.UtcNow);

            var depthRows = _wrapper.DepthRows
                .Where(d => d.TickerId == depthReqId)
                .OrderByDescending(d => d.TimestampUtc)
                .ToArray();
            var latestByLevel = depthRows
                .GroupBy(d => (d.Side, d.Position))
                .Select(g => g.OrderByDescending(x => x.TimestampUtc).First())
                .ToArray();
            var bidLevels = latestByLevel
                .Where(d => d.Side == 1 && d.Price > 0 && d.Size > 0)
                .OrderBy(d => d.Position)
                .Take(Math.Max(1, _options.DepthRows))
                .Select(d => new Strategy.ScannerV2DepthLevel(d.Position, d.Price, d.Size))
                .ToArray();
            var askLevels = latestByLevel
                .Where(d => d.Side == 0 && d.Price > 0 && d.Size > 0)
                .OrderBy(d => d.Position)
                .Take(Math.Max(1, _options.DepthRows))
                .Select(d => new Strategy.ScannerV2DepthLevel(d.Position, d.Price, d.Size))
                .ToArray();

            l2BySymbol[symbol] = new Strategy.ScannerV2L2DepthSnapshot(
                Symbol: symbol,
                BidLevels: bidLevels,
                AskLevels: askLevels,
                TimestampUtc: DateTime.UtcNow);
        }

        return (l1BySymbol, l2BySymbol);
    }

    private static double ResolveTargetNotional(string mode, double budget, double selectedScore, IReadOnlyList<LiveScannerCandidateRow> selected)
    {
        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? "manual" : mode.Trim().ToLowerInvariant();
        return normalizedMode switch
        {
            "equal" => budget / Math.Max(1, selected.Count),
            "score" => ResolveScoreWeightedNotional(budget, selectedScore, selected),
            _ => budget
        };
    }

    private static double ResolveScoreWeightedNotional(double budget, double selectedScore, IReadOnlyList<LiveScannerCandidateRow> selected)
    {
        var totalScore = selected.Sum(x => Math.Max(0, x.WeightedScore));
        if (totalScore <= 0)
        {
            return budget / Math.Max(1, selected.Count);
        }

        var share = Math.Max(0, selectedScore) / totalScore;
        return budget * share;
    }

}