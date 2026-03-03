using System.Text.Json;
using System.Text.Json.Serialization;
using Harvester.App.Backtest.Engine;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class V3LiveRuntime : IStrategyRuntime
{
    private readonly V3LiveConfig _config;
    private readonly V3LiveFeatureBuilder _featureBuilder = new();
    private readonly V3LiveSignalEngine _signalEngine = new();
    private readonly ScannerSelectionEngineV2 _scannerV2Engine;
    private readonly ReplayMtfCandleSignalEngine _mtfSignalEngine = new();
    private readonly V3LiveRiskGuard _riskGuard;
    private readonly V3LiveOrderBridge _orderBridge;
    private StrategyRuntimeContext? _context;
    private readonly Dictionary<string, V3LiveSymbolState> _stateBySymbol = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<V3LiveEvaluationRow> _evaluations = [];
    private readonly List<V3LiveSignalRow> _signals = [];
    private readonly List<V3LiveProposedOrder> _orderIntents = [];
    private readonly List<V3LiveRiskEventRow> _riskEvents = [];
    private bool _closeOnly;

    public V3LiveRuntime(V3LiveConfig? config = null)
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
    }

    public Task InitializeAsync(StrategyRuntimeContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _closeOnly = false;
        _evaluations.Clear();
        _signals.Clear();
        _orderIntents.Clear();
        _riskEvents.Clear();
        _stateBySymbol.Clear();

        foreach (var symbol in _config.Symbols)
        {
            _stateBySymbol[symbol] = new V3LiveSymbolState();
        }

        return Task.CompletedTask;
    }

    public Task OnScheduledEventAsync(string eventName, StrategyRuntimeContext context, CancellationToken cancellationToken)
    {
        if (string.Equals(eventName, "market-close-warning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventName, "pre-close", StringComparison.OrdinalIgnoreCase))
        {
            _closeOnly = true;
        }

        if (string.Equals(eventName, "mode-start", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventName, "session-open-reset", StringComparison.OrdinalIgnoreCase))
        {
            _closeOnly = false;
            foreach (var state in _stateBySymbol.Values)
            {
                state.EntriesToday = 0;
                state.LastSignalUtc = null;
            }
        }

        return Task.CompletedTask;
    }

    public Task OnDataAsync(StrategyDataSlice dataSlice, CancellationToken cancellationToken)
    {
        var symbol = ResolveSymbol(dataSlice);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return Task.CompletedTask;
        }

        if (!_stateBySymbol.TryGetValue(symbol, out var symbolState))
        {
            symbolState = new V3LiveSymbolState();
            _stateBySymbol[symbol] = symbolState;
        }

        var features = _featureBuilder.Build(dataSlice, _config.DepthLevels);
        var l1 = features.L1;
        var l2 = features.L2;

        var now = dataSlice.TimestampUtc;
        var reasonCodes = new List<string>();

        UpdateMtfSignalEngine(symbol, dataSlice, features);

        if (_closeOnly)
        {
            reasonCodes.Add("close-only-mode");
        }

        if (!IsWithinSession(now))
        {
            reasonCodes.Add("outside-session");
        }

        if (symbolState.EntriesToday >= _config.MaxEntriesPerSymbolPerDay)
        {
            reasonCodes.Add("max-entries-reached");
        }

        if (symbolState.LastSignalUtc.HasValue && (now - symbolState.LastSignalUtc.Value).TotalSeconds < _config.CooldownSeconds)
        {
            reasonCodes.Add("cooldown-active");
        }

        if (!l1.HasQuote)
        {
            reasonCodes.Add("l1-missing-quote");
        }
        else
        {
            if (l1.SpreadPct > _config.MaxSpreadPct)
            {
                reasonCodes.Add("l1-spread-too-wide");
            }

            if (l1.BidSize < _config.MinTopQuoteSize || l1.AskSize < _config.MinTopQuoteSize)
            {
                reasonCodes.Add("l1-size-too-small");
            }

            if ((now - l1.TimestampUtc).TotalSeconds > _config.MaxQuoteStalenessSeconds)
            {
                reasonCodes.Add("l1-stale");
            }
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
                {
                    reasonCodes.Add("l2-bid-depth-thin");
                }

                if (l2.AskDepthN < _config.MinDepthPerSideShares)
                {
                    reasonCodes.Add("l2-ask-depth-thin");
                }

                if (l2.ImbalanceRatio is > 0 and < 0.25)
                {
                    reasonCodes.Add("l2-imbalance-extreme");
                }
            }
        }

        if (_config.UseScannerSelectionV2Gate)
        {
            var scannerGate = EvaluateScannerV2Gate(symbol, dataSlice, features, now);
            if (!scannerGate.Passed)
            {
                reasonCodes.Add(scannerGate.ReasonCode);
            }
        }

        var passedPreTrade = reasonCodes.Count == 0;

        var decision = _signalEngine.Evaluate(features, _config);
        if (_config.RequireMtfConfirmation && decision.HasSignal && decision.Side.HasValue)
        {
            if (!_mtfSignalEngine.TryGetSnapshot(symbol, out var snapshot) || !snapshot.HasAllTimeframes)
            {
                if (!_config.AllowMtfUnready)
                {
                    reasonCodes.Add("mtf-unready");
                }
            }
            else
            {
                var mtfAligned = decision.Side == TradeSide.Long
                    ? snapshot.BullishEntryReady
                    : snapshot.BearishEntryReady;
                if (!mtfAligned)
                {
                    reasonCodes.Add(decision.Side == TradeSide.Long ? "mtf-not-bullish" : "mtf-not-bearish");
                }
            }
        }

        passedPreTrade = reasonCodes.Count == 0;
        if (!decision.HasSignal && !string.IsNullOrWhiteSpace(decision.Reason))
        {
            reasonCodes.Add(decision.Reason);
        }

        var emittedSignal = passedPreTrade && decision.HasSignal;
        var orderIntentAccepted = false;
        string orderIntentId = string.Empty;
        string orderIntentRejectReason = string.Empty;

        if (emittedSignal)
        {
            symbolState.LastSignalUtc = now;
            symbolState.EntriesToday++;

            _signals.Add(new V3LiveSignalRow(
                TimestampUtc: now,
                Symbol: symbol,
                Side: decision.Side?.ToString() ?? string.Empty,
                Setup: decision.Setup,
                Price: features.Price,
                Atr14: features.Atr14,
                Rsi14: features.Rsi14,
                DistFromVwapAtr: features.DistFromVwapAtr,
                BbPctB: features.BbPctB,
                ImbalanceRatio: features.L2.ImbalanceRatio,
                OfiSignal: features.OfiSignal,
                SpreadPct: features.L1.SpreadPct));

            if (_config.EmitOrderIntents && decision.Side.HasValue)
            {
                var proposed = _orderBridge.BuildOrder(symbol, now, decision.Side.Value, features, decision.Setup);
                if (proposed is null)
                {
                    orderIntentRejectReason = "order-bridge-null";
                    _riskEvents.Add(new V3LiveRiskEventRow(now, symbol, "order-bridge-null", "bridge-failed", 0.0, 0.0));
                }
                else
                {
                    var check = _riskGuard.Evaluate(
                        symbol,
                        now,
                        features,
                        symbolState.RiskState,
                        proposed);

                    if (check.Passed)
                    {
                        orderIntentAccepted = true;
                        orderIntentId = proposed.IntentId;
                        _orderIntents.Add(proposed);
                        symbolState.RiskState.OpenRiskDollars += proposed.EstimatedRiskDollars;
                    }
                    else
                    {
                        orderIntentRejectReason = string.Join(";", check.Reasons);
                        _riskEvents.Add(new V3LiveRiskEventRow(
                            now,
                            symbol,
                            "order-rejected",
                            orderIntentRejectReason,
                            check.CurrentOpenRiskDollars,
                            check.ProposedRiskDollars));
                    }
                }
            }
        }

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

        var summary = new V3LiveRuntimeSummary(
            TimestampUtc: DateTime.UtcNow,
            ExitCode: exitCode,
            Symbols: _config.Symbols,
            Evaluations: _evaluations.Count,
            Passed: _evaluations.Count(x => x.PassedPreTradeGates),
            Failed: _evaluations.Count(x => !x.PassedPreTradeGates),
            Config: _config);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, $"v3live_runtime_summary_{stamp}.json"),
            JsonSerializer.Serialize(summary, options),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, $"v3live_evaluations_{stamp}.json"),
            JsonSerializer.Serialize(_evaluations, options),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, $"v3live_signals_{stamp}.json"),
            JsonSerializer.Serialize(_signals, options),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, $"v3live_order_intents_{stamp}.json"),
            JsonSerializer.Serialize(_orderIntents, options),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, $"v3live_risk_events_{stamp}.json"),
            JsonSerializer.Serialize(_riskEvents, options),
            cancellationToken);
    }

    private string ResolveSymbol(StrategyDataSlice dataSlice)
    {
        if (_context is not null && !string.IsNullOrWhiteSpace(_context.Symbol))
        {
            return _context.Symbol.Trim().ToUpperInvariant();
        }

        return _config.Symbols.FirstOrDefault() ?? string.Empty;
    }

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
        {
            return (false, "scanner-v2-no-candidate");
        }

        if (!ranked.Eligible)
        {
            var reason = string.IsNullOrWhiteSpace(ranked.RejectReason)
                ? "rejected"
                : ranked.RejectReason.Replace(' ', '-').ToLowerInvariant();
            return (false, $"scanner-v2-{reason}");
        }

        if (ranked.CompositeScore < _config.ScannerMinCompositeScore)
        {
            return (false, "scanner-v2-score-low");
        }

        return (true, string.Empty);
    }

    private ScannerV2L2DepthSnapshot? BuildScannerL2Snapshot(string symbol, StrategyDataSlice dataSlice, DateTime nowUtc)
    {
        var depthRows = dataSlice.DepthRows
            .Where(x => x.Price > 0)
            .ToArray();
        if (depthRows.Length == 0)
        {
            return null;
        }

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
        {
            return null;
        }

        return new ScannerV2L2DepthSnapshot(symbol, bids, asks, nowUtc);
    }

    private void UpdateMtfSignalEngine(string symbol, StrategyDataSlice dataSlice, V3LiveFeatureSnapshot features)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        foreach (var bar in dataSlice.HistoricalBars)
        {
            _mtfSignalEngine.UpdateFromHistoricalBar(symbol, bar);
        }

        if (features.Price > 0)
        {
            _mtfSignalEngine.Update(symbol, dataSlice.TimestampUtc, features.Price);
        }
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

    private sealed class V3LiveSymbolState
    {
        public int EntriesToday { get; set; }
        public DateTime? LastSignalUtc { get; set; }
        public V3LiveSymbolRiskState RiskState { get; } = new();
    }

    private sealed record V3LiveRuntimeSummary(
        DateTime TimestampUtc,
        int ExitCode,
        IReadOnlyList<string> Symbols,
        int Evaluations,
        int Passed,
        int Failed,
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

    private sealed record V3LiveRiskEventRow(
        DateTime TimestampUtc,
        string Symbol,
        string EventType,
        string Reason,
        double CurrentOpenRiskDollars,
        double ProposedRiskDollars);
}
