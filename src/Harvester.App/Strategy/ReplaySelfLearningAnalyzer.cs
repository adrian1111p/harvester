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
        var raw = features.Select(x => Sigmoid(Dot(weights, x))).ToArray();

        var baseRate = labels.Average();
        var averageRaw = raw.Average();
        var shift = Logit(ClampProbability(baseRate)) - Logit(ClampProbability(averageRaw));

        var calibrated = raw
            .Select(p => Sigmoid(Logit(ClampProbability(p)) + shift))
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
            BrierScore(labels, raw),
            BrierScore(labels, calibrated),
            LogLoss(labels, raw),
            LogLoss(labels, calibrated),
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
                var prediction = Sigmoid(Dot(weights, x));
                var error = y - prediction;

                for (var j = 0; j < dimension; j++)
                {
                    weights[j] += learningRate * ((error * x[j]) - (l2 * weights[j]));
                }
            }
        }

        return weights;
    }

    private static double Dot(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var total = 0.0;
        for (var i = 0; i < left.Count; i++)
        {
            total += left[i] * right[i];
        }

        return total;
    }

    private static double Sigmoid(double value)
    {
        if (value >= 0)
        {
            var z = Math.Exp(-value);
            return 1.0 / (1.0 + z);
        }

        var exp = Math.Exp(value);
        return exp / (1.0 + exp);
    }

    private static double Logit(double probability)
    {
        var clamped = ClampProbability(probability);
        return Math.Log(clamped / (1.0 - clamped));
    }

    private static double ClampProbability(double probability)
    {
        return Math.Clamp(probability, 1e-6, 1.0 - 1e-6);
    }

    private static double BrierScore(IReadOnlyList<int> labels, IReadOnlyList<double> probabilities)
    {
        if (labels.Count == 0)
        {
            return 0;
        }

        var total = 0.0;
        for (var i = 0; i < labels.Count; i++)
        {
            var diff = labels[i] - probabilities[i];
            total += diff * diff;
        }

        return total / labels.Count;
    }

    private static double LogLoss(IReadOnlyList<int> labels, IReadOnlyList<double> probabilities)
    {
        if (labels.Count == 0)
        {
            return 0;
        }

        var total = 0.0;
        for (var i = 0; i < labels.Count; i++)
        {
            var probability = ClampProbability(probabilities[i]);
            total += labels[i] == 1 ? Math.Log(probability) : Math.Log(1.0 - probability);
        }

        return -total / labels.Count;
    }
}
