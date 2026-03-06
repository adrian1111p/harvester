using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed record ReplaySelfLearningSampleRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    string OrderType,
    string Source,
    double RealizedPnlDelta,
    int Label,
    double CommissionBps,
    double SlippageDeltaBps,
    double PeriodReturnBps,
    double DrawdownBps
);

public sealed record ReplaySelfLearningPredictionRow(
    DateTime TimestampUtc,
    string Symbol,
    string Side,
    string OrderType,
    string Source,
    int Label,
    double RawProbability,
    double CalibratedProbability
);

public sealed record ReplaySelfLearningSummaryRow(
    DateTime TimestampUtc,
    int SampleCount,
    int PositiveCount,
    double BaseRate,
    double RawAverageProbability,
    double CalibratedAverageProbability,
    double RawBrierScore,
    double CalibratedBrierScore,
    double RawLogLoss,
    double CalibratedLogLoss,
    IReadOnlyList<double> GlmWeights
);

public sealed record ReplaySelfLearningResult(
    IReadOnlyList<ReplaySelfLearningSampleRow> Samples,
    IReadOnlyList<ReplaySelfLearningPredictionRow> Predictions,
    ReplaySelfLearningSummaryRow Summary
);

/// <summary>
/// V1 Self-learning analyzer (DEPRECATED — retained for backward compatibility).
/// All new development should use <see cref="ReplaySelfLearningEngine"/> (V2.1).
/// V1 uses only 6 features, no normalization, no walk-forward, no AUC-ROC.
/// Math utilities now delegate to <see cref="SelfLearningMathUtils"/>.
/// </summary>
[Obsolete("Use ReplaySelfLearningEngine (V2.1) instead. V1 retained for backward-compatible JSON export only.")]
public sealed class ReplaySelfLearningAnalyzer
{
    public ReplaySelfLearningResult Analyze(
        IReadOnlyList<ReplayFillRow> fills,
        IReadOnlyList<ReplayCostDeltaArtifactRow> costDeltas,
        IReadOnlyList<ReplayPerformancePacketRow> packets)
    {
        var packetByTimestamp = packets
            .GroupBy(x => x.TimestampUtc)
            .ToDictionary(x => x.Key, x => x.Last());

        var sampleRows = new List<ReplaySelfLearningSampleRow>();
        var features = new List<double[]>();
        var labels = new List<int>();

        var count = Math.Min(fills.Count, costDeltas.Count);
        for (var i = 0; i < count; i++)
        {
            var fill = fills[i];
            var delta = costDeltas[i];

            var notional = Math.Max(1e-9, Math.Abs(fill.Quantity * fill.FillPrice));
            var commissionBps = (delta.CommissionDelta / notional) * 10000.0;
            var slippageDeltaBps = (delta.SlippageDelta ?? 0.0) / notional * 10000.0;

            var periodReturnBps = 0.0;
            var drawdownBps = 0.0;
            if (packetByTimestamp.TryGetValue(fill.TimestampUtc, out var packet))
            {
                periodReturnBps = packet.PeriodReturn * 10000.0;
                drawdownBps = packet.Drawdown * 10000.0;
            }

            commissionBps = Math.Clamp(commissionBps, -500.0, 500.0);
            slippageDeltaBps = Math.Clamp(slippageDeltaBps, -1000.0, 1000.0);
            periodReturnBps = Math.Clamp(periodReturnBps, -2000.0, 2000.0);
            drawdownBps = Math.Clamp(drawdownBps, -2000.0, 0.0);

            var label = fill.RealizedPnlDelta > 0 ? 1 : 0;
            var sideSign = string.Equals(fill.Side, "BUY", StringComparison.OrdinalIgnoreCase) ? 1.0 : -1.0;

            sampleRows.Add(new ReplaySelfLearningSampleRow(
                fill.TimestampUtc,
                fill.Symbol,
                fill.Side,
                fill.OrderType,
                fill.Source,
                fill.RealizedPnlDelta,
                label,
                commissionBps,
                slippageDeltaBps,
                periodReturnBps,
                drawdownBps));

            features.Add(
            [
                1.0,
                sideSign,
                commissionBps / 100.0,
                slippageDeltaBps / 100.0,
                periodReturnBps / 100.0,
                drawdownBps / 100.0
            ]);
            labels.Add(label);
        }

        if (sampleRows.Count == 0)
        {
            return new ReplaySelfLearningResult(
                [],
                [],
                new ReplaySelfLearningSummaryRow(
                    DateTime.UtcNow,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    []));
        }

        var weights = TrainLogisticRegression(features, labels);
        var raw = features.Select(x => SelfLearningMathUtils.Sigmoid(SelfLearningMathUtils.Dot(weights, x))).ToArray();

        var baseRate = labels.Average();
        var averageRaw = raw.Average();
        var shift = SelfLearningMathUtils.Logit(SelfLearningMathUtils.ClampProb(baseRate))
            - SelfLearningMathUtils.Logit(SelfLearningMathUtils.ClampProb(averageRaw));

        var calibrated = raw
            .Select(p => SelfLearningMathUtils.Sigmoid(
                SelfLearningMathUtils.Logit(SelfLearningMathUtils.ClampProb(p)) + shift))
            .ToArray();

        var predictions = sampleRows
            .Select((sample, index) => new ReplaySelfLearningPredictionRow(
                sample.TimestampUtc,
                sample.Symbol,
                sample.Side,
                sample.OrderType,
                sample.Source,
                sample.Label,
                raw[index],
                calibrated[index]))
            .ToArray();

        var summary = new ReplaySelfLearningSummaryRow(
            DateTime.UtcNow,
            sampleRows.Count,
            labels.Sum(),
            baseRate,
            averageRaw,
            calibrated.Average(),
            SelfLearningMathUtils.BrierScore(labels, raw),
            SelfLearningMathUtils.BrierScore(labels, calibrated),
            SelfLearningMathUtils.LogLoss(labels, raw),
            SelfLearningMathUtils.LogLoss(labels, calibrated),
            weights);

        return new ReplaySelfLearningResult(sampleRows, predictions, summary);
    }

    private static double[] TrainLogisticRegression(IReadOnlyList<double[]> features, IReadOnlyList<int> labels)
    {
        var dimension = features[0].Length;
        var weights = new double[dimension];

        const int epochs = 40;
        const double learningRate = 0.05;
        const double l2 = 1e-4;

        for (var epoch = 0; epoch < epochs; epoch++)
        {
            for (var i = 0; i < features.Count; i++)
            {
                var x = features[i];
                var y = labels[i];
                var prediction = SelfLearningMathUtils.Sigmoid(SelfLearningMathUtils.Dot(weights, x));
                var error = y - prediction;

                for (var j = 0; j < dimension; j++)
                {
                    weights[j] += learningRate * ((error * x[j]) - (l2 * weights[j]));
                }
            }
        }

        return weights;
    }
}
