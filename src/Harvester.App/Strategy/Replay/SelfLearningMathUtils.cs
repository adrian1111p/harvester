// ═══════════════════════════════════════════════════════════════════════
// SelfLearningMathUtils
// Shared mathematical utilities for self-learning modules to eliminate
// code duplication between V1 and V2 engines.
// ═══════════════════════════════════════════════════════════════════════

namespace Harvester.App.Strategy;

/// <summary>
/// Shared numerical utility methods used across self-learning engines.
/// Eliminates duplication of Sigmoid, Logit, BrierScore, LogLoss, etc.
/// </summary>
public static class SelfLearningMathUtils
{
    /// <summary>
    /// Numerically stable sigmoid: handles large positive and negative values
    /// without overflow by branching on sign.
    /// </summary>
    public static double Sigmoid(double x)
    {
        if (x >= 0)
        {
            var z = Math.Exp(-x);
            return 1.0 / (1.0 + z);
        }

        var exp = Math.Exp(x);
        return exp / (1.0 + exp);
    }

    /// <summary>
    /// Logit (log-odds) transform. Clamps probability to avoid log(0).
    /// </summary>
    public static double Logit(double p)
    {
        var c = ClampProb(p);
        return Math.Log(c / (1.0 - c));
    }

    /// <summary>
    /// Clamp probability to [1e-6, 1-1e-6] to avoid log(0) and division by zero.
    /// </summary>
    public static double ClampProb(double p) => Math.Clamp(p, 1e-6, 1.0 - 1e-6);

    /// <summary>
    /// Dot product of two equal-length vectors.
    /// </summary>
    public static double Dot(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Count; i++)
        {
            sum += a[i] * b[i];
        }
        return sum;
    }

    /// <summary>
    /// Brier score: mean squared error between labels (0/1) and predicted probabilities.
    /// Lower is better. Range [0, 1].
    /// </summary>
    public static double BrierScore(IReadOnlyList<int> labels, IReadOnlyList<double> probs)
    {
        if (labels.Count == 0) return 0;
        var sum = 0.0;
        for (var i = 0; i < labels.Count; i++)
        {
            var diff = labels[i] - probs[i];
            sum += diff * diff;
        }
        return sum / labels.Count;
    }

    /// <summary>
    /// Log-loss (cross-entropy loss). Lower is better.
    /// </summary>
    public static double LogLoss(IReadOnlyList<int> labels, IReadOnlyList<double> probs)
    {
        if (labels.Count == 0) return 0;
        var sum = 0.0;
        for (var i = 0; i < labels.Count; i++)
        {
            var p = ClampProb(probs[i]);
            sum += labels[i] == 1 ? Math.Log(p) : Math.Log(1 - p);
        }
        return -sum / labels.Count;
    }

    /// <summary>
    /// AUC-ROC via the trapezoidal rule on the ROC curve.
    /// Handles ties and edge cases (all same label → 0.5).
    /// </summary>
    public static double AucRoc(IReadOnlyList<int> labels, IReadOnlyList<double> scores)
    {
        if (labels.Count < 2) return 0.5;
        var positives = labels.Count(l => l == 1);
        var negatives = labels.Count - positives;
        if (positives == 0 || negatives == 0) return 0.5;

        // Sort by descending score
        var indices = Enumerable.Range(0, labels.Count)
            .OrderByDescending(i => scores[i])
            .ThenByDescending(i => labels[i])
            .ToArray();

        double auc = 0;
        double tp = 0, fp = 0;
        double prevTp = 0, prevFp = 0;
        var prevScore = double.NaN;

        for (var i = 0; i < indices.Length; i++)
        {
            var idx = indices[i];
            var score = scores[idx];

            // When score changes, add trapezoid
            if (!double.IsNaN(prevScore) && Math.Abs(score - prevScore) > 1e-12)
            {
                auc += (fp - prevFp) * (tp + prevTp) / 2.0;
                prevTp = tp;
                prevFp = fp;
            }

            if (labels[idx] == 1)
                tp++;
            else
                fp++;

            prevScore = score;
        }

        // Final trapezoid
        auc += (fp - prevFp) * (tp + prevTp) / 2.0;

        return auc / (positives * (double)negatives);
    }

    /// <summary>
    /// Compute percentile from an unsorted array. Creates a sorted copy.
    /// </summary>
    public static double Percentile(double[] values, double pct)
    {
        if (values.Length == 0) return 0;
        var copy = values.ToArray();
        Array.Sort(copy);
        var idx = (int)Math.Floor(pct * (copy.Length - 1));
        return copy[Math.Clamp(idx, 0, copy.Length - 1)];
    }

    /// <summary>
    /// Fisher-Yates shuffle in-place using parallel index arrays.
    /// Used for epoch-level data shuffling in SGD training.
    /// </summary>
    public static void ShuffleIndices(int[] indices, Random rng)
    {
        for (var i = indices.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }
    }
}
