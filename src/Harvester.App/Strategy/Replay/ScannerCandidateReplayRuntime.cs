using System.Text.Json;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class ScannerCandidateReplayRuntime :
    IStrategyRuntime,
    IReplayOrderSignalSource,
    IReplaySimulationFeedbackSink,
    IReplayScannerSelectionSource
{
    private ReplayScannerSymbolSelectionSnapshotRow _selectionSnapshot;
    private ScannerV2SelectionSnapshot? _selectionSnapshotV2;
    private readonly Ovl001FlattenReversalAndGivebackCapStrategy _overlay;
    private readonly ReplayMtfCandleSignalEngine _mtfSignalEngine;
    private readonly ReplayDayTradingPipeline _pipeline;
    private readonly ReplayRamSessionState _ramSessionState;
    private readonly ReplayTradeEpisodeRecorder _episodeRecorder;
    private readonly Dictionary<string, double> _positionBySymbol;
    private double _positionQuantity;
    private double _averagePrice;

    // Deferred V2 scanner init state (I/O moved to InitializeAsync)
    private readonly ReplayScannerSymbolSelectionSnapshotRow _v1Snapshot;
    private readonly int _topN;
    private readonly double _minScore;
    private readonly string _candidatesInputPath;

    public ScannerCandidateReplayRuntime(
        string candidatesInputPath,
        int topN,
        double minScore,
        double orderQuantity,
        string orderSide,
        string orderType,
        string timeInForce,
        double limitOffsetBps)
    {
        // â”€â”€ V1 selection (backward compat) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        _candidatesInputPath = candidatesInputPath;
        _topN = topN;
        _minScore = minScore;

        var selectionModule = new ReplayScannerSymbolSelectionModule(candidatesInputPath, topN, minScore);
        _v1Snapshot = selectionModule.GetSnapshot();

        // V2 scanner selection (with disk I/O) is deferred to InitializeAsync
        _selectionSnapshot = _v1Snapshot;
        _selectionSnapshotV2 = null;

        _overlay = new Ovl001FlattenReversalAndGivebackCapStrategy(BuildOverlayConfigFromEnvironment());
        _mtfSignalEngine = new ReplayMtfCandleSignalEngine();
        var entry = new ReplayScannerSingleShotEntryStrategy(
            orderQuantity,
            orderSide,
            orderType,
            timeInForce,
            limitOffsetBps,
            _mtfSignalEngine,
            BuildScannerRequireMtfAlignmentFromEnvironment(),
            BuildScannerRequireBuySetupConfirmationFromEnvironment(),
            BuildScannerRequireEnhancedBuySetupConfirmationFromEnvironment(),
            BuildScannerRequireSellSetupConfirmationFromEnvironment(),
            BuildScannerRequireBreakoutConfirmationFromEnvironment(),
            BuildScannerRequireOneTwoThreeConfirmationFromEnvironment());
        var tmgStrategies = TmgStrategyFactory.CreateAll(_mtfSignalEngine);
        var endOfDay = new Eod001ForceFlatStrategy(BuildEndOfDayConfigFromEnvironment());
        _pipeline = new ReplayDayTradingPipeline(
            globalSafetyOverlays: [_overlay],
            entryStrategies: [entry],
            tradeManagementStrategies: tmgStrategies,
            endOfDayStrategies: [endOfDay]);
        _ramSessionState = new ReplayRamSessionState(maxBarsPerSymbol: 2000, maxBucketMinutes: 60, imbalanceTopN: 5);
        _episodeRecorder = new ReplayTradeEpisodeRecorder(Path.Combine(Directory.GetCurrentDirectory(), "temp", "episodes"));
        _positionBySymbol = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        _positionQuantity = 0;
        _averagePrice = 0;
    }

    public Task InitializeAsync(StrategyRuntimeContext context, CancellationToken cancellationToken)
    {
        // ── V2 scanner selection (deferred from constructor to keep I/O out of ctors) ──
        var useV2 = TryReadEnvironmentBool("SCN_V2_ENABLED", false);
        if (useV2)
        {
            var v2Config = BuildScannerSelectionV2ConfigFromEnvironment(_topN, _minScore);
            var v2Engine = new ScannerSelectionEngineV2(v2Config);
            var fileCandidates = _v1Snapshot.RankedSymbols
                .Select(r => new ScannerV2CandidateFileRow
                {
                    Symbol = r.Symbol,
                    WeightedScore = r.WeightedScore,
                    Eligible = r.Eligible,
                    AverageRank = r.AverageRank
                })
                .ToList();

            var biasEntries = LoadScannerV2BiasEntries();

            var emptyL1 = new Dictionary<string, ScannerV2L1Snapshot>();
            var emptyL2 = new Dictionary<string, ScannerV2L2DepthSnapshot>();

            _selectionSnapshotV2 = v2Engine.Evaluate(
                fileCandidates,
                emptyL1,
                emptyL2,
                biasEntries,
                sessionOpenUtc: DateTime.MinValue,
                sessionCloseUtc: DateTime.MinValue,
                nowUtc: DateTime.UtcNow,
                sourcePath: _candidatesInputPath);

            _selectionSnapshot = ScannerSelectionEngineV2.ToV1Snapshot(_selectionSnapshotV2);

            Console.WriteLine($"[INFO] Scanner Selection V2.0: {_selectionSnapshotV2.SelectedCount} selected " +
                $"from {_selectionSnapshotV2.TotalCandidates} candidates " +
                $"({_selectionSnapshotV2.EligibleCandidates} eligible, phase={_selectionSnapshotV2.SessionPhase})");

            ExportScannerV2Snapshot(_selectionSnapshotV2);
        }

        return Task.CompletedTask;
    }

    public Task OnScheduledEventAsync(string eventName, StrategyRuntimeContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OnDataAsync(StrategyDataSlice dataSlice, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task OnShutdownAsync(StrategyRuntimeContext context, int exitCode, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public ReplayScannerSymbolSelectionSnapshotRow GetScannerSelectionSnapshot()
    {
        return _selectionSnapshot;
    }

    public IReadOnlyList<ReplayOrderIntent> GetReplayOrderIntents(StrategyDataSlice dataSlice, StrategyRuntimeContext context)
    {
        var symbol = (context.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        if (!_selectionSnapshot.SelectedSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
        {
            return [];
        }

        var bidPrice = ResolveBidPrice(dataSlice);
        var askPrice = ResolveAskPrice(dataSlice);
        var markPrice = ResolveMarkPrice(dataSlice, bidPrice, askPrice);
        if (markPrice <= 0)
        {
            return [];
        }

        if (bidPrice <= 0)
        {
            bidPrice = markPrice;
        }

        if (askPrice <= 0)
        {
            askPrice = markPrice;
        }

        if (dataSlice.HistoricalBars.Count > 0)
        {
            foreach (var historicalBar in dataSlice.HistoricalBars)
            {
                _mtfSignalEngine.UpdateFromHistoricalBar(symbol, historicalBar);
            }
        }
        else
        {
            _mtfSignalEngine.Update(symbol, dataSlice.TimestampUtc, markPrice);
        }

        var latestBar = dataSlice.HistoricalBars.LastOrDefault();

        var dayTradingContext = new ReplayDayTradingContext(
            TimestampUtc: dataSlice.TimestampUtc,
            Symbol: symbol,
            MarkPrice: markPrice,
            BidPrice: bidPrice,
            AskPrice: askPrice,
            PositionQuantity: _positionQuantity,
            AveragePrice: _averagePrice,
            BarOpen: latestBar?.Open ?? markPrice,
            BarHigh: latestBar?.High ?? markPrice,
            BarLow: latestBar?.Low ?? markPrice,
            BarClose: latestBar?.Close ?? markPrice,
            BarVolume: latestBar is null ? 0.0 : (double)latestBar.Volume);
        var intents = _pipeline.Evaluate(dayTradingContext, _selectionSnapshot);

        var gateCodes = intents
            .Select(x => x.Source)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _ramSessionState.UpdateSlice(symbol, dataSlice, gateCodes);

        return intents;
    }

    public void OnReplaySliceResult(StrategyDataSlice dataSlice, ReplaySliceSimulationResult result, string activeSymbol)
    {
        _positionQuantity = result.Portfolio.PositionQuantity;
        _averagePrice = result.Portfolio.AveragePrice;

        var symbol = (activeSymbol ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            _positionBySymbol[symbol] = result.Portfolio.PositionQuantity;
            _episodeRecorder.ProcessSlice(symbol, result, _ramSessionState.GetBuckets(symbol));
        }

        _overlay.OnPositionEvent(
            symbol,
            result.Portfolio.TimestampUtc,
            result.Portfolio.PositionQuantity,
            result.Portfolio.AveragePrice,
            result.Fills);
    }

    private static double ResolveMarkPrice(StrategyDataSlice dataSlice, double bidPrice, double askPrice)
    {
        var last = dataSlice.TopTicks
            .Where(x => x.Field == 4)
            .Select(x => x.Price)
            .LastOrDefault(x => x > 0);
        if (last > 0)
        {
            return last;
        }

        if (bidPrice > 0 && askPrice > 0)
        {
            return (bidPrice + askPrice) / 2.0;
        }

        return dataSlice.HistoricalBars.LastOrDefault()?.Close ?? 0;
    }

    private static double ResolveBidPrice(StrategyDataSlice dataSlice)
    {
        var depthBid = dataSlice.DepthRows
            .Where(x => x.Side == 1 && x.Price > 0 && x.Size > 0)
            .OrderByDescending(x => x.TimestampUtc)
            .ThenBy(x => x.Position)
            .Select(x => x.Price)
            .FirstOrDefault();
        if (depthBid > 0)
        {
            return depthBid;
        }

        return dataSlice.TopTicks
            .Where(x => x.Field == 1)
            .Select(x => x.Price)
            .LastOrDefault(x => x > 0);
    }

    private static double ResolveAskPrice(StrategyDataSlice dataSlice)
    {
        var depthAsk = dataSlice.DepthRows
            .Where(x => x.Side == 0 && x.Price > 0 && x.Size > 0)
            .OrderByDescending(x => x.TimestampUtc)
            .ThenBy(x => x.Position)
            .Select(x => x.Price)
            .FirstOrDefault();
        if (depthAsk > 0)
        {
            return depthAsk;
        }

        return dataSlice.TopTicks
            .Where(x => x.Field == 2)
            .Select(x => x.Price)
            .LastOrDefault(x => x > 0);
    }

    private static Ovl001FlattenConfig BuildOverlayConfigFromEnvironment()
    {
        return new Ovl001FlattenConfig(
            ImmediateWindowSec: Math.Max(1, TryReadEnvironmentInt("OVL_001_IMMEDIATE_WINDOW_SEC", 5)),
            ImmediateAdverseMovePct: Math.Max(0.0, TryReadEnvironmentDouble("OVL_001_IMMEDIATE_ADVERSE_MOVE_PCT", 0.002)),
            ImmediateAdverseMoveUsd: Math.Max(0.0, TryReadEnvironmentDouble("OVL_001_IMMEDIATE_ADVERSE_MOVE_USD", 10.0)),
            GivebackPctOfNotional: Math.Max(0.0, TryReadEnvironmentDouble("OVL_001_GIVEBACK_PCT_OF_NOTIONAL", 0.01)),
            GivebackUsdCap: Math.Max(0.0, TryReadEnvironmentDouble("OVL_001_GIVEBACK_USD_CAP", 30.0)),
            TrailingActivatesOnlyAfterProfit: TryReadEnvironmentBool("OVL_001_TRAILING_ACTIVATES_ONLY_AFTER_PROFIT", true),
            FlattenRoute: TryReadEnvironmentString("OVL_001_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("OVL_001_FLATTEN_TIF", "DAY+"),
            FlattenOrderType: TryReadEnvironmentString("OVL_001_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static bool BuildScannerRequireMtfAlignmentFromEnvironment()
    {
        return TryReadEnvironmentBool("SCN_001_REQUIRE_MTF_ALIGNMENT", false);
    }

    private static bool BuildScannerRequireBuySetupConfirmationFromEnvironment()
    {
        return TryReadEnvironmentBool("SCN_001_REQUIRE_BUY_SETUP_CONFIRMATION", false);
    }

    private static bool BuildScannerRequireEnhancedBuySetupConfirmationFromEnvironment()
    {
        return TryReadEnvironmentBool("SCN_001_REQUIRE_ENHANCED_BUY_SETUP_CONFIRMATION", false);
    }

    private static bool BuildScannerRequireSellSetupConfirmationFromEnvironment()
    {
        return TryReadEnvironmentBool("SCN_001_REQUIRE_SELL_SETUP_CONFIRMATION", false);
    }

    private static bool BuildScannerRequireBreakoutConfirmationFromEnvironment()
    {
        return TryReadEnvironmentBool("SCN_001_REQUIRE_BREAKOUT_CONFIRMATION", false);
    }

    private static bool BuildScannerRequireOneTwoThreeConfirmationFromEnvironment()
    {
        return TryReadEnvironmentBool("SCN_001_REQUIRE_123_CONFIRMATION", false);
    }

    private static Eod001ForceFlatConfig BuildEndOfDayConfigFromEnvironment()
    {
        return new Eod001ForceFlatConfig(
            Enabled: TryReadEnvironmentBool("EOD_001_ENABLED", true),
            SessionCloseHourUtc: Math.Clamp(TryReadEnvironmentInt("EOD_001_SESSION_CLOSE_HOUR_UTC", 21), 0, 23),
            SessionCloseMinuteUtc: Math.Clamp(TryReadEnvironmentInt("EOD_001_SESSION_CLOSE_MINUTE_UTC", 0), 0, 59),
            FlattenLeadMinutes: Math.Max(0, TryReadEnvironmentInt("EOD_001_FLATTEN_LEAD_MINUTES", 5)),
            FlattenRoute: TryReadEnvironmentString("EOD_001_FLATTEN_ROUTE", "SMART"),
            FlattenTif: TryReadEnvironmentString("EOD_001_FLATTEN_TIF", "DAY+"),
            FlattenOrderType: TryReadEnvironmentString("EOD_001_FLATTEN_ORDER_TYPE", "MARKET"));
    }

    private static bool TryReadEnvironmentBool(string name, bool fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "1" or "Y" or "YES" or "TRUE" => true,
            "0" or "N" or "NO" or "FALSE" => false,
            _ => fallback
        };
    }

    private static int TryReadEnvironmentInt(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static double TryReadEnvironmentDouble(string name, double fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return double.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string TryReadEnvironmentString(string name, string fallback)
    {
        if (name.EndsWith("_FLATTEN_ROUTE", StringComparison.OrdinalIgnoreCase))
        {
            return "MARKET";
        }

        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // SCANNER SELECTION V2 HELPERS
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static ScannerSelectionV2Config BuildScannerSelectionV2ConfigFromEnvironment(int topN, double minScore)
    {
        return new ScannerSelectionV2Config
        {
            TopN = Math.Max(1, TryReadEnvironmentInt("SCN_V2_TOP_N", topN)),
            MinFileScore = TryReadEnvironmentDouble("SCN_V2_MIN_FILE_SCORE", minScore),
            MinPrice = TryReadEnvironmentDouble("SCN_V2_MIN_PRICE", 0.50),
            MaxPrice = TryReadEnvironmentDouble("SCN_V2_MAX_PRICE", 10.0),
            MaxSpreadPct = TryReadEnvironmentDouble("SCN_V2_MAX_SPREAD_PCT", 0.03),
            MinVolume = TryReadEnvironmentDouble("SCN_V2_MIN_VOLUME", 1000),
            MinBidDepthShares = TryReadEnvironmentDouble("SCN_V2_MIN_BID_DEPTH", 500),
            MinAskDepthShares = TryReadEnvironmentDouble("SCN_V2_MIN_ASK_DEPTH", 500),
            DepthLevels = Math.Max(1, TryReadEnvironmentInt("SCN_V2_DEPTH_LEVELS", 5)),
            MaxAdverseMomentumBps = TryReadEnvironmentDouble("SCN_V2_MAX_ADVERSE_MOMENTUM_BPS", 50.0),
            MaxBiasShift = TryReadEnvironmentDouble("SCN_V2_MAX_BIAS_SHIFT", 20.0),
            OpenPhaseMinutes = Math.Max(0, TryReadEnvironmentInt("SCN_V2_OPEN_PHASE_MINUTES", 15)),
            ClosePhaseMinutes = Math.Max(0, TryReadEnvironmentInt("SCN_V2_CLOSE_PHASE_MINUTES", 30)),
            OpenPhaseScoreMultiplier = TryReadEnvironmentDouble("SCN_V2_OPEN_PHASE_MULTIPLIER", 0.90),
            ClosePhaseScoreMultiplier = TryReadEnvironmentDouble("SCN_V2_CLOSE_PHASE_MULTIPLIER", 0.85),
            MaxExchangeConcentration = Math.Clamp(TryReadEnvironmentDouble("SCN_V2_MAX_EXCHANGE_CONCENTRATION", 0.60), 0.0, 1.0),
            // Weights (must sum to ~1.0)
            FileScoreWeight = TryReadEnvironmentDouble("SCN_V2_W_FILE_SCORE", 0.25),
            SpreadWeight = TryReadEnvironmentDouble("SCN_V2_W_SPREAD", 0.15),
            VolumeWeight = TryReadEnvironmentDouble("SCN_V2_W_VOLUME", 0.10),
            DepthWeight = TryReadEnvironmentDouble("SCN_V2_W_DEPTH", 0.10),
            MomentumWeight = TryReadEnvironmentDouble("SCN_V2_W_MOMENTUM", 0.10),
            BiasWeight = TryReadEnvironmentDouble("SCN_V2_W_BIAS", 0.10),
            TimeOfDayWeight = TryReadEnvironmentDouble("SCN_V2_W_TIME_OF_DAY", 0.05),
            DiversificationWeight = TryReadEnvironmentDouble("SCN_V2_W_DIVERSIFICATION", 0.05),
            ConsistencyWeight = TryReadEnvironmentDouble("SCN_V2_W_CONSISTENCY", 0.10)
        };
    }

    private static IReadOnlyList<ScannerV2SymbolBias> LoadScannerV2BiasEntries()
    {
        var biasPath = Environment.GetEnvironmentVariable("SCN_V2_BIAS_STORE_PATH");
        if (string.IsNullOrWhiteSpace(biasPath))
        {
            // Try default path
            biasPath = Path.Combine(Directory.GetCurrentDirectory(), "temp", "scanner_v2_bias_store.json");
        }

        if (!File.Exists(biasPath))
        {
            return Array.Empty<ScannerV2SymbolBias>();
        }

        try
        {
            var json = File.ReadAllText(biasPath);
            return JsonSerializer.Deserialize<ScannerV2SymbolBias[]>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? Array.Empty<ScannerV2SymbolBias>();
        }
        catch
        {
            Console.WriteLine($"[WARN] Failed to load scanner V2 bias store from {biasPath}; using empty bias.");
            return Array.Empty<ScannerV2SymbolBias>();
        }
    }

    private static void ExportScannerV2Snapshot(ScannerV2SelectionSnapshot snapshot)
    {
        try
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "temp", "scanner_v2");
            Directory.CreateDirectory(dir);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(dir, $"scanner_v2_selection_{timestamp}.json");
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            File.WriteAllText(path, json);
            Console.WriteLine($"[OK] Scanner V2 selection export: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Failed to export scanner V2 snapshot: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the V2 selection snapshot if V2 is enabled, null otherwise.
    /// </summary>
    public ScannerV2SelectionSnapshot? GetScannerSelectionSnapshotV2()
    {
        return _selectionSnapshotV2;
    }
}
