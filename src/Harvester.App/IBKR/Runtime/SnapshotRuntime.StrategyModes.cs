using System.Text.Json;
using System.Text.Json.Serialization;
using Harvester.App.IBKR.Broker;
using Harvester.App.IBKR.Contracts;
using Harvester.App.IBKR.Orders;
using Harvester.App.IBKR.Wrapper;
using Harvester.App.Strategy;
using Harvester.Contracts;
using IBApi;

namespace Harvester.App.IBKR.Runtime;

// Phase 3 #10: Extracted from SnapshotRuntime.cs - Strategy modes
public sealed partial class SnapshotRuntime
{
    private async Task RunStrategyReplayMode(StrategyRuntimeContext strategyContext, CancellationToken token)
    {
        var replayDriver = new StrategyReplayDriver();
        var replayNormalizationMode = ReplayCorporateActionsEngine.ParseNormalizationMode(_options.ReplayPriceNormalization);
        var replayCorporateActions = ReplayCorporateActionsEngine.LoadCorporateActions(_options.ReplayCorporateActionsInputPath, _options.ReplayMaxRows);
        var replaySymbolMappings = ReplaySymbolEventsEngine.LoadSymbolMappings(_options.ReplaySymbolMappingsInputPath, _options.ReplayMaxRows);
        var replayDelistEvents = ReplaySymbolEventsEngine.LoadDelistEvents(_options.ReplayDelistEventsInputPath, _options.ReplayMaxRows);
        var replayBorrowLocateProfiles = ReplayFinancingEngine.LoadBorrowLocateProfiles(_options.ReplayBorrowLocateInputPath, _options.ReplayMaxRows);
        var replaySymbolTimeline = new ReplaySymbolTimeline(strategyContext.Symbol, replaySymbolMappings, replayDelistEvents);
        var replayBorrowLocateTimeline = new ReplayBorrowLocateTimeline(replayBorrowLocateProfiles);
        var slices = replayDriver.LoadSlices(_options.ReplayInputPath, _options.ReplayMaxRows, replayCorporateActions, replayNormalizationMode);
        var staticReplayOrders = ReplayExecutionSimulator.LoadOrderIntents(_options.ReplayOrdersInputPath, _options.ReplayMaxRows);
        var staticReplayCursor = 0;
        var replayClock = new DeterministicReplayClock(slices[0].TimestampUtc);
        var replaySimulator = new ReplayExecutionSimulator(
            _options.ReplayInitialCash,
            _options.ReplayCommissionPerUnit,
            _options.ReplaySlippageBps,
            replayCorporateActions,
            replayNormalizationMode,
            _options.ReplayInitialMarginRate,
            _options.ReplayMaintenanceMarginRate,
            _options.ReplaySecFeeRatePerDollar,
            _options.ReplayTafFeePerShare,
            _options.ReplayTafFeeCapPerOrder,
            _options.ReplayExchangeFeePerShare,
            _options.ReplayMaxFillParticipationRate,
            _options.ReplayPriceIncrement,
            _options.ReplayEnforceQueuePriority,
            _options.ReplaySettlementLagDays,
            _options.ReplayEnforceSettledCash);
        var replayOrderRows = new List<ReplayOrderIntent>();
        var replayFillRows = new List<ReplayFillRow>();
        var replayCorporateActionAppliedRows = new List<ReplayCorporateActionAppliedRow>();
        var replayDelistAppliedRows = new List<ReplayDelistAppliedRow>();
        var replaySymbolEventRows = new List<ReplaySymbolEventArtifactRow>();
        var replayBorrowLocateEventRows = new List<ReplayBorrowLocateEventArtifactRow>();
        var replayFinancingAppliedRows = new List<ReplayFinancingAppliedRow>();
        var replayLocateRejectionRows = new List<ReplayLocateRejectionRow>();
        var replayMarginRejectionRows = new List<ReplayMarginRejectionRow>();
        var replayMarginEventRows = new List<ReplayMarginEventRow>();
        var replayCashSettlementRows = new List<ReplayCashSettlementRow>();
        var replayCashRejectionRows = new List<ReplayCashRejectionRow>();
        var replayOrderActivationRows = new List<ReplayOrderActivationRow>();
        var replayOrderUpdateRows = new List<ReplayOrderUpdateRow>();
        var replayComboEventRows = new List<ReplayComboLifecycleRow>();
        var replayTrailingStopUpdateRows = new List<ReplayTrailingStopUpdateRow>();
        var replayOrderTriggerRows = new List<ReplayOrderTriggerRow>();
        var replayOrderCancellationRows = new List<ReplayOrderCancellationRow>();
        var replayPortfolioRows = new List<ReplayPortfolioRow>();

        Console.WriteLine($"[INFO] strategy-replay loaded rows={slices.Count} source={Path.GetFullPath(_options.ReplayInputPath)}");
        if (staticReplayOrders.Count > 0)
        {
            Console.WriteLine($"[INFO] strategy-replay loaded external orders={staticReplayOrders.Count} source={Path.GetFullPath(_options.ReplayOrdersInputPath)}");
        }
        if (replayCorporateActions.Count > 0)
        {
            Console.WriteLine($"[INFO] strategy-replay loaded corporate actions={replayCorporateActions.Count} source={Path.GetFullPath(_options.ReplayCorporateActionsInputPath)} mode={_options.ReplayPriceNormalization}");
        }
        if (replaySymbolMappings.Count > 0)
        {
            Console.WriteLine($"[INFO] strategy-replay loaded symbol mappings={replaySymbolMappings.Count} source={Path.GetFullPath(_options.ReplaySymbolMappingsInputPath)}");
        }
        if (replayDelistEvents.Count > 0)
        {
            Console.WriteLine($"[INFO] strategy-replay loaded delist events={replayDelistEvents.Count} source={Path.GetFullPath(_options.ReplayDelistEventsInputPath)}");
        }
        if (replayBorrowLocateProfiles.Count > 0)
        {
            Console.WriteLine($"[INFO] strategy-replay loaded borrow/locate profiles={replayBorrowLocateProfiles.Count} source={Path.GetFullPath(_options.ReplayBorrowLocateInputPath)}");
        }

        foreach (var slice in slices)
        {
            token.ThrowIfCancellationRequested();
            replayClock.AdvanceTo(slice.TimestampUtc);
            await NotifyScheduledEventsAsync(strategyContext, replayClock.UtcNow, token);
            await NotifyStrategyDataSliceAsync(slice, token);

            var timelineStep = replaySymbolTimeline.Apply(slice.TimestampUtc);
            replaySymbolEventRows.AddRange(timelineStep.SymbolEvents);
            var activeSymbol = timelineStep.CurrentSymbol;
            var borrowLocateStep = replayBorrowLocateTimeline.Apply(slice.TimestampUtc);
            replayBorrowLocateEventRows.AddRange(borrowLocateStep.Events);
            var activeBorrowLocateProfile = replayBorrowLocateTimeline.GetProfile(activeSymbol);

            var dueOrders = new List<ReplayOrderIntent>();
            while (staticReplayCursor < staticReplayOrders.Count && staticReplayOrders[staticReplayCursor].TimestampUtc <= slice.TimestampUtc)
            {
                var externalOrder = staticReplayOrders[staticReplayCursor];
                dueOrders.Add(externalOrder with
                {
                    Symbol = string.IsNullOrWhiteSpace(externalOrder.Symbol) ? activeSymbol : externalOrder.Symbol.ToUpperInvariant()
                });
                staticReplayCursor++;
            }

            if (_strategyRuntime is IReplayOrderSignalSource replaySignalSource)
            {
                var strategyOrders = replaySignalSource.GetReplayOrderIntents(slice, strategyContext)
                    .Where(x => x.Quantity > 0)
                    .Select(x => x with
                    {
                        TimestampUtc = x.TimestampUtc == default ? slice.TimestampUtc : x.TimestampUtc,
                        Symbol = string.IsNullOrWhiteSpace(x.Symbol) ? activeSymbol : x.Symbol,
                        Source = string.IsNullOrWhiteSpace(x.Source) ? "strategy" : x.Source
                    })
                    .Where(x => x.TimestampUtc <= slice.TimestampUtc)
                    .ToArray();
                dueOrders.AddRange(strategyOrders);
            }

            var simulation = replaySimulator.ProcessSlice(slice, activeSymbol, dueOrders, timelineStep.DueDelistEvents, activeBorrowLocateProfile);
            replayOrderRows.AddRange(simulation.Orders);
            replayFillRows.AddRange(simulation.Fills);
            replayCorporateActionAppliedRows.AddRange(simulation.AppliedCorporateActions);
            replayDelistAppliedRows.AddRange(simulation.AppliedDelists);
            replayFinancingAppliedRows.AddRange(simulation.AppliedFinancing);
            replayLocateRejectionRows.AddRange(simulation.LocateRejections);
            replayMarginRejectionRows.AddRange(simulation.MarginRejections);
            replayMarginEventRows.AddRange(simulation.MarginEvents);
            replayCashSettlementRows.AddRange(simulation.CashSettlements);
            replayCashRejectionRows.AddRange(simulation.CashRejections);
            replayOrderActivationRows.AddRange(simulation.Activations);
            replayOrderUpdateRows.AddRange(simulation.OrderUpdates);
            replayComboEventRows.AddRange(simulation.ComboEvents);
            replayTrailingStopUpdateRows.AddRange(simulation.TrailingStopUpdates);
            replayOrderTriggerRows.AddRange(simulation.Triggers);
            replayOrderCancellationRows.AddRange(simulation.Cancellations);
            replayPortfolioRows.Add(simulation.Portfolio);

            if (_strategyRuntime is IReplaySimulationFeedbackSink replayFeedbackSink)
            {
                replayFeedbackSink.OnReplaySliceResult(slice, simulation, activeSymbol);
            }

            if (_options.ReplayIntervalSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.ReplayIntervalSeconds), token);
            }
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var replayPath = Path.Combine(outputDir, $"strategy_replay_slices_{timestamp}.json");
        var replayOrdersPath = Path.Combine(outputDir, $"strategy_replay_orders_{timestamp}.json");
        var replayFillsPath = Path.Combine(outputDir, $"strategy_replay_fills_{timestamp}.json");
        var replayCorporateActionsAppliedPath = Path.Combine(outputDir, $"strategy_replay_corporate_actions_applied_{timestamp}.json");
        var replaySymbolEventsPath = Path.Combine(outputDir, $"strategy_replay_symbol_events_{timestamp}.json");
        var replayDelistAppliedPath = Path.Combine(outputDir, $"strategy_replay_delist_applied_{timestamp}.json");
        var replayBorrowLocateEventsPath = Path.Combine(outputDir, $"strategy_replay_borrow_locate_events_{timestamp}.json");
        var replayFinancingAppliedPath = Path.Combine(outputDir, $"strategy_replay_financing_applied_{timestamp}.json");
        var replayLocateRejectionsPath = Path.Combine(outputDir, $"strategy_replay_locate_rejections_{timestamp}.json");
        var replayMarginRejectionsPath = Path.Combine(outputDir, $"strategy_replay_margin_rejections_{timestamp}.json");
        var replayMarginEventsPath = Path.Combine(outputDir, $"strategy_replay_margin_events_{timestamp}.json");
        var replayCashSettlementsPath = Path.Combine(outputDir, $"strategy_replay_cash_settlements_{timestamp}.json");
        var replayCashRejectionsPath = Path.Combine(outputDir, $"strategy_replay_cash_rejections_{timestamp}.json");
        var replayOrderActivationsPath = Path.Combine(outputDir, $"strategy_replay_order_activations_{timestamp}.json");
        var replayOrderUpdatesPath = Path.Combine(outputDir, $"strategy_replay_order_updates_{timestamp}.json");
        var replayComboEventsPath = Path.Combine(outputDir, $"strategy_replay_combo_events_{timestamp}.json");
        var replayTrailingStopUpdatesPath = Path.Combine(outputDir, $"strategy_replay_trailing_stop_updates_{timestamp}.json");
        var replayOrderTriggersPath = Path.Combine(outputDir, $"strategy_replay_order_triggers_{timestamp}.json");
        var replayOrderCancellationsPath = Path.Combine(outputDir, $"strategy_replay_order_cancellations_{timestamp}.json");
        var replayFeeBreakdownPath = Path.Combine(outputDir, $"strategy_replay_fee_breakdown_{timestamp}.json");
        var replayCostDeltasPath = Path.Combine(outputDir, $"strategy_replay_cost_deltas_{timestamp}.json");
        var replayPartialFillEventsPath = Path.Combine(outputDir, $"strategy_replay_partial_fill_events_{timestamp}.json");
        var replayPortfolioPath = Path.Combine(outputDir, $"strategy_replay_portfolio_{timestamp}.json");
        var replayBenchmarkPath = Path.Combine(outputDir, $"strategy_replay_benchmark_{timestamp}.json");
        var replayPacketsPath = Path.Combine(outputDir, $"strategy_replay_performance_packets_{timestamp}.json");
        var replaySummaryPath = Path.Combine(outputDir, $"strategy_replay_performance_summary_{timestamp}.json");
        var replayValidationSummaryPath = Path.Combine(outputDir, $"strategy_replay_validation_summary_{timestamp}.json");
        var replayScannerSelectionPath = Path.Combine(outputDir, $"strategy_replay_scanner_symbol_selection_{timestamp}.json");
        var replayHistoricalCandlesPath = Path.Combine(outputDir, $"strategy_replay_historical_candles_{timestamp}.json");
        var replayScannerHistoricalEvaluationPath = Path.Combine(outputDir, $"strategy_replay_scanner_historical_evaluation_{timestamp}.json");
        var replayLimitOrderCaseMatrixPath = Path.Combine(outputDir, $"strategy_replay_limit_order_case_matrix_{timestamp}.json");
        var replaySelfLearningSamplesPath = Path.Combine(outputDir, $"strategy_replay_self_learning_samples_{timestamp}.json");
        var replaySelfLearningPredictionsPath = Path.Combine(outputDir, $"strategy_replay_self_learning_predictions_{timestamp}.json");
        var replaySelfLearningSummaryPath = Path.Combine(outputDir, $"strategy_replay_self_learning_summary_{timestamp}.json");
        var replaySelfLearningStoreSnapshotPath = Path.Combine(outputDir, $"strategy_replay_self_learning_store_snapshot_{timestamp}.json");
        var replaySelfLearningPostprocessPath = Path.Combine(outputDir, $"strategy_replay_self_learning_m9_postprocess_{timestamp}.json");
        var replaySelfLearningGovernancePath = Path.Combine(outputDir, $"strategy_replay_self_learning_promotion_governance_{timestamp}.json");
        var replaySelfLearningLifecycleSnapshotPath = Path.Combine(outputDir, $"strategy_replay_self_learning_lifecycle_{timestamp}.json");
        var replaySelfLearningModelRegistrySnapshotPath = Path.Combine(outputDir, $"strategy_replay_self_learning_model_registry_{timestamp}.json");
        var replaySelfLearningStorePath = Path.Combine(outputDir, "strategy_replay_self_learning_store.json");
        var replaySelfLearningLifecycleStorePath = Path.Combine(outputDir, "strategy_replay_self_learning_lifecycle.json");
        var replaySelfLearningModelRegistryStorePath = Path.Combine(outputDir, "strategy_replay_self_learning_model_registry.json");
        // V2 self-learning engine paths
        var replaySelfLearningV2SamplesPath = Path.Combine(outputDir, $"strategy_replay_self_learning_v2_samples_{timestamp}.json");
        var replaySelfLearningV2PredictionsPath = Path.Combine(outputDir, $"strategy_replay_self_learning_v2_predictions_{timestamp}.json");
        var replaySelfLearningV2SummaryPath = Path.Combine(outputDir, $"strategy_replay_self_learning_v2_summary_{timestamp}.json");
        var replaySelfLearningV2RecommendationsPath = Path.Combine(outputDir, $"strategy_replay_self_learning_v2_recommendations_{timestamp}.json");
        var replaySelfLearningV2BiasStorePath = Path.Combine(outputDir, "strategy_replay_self_learning_v2_bias_store.json");

