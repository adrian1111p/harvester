using System.Text.Json;
using System.Text.Json.Serialization;
using Harvester.App.Backtest.Engine;
using Harvester.App.IBKR.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harvester.App.Strategy;

/// <summary>
/// V3Live strategy runtime — refactored to implement live order flow and continuous
/// position monitoring ("backside monitoring") using L1, L2, and MTF candle data.
///
/// Architecture:
///   OnDataAsync (called ~1/sec per symbol by SnapshotRuntime)
///     1. Sync position state from IBKR positions snapshot
///     2. Update candle aggregator with tick and bar data
///     3. Build features (L1 + L2 + indicators from historical bars)
///     4. Monitor existing positions — evaluate exit conditions every tick
///     5. If no open position: evaluate entry signals through gates and risk guard
///     6. Accepted entries and exits are queued as LiveOrderIntents for SnapshotRuntime
///
/// Implements <see cref="ILiveOrderSignalSource"/> so SnapshotRuntime can consume
/// queued order intents and transmit them to IBKR, then acknowledge back.
/// </summary>
public sealed class V3LiveRuntime : IStrategyRuntime, ILiveOrderSignalSource
{
    // ── Core config & context ──────────────────────────────────────────────
    private readonly V3LiveConfig _config;
    private StrategyRuntimeContext? _context;
    private bool _closeOnly;

    // ── Sub-engines ────────────────────────────────────────────────────────
    private readonly V3LiveFeatureBuilder _featureBuilder = new();
    private readonly V3LiveSignalEngine _signalEngine = new();
    private readonly V3LiveRiskGuard _riskGuard;
    private readonly V3LiveOrderBridge _orderBridge;
    private readonly ScannerSelectionEngineV2 _scannerV2Engine;

    // ── New components (audit fixes) ───────────────────────────────────────
    private readonly V3LiveCandleAggregator _candleAggregator = new();
    private readonly V3LivePositionTracker _positionTracker = new();
    private readonly V3LivePositionMonitor _positionMonitor;
    private readonly ILogger<V3LiveRuntime> _logger;

    // ── Per-symbol state ───────────────────────────────────────────────────
    private readonly Dictionary<string, V3LiveSymbolState> _stateBySymbol = new(StringComparer.OrdinalIgnoreCase);

    // ── Live order intent queue (consumed by SnapshotRuntime via ILiveOrderSignalSource) ──
    private readonly List<LiveOrderIntent> _pendingIntents = [];
    private readonly object _intentLock = new();

    // ── Logging / export ───────────────────────────────────────────────────
    private readonly List<V3LiveEvaluationRow> _evaluations = [];
    private readonly List<V3LiveSignalRow> _signals = [];
    private readonly List<V3LiveExitEventRow> _exitEvents = [];
    private readonly List<V3LiveRiskEventRow> _riskEvents = [];
    private readonly object _eventsLock = new();

