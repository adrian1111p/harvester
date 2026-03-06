using System.Text.Json;
using System.Text.Json.Serialization;
using Harvester.App.Backtest.Engine;
using Harvester.App.Backtest.Indicators;
using Harvester.App.Backtest.Runner;
using Harvester.App.Backtest.Strategies;

namespace Harvester.App.Strategy;

/// <summary>
/// Walk-forward optimization pipeline (Phase 3 Item #15).
///
/// Weekly automated cycle:
///   1. Window generation — expanding training windows + held-out OOS windows
///   2. In-sample optimization — <see cref="ParameterSweep.RunSweep"/> on train data
///   3. Out-of-sample validation — single backtest with best IS config on OOS data
///   4. Stability check — parameter drift across folds
///   5. Conditional config deployment — only if OOS edge persists + params are stable
///
/// Uses the existing <see cref="ParameterSweep"/> and <see cref="BacktestEngine"/>
/// infrastructure for backtest runs. Outputs a <see cref="WalkForwardReport"/>
/// with per-fold IS/OOS statistics, parameter stability metrics, and a deploy decision.
/// </summary>
public sealed class WalkForwardOrchestrator
{
    private readonly WalkForwardConfig _config;
    private readonly Action<string> _log;

    public WalkForwardOrchestrator(WalkForwardConfig? config = null, Action<string>? log = null)
    {
        _config = config ?? new WalkForwardConfig();
        _log = log ?? Console.WriteLine;
    }