        var replayFeeBreakdownRows = replayFillRows
            .Select(fill =>
            {
                var baseCommission = fill.Quantity * _options.ReplayCommissionPerUnit;
                var secFee = string.Equals(fill.Side, "SELL", StringComparison.OrdinalIgnoreCase)
                    ? (fill.Quantity * fill.FillPrice * _options.ReplaySecFeeRatePerDollar)
                    : 0.0;
                var tafRaw = string.Equals(fill.Side, "SELL", StringComparison.OrdinalIgnoreCase)
                    ? (fill.Quantity * _options.ReplayTafFeePerShare)
                    : 0.0;
                var tafFee = _options.ReplayTafFeeCapPerOrder > 0
                    ? Math.Min(tafRaw, _options.ReplayTafFeeCapPerOrder)
                    : tafRaw;
                var exchangeFee = fill.Quantity * _options.ReplayExchangeFeePerShare;

                return new ReplayFeeBreakdownArtifactRow(
                    fill.TimestampUtc,
                    fill.Symbol,
                    fill.Side,
                    fill.Quantity,
                    fill.FillPrice,
                    baseCommission,
                    secFee,
                    tafFee,
                    exchangeFee,
                    fill.Commission,
                    fill.Source);
            })
            .ToArray();

        var replayPartialFillRows = replayFillRows
            .Where(fill => fill.IsPartial)
            .Select(fill => new ReplayPartialFillArtifactRow(
                fill.TimestampUtc,
                fill.Symbol,
                fill.Side,
                fill.SubmittedAtUtc,
                fill.OrderType,
                fill.RequestedQuantity,
                fill.Quantity,
                fill.RemainingQuantity,
                fill.FillPrice,
                fill.Source))
            .ToArray();

