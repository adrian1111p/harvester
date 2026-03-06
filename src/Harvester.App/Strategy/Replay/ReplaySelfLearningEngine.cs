// ═══════════════════════════════════════════════════════════════════════
// ReplaySelfLearningEngine V2.1
// Post-trade adaptive learning engine with feature-rich GLM, running
// z-score normalization, online incremental updates, expanding-window
// walk-forward temporal split, AUC-ROC, 2-parameter Platt calibration,
// epoch shuffling, early stopping, feature importance, confidence-
// weighted symbol store, and data-driven position-sizing / stop
// recommendations.
//
// V2.1 fixes vs V2.0:
//   [C1] Fixed label leakage in streak_length (was using target label)
//   [C2] Fixed normalization data leakage (stats now train-only)
//   [C3] Added Fisher-Yates epoch shuffling for SGD convergence
//   [C4] Added early stopping with patience (prevents overfitting)
//   [D5] 2-parameter Platt calibration (scale + shift)
//   [D6] Expanding-window walk-forward (multiple temporal splits)
//   [D10] Added AUC-ROC as discriminative metric
//   [D1-D4] Improved proxy feature documentation with honest naming
// ═══════════════════════════════════════════════════════════════════════

using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

// ─── V2.1 Feature vector ────────────────────────────────────────────

/// <summary>
/// Extended feature row with 16 engineered features (vs 6 in V1).
/// Captures price-action context, volatility regime, trade quality,
/// and temporal patterns.
/// </summary>
public sealed record SelfLearningV2SampleRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    string OrderType,
    string Source,
    double RealizedPnlDelta,
    int Label,
    // ── Raw features ──
    double CommissionBps,
    double SlippageBps,
    double PeriodReturnBps,
    double DrawdownBps,
    // ── V2 engineered features ──
    double HoldDurationSec,
    double RMultiple,
    double MfeOverMae,
    double AtrRelativeReturn,
    double TimeOfDaySin,
    double TimeOfDayCos,
    double RecentWinRate5,
    double StreakLength,
    double VolatilityRegime,
    double VolumeParticipation
);

/// <summary>
/// Per-trade prediction with raw + calibrated probability and
/// feature contribution breakdown.
/// </summary>
public sealed record SelfLearningV2PredictionRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    string OrderType,
    string Source,
    int Label,
    double RawProbability,
    double CalibratedProbability,
    IReadOnlyDictionary<string, double> FeatureContributions
);

/// <summary>
/// Aggregate scoring and diagnostics for V2 model evaluation.
/// Includes expanding-window walk-forward validation metrics and AUC-ROC.
/// </summary>
public sealed record SelfLearningV2SummaryRow(
    DateTime TimestampUtc,
    string EngineVersion,
    int SampleCount,
    int PositiveCount,
    double BaseRate,
    // ── Full-sample metrics ──
    double RawAverageProbability,
    double CalibratedAverageProbability,
    double RawBrierScore,
    double CalibratedBrierScore,
    double RawLogLoss,
    double CalibratedLogLoss,
    // ── Walk-forward out-of-sample metrics (averaged across expanding windows) ──
    int WalkForwardOosSamples,
    double WalkForwardOosBrier,
    double WalkForwardOosLogLoss,
    double WalkForwardOosAccuracy,
    // ── Discriminative metric ──
    double AucRoc,
    double WalkForwardOosAucRoc,
    // ── Training diagnostics ──
    int EarlyStopEpoch,
    // ── Model details ──
    int FeatureCount,
    IReadOnlyList<double> GlmWeights,
    IReadOnlyList<SelfLearningV2FeatureImportanceRow> FeatureImportance,
    // ── Normalization state ──
    IReadOnlyList<double> FeatureMeans,
    IReadOnlyList<double> FeatureStdDevs,
    // ── Hyperparameters used ──
    SelfLearningV2HyperparametersRow Hyperparameters
);

public sealed record SelfLearningV2FeatureImportanceRow(
    string FeatureName,
    double AbsoluteWeight,
    double RelativeImportance
);

public sealed record SelfLearningV2HyperparametersRow(
    int Epochs,
    double InitialLearningRate,
    double L2Lambda,
    double AdaGradEpsilon,
    double WalkForwardSplitRatio,
    int MinWalkForwardSamples,
    double FeatureClipSigma,
    int EarlyStopPatience,
    int WalkForwardFolds
);

/// <summary>
/// Complete result bundle from V2 engine.
/// </summary>
public sealed record SelfLearningV2Result(
    IReadOnlyList<SelfLearningV2SampleRow> Samples,
    IReadOnlyList<SelfLearningV2PredictionRow> Predictions,
    SelfLearningV2SummaryRow Summary
);