    /// <summary>
    /// Execute the full walk-forward pipeline.
    /// </summary>
    public WalkForwardReport Run(string[]? symbols = null)
    {
        _log("═══════════════════════════════════════════════════════════");
        _log("  WALK-FORWARD OPTIMIZATION PIPELINE");
        _log("═══════════════════════════════════════════════════════════");

        // 1. Load all data
        _log("\n[1/5] Loading data...");
        var allData = ParameterSweep.LoadAllData(symbols, _log);
        if (allData.Count == 0)
        {
            _log("ERROR: No data loaded.");
            return WalkForwardReport.Failed("No data loaded");
        }

        // Determine date range from data
        var allTimestamps = allData.Values
            .SelectMany(d => d.Trigger.Select(b => b.Bar.Timestamp))
            .OrderBy(t => t)
            .ToArray();

        if (allTimestamps.Length < 100)
        {
            _log($"ERROR: Insufficient data ({allTimestamps.Length} bars).");
            return WalkForwardReport.Failed("Insufficient data");
        }

        var dataStart = allTimestamps[0];
        var dataEnd = allTimestamps[^1];
        _log($"  Data range: {dataStart:yyyy-MM-dd} → {dataEnd:yyyy-MM-dd} ({allTimestamps.Length:N0} bars across {allData.Count} symbols)");

        // 2. Generate windows
        _log("\n[2/5] Generating walk-forward windows...");
        var windows = GenerateWindows(dataStart, dataEnd);
        if (windows.Count == 0)
        {
            _log("ERROR: Could not generate any valid windows.");
            return WalkForwardReport.Failed("No valid windows");
        }

        foreach (var (i, w) in windows.Select((w, i) => (i, w)))
        {
            _log($"  Fold {i + 1}: Train {w.TrainStart:yyyy-MM-dd} → {w.TrainEnd:yyyy-MM-dd} | OOS {w.OosStart:yyyy-MM-dd} → {w.OosEnd:yyyy-MM-dd}");
        }

        // 3. Run walk-forward folds
        _log("\n[3/5] Running walk-forward folds...");
        var foldResults = new List<WalkForwardFoldResult>();

        for (int fold = 0; fold < windows.Count; fold++)
        {
            var window = windows[fold];
            _log($"\n  ── Fold {fold + 1}/{windows.Count} ──");

            // Partition data into train & OOS
            var trainData = PartitionData(allData, window.TrainStart, window.TrainEnd);
            var oosData = PartitionData(allData, window.OosStart, window.OosEnd);

            var trainBars = trainData.Values.Sum(d => d.Trigger.Length);
            var oosBars = oosData.Values.Sum(d => d.Trigger.Length);
            _log($"  Train: {trainBars:N0} bars | OOS: {oosBars:N0} bars");

            if (trainBars < _config.MinTrainBars)
            {
                _log($"  SKIP: Train data too small ({trainBars} < {_config.MinTrainBars})");
                continue;
            }
            if (oosBars < _config.MinOosBars)
            {
                _log($"  SKIP: OOS data too small ({oosBars} < {_config.MinOosBars})");
                continue;
            }

            // IS optimization
            _log("  Running IS parameter sweep...");
            var configs = GenerateParamGrid();
            var isResults = ParameterSweep.RunSweep(trainData, configs);

            if (isResults.Count == 0)
            {
                _log("  SKIP: No IS results.");
                continue;
            }

            var bestIs = isResults[0]; // already sorted by Sharpe desc
            _log($"  Best IS: Sharpe={bestIs.Stats.Sharpe:F2} PnL=${bestIs.Stats.TotalPnl:F0} " +
                 $"WR={bestIs.Stats.WinRate:P0} PF={bestIs.Stats.ProfitFactor:F2} " +
                 $"({bestIs.Stats.TotalTrades} trades)");
            _log($"  Config: T={bestIs.Config.TrailR:F2} G={bestIs.Config.GivebackPct:P0} " +
                 $"TP1={bestIs.Config.Tp1R:F2} TP2={bestIs.Config.Tp2R:F2} " +
                 $"Stop={bestIs.Config.HardStopR:F2} BE={bestIs.Config.BreakevenR:F2}");

            // OOS validation
            _log("  Running OOS validation...");
            var oosStats = RunSingleConfig(oosData, bestIs.Config);
            _log($"  OOS: Sharpe={oosStats.Sharpe:F2} PnL=${oosStats.TotalPnl:F0} " +
                 $"WR={oosStats.WinRate:P0} PF={oosStats.ProfitFactor:F2} " +
                 $"({oosStats.TotalTrades} trades)");

            var passed = EvaluateOosGate(oosStats);
            _log($"  OOS gate: {(passed ? "PASSED ✓" : "FAILED ✗")}");

            foldResults.Add(new WalkForwardFoldResult
            {
                FoldIndex = fold,
                Window = window,
                BestIsConfig = bestIs.Config,
                IsStats = bestIs.Stats,
                OosStats = oosStats,
                OosPassed = passed,
            });
        }

        if (foldResults.Count == 0)
        {
            _log("\nERROR: No valid folds completed.");
            return WalkForwardReport.Failed("No valid folds completed");
        }

        // 4. Stability check
        _log("\n[4/5] Parameter stability analysis...");
        var stability = AnalyzeStability(foldResults);
        _log($"  TrailR  drift: {stability.TrailRDrift:F3} (threshold {_config.MaxParamDrift:F3})");
        _log($"  Tp1R   drift: {stability.Tp1RDrift:F3}");
        _log($"  StopR  drift: {stability.HardStopRDrift:F3}");
        _log($"  Params stable: {(stability.IsStable ? "YES" : "NO — edge may be fragile")}");

        // 5. Deploy decision
        _log("\n[5/5] Deploy decision...");
        var passedFolds = foldResults.Count(f => f.OosPassed);
        var passRate = (double)passedFolds / foldResults.Count;
        var shouldDeploy = passRate >= _config.MinOosPassRate && stability.IsStable;

        StrategyConfig? deployedConfig = null;
        if (shouldDeploy)
        {
            // Use the config from the most recent passing fold
            var latestPassed = foldResults.LastOrDefault(f => f.OosPassed);
            deployedConfig = latestPassed?.BestIsConfig;

            if (deployedConfig != null)
            {
                _log($"  DEPLOY: Using config from fold {latestPassed!.FoldIndex + 1}");
                _log($"    TrailR={deployedConfig.TrailR:F2} GivebackPct={deployedConfig.GivebackPct:P0} " +
                     $"Tp1R={deployedConfig.Tp1R:F2} Tp2R={deployedConfig.Tp2R:F2} " +
                     $"HardStopR={deployedConfig.HardStopR:F2} BreakevenR={deployedConfig.BreakevenR:F2}");
            }
        }
        else
        {
            _log($"  NO DEPLOY: OOS pass rate {passRate:P0} ({passedFolds}/{foldResults.Count}), " +
                 $"stable={stability.IsStable}");
            _log("  Keeping current configuration.");
        }

        var report = new WalkForwardReport
        {
            RunTimestampUtc = DateTime.UtcNow,
            Symbols = allData.Keys.ToArray(),
            DataRange = (dataStart, dataEnd),
            TotalFolds = foldResults.Count,
            PassedFolds = passedFolds,
            OosPassRate = passRate,
            Folds = foldResults,
            Stability = stability,
            ShouldDeploy = shouldDeploy,
            DeployedConfig = deployedConfig,
        };

        _log($"\n  Summary: {passedFolds}/{foldResults.Count} folds passed OOS " +
             $"(avg OOS Sharpe: {foldResults.Average(f => f.OosStats.Sharpe):F2})");
        _log("═══════════════════════════════════════════════════════════");

        return report;
    }