        var closeByTimestampAndSymbol = slices
            .Select(slice =>
            {
                var symbol = slice.TopTicks.FirstOrDefault()?.Value
                    ?? strategyContext.Symbol;
                symbol = slice.TopTicks.FirstOrDefault()?.Kind
                    ?? symbol;
                var close = slice.HistoricalBars.FirstOrDefault()?.Close
                    ?? slice.TopTicks.FirstOrDefault()?.Price
                    ?? 0.0;

                return new
                {
                    slice.TimestampUtc,
                    Symbol = symbol.ToUpperInvariant(),
                    Close = close
                };
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Symbol) && x.Close > 0)
            .GroupBy(x => (x.TimestampUtc, x.Symbol))
            .ToDictionary(g => g.Key, g => g.Last().Close);

        var replayCostDeltaRows = replayFillRows
            .Select(fill =>
            {
                var hasReference = closeByTimestampAndSymbol.TryGetValue((fill.TimestampUtc, fill.Symbol.ToUpperInvariant()), out var referencePrice)
                    && referencePrice > 0;

                var estimatedCommission = fill.Quantity * _options.ReplayCommissionPerUnit;
                var realizedCommission = fill.Commission;
                var estimatedSlippage = hasReference
                    ? (fill.Quantity * referencePrice * (_options.ReplaySlippageBps / 10000.0))
                    : (double?)null;
                var realizedSlippage = hasReference
                    ? ComputeReplayRealizedSlippage(fill.Side, fill.Quantity, referencePrice, fill.FillPrice)
                    : (double?)null;

                return new ReplayCostDeltaArtifactRow(
                    fill.TimestampUtc,
                    fill.Symbol,
                    fill.Side,
                    fill.OrderType,
                    fill.Quantity,
                    fill.FillPrice,
                    hasReference ? referencePrice : null,
                    estimatedCommission,
                    realizedCommission,
                    realizedCommission - estimatedCommission,
                    estimatedSlippage,
                    realizedSlippage,
                    estimatedSlippage is null || realizedSlippage is null ? null : realizedSlippage.Value - estimatedSlippage.Value,
                    fill.Source);
            })
            .ToArray();