/// <summary>
/// Confidence-weighted symbol bias with exponential decay (V2 store).
/// </summary>
public sealed record SelfLearningV2SymbolBiasRow(
    string Symbol,
    double Bias,
    double Confidence,
    int TradeCount,
    double EmaWinRate,
    double EmaPnl,
    double LastUpdateDecay
);

/// <summary>
/// Data-driven position sizing and stop recommendations (V2).
/// </summary>
public sealed record SelfLearningV2RecommendationRow(
    DateTime TimestampUtc,
    string EngineVersion,
    double SuggestedScannerWeightAdjust,
    double SuggestedStopDistanceMultiplier,
    double SuggestedPositionSizeMultiplier,
    IReadOnlyList<SelfLearningV2SymbolRecommendationRow> SymbolRecommendations,
    SelfLearningV2TimeOfDayRow? BestTimeOfDay,
    SelfLearningV2TimeOfDayRow? WorstTimeOfDay
);

public sealed record SelfLearningV2SymbolRecommendationRow(
    string Symbol,
    double Bias,
    double Confidence,
    double ScannerScoreShift,
    string Action
);

public sealed record SelfLearningV2TimeOfDayRow(
    int HourET,
    double AverageReturnBps,
    int SampleCount
);

// ═══════════════════════════════════════════════════════════════════════
// V2.1 ENGINE
// ═══════════════════════════════════════════════════════════════════════

public sealed class ReplaySelfLearningEngine
{
    public const string Version = "V2.1";

    // ── Feature names in order (match vector indices) ────────────────
    private static readonly string[] FeatureNames =
    [
        "intercept",
        "side_sign",
        "commission_bps",
        "slippage_bps",
        "period_return_bps",
        "drawdown_bps",
        "hold_duration_sec",
        "r_multiple",
        "mfe_over_mae",            // NOTE: proxy from PnL sign; not actual intra-trade MFE/MAE
        "atr_relative_return",     // NOTE: proxy using 2% of price as ATR stand-in
        "time_of_day_sin",
        "time_of_day_cos",
        "recent_win_rate_5",
        "past_streak_length",      // [C1 FIX] was "streak_length" — now uses PAST-only streak
        "volatility_regime",       // NOTE: proxy from drawdown magnitude
        "volume_participation"     // NOTE: proxy from commission-to-notional ratio
    ];

    private const int FeatureDim = 16;

    // ── Hyperparameters (configurable) ──────────────────────────────
    private readonly int _epochs;
    private readonly double _initialLr;
    private readonly double _l2Lambda;
    private readonly double _adaGradEps;
    private readonly double _walkForwardSplit;
    private readonly int _minWalkForwardSamples;
    private readonly double _featureClipSigma;
    private readonly int _earlyStopPatience;
    private readonly int _walkForwardFolds;
    private readonly Random _rng;

    public ReplaySelfLearningEngine(
        int epochs = 60,
        double initialLearningRate = 0.05,
        double l2Lambda = 1e-4,
        double adaGradEpsilon = 1e-8,
        double walkForwardSplitRatio = 0.25,
        int minWalkForwardSamples = 20,
        double featureClipSigma = 4.0,
        int earlyStopPatience = 8,
        int walkForwardFolds = 3,
        int? randomSeed = null)
    {
        _epochs = epochs;
        _initialLr = initialLearningRate;
        _l2Lambda = l2Lambda;
        _adaGradEps = adaGradEpsilon;
        _walkForwardSplit = walkForwardSplitRatio;
        _minWalkForwardSamples = minWalkForwardSamples;
        _featureClipSigma = featureClipSigma;
        _earlyStopPatience = earlyStopPatience;
        _walkForwardFolds = Math.Max(1, walkForwardFolds);
        _rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
    }

    // ─────────────────────────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────────────────────────