    /// <summary>
    /// Run the pipeline and save the report to a JSON file.
    /// </summary>
    public WalkForwardReport RunAndExport(string outputDirectory, string[]? symbols = null)
    {
        var report = Run(symbols);

        Directory.CreateDirectory(outputDirectory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(outputDirectory, $"walk_forward_report_{stamp}.json");
        var json = JsonSerializer.Serialize(report, WalkForwardJsonContext.Default.WalkForwardReport);
        File.WriteAllText(path, json);
        _log($"\nReport saved: {path}");

        if (report.ShouldDeploy && report.DeployedConfig != null)
        {
            var configPath = Path.Combine(outputDirectory, "walk_forward_deployed_config.json");
            var configJson = JsonSerializer.Serialize(
                DeployedConfigDto.From(report.DeployedConfig),
                WalkForwardJsonContext.Default.DeployedConfigDto);
            File.WriteAllText(configPath, configJson);
            _log($"Config deployed: {configPath}");
        }

        return report;
    }

    // ─── Window Generation ────────────────────────────────────────────────

    private List<WalkForwardWindow> GenerateWindows(DateTime dataStart, DateTime dataEnd)
    {
        var windows = new List<WalkForwardWindow>();
        var trainDays = _config.TrainWindowDays;
        var oosDays = _config.OosWindowDays;

        // Start the first training window at dataStart
        var trainStart = dataStart.Date;

        for (int fold = 0; fold < _config.MaxFolds; fold++)
        {
            var trainEnd = trainStart.AddDays(trainDays).AddSeconds(-1);
            var oosStart = trainEnd.Date.AddDays(1);
            var oosEnd = oosStart.AddDays(oosDays).AddSeconds(-1);

            if (oosEnd > dataEnd) break;

            windows.Add(new WalkForwardWindow
            {
                TrainStart = trainStart,
                TrainEnd = trainEnd,
                OosStart = oosStart,
                OosEnd = oosEnd,
            });

            // Advance by the OOS window size (rolling forward)
            trainStart = trainStart.AddDays(_config.StepForwardDays);
        }

        return windows;
    }

    // ─── Data Partitioning ────────────────────────────────────────────────

    private static Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)>
        PartitionData(
            Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> allData,
            DateTime start, DateTime end)
    {
        var result = new Dictionary<string, (EnrichedBar[], EnrichedBar[]?, EnrichedBar[]?, EnrichedBar[]?, EnrichedBar[]?)>();

        foreach (var (sym, data) in allData)
        {
            var trigger = FilterByDate(data.Trigger, start, end);
            if (trigger.Length == 0) continue;

            result[sym] = (
                trigger,
                data.Ctx5m != null ? FilterByDate(data.Ctx5m, start, end) : null,
                data.Ctx15m != null ? FilterByDate(data.Ctx15m, start, end) : null,
                data.Ctx1h != null ? FilterByDate(data.Ctx1h, start, end) : null,
                data.Ctx1d != null ? FilterByDate(data.Ctx1d, start, end) : null
            );
        }

        return result;
    }

