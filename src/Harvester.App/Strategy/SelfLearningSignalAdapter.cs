using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Harvester.App.Strategy;

/// <summary>
/// Adapts self-learning V2 recommendations into signal weight adjustments
/// that the V3LiveSignalEngine can use at runtime.
///
/// Phase 2 Item #9 — closes the feedback loop between the replay GLM model
/// and live signal component weighting.
///
/// Flow: Replay produces recommendations JSON → this adapter loads it at startup
/// → provides per-symbol bias, time-of-day adjustments, and position sizing multipliers
/// to the signal engine and risk management stack.
/// </summary>
public sealed class SelfLearningSignalAdapter
{
    private readonly Dictionary<string, SymbolSignalAdjustment> _symbolAdjustments = new(StringComparer.OrdinalIgnoreCase);
    private double _scannerWeightAdjust;
    private double _stopDistanceMultiplier = 1.0;
    private double _positionSizeMultiplier = 1.0;
    private int _bestHourEt = -1;
    private int _worstHourEt = -1;
    private bool _isLoaded;

    /// <summary>Whether recommendations have been successfully loaded.</summary>
    public bool IsLoaded => _isLoaded;

    /// <summary>Scanner composite score shift from GLM model.</summary>
    public double ScannerWeightAdjust => _scannerWeightAdjust;

    /// <summary>Stop distance multiplier (0.8–1.3) from loss distribution analysis.</summary>
    public double StopDistanceMultiplier => _stopDistanceMultiplier;

    /// <summary>Position size multiplier (0.5–1.5) from Kelly criterion approximation.</summary>
    public double PositionSizeMultiplier => _positionSizeMultiplier;

    /// <summary>Best-performing hour (Eastern Time) from historical trades.</summary>
    public int BestHourEt => _bestHourEt;

    /// <summary>Worst-performing hour (Eastern Time) from historical trades.</summary>
    public int WorstHourEt => _worstHourEt;

    /// <summary>
    /// Load the latest self-learning recommendations from a replay output directory.
    /// Finds the most recent recommendations JSON file and parses it.
    /// </summary>
    /// <param name="outputDir">Replay output directory containing recommendation files.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>True if loaded successfully, false otherwise.</returns>
    public bool TryLoadFromDirectory(string outputDir, ILogger logger)
    {
        try
        {
            if (!Directory.Exists(outputDir))
            {
                logger.LogWarning("SelfLearningSignalAdapter: output directory not found: {Dir}", outputDir);
                return false;
            }

            // Find the most recent recommendations file
            var files = Directory.GetFiles(outputDir, "strategy_replay_self_learning_v2_recommendations_*.json")
                .OrderByDescending(f => f)
                .ToArray();

            if (files.Length == 0)
            {
                logger.LogInformation("SelfLearningSignalAdapter: no recommendations files found in {Dir}", outputDir);
                return false;
            }

            var latestFile = files[0];
            logger.LogInformation("SelfLearningSignalAdapter: loading recommendations from {File}", latestFile);

            var json = File.ReadAllText(latestFile);
            var recommendations = JsonSerializer.Deserialize<SelfLearningV2RecommendationRow[]>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (recommendations == null || recommendations.Length == 0)
            {
                logger.LogWarning("SelfLearningSignalAdapter: empty or invalid recommendations file");
                return false;
            }

            // Use the most recent recommendation (last in array)
            var rec = recommendations[^1];
            Apply(rec, logger);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SelfLearningSignalAdapter: failed to load recommendations");
            return false;
        }
    }

    /// <summary>
    /// Apply a recommendations row directly (for testing or programmatic use).
    /// </summary>
    public void Apply(SelfLearningV2RecommendationRow rec, ILogger logger)
    {
        _scannerWeightAdjust = rec.SuggestedScannerWeightAdjust;
        _stopDistanceMultiplier = rec.SuggestedStopDistanceMultiplier;
        _positionSizeMultiplier = rec.SuggestedPositionSizeMultiplier;

        _symbolAdjustments.Clear();
        foreach (var sym in rec.SymbolRecommendations)
        {
            _symbolAdjustments[sym.Symbol] = new SymbolSignalAdjustment(
                Bias: sym.Bias,
                Confidence: sym.Confidence,
                ScannerScoreShift: sym.ScannerScoreShift,
                Action: sym.Action);
        }

        _bestHourEt = rec.BestTimeOfDay?.HourET ?? -1;
        _worstHourEt = rec.WorstTimeOfDay?.HourET ?? -1;

        _isLoaded = true;

        logger.LogInformation(
            "SelfLearningSignalAdapter loaded: ScannerAdj={ScanAdj:+0.000;-0.000} StopMult={StopM:F3} PosSizeMult={PosM:F3} " +
            "BestHour={BH}ET WorstHour={WH}ET Symbols={SymCount}",
            _scannerWeightAdjust, _stopDistanceMultiplier, _positionSizeMultiplier,
            _bestHourEt, _worstHourEt, _symbolAdjustments.Count);
    }

    /// <summary>
    /// Get the signal score adjustment for a symbol.
    /// Returns a score bonus/penalty based on the symbol's historical bias.
    /// Positive bias → +1 score, negative bias → -1 score, neutral → 0.
    /// </summary>
    public int GetSymbolScoreAdjustment(string symbol)
    {
        if (!_isLoaded || string.IsNullOrWhiteSpace(symbol))
            return 0;

        if (!_symbolAdjustments.TryGetValue(symbol, out var adj))
            return 0;

        // Only adjust if confidence >= 0.5 (at least 10 historical trades)
        if (adj.Confidence < 0.5)
            return 0;

        return adj.Action switch
        {
            "upweight" => 1,
            "downweight" => -1,
            _ => 0
        };
    }

    /// <summary>
    /// Get the effective position size multiplier for a symbol (combines global + symbol-specific).
    /// </summary>
    public double GetEffectivePositionSizeMultiplier(string symbol)
    {
        if (!_isLoaded)
            return 1.0;

        var baseMult = _positionSizeMultiplier;

        if (!string.IsNullOrWhiteSpace(symbol) && _symbolAdjustments.TryGetValue(symbol, out var adj))
        {
            // Scale position by symbol bias: upweight → +10% max, downweight → -10% max
            baseMult *= 1.0 + Math.Clamp(adj.Bias * adj.Confidence * 0.1, -0.10, 0.10);
        }

        return Math.Clamp(baseMult, 0.5, 1.5);
    }

    /// <summary>
    /// Get the effective stop distance multiplier (symbol-independent, from loss distribution).
    /// </summary>
    public double GetEffectiveStopMultiplier() => _isLoaded ? _stopDistanceMultiplier : 1.0;

    /// <summary>
    /// Check if the current hour (Eastern Time) is the historically worst hour.
    /// Callers can use this to apply additional caution.
    /// </summary>
    public bool IsWorstHour(DateTime utcNow)
    {
        if (!_isLoaded || _worstHourEt < 0)
            return false;

        try
        {
            var et = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utcNow, "Eastern Standard Time");
            return et.Hour == _worstHourEt;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Per-symbol signal adjustment derived from self-learning V2 GLM output.</summary>
    public sealed record SymbolSignalAdjustment(
        double Bias,
        double Confidence,
        double ScannerScoreShift,
        string Action);
}