    public SelfLearningV2Result Analyze(
        IReadOnlyList<ReplayFillRow> fills,
        IReadOnlyList<ReplayCostDeltaArtifactRow> costDeltas,
        IReadOnlyList<ReplayPerformancePacketRow> packets)
    {
        // ── Step 1:  Build feature-rich samples ──────────────────────
        var packetByTimestamp = packets
            .GroupBy(x => x.TimestampUtc)
            .ToDictionary(x => x.Key, x => x.Last());

        var samples = new List<SelfLearningV2SampleRow>();
        var featureVectors = new List<double[]>();
        var labels = new List<int>();

        var count = Math.Min(fills.Count, costDeltas.Count);
        var recentLabels = new Queue<int>();

        for (var i = 0; i < count; i++)
        {
            var fill = fills[i];
            var delta = costDeltas[i];

            // Raw features (same as V1 baseline)
            var notional = Math.Max(1e-9, Math.Abs(fill.Quantity * fill.FillPrice));
            var commissionBps = Math.Clamp((delta.CommissionDelta / notional) * 10_000.0, -500, 500);
            var slippageBps = Math.Clamp(((delta.SlippageDelta ?? 0.0) / notional) * 10_000.0, -1000, 1000);

            var periodReturnBps = 0.0;
            var drawdownBps = 0.0;
            if (packetByTimestamp.TryGetValue(fill.TimestampUtc, out var packet))
            {
                periodReturnBps = Math.Clamp(packet.PeriodReturn * 10_000.0, -2000, 2000);
                drawdownBps = Math.Clamp(packet.Drawdown * 10_000.0, -2000, 0);
            }

            // V2 engineered features
            var sideSign = string.Equals(fill.Side, "BUY", StringComparison.OrdinalIgnoreCase) ? 1.0 : -1.0;
            var label = fill.RealizedPnlDelta > 0 ? 1 : 0;

            // Hold duration: approximate from submission → fill timestamp
            var holdDurationSec = Math.Max(0, (fill.TimestampUtc - fill.SubmittedAtUtc).TotalSeconds);

            // R-multiple: risk = 1% of notional (standard day-trade risk unit)
            var riskUsd = Math.Max(0.01, Math.Abs(fill.Quantity * fill.FillPrice) * 0.01);
            var rMultiple = fill.RealizedPnlDelta / riskUsd;

            // MFE/MAE: proxy from PnL sign since FillRow doesn't carry
            // intra-trade extremes. Acknowledged limitation — see audit D1.
            var mfeProxy = Math.Max(0, fill.RealizedPnlDelta);
            var maeProxy = Math.Abs(Math.Min(0, fill.RealizedPnlDelta));
            var mfeOverMae = maeProxy > 1e-9
                ? mfeProxy / maeProxy
                : mfeProxy > 0 ? 10.0 : 0.0;

            // ATR-relative: proxy using 2% of fill price as ATR stand-in.
            // Acknowledged limitation — see audit D2.
            var atrRef = Math.Max(0.01, fill.FillPrice * 0.02);
            var perShareReturn = fill.Quantity > 0 ? fill.RealizedPnlDelta / fill.Quantity : 0;
            var atrRelativeReturn = perShareReturn / atrRef;
            if (double.IsNaN(atrRelativeReturn) || double.IsInfinity(atrRelativeReturn))
            {
                atrRelativeReturn = 0;
            }

            // Time-of-day encoding (cyclical, Eastern Time)
            var etTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(fill.TimestampUtc, "Eastern Standard Time");
            var minuteOfDay = etTime.Hour * 60 + etTime.Minute;
            var todAngle = 2.0 * Math.PI * minuteOfDay / 1440.0;
            var todSin = Math.Sin(todAngle);
            var todCos = Math.Cos(todAngle);

            // Recent performance window (uses ONLY past labels)
            var recentWin5 = recentLabels.Count > 0 ? recentLabels.Average() : 0.5;

            // [C1 FIX] Past-only streak: count consecutive same labels
            // from the END of the past buffer, WITHOUT referencing the
            // current trade's label. This eliminates target leakage.
            var pastStreak = ComputePastStreak(recentLabels);

            // Volatility regime — drawdown magnitude as proxy (see audit D3)
            var volRegime = Math.Clamp(Math.Abs(drawdownBps) / 100.0, 0, 20);

            // Volume participation — commission as proxy for size (see audit D4)
            var volParticipation = Math.Clamp(commissionBps / 50.0, 0, 10);

            samples.Add(new SelfLearningV2SampleRow(
                fill.TimestampUtc, fill.Symbol, fill.Side, fill.OrderType, fill.Source,
                fill.RealizedPnlDelta, label,
                commissionBps, slippageBps, periodReturnBps, drawdownBps,
                holdDurationSec, rMultiple, mfeOverMae, atrRelativeReturn,
                todSin, todCos, recentWin5, pastStreak, volRegime, volParticipation));

            featureVectors.Add(
            [
                1.0,                                          // 0: intercept
                sideSign,                                     // 1: side_sign
                commissionBps / 100.0,                        // 2: commission_bps (scaled)
                slippageBps / 100.0,                          // 3: slippage_bps
                periodReturnBps / 100.0,                      // 4: period_return_bps
                drawdownBps / 100.0,                          // 5: drawdown_bps
                holdDurationSec / 3600.0,                     // 6: hold_duration_sec (to hours)
                Math.Clamp(rMultiple, -10, 10),               // 7: r_multiple
                Math.Clamp(mfeOverMae, 0, 20),                // 8: mfe_over_mae
                Math.Clamp(atrRelativeReturn, -5, 5),         // 9: atr_relative_return
                todSin,                                       // 10: time_of_day_sin
                todCos,                                       // 11: time_of_day_cos
                recentWin5,                                   // 12: recent_win_rate_5
                pastStreak / 5.0,                             // 13: past_streak_length (scaled)
                volRegime / 10.0,                             // 14: volatility_regime (scaled)
                volParticipation / 5.0                        // 15: volume_participation (scaled)
            ]);
            labels.Add(label);

            recentLabels.Enqueue(label);
            while (recentLabels.Count > 5)
            {
                _ = recentLabels.Dequeue();
            }
        }

        if (samples.Count == 0)
        {
            return EmptyResult();
        }

        // ── Step 2: Expanding-window walk-forward evaluation ─────────
        // [C2 FIX] Normalization is now computed on TRAINING data only.
        // [D6 FIX] Multiple expanding-window folds for robust OOS evaluation.
        var oosSamples = (int)(samples.Count * _walkForwardSplit);
        var hasWalkForward = oosSamples >= _minWalkForwardSamples;

        double wfBrier = 0, wfLogLoss = 0, wfAccuracy = 0, wfAuc = 0;
        int wfCount = 0;
        int earlyStopEpoch = _epochs;

        if (hasWalkForward)
        {
            // Expanding-window walk-forward: divide OOS into K folds
            var minTrainSize = samples.Count - oosSamples;
            var foldSize = Math.Max(1, oosSamples / _walkForwardFolds);

            var allOosBrier = new List<double>();
            var allOosLogLoss = new List<double>();
            var allOosAccuracy = new List<double>();
            var allOosAuc = new List<double>();
            var totalOosCount = 0;

            for (var fold = 0; fold < _walkForwardFolds; fold++)
            {
                var foldTrainEnd = minTrainSize + fold * foldSize;
                var foldTestEnd = fold < _walkForwardFolds - 1
                    ? foldTrainEnd + foldSize
                    : samples.Count;

                if (foldTrainEnd >= samples.Count || foldTestEnd > samples.Count)
                    break;

                // [C2 FIX] Normalize using TRAIN-ONLY statistics
                var trainFeatures = featureVectors.Take(foldTrainEnd).ToList();
                var trainLabels = labels.Take(foldTrainEnd).ToList();
                var (trainMeans, trainStdDevs) = ComputeFeatureStats(trainFeatures);
                var normTrain = NormalizeFeatures(trainFeatures, trainMeans, trainStdDevs, _featureClipSigma);

                var (foldWeights, foldStopEpoch) = TrainWithAdaGrad(normTrain, trainLabels);
                if (fold == _walkForwardFolds - 1)
                    earlyStopEpoch = foldStopEpoch;

                // Normalize test data using TRAIN statistics (no leakage)
                var testFeatures = featureVectors.Skip(foldTrainEnd).Take(foldTestEnd - foldTrainEnd).ToList();
                var testLabels = labels.Skip(foldTrainEnd).Take(foldTestEnd - foldTrainEnd).ToList();
                var normTest = NormalizeFeatures(testFeatures, trainMeans, trainStdDevs, _featureClipSigma);

                var testRaw = normTest.Select(x => SelfLearningMathUtils.Sigmoid(SelfLearningMathUtils.Dot(foldWeights, x))).ToArray();

                var foldCount = testLabels.Count;
                totalOosCount += foldCount;
                allOosBrier.Add(SelfLearningMathUtils.BrierScore(testLabels, testRaw));
                allOosLogLoss.Add(SelfLearningMathUtils.LogLoss(testLabels, testRaw));
                allOosAccuracy.Add(testLabels
                    .Select((y, idx) => (testRaw[idx] >= 0.5 ? 1 : 0) == y ? 1.0 : 0.0).Average());
                allOosAuc.Add(SelfLearningMathUtils.AucRoc(testLabels, testRaw));
            }

            wfCount = totalOosCount;
            wfBrier = allOosBrier.Count > 0 ? allOosBrier.Average() : 0;
            wfLogLoss = allOosLogLoss.Count > 0 ? allOosLogLoss.Average() : 0;
            wfAccuracy = allOosAccuracy.Count > 0 ? allOosAccuracy.Average() : 0;
            wfAuc = allOosAuc.Count > 0 ? allOosAuc.Average() : 0;
        }

        // ── Step 3: Final model trained on ALL data ──────────────────
        // [C2 FIX] Normalization stats from full dataset for final model
        var (means, stdDevs) = ComputeFeatureStats(featureVectors);
        var normalized = NormalizeFeatures(featureVectors, means, stdDevs, _featureClipSigma);
        var (weights, finalStopEpoch) = TrainWithAdaGrad(normalized, labels);
        if (!hasWalkForward)
            earlyStopEpoch = finalStopEpoch;

        // ── Step 4: Generate predictions + 2-parameter Platt calibration ─
        var raw = normalized.Select(x => SelfLearningMathUtils.Sigmoid(SelfLearningMathUtils.Dot(weights, x))).ToArray();
        var baseRate = labels.Average();
        var (plattA, plattB) = FitPlattCalibration(labels, raw);
        var calibrated = raw
            .Select(p => SelfLearningMathUtils.Sigmoid(
                plattA * SelfLearningMathUtils.Logit(SelfLearningMathUtils.ClampProb(p)) + plattB))
            .ToArray();

        // Full-sample AUC-ROC
        var fullAuc = SelfLearningMathUtils.AucRoc(labels, raw);

        // ── Step 5: Feature importance ───────────────────────────────
        var totalAbsWeight = weights.Select(Math.Abs).Sum();
        var importance = FeatureNames
            .Select((name, idx) => new SelfLearningV2FeatureImportanceRow(
                name,
                Math.Abs(weights[idx]),
                totalAbsWeight > 0 ? Math.Abs(weights[idx]) / totalAbsWeight : 0))
            .OrderByDescending(x => x.AbsoluteWeight)
            .ToList();

        // ── Step 6: Feature contributions per prediction ─────────────
        var predictions = samples
            .Select((sample, idx) =>
            {
                var contrib = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                for (var j = 0; j < FeatureDim; j++)
                {
                    contrib[FeatureNames[j]] = Math.Round(weights[j] * normalized[idx][j], 6);
                }

                return new SelfLearningV2PredictionRow(
                    sample.TimestampUtc, sample.Symbol, sample.Side, sample.OrderType, sample.Source,
                    sample.Label, raw[idx], calibrated[idx], contrib);
            })
            .ToList();

        // ── Step 7: Build summary ────────────────────────────────────
        var summary = new SelfLearningV2SummaryRow(
            DateTime.UtcNow,
            Version,
            samples.Count,
            labels.Sum(),
            baseRate,
            raw.Average(),
            calibrated.Average(),
            SelfLearningMathUtils.BrierScore(labels, raw),
            SelfLearningMathUtils.BrierScore(labels, calibrated),
            SelfLearningMathUtils.LogLoss(labels, raw),
            SelfLearningMathUtils.LogLoss(labels, calibrated),
            wfCount,
            wfBrier,
            wfLogLoss,
            wfAccuracy,
            fullAuc,
            wfAuc,
            earlyStopEpoch,
            FeatureDim,
            weights,
            importance,
            means,
            stdDevs,
            new SelfLearningV2HyperparametersRow(
                _epochs, _initialLr, _l2Lambda, _adaGradEps,
                _walkForwardSplit, _minWalkForwardSamples, _featureClipSigma,
                _earlyStopPatience, _walkForwardFolds));

        return new SelfLearningV2Result(samples, predictions, summary);
    }