        var performanceAnalyzer = new ReplayPerformanceAnalyzer();
        var performance = performanceAnalyzer.Analyze(slices, replayFillRows, replayPortfolioRows, _options.ReplayInitialCash);
#pragma warning disable CS0612, CS0618 // V1 analyzer retained for backward-compatible JSON export
        var selfLearningAnalyzer = new ReplaySelfLearningAnalyzer();
#pragma warning restore CS0612, CS0618
        var selfLearning = selfLearningAnalyzer.Analyze(replayFillRows, replayCostDeltaRows, performance.Packets);
        // V2.1 self-learning engine
        var selfLearningEngineV2 = new ReplaySelfLearningEngine();
        var selfLearningV2 = selfLearningEngineV2.Analyze(replayFillRows, replayCostDeltaRows, performance.Packets);
        var existingV2BiasStore = LoadSelfLearningV2BiasStore(replaySelfLearningV2BiasStorePath);
        var selfLearningV2Recommendations = ReplaySelfLearningEngine.BuildRecommendations(selfLearningV2, existingV2BiasStore);
        var selfLearningV2BiasStore = ReplaySelfLearningEngine.UpdateSymbolBiasStore(existingV2BiasStore, replayFillRows);
        var selfLearningStore = UpdateReplaySelfLearningStore(replaySelfLearningStorePath, replayFillRows, strategyContext.Symbol);
        var selfLearningPostprocess = BuildReplaySelfLearningPostprocessReport(selfLearningStore, replayFillRows);
        var selfLearningGovernance = BuildReplaySelfLearningPromotionGovernanceReport(selfLearningStore, replayFillRows, strategyContext.Symbol);
        var selfLearningLifecycleAndRegistry = UpdateReplaySelfLearningLifecycleAndRegistry(
            replaySelfLearningLifecycleStorePath,
            replaySelfLearningModelRegistryStorePath,
            selfLearningStore,
            selfLearningGovernance,
            performance.Summary,
            slices.Count,
            performance.Packets.Count,
            strategyContext.Symbol);
        var strategySourceCounts = replayOrderRows
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Source) ? "unknown" : x.Source.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var replayValidationSummary = new[]
        {
            new ReplayValidationSummaryRow(
                DateTime.UtcNow,
                _strategyRuntime.GetType().Name,
                strategyContext.Symbol,
                slices.Count,
                replayOrderRows.Count,
                replayFillRows.Count,
                replayLocateRejectionRows.Count,
                replayMarginRejectionRows.Count,
                replayCashRejectionRows.Count,
                replayOrderCancellationRows.Count,
                strategySourceCounts,
                performance.Summary.TotalReturn,
                performance.Summary.MaxDrawdown,
                performance.Summary.SharpeLike,
                performance.Summary.WinRate,
                performance.Summary.FillCount,
                _options.ReplayScannerCandidatesInputPath,
                _options.ReplayScannerTopN,
                _options.ReplayScannerMinScore,
                _options.ReplayScannerOrderQuantity,
                _options.ReplayScannerOrderSide,
                _options.ReplayScannerOrderType,
                _options.ReplayScannerOrderTimeInForce,
                _options.ReplayScannerLimitOffsetBps)
        };

            var replayHistoricalCandles = ReplayHistoricalCandlestickCharts.BuildFromSlices(slices);

            var replayLimitOrderCaseMatrixRows = BuildReplayLimitOrderCaseMatrixRows();

        WriteJson(replayPath, slices);
        WriteJson(replayOrdersPath, replayOrderRows);
        WriteJson(replayFillsPath, replayFillRows);
        WriteJson(replayCorporateActionsAppliedPath, replayCorporateActionAppliedRows);
        WriteJson(replaySymbolEventsPath, replaySymbolEventRows);
        WriteJson(replayDelistAppliedPath, replayDelistAppliedRows);
        WriteJson(replayBorrowLocateEventsPath, replayBorrowLocateEventRows);
        WriteJson(replayFinancingAppliedPath, replayFinancingAppliedRows);
        WriteJson(replayLocateRejectionsPath, replayLocateRejectionRows);
        WriteJson(replayMarginRejectionsPath, replayMarginRejectionRows);
        WriteJson(replayMarginEventsPath, replayMarginEventRows);
        WriteJson(replayCashSettlementsPath, replayCashSettlementRows);
        WriteJson(replayCashRejectionsPath, replayCashRejectionRows);
        WriteJson(replayOrderActivationsPath, replayOrderActivationRows);
        WriteJson(replayOrderUpdatesPath, replayOrderUpdateRows);
        WriteJson(replayComboEventsPath, replayComboEventRows);
        WriteJson(replayTrailingStopUpdatesPath, replayTrailingStopUpdateRows);
        WriteJson(replayOrderTriggersPath, replayOrderTriggerRows);
        WriteJson(replayOrderCancellationsPath, replayOrderCancellationRows);
        WriteJson(replayFeeBreakdownPath, replayFeeBreakdownRows);
        WriteJson(replayCostDeltasPath, replayCostDeltaRows);
        WriteJson(replayPartialFillEventsPath, replayPartialFillRows);
        WriteJson(replayPortfolioPath, replayPortfolioRows);
        WriteJson(replayBenchmarkPath, performance.Benchmark);
        WriteJson(replayPacketsPath, performance.Packets);
        WriteJson(replaySummaryPath, new[] { performance.Summary });
        WriteJson(replayValidationSummaryPath, replayValidationSummary);
        WriteJson(replayHistoricalCandlesPath, replayHistoricalCandles);
        if (_strategyRuntime is IReplayScannerSelectionSource scannerSelectionSource)
        {
            var selectionSnapshot = scannerSelectionSource.GetScannerSelectionSnapshot();
            WriteJson(replayScannerSelectionPath, new[] { selectionSnapshot });
            var scannerHistoricalEvaluation = ReplayHistoricalCandlestickCharts.BuildScannerEvaluations(replayHistoricalCandles, selectionSnapshot);
            WriteJson(replayScannerHistoricalEvaluationPath, scannerHistoricalEvaluation);
        }
        WriteJson(replayLimitOrderCaseMatrixPath, replayLimitOrderCaseMatrixRows);
        WriteJson(replaySelfLearningSamplesPath, selfLearning.Samples);
        WriteJson(replaySelfLearningPredictionsPath, selfLearning.Predictions);
        WriteJson(replaySelfLearningSummaryPath, new[] { selfLearning.Summary });
        WriteJson(replaySelfLearningStoreSnapshotPath, new[] { selfLearningStore });
        WriteJson(replaySelfLearningPostprocessPath, new[] { selfLearningPostprocess });
        WriteJson(replaySelfLearningGovernancePath, new[] { selfLearningGovernance });
        WriteJson(replaySelfLearningLifecycleSnapshotPath, new[] { selfLearningLifecycleAndRegistry.Lifecycle });
        WriteJson(replaySelfLearningModelRegistrySnapshotPath, new[] { selfLearningLifecycleAndRegistry.Registry });
        // V2 self-learning engine outputs
        WriteJson(replaySelfLearningV2SamplesPath, selfLearningV2.Samples);
        WriteJson(replaySelfLearningV2PredictionsPath, selfLearningV2.Predictions);
        WriteJson(replaySelfLearningV2SummaryPath, new[] { selfLearningV2.Summary });
        WriteJson(replaySelfLearningV2RecommendationsPath, new[] { selfLearningV2Recommendations });
        WriteSelfLearningV2BiasStore(replaySelfLearningV2BiasStorePath, selfLearningV2BiasStore);
        Console.WriteLine($"[OK] Strategy replay slices export: {replayPath} (rows={slices.Count})");
        Console.WriteLine($"[OK] Strategy replay orders export: {replayOrdersPath} (rows={replayOrderRows.Count})");
        Console.WriteLine($"[OK] Strategy replay fills export: {replayFillsPath} (rows={replayFillRows.Count})");
        Console.WriteLine($"[OK] Strategy replay applied corporate actions export: {replayCorporateActionsAppliedPath} (rows={replayCorporateActionAppliedRows.Count})");
        Console.WriteLine($"[OK] Strategy replay symbol events export: {replaySymbolEventsPath} (rows={replaySymbolEventRows.Count})");
        Console.WriteLine($"[OK] Strategy replay applied delist export: {replayDelistAppliedPath} (rows={replayDelistAppliedRows.Count})");
        Console.WriteLine($"[OK] Strategy replay borrow/locate events export: {replayBorrowLocateEventsPath} (rows={replayBorrowLocateEventRows.Count})");
        Console.WriteLine($"[OK] Strategy replay financing applied export: {replayFinancingAppliedPath} (rows={replayFinancingAppliedRows.Count})");
        Console.WriteLine($"[OK] Strategy replay locate rejections export: {replayLocateRejectionsPath} (rows={replayLocateRejectionRows.Count})");
        Console.WriteLine($"[OK] Strategy replay margin rejections export: {replayMarginRejectionsPath} (rows={replayMarginRejectionRows.Count})");
        Console.WriteLine($"[OK] Strategy replay margin events export: {replayMarginEventsPath} (rows={replayMarginEventRows.Count})");
        Console.WriteLine($"[OK] Strategy replay cash settlements export: {replayCashSettlementsPath} (rows={replayCashSettlementRows.Count})");
        Console.WriteLine($"[OK] Strategy replay cash rejections export: {replayCashRejectionsPath} (rows={replayCashRejectionRows.Count})");
        Console.WriteLine($"[OK] Strategy replay order activations export: {replayOrderActivationsPath} (rows={replayOrderActivationRows.Count})");
        Console.WriteLine($"[OK] Strategy replay order updates export: {replayOrderUpdatesPath} (rows={replayOrderUpdateRows.Count})");
        Console.WriteLine($"[OK] Strategy replay combo events export: {replayComboEventsPath} (rows={replayComboEventRows.Count})");
        Console.WriteLine($"[OK] Strategy replay trailing stop updates export: {replayTrailingStopUpdatesPath} (rows={replayTrailingStopUpdateRows.Count})");
        Console.WriteLine($"[OK] Strategy replay order triggers export: {replayOrderTriggersPath} (rows={replayOrderTriggerRows.Count})");
        Console.WriteLine($"[OK] Strategy replay order cancellations export: {replayOrderCancellationsPath} (rows={replayOrderCancellationRows.Count})");
        Console.WriteLine($"[OK] Strategy replay fee breakdown export: {replayFeeBreakdownPath} (rows={replayFeeBreakdownRows.Length})");
        Console.WriteLine($"[OK] Strategy replay cost delta export: {replayCostDeltasPath} (rows={replayCostDeltaRows.Length})");
        Console.WriteLine($"[OK] Strategy replay partial fill events export: {replayPartialFillEventsPath} (rows={replayPartialFillRows.Length})");
        Console.WriteLine($"[OK] Strategy replay portfolio export: {replayPortfolioPath} (rows={replayPortfolioRows.Count})");
        Console.WriteLine($"[OK] Strategy replay benchmark export: {replayBenchmarkPath} (rows={performance.Benchmark.Count})");
        Console.WriteLine($"[OK] Strategy replay performance packets export: {replayPacketsPath} (rows={performance.Packets.Count})");
        Console.WriteLine($"[OK] Strategy replay performance summary export: {replaySummaryPath}");
        Console.WriteLine($"[OK] Strategy replay validation summary export: {replayValidationSummaryPath}");
        Console.WriteLine($"[OK] Strategy replay historical candles export: {replayHistoricalCandlesPath} (rows={replayHistoricalCandles.Count})");
        if (_strategyRuntime is IReplayScannerSelectionSource)
        {
            Console.WriteLine($"[OK] Strategy replay scanner symbol selection export: {replayScannerSelectionPath}");
            Console.WriteLine($"[OK] Strategy replay scanner historical evaluation export: {replayScannerHistoricalEvaluationPath}");
        }
        Console.WriteLine($"[OK] Strategy replay limit-order case matrix export: {replayLimitOrderCaseMatrixPath} (rows={replayLimitOrderCaseMatrixRows.Length})");
        Console.WriteLine($"[OK] Strategy replay self-learning samples export: {replaySelfLearningSamplesPath} (rows={selfLearning.Samples.Count})");
        Console.WriteLine($"[OK] Strategy replay self-learning predictions export: {replaySelfLearningPredictionsPath} (rows={selfLearning.Predictions.Count})");
        Console.WriteLine($"[OK] Strategy replay self-learning summary export: {replaySelfLearningSummaryPath}");
        Console.WriteLine($"[OK] Strategy replay self-learning store snapshot export: {replaySelfLearningStoreSnapshotPath}");
        Console.WriteLine($"[OK] Strategy replay self-learning M9 postprocess export: {replaySelfLearningPostprocessPath}");
        Console.WriteLine($"[OK] Strategy replay self-learning promotion governance export: {replaySelfLearningGovernancePath}");
        Console.WriteLine($"[OK] Strategy replay self-learning lifecycle export: {replaySelfLearningLifecycleSnapshotPath}");
        Console.WriteLine($"[OK] Strategy replay self-learning model registry export: {replaySelfLearningModelRegistrySnapshotPath}");
        Console.WriteLine($"[OK] Strategy replay self-learning V2 samples export: {replaySelfLearningV2SamplesPath} (rows={selfLearningV2.Samples.Count})");
        Console.WriteLine($"[OK] Strategy replay self-learning V2 predictions export: {replaySelfLearningV2PredictionsPath} (rows={selfLearningV2.Predictions.Count})");
        Console.WriteLine($"[OK] Strategy replay self-learning V2 summary export: {replaySelfLearningV2SummaryPath} (engine={ReplaySelfLearningEngine.Version} features={selfLearningV2.Summary.FeatureCount} oosSamples={selfLearningV2.Summary.WalkForwardOosSamples})");
        Console.WriteLine($"[OK] Strategy replay self-learning V2 recommendations export: {replaySelfLearningV2RecommendationsPath}");
        Console.WriteLine($"[OK] Strategy replay self-learning V2 bias store export: {replaySelfLearningV2BiasStorePath} (symbols={selfLearningV2BiasStore.Count})");
    }

    private async Task RunStrategyLiveV3Mode(
        EClientSocket client,
        IBrokerAdapter brokerAdapter,
        StrategyRuntimeContext strategyContext,
        CancellationToken token)
    {
        ValidateHistoricalBarRequestLimitations(_options.HistoricalDuration, _options.HistoricalBarSize);

        if (BarSizeToSeconds(_options.HistoricalBarSize) < 5)
        {
            throw new InvalidOperationException("strategy-live-v3 requires --hist-bar-size >= 5 secs.");
        }

        var topContract = brokerAdapter.BuildContract(new BrokerContractSpec(
            BrokerAssetType.Stock,
            _options.Symbol,
            "SMART",
            "USD",
            _options.PrimaryExchange));
        var depthContract = brokerAdapter.BuildContract(new BrokerContractSpec(
            BrokerAssetType.Stock,
            _options.Symbol,
            _options.DepthExchange,
            "USD",
            _options.PrimaryExchange));

        const int topReqId = 9751;
        const int depthReqId = 9752;
        const int historicalReqId = 9753;

        brokerAdapter.RequestMarketDataType(client, _options.MarketDataType);
        brokerAdapter.RequestMarketData(client, topReqId, topContract);
        brokerAdapter.RequestMarketDepth(client, depthReqId, depthContract, _options.DepthRows, isSmartDepth: false);
        brokerAdapter.RequestHistoricalData(
            client,
            historicalReqId,
            topContract,
            string.Empty,
            _options.HistoricalDuration,
            _options.HistoricalBarSize,
            _options.HistoricalWhatToShow,
            _options.HistoricalUseRth,
            _options.HistoricalFormatDate,
            keepUpToDate: true);

        Console.WriteLine($"[INFO] strategy-live-v3 started symbol={_options.Symbol} captureSeconds={_options.CaptureSeconds} topReqId={topReqId} depthReqId={depthReqId} historicalReqId={historicalReqId}");

        try
        {
            var startedAt = DateTime.UtcNow;
            var cadence = TimeSpan.FromSeconds(1);
            while ((DateTime.UtcNow - startedAt).TotalSeconds < Math.Max(1, _options.CaptureSeconds))
            {
                token.ThrowIfCancellationRequested();

                await NotifyScheduledEventsAsync(strategyContext, DateTime.UtcNow, token);
                await NotifyStrategyDataSliceAsync(BuildStrategyDataSlice("strategy-live-v3"), token);
                await TryTransmitLiveOrderIntentsAsync(client, brokerAdapter, token);

                await Task.Delay(cadence, token);
            }
        }
        finally
        {
            brokerAdapter.CancelMarketData(client, topReqId);
            brokerAdapter.CancelMarketDepth(client, depthReqId, isSmartDepth: false);
            brokerAdapter.CancelHistoricalData(client, historicalReqId);
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var topPath = Path.Combine(outputDir, $"strategy_live_v3_top_data_{_options.Symbol}_{timestamp}.json");
        var depthPath = Path.Combine(outputDir, $"strategy_live_v3_depth_data_{_options.Symbol}_{timestamp}.json");
        var barsPath = Path.Combine(outputDir, $"strategy_live_v3_historical_bars_{_options.Symbol}_{timestamp}.json");
        var barUpdatesPath = Path.Combine(outputDir, $"strategy_live_v3_historical_bar_updates_{_options.Symbol}_{timestamp}.json");
        var sanitizationPath = Path.Combine(outputDir, $"strategy_live_v3_market_data_sanitization_{_options.Symbol}_{timestamp}.json");

        WriteJson(topPath, _wrapper.TopTicks.ToArray());
        WriteJson(depthPath, _wrapper.DepthRows.ToArray());
        WriteJson(barsPath, _wrapper.HistoricalBars.ToArray());
        WriteJson(barUpdatesPath, _wrapper.HistoricalBarUpdates.ToArray());
        WriteJson(sanitizationPath, _wrapper.MarketDataSanitizationRows.ToArray());

        Console.WriteLine($"[OK] Strategy live V3 top data export: {topPath} (rows={_wrapper.TopTicks.Count})");
        Console.WriteLine($"[OK] Strategy live V3 depth data export: {depthPath} (rows={_wrapper.DepthRows.Count})");
        Console.WriteLine($"[OK] Strategy live V3 historical bars export: {barsPath} (rows={_wrapper.HistoricalBars.Count})");
        Console.WriteLine($"[OK] Strategy live V3 historical bar updates export: {barUpdatesPath} (rows={_wrapper.HistoricalBarUpdates.Count})");
        Console.WriteLine($"[OK] Strategy live V3 market data sanitization export: {sanitizationPath} (rows={_wrapper.MarketDataSanitizationRows.Count})");
    }

}