    private static EnrichedBar[] FilterByDate(EnrichedBar[] bars, DateTime start, DateTime end)
    {
        // Bars are pre-sorted by timestamp. Use linear scan (typically small datasets).
        int startIdx = -1, endIdx = -1;
        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i].Bar.Timestamp >= start)
            {
                startIdx = i;
                break;
            }
        }
        if (startIdx < 0) return [];

        for (int i = bars.Length - 1; i >= startIdx; i--)
        {
            if (bars[i].Bar.Timestamp <= end)
            {
                endIdx = i;
                break;
            }
        }
        if (endIdx < startIdx) return [];

        return bars[startIdx..(endIdx + 1)];
    }

    // ─── Parameter Grid ──────────────────────────────────────────────────

    private List<StrategyConfig> GenerateParamGrid()
    {
        var configs = new List<StrategyConfig>();
        var seen = new HashSet<string>();

        // Baseline
        configs.Add(new StrategyConfig());
        seen.Add(ConfigKey(configs[0]));

        foreach (var trail in _config.TrailRValues)
        foreach (var giveback in _config.GivebackPctValues)
        foreach (var tp1 in _config.Tp1RValues)
        foreach (var hardStop in _config.HardStopRValues)
        {
            var cfg = new StrategyConfig
            {
                TrailR = trail,
                GivebackPct = giveback,
                Tp1R = tp1,
                Tp2R = Math.Max(tp1 + 1.0, 3.0),
                HardStopR = hardStop,
                BreakevenR = hardStop * 0.8,
            };
            var key = ConfigKey(cfg);
            if (seen.Add(key))
                configs.Add(cfg);
        }

        return configs;
    }

    private static string ConfigKey(StrategyConfig c) =>
        $"{c.TrailR:F2}|{c.GivebackPct:F2}|{c.Tp1R:F2}|{c.HardStopR:F2}|{c.BreakevenR:F2}";

    // ─── Single Config Evaluation ────────────────────────────────────────

    private static BacktestStatistics RunSingleConfig(
        Dictionary<string, (EnrichedBar[] Trigger, EnrichedBar[]? Ctx5m, EnrichedBar[]? Ctx15m, EnrichedBar[]? Ctx1h, EnrichedBar[]? Ctx1d)> data,
        StrategyConfig config)
    {
        var strategy = new ConductStrategyV3(config);
        var allTrades = new List<BacktestTradeResult>();

        foreach (var (sym, (trigger, ctx5m, ctx15m, ctx1h, ctx1d)) in data)
        {
            var bt = BacktestEngine.RunBacktest(sym, strategy, trigger, "1m", ctx5m, ctx15m, ctx1h, ctx1d, config.AccountSize);
            allTrades.AddRange(bt.Trades);
        }

        return BacktestEngine.ComputeStatistics(allTrades, config.AccountSize);
    }

    // ─── OOS Gate Evaluation ─────────────────────────────────────────────

    private bool EvaluateOosGate(BacktestStatistics stats)
    {
        if (stats.TotalTrades < _config.MinOosTrades) return false;
        if (stats.Sharpe < _config.MinOosSharpe) return false;
        if (stats.ProfitFactor < _config.MinOosProfitFactor) return false;
        if (stats.WinRate < _config.MinOosWinRate) return false;
        return true;
    }

    // ─── Stability Analysis ──────────────────────────────────────────────

    private WalkForwardStability AnalyzeStability(List<WalkForwardFoldResult> folds)
    {
        if (folds.Count < 2)
        {
            return new WalkForwardStability
            {
                TrailRDrift = 0, Tp1RDrift = 0, HardStopRDrift = 0,
                IsStable = true,
            };
        }

        var trailRs = folds.Select(f => f.BestIsConfig.TrailR).ToArray();
        var tp1Rs = folds.Select(f => f.BestIsConfig.Tp1R).ToArray();
        var stopRs = folds.Select(f => f.BestIsConfig.HardStopR).ToArray();

        var trailDrift = CoefficientOfVariation(trailRs);
        var tp1Drift = CoefficientOfVariation(tp1Rs);
        var stopDrift = CoefficientOfVariation(stopRs);

        var maxDrift = Math.Max(trailDrift, Math.Max(tp1Drift, stopDrift));

        return new WalkForwardStability
        {
            TrailRDrift = trailDrift,
            Tp1RDrift = tp1Drift,
            HardStopRDrift = stopDrift,
            IsStable = maxDrift <= _config.MaxParamDrift,
        };
    }

    private static double CoefficientOfVariation(double[] values)
    {
        if (values.Length < 2) return 0;
        var mean = values.Average();
        if (Math.Abs(mean) < 1e-9) return 0;
        var variance = values.Select(v => (v - mean) * (v - mean)).Average();
        return Math.Sqrt(variance) / Math.Abs(mean);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Configuration
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Configuration for the walk-forward optimization pipeline.
/// </summary>
public sealed record WalkForwardConfig
{
    // ── Window geometry ──
    /// <summary>Training window size in calendar days.</summary>
    public int TrainWindowDays { get; init; } = 60;
    /// <summary>Out-of-sample window size in calendar days.</summary>
    public int OosWindowDays { get; init; } = 5;
    /// <summary>Days to step forward between folds.</summary>
    public int StepForwardDays { get; init; } = 5;
    /// <summary>Maximum number of folds to generate.</summary>
    public int MaxFolds { get; init; } = 20;

    // ── Data minimums ──
    public int MinTrainBars { get; init; } = 500;
    public int MinOosBars { get; init; } = 50;

    // ── OOS gate thresholds ──
    public int MinOosTrades { get; init; } = 3;
    public double MinOosSharpe { get; init; } = 0.0;
    public double MinOosProfitFactor { get; init; } = 1.0;
    public double MinOosWinRate { get; init; } = 0.35;

    /// <summary>Minimum fraction of folds that must pass OOS gate to deploy.</summary>
    public double MinOosPassRate { get; init; } = 0.50;

    // ── Stability thresholds ──
    /// <summary>Max coefficient of variation for params across folds.</summary>
    public double MaxParamDrift { get; init; } = 0.40;

    // ── Parameter grid ──
    public double[] TrailRValues { get; init; } = [0.5, 0.75, 1.0, 1.5, 2.0];
    public double[] GivebackPctValues { get; init; } = [0.40, 0.50, 0.60, 0.70];
    public double[] Tp1RValues { get; init; } = [1.0, 1.5, 2.0, 2.5];
    public double[] HardStopRValues { get; init; } = [0.8, 1.0, 1.5, 2.0];
}

// ═════════════════════════════════════════════════════════════════════════════
// Result Types
// ═════════════════════════════════════════════════════════════════════════════

public sealed class WalkForwardReport
{
    public DateTime RunTimestampUtc { get; init; }
    public string[] Symbols { get; init; } = [];
    public (DateTime Start, DateTime End) DataRange { get; init; }
    public int TotalFolds { get; init; }
    public int PassedFolds { get; init; }
    public double OosPassRate { get; init; }
    public List<WalkForwardFoldResult> Folds { get; init; } = [];
    public WalkForwardStability Stability { get; init; } = new();
    public bool ShouldDeploy { get; init; }
    public StrategyConfig? DeployedConfig { get; init; }
    public string? Error { get; init; }

    public static WalkForwardReport Failed(string error) => new()
    {
        RunTimestampUtc = DateTime.UtcNow,
        Error = error,
    };
}

public sealed class WalkForwardFoldResult
{
    public int FoldIndex { get; init; }
    public WalkForwardWindow Window { get; init; } = new();
    public StrategyConfig BestIsConfig { get; init; } = new();
    public BacktestStatistics IsStats { get; init; } = null!;
    public BacktestStatistics OosStats { get; init; } = null!;
    public bool OosPassed { get; init; }
}

public sealed class WalkForwardWindow
{
    public DateTime TrainStart { get; init; }
    public DateTime TrainEnd { get; init; }
    public DateTime OosStart { get; init; }
    public DateTime OosEnd { get; init; }
}

public sealed class WalkForwardStability
{
    public double TrailRDrift { get; init; }
    public double Tp1RDrift { get; init; }
    public double HardStopRDrift { get; init; }
    public bool IsStable { get; init; }
}

/// <summary>
/// Deployed config DTO for JSON serialization (flat structure with only strategy-relevant params).
/// </summary>
public sealed class DeployedConfigDto
{
    public DateTime DeployedUtc { get; init; }
    public double TrailR { get; init; }
    public double GivebackPct { get; init; }
    public double Tp1R { get; init; }
    public double Tp2R { get; init; }
    public double HardStopR { get; init; }
    public double BreakevenR { get; init; }
    public double RvolMin { get; init; }
    public double AdxThreshold { get; init; }
    public int MaxHoldBars { get; init; }

    public static DeployedConfigDto From(StrategyConfig cfg) => new()
    {
        DeployedUtc = DateTime.UtcNow,
        TrailR = cfg.TrailR,
        GivebackPct = cfg.GivebackPct,
        Tp1R = cfg.Tp1R,
        Tp2R = cfg.Tp2R,
        HardStopR = cfg.HardStopR,
        BreakevenR = cfg.BreakevenR,
        RvolMin = cfg.RvolMin,
        AdxThreshold = cfg.AdxThreshold,
        MaxHoldBars = cfg.MaxHoldBars,
    };
}

// ═════════════════════════════════════════════════════════════════════════════
// JSON Serialization Context
// ═════════════════════════════════════════════════════════════════════════════

[JsonSerializable(typeof(WalkForwardReport))]
[JsonSerializable(typeof(DeployedConfigDto))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class WalkForwardJsonContext : JsonSerializerContext;