    public V3LiveRuntime(V3LiveConfig? config = null, ILogger<V3LiveRuntime>? logger = null)
    {
        _config = config ?? V3LiveConfig.FromEnvironment();
        _scannerV2Engine = new ScannerSelectionEngineV2(new ScannerSelectionV2Config
        {
            TopN = 1,
            MinFileScore = _config.ScannerMinCompositeScore,
            MaxSpreadPct = _config.MaxSpreadPct,
            MinBidDepthShares = _config.MinDepthPerSideShares,
            MinAskDepthShares = _config.MinDepthPerSideShares,
            DepthLevels = _config.DepthLevels
        });
        _riskGuard = new V3LiveRiskGuard(_config);
        _orderBridge = new V3LiveOrderBridge(_config);
        _positionMonitor = new V3LivePositionMonitor(_config);
        _logger = logger ?? NullLogger<V3LiveRuntime>.Instance;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  IStrategyRuntime
    // ════════════════════════════════════════════════════════════════════════

    public Task InitializeAsync(StrategyRuntimeContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _closeOnly = false;
        lock (_eventsLock)
        {
            _evaluations.Clear();
            _signals.Clear();
            _exitEvents.Clear();
            _riskEvents.Clear();
        }
        _stateBySymbol.Clear();

        lock (_intentLock) _pendingIntents.Clear();

        foreach (var symbol in _config.Symbols)
        {
            _stateBySymbol[symbol] = new V3LiveSymbolState();
        }

        _logger.LogInformation("V3Live initialized symbols={Symbols} account={Account}", string.Join(",", _config.Symbols), context.Account);
        return Task.CompletedTask;
    }

    public Task OnScheduledEventAsync(string eventName, StrategyRuntimeContext context, CancellationToken cancellationToken)
    {
        if (string.Equals(eventName, "market-close-warning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventName, "pre-close", StringComparison.OrdinalIgnoreCase))
        {
            _closeOnly = true;
            _logger.LogInformation("V3Live close-only mode activated");
        }

        if (string.Equals(eventName, "mode-start", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventName, "session-open-reset", StringComparison.OrdinalIgnoreCase))
        {
            _closeOnly = false;
            _positionTracker.ResetDaily();
            _signalEngine.ResetState();
            foreach (var state in _stateBySymbol.Values)
            {
                state.EntriesToday = 0;
                state.LastSignalUtc = null;
            }
            _logger.LogInformation("V3Live session reset; daily counters cleared");
        }

        return Task.CompletedTask;
    }

    public Task OnDataAsync(StrategyDataSlice dataSlice, CancellationToken cancellationToken)
    {
        var symbol = ResolveSymbol(dataSlice);
        if (string.IsNullOrWhiteSpace(symbol))
            return Task.CompletedTask;

        if (!_stateBySymbol.TryGetValue(symbol, out var symbolState))
        {
            symbolState = new V3LiveSymbolState();
            _stateBySymbol[symbol] = symbolState;
        }

        var now = dataSlice.TimestampUtc;

        // ── Step 1: Sync positions from IBKR ──
        var account = _context?.Account ?? string.Empty;
        _positionTracker.SyncFromPositions(dataSlice.Positions, account);

        // ── Step 2: Update candle aggregator (L1 ticks + historical bars) ──
        UpdateCandleAggregator(symbol, dataSlice);

        // ── Step 3: Build features (L1 + L2 + indicators) ──
        var features = _featureBuilder.Build(dataSlice, _config.DepthLevels);
        var l1 = features.L1;
        var l2 = features.L2;

        // Update mark price for position tracker
        if (features.Price > 0)
            _positionTracker.UpdateMarkPrice(symbol, features.Price, now);

        // ── Step 4: Monitor open positions (backside monitoring) ──
        var hasOpenPosition = _positionTracker.HasOpenPosition(symbol);
        if (hasOpenPosition)
        {
            EvaluateExitMonitoring(symbol, dataSlice, features, now);
        }

        // ── Step 5: Entry signal evaluation (only if no open position) ──
        if (!hasOpenPosition)
        {
            EvaluateEntrySignal(symbol, symbolState, dataSlice, features, now);
        }
        else
        {
            // Record evaluation row for monitoring ticks (no entry attempt)
            lock (_eventsLock)
            {
                _evaluations.Add(new V3LiveEvaluationRow(
                    TimestampUtc: now,
                    Symbol: symbol,
                    PassedPreTradeGates: false,
                    EmittedSignal: false,
                    SignalSide: string.Empty,
                    SignalSetup: string.Empty,
                    OrderIntentAccepted: false,
                    OrderIntentId: string.Empty,
                    OrderIntentRejectReason: "position-open-monitoring",
                    ReasonCodes: ["position-open"],
                    Bid: l1.Bid,
                    Ask: l1.Ask,
                    Last: l1.Last,
                    SpreadPct: l1.SpreadPct,
                    BidDepthN: l2.BidDepthN,
                    AskDepthN: l2.AskDepthN,
                    ImbalanceRatio: l2.ImbalanceRatio,
                    OfiSignal: features.OfiSignal,
                    DistFromVwapAtr: features.DistFromVwapAtr,
                    Rsi14: features.Rsi14,
                    BbPctB: features.BbPctB,
                    EntriesToday: symbolState.EntriesToday,
                    CloseOnlyMode: _closeOnly));
            }
        }

        return Task.CompletedTask;
    }

    public async Task OnShutdownAsync(StrategyRuntimeContext context, int exitCode, CancellationToken cancellationToken)
    {
        var outputDir = string.IsNullOrWhiteSpace(context.OutputDirectory)
            ? Path.Combine(Directory.GetCurrentDirectory(), "exports")
            : context.OutputDirectory;

        Directory.CreateDirectory(outputDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        // Build position summary from tracker
        var positionSummaries = _positionTracker.Positions.Values.Select(p => new
        {
            p.Symbol,
            p.Side,
            p.Quantity,
            p.EntryPrice,
            p.LastMarkPrice,
            p.UnrealizedPnl,
            p.RealizedPnl,
            p.PeakPriceSinceEntry,
            p.TroughPriceSinceEntry,
            p.IsFlat
        }).ToArray();

        V3LiveEvaluationRow[] evaluationsSnapshot;
        V3LiveSignalRow[] signalsSnapshot;
        V3LiveExitEventRow[] exitEventsSnapshot;
        V3LiveRiskEventRow[] riskEventsSnapshot;
        int evaluationCount;
        int passedCount;
        int failedCount;
        int exitEventCount;

        lock (_eventsLock)
        {
            evaluationsSnapshot = _evaluations.ToArray();
            signalsSnapshot = _signals.ToArray();
            exitEventsSnapshot = _exitEvents.ToArray();
            riskEventsSnapshot = _riskEvents.ToArray();
            evaluationCount = evaluationsSnapshot.Length;
            passedCount = evaluationsSnapshot.Count(x => x.PassedPreTradeGates);
            failedCount = evaluationCount - passedCount;
            exitEventCount = exitEventsSnapshot.Length;
        }

        var summary = new V3LiveRuntimeSummary(
            TimestampUtc: DateTime.UtcNow,
            ExitCode: exitCode,
            Symbols: _config.Symbols,
            Evaluations: evaluationCount,
            Passed: passedCount,
            Failed: failedCount,
            ExitEvents: exitEventCount,
            RealizedPnlToday: _positionTracker.TotalRealizedPnlToday,
            FilledOrdersToday: _positionTracker.TotalFilledOrdersToday,
            Config: _config);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, $"v3live_runtime_summary_{stamp}.json"),
            JsonSerializer.Serialize(summary, options),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, $"v3live_evaluations_{stamp}.json"),
            JsonSerializer.Serialize(evaluationsSnapshot, options),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, $"v3live_signals_{stamp}.json"),
            JsonSerializer.Serialize(signalsSnapshot, options),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, $"v3live_exit_events_{stamp}.json"),
            JsonSerializer.Serialize(exitEventsSnapshot, options),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, $"v3live_risk_events_{stamp}.json"),
            JsonSerializer.Serialize(riskEventsSnapshot, options),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, $"v3live_positions_{stamp}.json"),
            JsonSerializer.Serialize(positionSummaries, options),
            cancellationToken);

        _logger.LogInformation("V3Live shutdown evaluations={Evaluations} signals={Signals} exits={Exits} pnl={Pnl}", evaluationCount, signalsSnapshot.Length, exitEventCount, _positionTracker.TotalRealizedPnlToday);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ILiveOrderSignalSource (C-01 FIX: wires intents to SnapshotRuntime)
    // ════════════════════════════════════════════════════════════════════════

    public IReadOnlyList<LiveOrderIntent> ConsumeOrderIntents()
    {
        lock (_intentLock)
        {
            if (_pendingIntents.Count == 0)
                return Array.Empty<LiveOrderIntent>();

            var snapshot = _pendingIntents.ToArray();
            _pendingIntents.Clear();
            return snapshot;
        }
    }

    public void AcknowledgeOrderTransmitted(string intentId, string symbol, double filledQuantity, double fillPrice)
    {
        _logger.LogInformation("V3Live order transmitted intentId={IntentId} symbol={Symbol} quantity={Quantity} fillPrice={FillPrice}", intentId, symbol, filledQuantity, fillPrice);

        // Find the matching proposed order to get risk/side info
        var side = "LONG"; // default
        var estimatedRisk = 0.0;
        if (_stateBySymbol.TryGetValue(symbol, out var symbolState) && symbolState.LastProposedOrder is not null)
        {
            side = symbolState.LastProposedOrder.Side == "BUY" ? "LONG" : "SHORT";
            estimatedRisk = symbolState.LastProposedOrder.EstimatedRiskDollars;
        }

        _positionTracker.RecordEntry(symbol, side, filledQuantity, fillPrice, estimatedRisk, intentId);

        // Update tracked position's stop/TP from the proposed order
        var tracked = _positionTracker.GetPosition(symbol);
        if (tracked is not null && symbolState?.LastProposedOrder is not null)
        {
            tracked.StopPrice = symbolState.LastProposedOrder.StopPrice;
            tracked.TakeProfitPrice = symbolState.LastProposedOrder.TakeProfitPrice;
        }
    }

    public void AcknowledgePositionClosed(string symbol, double closedQuantity, double realizedPnl)
    {
        _logger.LogInformation("V3Live position closed symbol={Symbol} quantity={Quantity} realizedPnl={RealizedPnl}", symbol, closedQuantity, realizedPnl);
        _positionTracker.RecordClose(symbol, closedQuantity, realizedPnl);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Backside Monitoring — exit evaluation on every data tick
    // ════════════════════════════════════════════════════════════════════════

    private void EvaluateExitMonitoring(string symbol, StrategyDataSlice dataSlice, V3LiveFeatureSnapshot features, DateTime now)
    {
        var position = _positionTracker.GetPosition(symbol);
        if (position is null || position.IsFlat)
            return;

        // Get MTF alignment from our own candle aggregator (not ReplayMtfCandleSignalEngine)
        var mtfAlignment = _candleAggregator.GetMtfAlignment(symbol);
        var candleSnapshot = _candleAggregator.GetSnapshot(symbol);

        // Evaluate all 11 exit conditions
        var exitDecision = _positionMonitor.Evaluate(
            position,
            features,
            mtfAlignment,
            candleSnapshot,
            _closeOnly,
            now);

        if (exitDecision.ShouldExit)
        {
            _logger.LogInformation("V3Live exit signal symbol={Symbol} reason={Reason} detail={Detail}", symbol, exitDecision.Reason, exitDecision.Detail);

            // Build exit order intent
            var exitSide = position.Side == "LONG" ? "SELL" : "BUY";
            var exitQty = Math.Abs(position.Quantity);
            var exitPrice = position.Side == "LONG"
                ? (features.L1.HasQuote ? features.L1.Bid : features.Price)
                : (features.L1.HasQuote ? features.L1.Ask : features.Price);

            var exitIntent = new LiveOrderIntent(
                IntentId: $"V3EXIT-{symbol}-{now:yyyyMMddHHmmssfff}",
                TimestampUtc: now,
                Symbol: symbol,
                Side: exitSide,
                OrderType: "MKT",
                TimeInForce: "IOC",
                Quantity: (int)exitQty,
                EntryPrice: exitPrice,
                StopPrice: 0,
                TakeProfitPrice: 0,
                EstimatedRiskDollars: 0,
                Setup: $"EXIT:{exitDecision.Reason}",
                Source: "v3-live-exit-monitor");

            EnqueueIntent(exitIntent);

            lock (_eventsLock)
            {
                _exitEvents.Add(new V3LiveExitEventRow(
                    TimestampUtc: now,
                    Symbol: symbol,
                    Side: exitSide,
                    Quantity: (int)exitQty,
                    ExitPrice: exitPrice,
                    Reason: exitDecision.Reason,
                    Detail: exitDecision.Detail,
                    UnrealizedPnl: position.UnrealizedPnl,
                    UnrealizedPnlPeak: position.UnrealizedPnlPeak,
                    HoldSeconds: (now - position.EntryUtc).TotalSeconds,
                    EntryPrice: position.EntryPrice));
            }
        }
        else if (exitDecision.IsAdvisory)
        {
            // Log advisory but don't exit
            _logger.LogInformation("V3Live advisory symbol={Symbol} reason={Reason} detail={Detail}", symbol, exitDecision.Reason, exitDecision.Detail);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Entry signal evaluation
    // ════════════════════════════════════════════════════════════════════════

    private void EvaluateEntrySignal(string symbol, V3LiveSymbolState symbolState, StrategyDataSlice dataSlice, V3LiveFeatureSnapshot features, DateTime now)
    {
        var l1 = features.L1;
        var l2 = features.L2;
        var reasonCodes = new List<string>();

        // ── Pre-trade gates ──
        if (_closeOnly)
            reasonCodes.Add("close-only-mode");

        if (!IsWithinSession(now))
            reasonCodes.Add("outside-session");

        if (symbolState.EntriesToday >= _config.MaxEntriesPerSymbolPerDay)
            reasonCodes.Add("max-entries-reached");

        if (symbolState.LastSignalUtc.HasValue && (now - symbolState.LastSignalUtc.Value).TotalSeconds < _config.CooldownSeconds)
            reasonCodes.Add("cooldown-active");

        if (!l1.HasQuote)
        {
            reasonCodes.Add("l1-missing-quote");
        }
        else
        {
            if (l1.SpreadPct > _config.MaxSpreadPct)
                reasonCodes.Add("l1-spread-too-wide");
            if (l1.BidSize < _config.MinTopQuoteSize || l1.AskSize < _config.MinTopQuoteSize)
                reasonCodes.Add("l1-size-too-small");
            if ((now - l1.TimestampUtc).TotalSeconds > _config.MaxQuoteStalenessSeconds)
                reasonCodes.Add("l1-stale");
        }

        if (_config.RequireL2Depth)
        {
            if (!l2.HasDepth)
            {
                reasonCodes.Add("l2-missing");
            }
            else
            {
                if (l2.BidDepthN < _config.MinDepthPerSideShares)
                    reasonCodes.Add("l2-bid-depth-thin");
                if (l2.AskDepthN < _config.MinDepthPerSideShares)
                    reasonCodes.Add("l2-ask-depth-thin");
                if (l2.ImbalanceRatio is > 0 and < 0.25)
                    reasonCodes.Add("l2-imbalance-extreme");
            }
        }

        if (_config.UseScannerSelectionV2Gate)
        {
            var scannerGate = EvaluateScannerV2Gate(symbol, dataSlice, features, now);
            if (!scannerGate.Passed)
                reasonCodes.Add(scannerGate.ReasonCode);
        }

        // ── Signal evaluation ──
        var decision = _signalEngine.Evaluate(features, _config, symbol);

        // ── MTF confirmation (uses our own candle aggregator for live) ──
        if (_config.RequireMtfConfirmation && decision.HasSignal && decision.Side.HasValue)
        {
            var mtfAlignment = _candleAggregator.GetMtfAlignment(symbol);
            if (!mtfAlignment.HasAllTimeframes)
            {
                if (!_config.AllowMtfUnready)
                    reasonCodes.Add("mtf-unready");
            }
            else
            {
                var mtfAligned = decision.Side == TradeSide.Long
                    ? mtfAlignment.AllBullish || mtfAlignment.ShortTermBullish
                    : mtfAlignment.AllBearish || mtfAlignment.ShortTermBearish;
                if (!mtfAligned)
                    reasonCodes.Add(decision.Side == TradeSide.Long ? "mtf-not-bullish" : "mtf-not-bearish");
            }
        }

        var passedPreTrade = reasonCodes.Count == 0;
        if (!decision.HasSignal && !string.IsNullOrWhiteSpace(decision.Reason))
            reasonCodes.Add(decision.Reason);

        var emittedSignal = passedPreTrade && decision.HasSignal;
        var orderIntentAccepted = false;
        var orderIntentId = string.Empty;
        var orderIntentRejectReason = string.Empty;

        if (emittedSignal && decision.Side is { } signalSide)
        {
            symbolState.LastSignalUtc = now;
            symbolState.EntriesToday++;

            lock (_eventsLock)
            {
                _signals.Add(new V3LiveSignalRow(
                    TimestampUtc: now,
                    Symbol: symbol,
                    Side: signalSide.ToString(),
                    Setup: decision.Setup,
                    Price: features.Price,
                    Atr14: features.Atr14,
                    Rsi14: features.Rsi14,
                    DistFromVwapAtr: features.DistFromVwapAtr,
                    BbPctB: features.BbPctB,
                    ImbalanceRatio: features.L2.ImbalanceRatio,
                    OfiSignal: features.OfiSignal,
                    SpreadPct: features.L1.SpreadPct));
            }

            if (_config.EmitOrderIntents)
            {
                var proposed = _orderBridge.BuildOrder(symbol, now, signalSide, features, decision.Setup);
                if (proposed is null)
                {
                    orderIntentRejectReason = "order-bridge-null";
                    lock (_eventsLock)
                    {
                        _riskEvents.Add(new V3LiveRiskEventRow(now, symbol, "order-bridge-null", "bridge-failed", 0.0, 0.0));
                    }
                }
                else
                {
                    // C-02/C-03 FIX: risk state from live position tracker
                    var riskState = _positionTracker.BuildRiskState(symbol);
                    riskState.HasOpenPosition = _positionTracker.HasOpenPosition(symbol);

                    var check = _riskGuard.Evaluate(symbol, now, features, riskState, proposed);

                    if (check.Passed)
                    {
                        orderIntentAccepted = true;
                        orderIntentId = proposed.IntentId;
                        symbolState.LastProposedOrder = proposed;

                        // C-01 FIX: enqueue as LiveOrderIntent for SnapshotRuntime consumption
                        var liveIntent = new LiveOrderIntent(
                            IntentId: proposed.IntentId,
                            TimestampUtc: proposed.TimestampUtc,
                            Symbol: proposed.Symbol,
                            Side: proposed.Side,
                            OrderType: proposed.OrderType,
                            TimeInForce: proposed.TimeInForce,
                            Quantity: proposed.Quantity,
                            EntryPrice: proposed.EntryPrice,
                            StopPrice: proposed.StopPrice,
                            TakeProfitPrice: proposed.TakeProfitPrice,
                            EstimatedRiskDollars: proposed.EstimatedRiskDollars,
                            Setup: proposed.Setup,
                            Source: proposed.Source);

                        EnqueueIntent(liveIntent);
                        _logger.LogInformation("V3Live entry intent symbol={Symbol} side={Side} quantity={Quantity} entryPrice={EntryPrice} stopPrice={StopPrice} setup={Setup}", symbol, proposed.Side, proposed.Quantity, proposed.EntryPrice, proposed.StopPrice, proposed.Setup);
                    }
                    else
                    {
                        orderIntentRejectReason = string.Join(";", check.Reasons);
                        lock (_eventsLock)
                        {
                            _riskEvents.Add(new V3LiveRiskEventRow(
                                now, symbol, "order-rejected", orderIntentRejectReason,
                                check.CurrentOpenRiskDollars, check.ProposedRiskDollars));
                        }
                    }
                }
            }
        }

        lock (_eventsLock)
        {
            _evaluations.Add(new V3LiveEvaluationRow(
                TimestampUtc: now,
                Symbol: symbol,
                PassedPreTradeGates: passedPreTrade,
                EmittedSignal: emittedSignal,
                SignalSide: decision.Side?.ToString() ?? string.Empty,
                SignalSetup: decision.Setup,
                OrderIntentAccepted: orderIntentAccepted,
                OrderIntentId: orderIntentId,
                OrderIntentRejectReason: orderIntentRejectReason,
                ReasonCodes: reasonCodes,
                Bid: l1.Bid,
                Ask: l1.Ask,
                Last: l1.Last,
                SpreadPct: l1.SpreadPct,
                BidDepthN: l2.BidDepthN,
                AskDepthN: l2.AskDepthN,
                ImbalanceRatio: l2.ImbalanceRatio,
                OfiSignal: features.OfiSignal,
                DistFromVwapAtr: features.DistFromVwapAtr,
                Rsi14: features.Rsi14,
                BbPctB: features.BbPctB,
                EntriesToday: symbolState.EntriesToday,
                CloseOnlyMode: _closeOnly));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Candle aggregator updates
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateCandleAggregator(string symbol, StrategyDataSlice dataSlice)
    {
        // Feed historical bars (1m bars from IBKR)
        foreach (var bar in dataSlice.HistoricalBars)
        {
            _candleAggregator.UpdateFromHistoricalBar(symbol, bar);
        }

        // Feed L1 last tick for sub-minute candle updates
        var lastTick = dataSlice.TopTicks
            .Where(t => t.Field == 4 && t.Price > 0) // Field 4 = Last
            .OrderByDescending(t => t.TimestampUtc)
            .FirstOrDefault();

        if (lastTick is not null)
        {
            _candleAggregator.UpdateFromTick(symbol, lastTick.TimestampUtc, lastTick.Price, lastTick.Size);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Intent queue management
    // ════════════════════════════════════════════════════════════════════════

    private void EnqueueIntent(LiveOrderIntent intent)
    {
        lock (_intentLock)
        {
            _pendingIntents.Add(intent);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Symbol resolution (H-01 FIX)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolve the active symbol from available data sources.
    /// Priority: 1) context.Symbol, 2) positions data, 3) TopTick.Kind, 4) config fallback.
    /// </summary>
    private string ResolveSymbol(StrategyDataSlice dataSlice)
    {
        // Primary: context symbol (set by SnapshotRuntime from _options.Symbol)
        if (_context is not null && !string.IsNullOrWhiteSpace(_context.Symbol))
            return _context.Symbol.Trim().ToUpperInvariant();

        // Secondary: extract from position data
        var posSymbol = dataSlice.Positions
            .Where(p => !string.IsNullOrWhiteSpace(p.Symbol)
                && string.Equals(p.SecurityType, "STK", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Symbol.Trim().ToUpperInvariant())
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(posSymbol))
            return posSymbol;

        // Tertiary: extract from TopTick.Kind (carries symbol in some flows)
        var tickSymbol = dataSlice.TopTicks
            .Where(t => !string.IsNullOrWhiteSpace(t.Kind))
            .Select(t => t.Kind.Trim().ToUpperInvariant())
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(tickSymbol))
            return tickSymbol;

        // Fallback: first configured symbol
        return _config.Symbols.FirstOrDefault() ?? string.Empty;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Utilities
    // ════════════════════════════════════════════════════════════════════════

    private bool IsWithinSession(DateTime timestampUtc)
    {
        if (!TimeSpan.TryParse(_config.SessionStartUtc, out var start)) return true;
        if (!TimeSpan.TryParse(_config.SessionEndUtc, out var end)) return true;

        var t = timestampUtc.TimeOfDay;
        return t >= start && t <= end;
    }

    private (bool Passed, string ReasonCode) EvaluateScannerV2Gate(
        string symbol,
        StrategyDataSlice dataSlice,
        V3LiveFeatureSnapshot features,
        DateTime nowUtc)
    {
        var candidate = new ScannerV2CandidateFileRow
        {
            Symbol = symbol,
            WeightedScore = 100.0,
            Eligible = true,
            AverageRank = 1.0,
            Bid = features.L1.Bid,
            Ask = features.L1.Ask,
            Mark = features.Price,
            Exchange = "SMART"
        };

        var volume = dataSlice.HistoricalBars
            .OrderByDescending(x => x.TimestampUtc)
            .Select(x => (double)x.Volume)
            .FirstOrDefault();
        var l1Snapshot = new ScannerV2L1Snapshot(
            Symbol: symbol,
            Bid: features.L1.Bid,
            Ask: features.L1.Ask,
            Last: features.L1.Last > 0 ? features.L1.Last : features.Price,
            Volume: Math.Max(0.0, volume),
            TimestampUtc: nowUtc);

        var l2Snapshot = BuildScannerL2Snapshot(symbol, dataSlice, nowUtc);
        var l2Map = l2Snapshot is null
            ? new Dictionary<string, ScannerV2L2DepthSnapshot>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ScannerV2L2DepthSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [symbol] = l2Snapshot
            };

        var sessionStart = ParseSessionBoundary(nowUtc, _config.SessionStartUtc, new TimeSpan(13, 35, 0));
        var sessionEnd = ParseSessionBoundary(nowUtc, _config.SessionEndUtc, new TimeSpan(20, 0, 0));

        var selection = _scannerV2Engine.Evaluate(
            fileCandidates: [candidate],
            l1Snapshots: new Dictionary<string, ScannerV2L1Snapshot>(StringComparer.OrdinalIgnoreCase) { [symbol] = l1Snapshot },
            l2Snapshots: l2Map,
            biasEntries: [],
            sessionOpenUtc: sessionStart,
            sessionCloseUtc: sessionEnd,
            nowUtc: nowUtc,
            sourcePath: "strategy-live-v3-inline");

        var ranked = selection.RankedCandidates.FirstOrDefault();
        if (ranked is null)
            return (false, "scanner-v2-no-candidate");

        if (!ranked.Eligible)
        {
            var reason = string.IsNullOrWhiteSpace(ranked.RejectReason)
                ? "rejected"
                : ranked.RejectReason.Replace(' ', '-').ToLowerInvariant();
            return (false, $"scanner-v2-{reason}");
        }

        if (ranked.CompositeScore < _config.ScannerMinCompositeScore)
            return (false, "scanner-v2-score-low");

        return (true, string.Empty);
    }

    private ScannerV2L2DepthSnapshot? BuildScannerL2Snapshot(string symbol, StrategyDataSlice dataSlice, DateTime nowUtc)
    {
        var depthRows = dataSlice.DepthRows
            .Where(x => x.Price > 0)
            .ToArray();
        if (depthRows.Length == 0)
            return null;

        var bids = depthRows
            .Where(x => x.Side == 1)
            .GroupBy(x => x.Position)
            .Select(group => group.OrderByDescending(x => x.TimestampUtc).First())
            .OrderBy(x => x.Position)
            .Take(Math.Max(1, _config.DepthLevels))
            .Select(x => new ScannerV2DepthLevel(x.Position, x.Price, Math.Max(0.0, x.Size)))
            .ToArray();

        var asks = depthRows
            .Where(x => x.Side == 0)
            .GroupBy(x => x.Position)
            .Select(group => group.OrderByDescending(x => x.TimestampUtc).First())
            .OrderBy(x => x.Position)
            .Take(Math.Max(1, _config.DepthLevels))
            .Select(x => new ScannerV2DepthLevel(x.Position, x.Price, Math.Max(0.0, x.Size)))
            .ToArray();

        if (bids.Length == 0 || asks.Length == 0)
            return null;

        return new ScannerV2L2DepthSnapshot(symbol, bids, asks, nowUtc);
    }

    private static DateTime ParseSessionBoundary(DateTime nowUtc, string value, TimeSpan fallback)
    {
        var time = TimeSpan.TryParse(value, out var parsed) ? parsed : fallback;
        return new DateTime(
            nowUtc.Year,
            nowUtc.Month,
            nowUtc.Day,
            time.Hours,
            time.Minutes,
            time.Seconds,
            DateTimeKind.Utc);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Internal types
    // ════════════════════════════════════════════════════════════════════════

    private sealed class V3LiveSymbolState
    {
        public int EntriesToday { get; set; }
        public DateTime? LastSignalUtc { get; set; }
        /// <summary>Last accepted proposed order — cached for AcknowledgeOrderTransmitted.</summary>
        public V3LiveProposedOrder? LastProposedOrder { get; set; }
    }

    private sealed record V3LiveRuntimeSummary(
        DateTime TimestampUtc,
        int ExitCode,
        IReadOnlyList<string> Symbols,
        int Evaluations,
        int Passed,
        int Failed,
        int ExitEvents,
        double RealizedPnlToday,
        int FilledOrdersToday,
        V3LiveConfig Config);

    private sealed record V3LiveEvaluationRow(
        DateTime TimestampUtc,
        string Symbol,
        bool PassedPreTradeGates,
        bool EmittedSignal,
        string SignalSide,
        string SignalSetup,
        bool OrderIntentAccepted,
        string OrderIntentId,
        string OrderIntentRejectReason,
        IReadOnlyList<string> ReasonCodes,
        double Bid,
        double Ask,
        double Last,
        double SpreadPct,
        double BidDepthN,
        double AskDepthN,
        double ImbalanceRatio,
        double OfiSignal,
        double DistFromVwapAtr,
        double Rsi14,
        double BbPctB,
        int EntriesToday,
        bool CloseOnlyMode);

    private sealed record V3LiveSignalRow(
        DateTime TimestampUtc,
        string Symbol,
        string Side,
        string Setup,
        double Price,
        double Atr14,
        double Rsi14,
        double DistFromVwapAtr,
        double BbPctB,
        double ImbalanceRatio,
        double OfiSignal,
        double SpreadPct);

    private sealed record V3LiveExitEventRow(
        DateTime TimestampUtc,
        string Symbol,
        string Side,
        int Quantity,
        double ExitPrice,
        string Reason,
        string Detail,
        double UnrealizedPnl,
        double UnrealizedPnlPeak,
        double HoldSeconds,
        double EntryPrice);

    private sealed record V3LiveRiskEventRow(
        DateTime TimestampUtc,
        string Symbol,
        string EventType,
        string Reason,
        double CurrentOpenRiskDollars,
        double ProposedRiskDollars);
}
