using System.Text.Json;
using Harvester.App.Backtest.Engine;

namespace Harvester.App.Strategy;

public sealed class V3LiveRuntime : IStrategyRuntime
{
    private readonly V3LiveConfig _config;
    private readonly V3LiveFeatureBuilder _featureBuilder = new();
    private readonly V3LiveSignalEngine _signalEngine = new();
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

        var passedPreTrade = reasonCodes.Count == 0;

        var decision = _signalEngine.Evaluate(features, _config);
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
            WriteIndented = true
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