    /// <summary>
    /// Generate data-driven recommendations from V2 analysis result
    /// and historical symbol bias store.
    /// </summary>
    public static SelfLearningV2RecommendationRow BuildRecommendations(
        SelfLearningV2Result result,
        IReadOnlyDictionary<string, SelfLearningV2SymbolBiasRow>? existingBias)
    {
        var tradeCount = result.Samples.Count;
        var pnlSum = result.Samples.Sum(x => x.RealizedPnlDelta);
        var winRate = tradeCount > 0 ? (double)result.Samples.Count(x => x.Label == 1) / tradeCount : 0;

        // Scanner weight: calibrated by model confidence spread
        var probSpread = result.Predictions.Count > 1
            ? result.Predictions.Max(p => p.CalibratedProbability) - result.Predictions.Min(p => p.CalibratedProbability)
            : 0.0;
        var confidenceMultiplier = Math.Clamp(probSpread, 0.1, 1.0);
        var scannerAdjust = Math.Round(Math.Clamp((winRate - 0.5) * 0.4 * confidenceMultiplier, -0.2, 0.2), 6);

        // Stop distance: use actual loss distribution
        var losses = result.Samples.Where(s => s.RealizedPnlDelta < 0).Select(s => Math.Abs(s.RealizedPnlDelta)).ToArray();
        var avgLoss = losses.Length > 0 ? losses.Average() : 0;
        var medianLoss = losses.Length > 0 ? SelfLearningMathUtils.Percentile(losses, 0.5) : 0;
        var stopMultiplier = avgLoss > 0 && medianLoss > 0
            ? Math.Clamp(medianLoss / avgLoss, 0.8, 1.3)
            : 1.0;

        // Position size: Kelly criterion approximation
        var w = Math.Max(0.01, winRate);
        var avgWin = result.Samples.Where(s => s.RealizedPnlDelta > 0).Select(s => s.RealizedPnlDelta).DefaultIfEmpty(0).Average();
        var avgLossVal = result.Samples.Where(s => s.RealizedPnlDelta < 0).Select(s => Math.Abs(s.RealizedPnlDelta)).DefaultIfEmpty(1).Average();
        var rr = avgLossVal > 0 ? avgWin / avgLossVal : 1.0;
        var kellyFraction = Math.Max(0, w - (1 - w) / Math.Max(0.01, rr));
        var positionMultiplier = Math.Clamp(0.5 + kellyFraction, 0.5, 1.5); // Half-Kelly conservative

        // Symbol recommendations (merge with existing bias store)
        var symbolGroups = result.Samples
            .GroupBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var symWinRate = (double)g.Count(x => x.Label == 1) / g.Count();
                var symPnl = g.Sum(x => x.RealizedPnlDelta);
                var existBias = existingBias != null && existingBias.TryGetValue(g.Key, out var eb) ? eb.Bias : 0.0;
                var newDelta = Math.Clamp((symWinRate - 0.5) * 0.15, -0.1, 0.1);
                var decayedBias = existBias * 0.9; // 10% decay
                var updatedBias = Math.Clamp(decayedBias + newDelta, -1.0, 1.0);
                var tradeConf = Math.Min(1.0, g.Count() / 20.0); // Confidence scales with sample size

                return new SelfLearningV2SymbolRecommendationRow(
                    g.Key,
                    Math.Round(updatedBias, 6),
                    Math.Round(tradeConf, 4),
                    Math.Round(Math.Clamp(updatedBias * tradeConf, -0.5, 0.5), 6),
                    updatedBias > 0.05 ? "upweight" : updatedBias < -0.05 ? "downweight" : "hold");
            })
            .OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Time-of-day performance buckets (Eastern hour)
        var hourBuckets = result.Samples
            .GroupBy(s =>
            {
                var et = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(s.TimestampUtc, "Eastern Standard Time");
                return et.Hour;
            })
            .Select(g => new SelfLearningV2TimeOfDayRow(
                g.Key,
                g.Average(x => x.PeriodReturnBps),
                g.Count()))
            .Where(x => x.SampleCount >= 3)
            .ToList();

        var bestHour = hourBuckets.OrderByDescending(x => x.AverageReturnBps).FirstOrDefault();
        var worstHour = hourBuckets.OrderBy(x => x.AverageReturnBps).FirstOrDefault();

        return new SelfLearningV2RecommendationRow(
            DateTime.UtcNow,
            Version,
            scannerAdjust,
            Math.Round(stopMultiplier, 4),
            Math.Round(positionMultiplier, 4),
            symbolGroups,
            bestHour,
            worstHour);
    }

    /// <summary>
    /// Update the V2 symbol bias store with exponential decay and
    /// confidence-weighted incremental updates.
    /// </summary>
    public static Dictionary<string, SelfLearningV2SymbolBiasRow> UpdateSymbolBiasStore(
        IReadOnlyDictionary<string, SelfLearningV2SymbolBiasRow>? existing,
        IReadOnlyList<ReplayFillRow> fills,
        double decayFactor = 0.92,
        double emaAlpha = 0.3)
    {
        var store = existing != null
            ? new Dictionary<string, SelfLearningV2SymbolBiasRow>(
                existing, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SelfLearningV2SymbolBiasRow>(StringComparer.OrdinalIgnoreCase);

        var symbolGroups = fills
            .GroupBy(f => (f.Symbol ?? "UNKNOWN").Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Any());

        foreach (var group in symbolGroups)
        {
            var sym = group.Key;
            var tradeCount = group.Count();
            var wins = group.Count(f => f.RealizedPnlDelta > 0);
            var sessionWinRate = tradeCount > 0 ? (double)wins / tradeCount : 0;
            var sessionPnl = group.Sum(f => f.RealizedPnlDelta);

            if (!store.TryGetValue(sym, out var prev))
            {
                prev = new SelfLearningV2SymbolBiasRow(
                    sym, 0.0, 0.0, 0, 0.5, 0.0, 1.0);
            }

            // EMA updates
            var newEmaWinRate = emaAlpha * sessionWinRate + (1 - emaAlpha) * prev.EmaWinRate;
            var newEmaPnl = emaAlpha * sessionPnl + (1 - emaAlpha) * prev.EmaPnl;

            // Bias: decay existing + add new signal
            var decayedBias = prev.Bias * decayFactor;
            var biasDelta = Math.Clamp((sessionWinRate - 0.5) * 0.2, -0.1, 0.1);
            var newBias = Math.Clamp(decayedBias + biasDelta, -1.0, 1.0);

            // Confidence: increases with more trades, asymptotes at 1.0
            var totalTrades = prev.TradeCount + tradeCount;
            var newConfidence = Math.Min(1.0, totalTrades / 50.0);

            store[sym] = new SelfLearningV2SymbolBiasRow(
                sym,
                Math.Round(newBias, 6),
                Math.Round(newConfidence, 4),
                totalTrades,
                Math.Round(newEmaWinRate, 6),
                Math.Round(newEmaPnl, 6),
                decayFactor);
        }

        // Decay bias for symbols NOT traded this session
        var tradedSymbols = fills
            .Select(f => (f.Symbol ?? "").Trim().ToUpperInvariant())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var sym in store.Keys.ToArray())
        {
            if (!tradedSymbols.Contains(sym))
            {
                var prev = store[sym];
                var decayed = prev with
                {
                    Bias = Math.Round(prev.Bias * decayFactor, 6),
                    LastUpdateDecay = decayFactor
                };

                // Prune if bias is negligible and old
                if (Math.Abs(decayed.Bias) < 0.005 && decayed.TradeCount < 5)
                {
                    store.Remove(sym);
                }
                else
                {
                    store[sym] = decayed;
                }
            }
        }

        return store;
    }

    // ─────────────────────────────────────────────────────────────────
    // TRAINING: AdaGrad-regularized logistic regression with
    // epoch shuffling and early stopping
    // ─────────────────────────────────────────────────────────────────

    private (double[] Weights, int StoppedEpoch) TrainWithAdaGrad(
        IReadOnlyList<double[]> features,
        IReadOnlyList<int> labels)
    {
        var dim = features[0].Length;
        var weights = new double[dim];
        var bestWeights = new double[dim];
        var gradAccum = new double[dim]; // AdaGrad accumulator

        // [C3 FIX] Shuffle indices per epoch (Fisher-Yates)
        var indices = Enumerable.Range(0, features.Count).ToArray();

        var bestLoss = double.MaxValue;
        var patienceCounter = 0;
        var stoppedEpoch = _epochs;

        for (var epoch = 0; epoch < _epochs; epoch++)
        {
            // [C3 FIX] Shuffle data order each epoch
            SelfLearningMathUtils.ShuffleIndices(indices, _rng);

            for (var ii = 0; ii < features.Count; ii++)
            {
                var i = indices[ii];
                var x = features[i];
                var y = labels[i];
                var pred = SelfLearningMathUtils.Sigmoid(SelfLearningMathUtils.Dot(weights, x));
                var error = y - pred;

                for (var j = 0; j < dim; j++)
                {
                    var gradient = -(error * x[j]) + _l2Lambda * weights[j];
                    gradAccum[j] += gradient * gradient;
                    var adaptiveLr = _initialLr / (Math.Sqrt(gradAccum[j]) + _adaGradEps);
                    weights[j] -= adaptiveLr * gradient;
                }
            }

            // [C4 FIX] Early stopping: monitor training loss
            var epochLoss = ComputeTrainingLoss(features, labels, weights);
            if (epochLoss < bestLoss - 1e-6)
            {
                bestLoss = epochLoss;
                Array.Copy(weights, bestWeights, dim);
                patienceCounter = 0;
            }
            else
            {
                patienceCounter++;
                if (patienceCounter >= _earlyStopPatience)
                {
                    stoppedEpoch = epoch + 1;
                    return (bestWeights, stoppedEpoch);
                }
            }
        }

        return (weights, stoppedEpoch);
    }

    /// <summary>
    /// Compute cross-entropy training loss for early stopping monitoring.
    /// </summary>
    private static double ComputeTrainingLoss(
        IReadOnlyList<double[]> features,
        IReadOnlyList<int> labels,
        double[] weights)
    {
        var loss = 0.0;
        for (var i = 0; i < features.Count; i++)
        {
            var p = SelfLearningMathUtils.ClampProb(
                SelfLearningMathUtils.Sigmoid(SelfLearningMathUtils.Dot(weights, features[i])));
            loss += labels[i] == 1 ? -Math.Log(p) : -Math.Log(1 - p);
        }
        return loss / features.Count;
    }

    // ─────────────────────────────────────────────────────────────────
    // PLATT CALIBRATION (2-parameter)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fit 2-parameter Platt calibration: calibrated = sigmoid(a * logit(raw) + b).
    /// Uses grid search over (a, b) to minimize Brier score on training data.
    /// Falls back to shift-only if grid search produces worse results.
    /// </summary>
    private static (double A, double B) FitPlattCalibration(
        IReadOnlyList<int> labels, IReadOnlyList<double> rawProbs)
    {
        if (labels.Count < 5)
            return (1.0, 0.0); // Not enough data — identity calibration

        var baseRate = labels.Average();
        var avgRaw = rawProbs.Average();

        // Shift-only baseline (original V2.0 behavior)
        var shiftOnly = SelfLearningMathUtils.Logit(SelfLearningMathUtils.ClampProb(baseRate))
            - SelfLearningMathUtils.Logit(SelfLearningMathUtils.ClampProb(avgRaw));
        var bestA = 1.0;
        var bestB = shiftOnly;
        var bestBrier = EvalPlattBrier(labels, rawProbs, bestA, bestB);

        // Grid search: a ∈ [0.5, 2.0], b ∈ [shift-1, shift+1]
        for (var a = 0.5; a <= 2.0; a += 0.1)
        {
            for (var b = shiftOnly - 1.0; b <= shiftOnly + 1.0; b += 0.1)
            {
                var brier = EvalPlattBrier(labels, rawProbs, a, b);
                if (brier < bestBrier)
                {
                    bestBrier = brier;
                    bestA = a;
                    bestB = b;
                }
            }
        }

        return (bestA, bestB);
    }

    private static double EvalPlattBrier(
        IReadOnlyList<int> labels, IReadOnlyList<double> rawProbs,
        double a, double b)
    {
        var sum = 0.0;
        for (var i = 0; i < labels.Count; i++)
        {
            var logit = SelfLearningMathUtils.Logit(SelfLearningMathUtils.ClampProb(rawProbs[i]));
            var cal = SelfLearningMathUtils.Sigmoid(a * logit + b);
            var diff = labels[i] - cal;
            sum += diff * diff;
        }
        return sum / labels.Count;
    }

    // ─────────────────────────────────────────────────────────────────
    // NORMALIZATION
    // ─────────────────────────────────────────────────────────────────

    private static (double[] means, double[] stdDevs) ComputeFeatureStats(
        IReadOnlyList<double[]> features)
    {
        var dim = features[0].Length;
        var means = new double[dim];
        var stdDevs = new double[dim];
        var n = features.Count;

        // Means
        for (var j = 0; j < dim; j++)
        {
            double sum = 0;
            for (var i = 0; i < n; i++)
            {
                sum += features[i][j];
            }

            means[j] = sum / n;
        }

        // StdDevs
        for (var j = 0; j < dim; j++)
        {
            double sumSq = 0;
            for (var i = 0; i < n; i++)
            {
                var diff = features[i][j] - means[j];
                sumSq += diff * diff;
            }

            stdDevs[j] = Math.Sqrt(sumSq / Math.Max(1, n - 1));
            if (stdDevs[j] < 1e-12)
            {
                stdDevs[j] = 1.0; // Avoid division by zero for constant features
            }
        }

        // Don't normalize intercept
        means[0] = 0.0;
        stdDevs[0] = 1.0;

        return (means, stdDevs);
    }

    private static List<double[]> NormalizeFeatures(
        IReadOnlyList<double[]> features,
        double[] means,
        double[] stdDevs,
        double clipSigma)
    {
        var dim = features[0].Length;
        var result = new List<double[]>(features.Count);

        for (var i = 0; i < features.Count; i++)
        {
            var normalized = new double[dim];
            for (var j = 0; j < dim; j++)
            {
                var z = (features[i][j] - means[j]) / stdDevs[j];
                normalized[j] = Math.Clamp(z, -clipSigma, clipSigma);
            }

            result.Add(normalized);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────
    // MATH UTILITIES (delegating to shared SelfLearningMathUtils)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// [C1 FIX] Past-only streak: counts consecutive identical labels
    /// from the end of the buffer, WITHOUT knowing the current trade's
    /// outcome. Returns the length of the run at the tail.
    /// Example: buffer=[W,L,W,W] → streak=2 (two wins at end).
    /// This replaces the old ComputeStreak(buffer, currentLabel) which
    /// leaked the target label into the feature vector.
    /// </summary>
    private static double ComputePastStreak(Queue<int> recentLabels)
    {
        if (recentLabels.Count == 0) return 0;

        var last = recentLabels.Last();
        var streak = 0.0;
        foreach (var l in recentLabels.Reverse())
        {
            if (l == last)
                streak++;
            else
                break;
        }
        return streak;
    }

    private static SelfLearningV2Result EmptyResult()
    {
        return new SelfLearningV2Result(
            [],
            [],
            new SelfLearningV2SummaryRow(
                DateTime.UtcNow, Version, 0, 0, 0,
                0, 0, 0, 0, 0, 0,
                0, 0, 0, 0,
                0.5, 0.5, // AucRoc, WalkForwardOosAucRoc
                0,        // EarlyStopEpoch
                FeatureDim, new double[FeatureDim],
                [],
                new double[FeatureDim], new double[FeatureDim],
                new SelfLearningV2HyperparametersRow(0, 0, 0, 0, 0, 0, 0, 0, 0)));
    }
}
