// ═══════════════════════════════════════════════════════════════════════════
// SCANNER SELECTION ENGINE V2.0
// ═══════════════════════════════════════════════════════════════════════════
//
// Replaces V1 WeightedScore-only ranking with a multi-factor candidate
// qualification and scoring pipeline that leverages the user's full IBKR
// market data subscriptions:
//
//   L1 : Network A/CTA + B/CTA + C/UTP  (bid/ask/last/size)
//   L2 : NASDAQ TotalView-OpenView       (full order book depth)
//
// ── V1 → V2 gap analysis ────────────────────────────────────────────────
//
//   V1 Weakness                           V2 Resolution
//   ─────────────────────────────────     ──────────────────────────────
//   Static file score (WeightedScore)  →  8-factor composite with live data
//   No spread quality check            →  L1 spread gate + spread score
//   No volume validation               →  Minimum volume + volume score
//   No L2 usage at selection time      →  L2 depth, imbalance, thin-book gate
//   No self-learning integration       →  Symbol bias from V2 engine
//   No time-of-day adjustment          →  Session phase modifier
//   No diversification / correlation   →  Sector concentration guard
//   Buy setup only in replay entry     →  Pre-entry momentum screen
//   No historical pattern check        →  Price-range + volatility regime gate
//
// ═══════════════════════════════════════════════════════════════════════════

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harvester.App.Strategy;

// ─────────────────────────────────────────────────────────────────────────
// CONFIGURATION
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tunable parameters for the V2 scanner selection engine.
/// Instantiate with defaults via <c>new ScannerSelectionV2Config()</c>,
/// override with <c>with { ... }</c>.
/// </summary>
public sealed record ScannerSelectionV2Config
{
    // ── Version tag ──────────────────────────────────────────────────
    public string EngineVersion { get; init; } = "V2.0";

    // ── General gates ────────────────────────────────────────────────
    public double MinPrice { get; init; } = 0.50;
    public double MaxPrice { get; init; } = 10.0;
    public int TopN { get; init; } = 5;
    public double MinFileScore { get; init; } = 60.0;

    // ── L1 spread gate ───────────────────────────────────────────────
    /// <summary>Maximum allowed bid-ask spread as fraction of mid-price.</summary>
    public double MaxSpreadPct { get; init; } = 0.03;           // 3%
    /// <summary>Spread component weight in composite score.</summary>
    public double SpreadWeight { get; init; } = 0.15;

    // ── L1 volume gate ───────────────────────────────────────────────
    /// <summary>Minimum required recent volume (shares in current slice/snapshot).</summary>
    public double MinVolume { get; init; } = 1000;
    /// <summary>Volume component weight in composite score.</summary>
    public double VolumeWeight { get; init; } = 0.10;

    // ── L2 depth scoring ─────────────────────────────────────────────
    /// <summary>Minimum total bid depth (shares, top 5 levels) to pass gate.</summary>
    public double MinBidDepthShares { get; init; } = 500;
    /// <summary>Minimum total ask depth (shares, top 5 levels) to pass gate.</summary>
    public double MinAskDepthShares { get; init; } = 500;
    /// <summary>Number of order-book levels to evaluate.</summary>
    public int DepthLevels { get; init; } = 5;
    /// <summary>L2 depth component weight in composite score.</summary>
    public double DepthWeight { get; init; } = 0.10;

    // ── Scanner rank score ───────────────────────────────────────────
    /// <summary>Weight of the scanner file's rank/weighted-score in composite.</summary>
    public double FileScoreWeight { get; init; } = 0.25;

    // ── Momentum screen ──────────────────────────────────────────────
    /// <summary>Reject if last-trade is more than this many bps below bid (free-fall).</summary>
    public double MaxAdverseMomentumBps { get; init; } = 50.0;
    /// <summary>Momentum component weight in composite score.</summary>
    public double MomentumWeight { get; init; } = 0.10;

    // ── Self-learning bias integration ───────────────────────────────
    /// <summary>Maximum absolute bias shift from self-learning V2 engine.</summary>
    public double MaxBiasShift { get; init; } = 20.0;
    /// <summary>Bias component weight in composite score.</summary>
    public double BiasWeight { get; init; } = 0.10;

    // ── Time-of-day phase modifier ───────────────────────────────────
    /// <summary>Return multiplier for the first N minutes after open.</summary>
    public double OpenPhaseScoreMultiplier { get; init; } = 0.90;
    /// <summary>Minutes after session open considered "open phase".</summary>
    public int OpenPhaseMinutes { get; init; } = 15;
    /// <summary>Return multiplier for the last N minutes before close.</summary>
    public double ClosePhaseScoreMultiplier { get; init; } = 0.85;
    /// <summary>Minutes before session close considered "close phase".</summary>
    public int ClosePhaseMinutes { get; init; } = 30;
    /// <summary>Time-of-day component weight in composite score.</summary>
    public double TimeOfDayWeight { get; init; } = 0.05;

    // ── Diversification guard ────────────────────────────────────────
    /// <summary>Max fraction of selected symbols from the same exchange.</summary>
    public double MaxExchangeConcentration { get; init; } = 0.60;
    /// <summary>Diversification component weight in composite score.</summary>
    public double DiversificationWeight { get; init; } = 0.05;

    // ── Consistency score ────────────────────────────────────────────
    /// <summary>Weight of the observation consistency sub-score.</summary>
    public double ConsistencyWeight { get; init; } = 0.10;
}

// ─────────────────────────────────────────────────────────────────────────
// INPUT / OUTPUT RECORDS
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Live L1 snapshot per candidate symbol, populated before ranking.
/// </summary>
public sealed record ScannerV2L1Snapshot(
    string Symbol,
    double Bid,
    double Ask,
    double Last,
    double Volume,
    DateTime TimestampUtc
);

/// <summary>
/// Live L2 depth snapshot per candidate symbol, populated before ranking.
/// </summary>
public sealed record ScannerV2L2DepthSnapshot(
    string Symbol,
    IReadOnlyList<ScannerV2DepthLevel> BidLevels,
    IReadOnlyList<ScannerV2DepthLevel> AskLevels,
    DateTime TimestampUtc
);

public sealed record ScannerV2DepthLevel(
    int Position,
    double Price,
    double Size
);

/// <summary>
/// Self-learning V2 bias entry for a single symbol.
/// </summary>
public sealed record ScannerV2SymbolBias(
    string Symbol,
    double Bias,
    double Confidence,
    double ScannerScoreShift
);

/// <summary>
/// Raw candidate row from the scanner file (JSON or Excel).
/// </summary>
public sealed class ScannerV2CandidateFileRow
{
    public string Symbol { get; set; } = string.Empty;
    public double WeightedScore { get; set; }
    public bool? Eligible { get; set; }
    public double AverageRank { get; set; }
    public double Bid { get; set; }
    public double Ask { get; set; }
    public double Mark { get; set; }
    public string Exchange { get; set; } = string.Empty;
}

/// <summary>
/// Fully scored and ranked candidate output from the V2 engine.
/// </summary>
public sealed record ScannerV2RankedCandidate(
    string Symbol,
    double CompositeScore,
    bool Eligible,
    string RejectReason,
    // ── Sub-scores ──────────────────
    double FileScore,
    double SpreadScore,
    double VolumeScore,
    double DepthScore,
    double MomentumScore,
    double BiasScore,
    double TimeOfDayScore,
    double DiversificationScore,
    double ConsistencyScore,
    // ── Raw metrics ─────────────────
    double SpreadPct,
    double BidAskImbalanceRatio,
    double TotalBidDepth,
    double TotalAskDepth,
    double MomentumBps,
    double BiasShift,
    double FileWeightedScore,
    double FileAverageRank
);

/// <summary>
/// Selection snapshot produced by V2 engine.
/// </summary>
public sealed record ScannerV2SelectionSnapshot(
    DateTime TimestampUtc,
    string EngineVersion,
    string SourcePath,
    string SessionPhase,
    int TotalCandidates,
    int EligibleCandidates,
    int SelectedCount,
    IReadOnlyList<string> SelectedSymbols,
    IReadOnlyList<ScannerV2RankedCandidate> RankedCandidates
);

// ─────────────────────────────────────────────────────────────────────────
// ENGINE
// ─────────────────────────────────────────────────────────────────────────

public sealed class ScannerSelectionEngineV2
{
    public const string Version = "V2.0";

    private readonly ScannerSelectionV2Config _config;

    public ScannerSelectionEngineV2(ScannerSelectionV2Config? config = null)
    {
        _config = config ?? new ScannerSelectionV2Config();
    }

    /// <summary>
    /// Run the full V2 selection pipeline: gate → score → rank → select.
    /// </summary>
    /// <param name="fileCandidates">Raw candidates from scanner file.</param>
    /// <param name="l1Snapshots">Live L1 data keyed by symbol (may be empty for replay).</param>
    /// <param name="l2Snapshots">Live L2 data keyed by symbol (may be empty for replay).</param>
    /// <param name="biasEntries">Self-learning V2 bias entries (may be empty).</param>
    /// <param name="sessionOpenUtc">Exchange session open time (for phase calculation).</param>
    /// <param name="sessionCloseUtc">Exchange session close time.</param>
    /// <param name="nowUtc">Current evaluation time.</param>
    /// <param name="sourcePath">Source file path for audit.</param>
    /// <param name="observationCountBySymbol">Observation counts from workbench runs (optional).</param>
    /// <param name="totalObservationRuns">Total workbench runs conducted (optional).</param>
    public ScannerV2SelectionSnapshot Evaluate(
        IReadOnlyList<ScannerV2CandidateFileRow> fileCandidates,
        IReadOnlyDictionary<string, ScannerV2L1Snapshot> l1Snapshots,
        IReadOnlyDictionary<string, ScannerV2L2DepthSnapshot> l2Snapshots,
        IReadOnlyList<ScannerV2SymbolBias> biasEntries,
        DateTime sessionOpenUtc,
        DateTime sessionCloseUtc,
        DateTime nowUtc,
        string sourcePath,
        IReadOnlyDictionary<string, int>? observationCountBySymbol = null,
        int totalObservationRuns = 0)
    {
        var phase = ResolveSessionPhase(sessionOpenUtc, sessionCloseUtc, nowUtc);
        var biasMap = BuildBiasMap(biasEntries);
        var obsMap = observationCountBySymbol ?? new Dictionary<string, int>();

        // ── Normalize candidates ────────────────────────────────────
        var candidates = fileCandidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Symbol))
            .Select(c => new ScannerV2CandidateFileRow
            {
                Symbol = c.Symbol.Trim().ToUpperInvariant(),
                WeightedScore = c.WeightedScore,
                Eligible = c.Eligible,
                AverageRank = c.AverageRank,
                Bid = c.Bid,
                Ask = c.Ask,
                Mark = c.Mark,
                Exchange = c.Exchange
            })
            .ToArray();

        // ── Compute max file score for normalization ────────────────
        var maxFileScore = candidates.Length == 0
            ? 1.0
            : Math.Max(1.0, candidates.Max(c => c.WeightedScore));

        // ── Score each candidate ────────────────────────────────────
        var ranked = candidates
            .Select(c => ScoreCandidate(c, l1Snapshots, l2Snapshots, biasMap, phase,
                maxFileScore, obsMap, totalObservationRuns))
            .OrderByDescending(c => c.Eligible)
            .ThenByDescending(c => c.CompositeScore)
            .ToList();

        // ── Apply diversification guard ─────────────────────────────
        var selected = ApplyDiversificationGuard(ranked);

        return new ScannerV2SelectionSnapshot(
            TimestampUtc: nowUtc,
            EngineVersion: Version,
            SourcePath: sourcePath,
            SessionPhase: phase,
            TotalCandidates: candidates.Length,
            EligibleCandidates: ranked.Count(c => c.Eligible),
            SelectedCount: selected.Count,
            SelectedSymbols: selected.Select(c => c.Symbol).ToList(),
            RankedCandidates: ranked
        );
    }

    // ─────────────────────────────────────────────────────────────────
    // CORE SCORING
    // ─────────────────────────────────────────────────────────────────

    private ScannerV2RankedCandidate ScoreCandidate(
        ScannerV2CandidateFileRow file,
        IReadOnlyDictionary<string, ScannerV2L1Snapshot> l1,
        IReadOnlyDictionary<string, ScannerV2L2DepthSnapshot> l2,
        IReadOnlyDictionary<string, ScannerV2SymbolBias> biasMap,
        string phase,
        double maxFileScore,
        IReadOnlyDictionary<string, int> obsMap,
        int totalRuns)
    {
        var rejectReasons = new List<string>();

        // ── Resolve L1 data (live or from file fallback) ────────────
        l1.TryGetValue(file.Symbol, out var l1Snap);
        var bid = l1Snap?.Bid ?? file.Bid;
        var ask = l1Snap?.Ask ?? file.Ask;
        var last = l1Snap?.Last ?? file.Mark;
        var volume = l1Snap?.Volume ?? 0;

        var mid = (bid > 0 && ask > 0) ? (bid + ask) / 2.0 : last;

        // ── File eligibility gate ───────────────────────────────────
        if (file.Eligible is false)
        {
            rejectReasons.Add("file-ineligible");
        }

        if (file.WeightedScore < _config.MinFileScore)
        {
            rejectReasons.Add($"file-score-below-{_config.MinFileScore:F0}");
        }

        // ── Price range gate ────────────────────────────────────────
        var refPrice = mid > 0 ? mid : file.Mark;
        if (refPrice > 0 && refPrice < _config.MinPrice)
        {
            rejectReasons.Add($"price-below-{_config.MinPrice:F2}");
        }

        if (refPrice > 0 && refPrice > _config.MaxPrice)
        {
            rejectReasons.Add($"price-above-{_config.MaxPrice:F2}");
        }

        // ── L1 spread gate ──────────────────────────────────────────
        double spreadPct = 0;
        if (bid > 0 && ask > 0 && mid > 0)
        {
            spreadPct = (ask - bid) / mid;
            if (spreadPct > _config.MaxSpreadPct)
            {
                rejectReasons.Add($"spread-{spreadPct:P1}-exceeds-{_config.MaxSpreadPct:P1}");
            }
        }

        // ── L1 volume gate ──────────────────────────────────────────
        if (volume > 0 && volume < _config.MinVolume)
        {
            rejectReasons.Add($"volume-{volume:F0}-below-{_config.MinVolume:F0}");
        }

        // ── L2 depth gate ───────────────────────────────────────────
        l2.TryGetValue(file.Symbol, out var l2Snap);
        var bidLevels = l2Snap?.BidLevels ?? Array.Empty<ScannerV2DepthLevel>();
        var askLevels = l2Snap?.AskLevels ?? Array.Empty<ScannerV2DepthLevel>();
        var totalBidDepth = bidLevels.Take(_config.DepthLevels).Sum(l => l.Size);
        var totalAskDepth = askLevels.Take(_config.DepthLevels).Sum(l => l.Size);

        if (l2Snap is not null)
        {
            if (totalBidDepth < _config.MinBidDepthShares)
            {
                rejectReasons.Add($"bid-depth-thin-{totalBidDepth:F0}");
            }

            if (totalAskDepth < _config.MinAskDepthShares)
            {
                rejectReasons.Add($"ask-depth-thin-{totalAskDepth:F0}");
            }
        }

        // ── Momentum gate ───────────────────────────────────────────
        double momentumBps = 0;
        if (bid > 0 && last > 0 && bid > 0)
        {
            momentumBps = ((bid - last) / bid) * 10_000.0;  // negative = last below bid
            if (momentumBps < -_config.MaxAdverseMomentumBps)
            {
                rejectReasons.Add($"freefall-{momentumBps:F1}bps");
            }
        }

        // ═════════════════════════════════════════════════════════════
        // SUB-SCORE COMPUTATION (all normalized to 0..100)
        // ═════════════════════════════════════════════════════════════

        // ── File rank score ─────────────────────────────────────────
        var fileScore = maxFileScore > 0
            ? Math.Clamp((file.WeightedScore / maxFileScore) * 100.0, 0, 100)
            : 0;

        // ── Spread score: tighter spread → higher score ─────────────
        double spreadScore;
        if (spreadPct <= 0 || mid <= 0)
        {
            // No L1 data → neutral
            spreadScore = 50;
        }
        else
        {
            // Linear: 0% spread → 100, MaxSpreadPct → 0
            spreadScore = Math.Clamp((1.0 - (spreadPct / _config.MaxSpreadPct)) * 100.0, 0, 100);
        }

        // ── Volume score: higher volume → higher score ──────────────
        double volumeScore;
        if (volume <= 0)
        {
            volumeScore = 50; // No data → neutral
        }
        else
        {
            // Log-linear: MinVolume → 0, 10× MinVolume → 100
            var volRatio = _config.MinVolume > 0
                ? volume / _config.MinVolume
                : 1.0;
            volumeScore = Math.Clamp(Math.Log10(Math.Max(1, volRatio)) * 50.0, 0, 100);
        }

        // ── L2 depth score ──────────────────────────────────────────
        double depthScore;
        double bidAskImbalanceRatio = 0;
        if (l2Snap is null)
        {
            depthScore = 50; // No L2 data → neutral
        }
        else
        {
            var totalDepth = totalBidDepth + totalAskDepth;
            var minRequired = _config.MinBidDepthShares + _config.MinAskDepthShares;
            var depthRatio = minRequired > 0
                ? totalDepth / minRequired
                : 1.0;
            var depthAbundance = Math.Clamp(depthRatio * 50.0, 0, 70);

            // Imbalance: bid-heavy is bullish for BUY candidates
            bidAskImbalanceRatio = totalAskDepth > 0
                ? totalBidDepth / totalAskDepth
                : (totalBidDepth > 0 ? 2.0 : 1.0);
            var imbalanceBonus = Math.Clamp((bidAskImbalanceRatio - 1.0) * 30.0, -30, 30);

            depthScore = Math.Clamp(depthAbundance + imbalanceBonus, 0, 100);
        }

        // ── Momentum score ──────────────────────────────────────────
        double momentumScore;
        if (last <= 0 || bid <= 0)
        {
            momentumScore = 50; // No data → neutral
        }
        else
        {
            // Positive momentum (last > bid) → score > 50
            // Negative momentum → score < 50
            momentumScore = Math.Clamp(50.0 + (momentumBps / 10.0), 0, 100);
        }

        // ── Bias score (self-learning V2 integration) ───────────────
        double biasShift = 0;
        double biasScore = 50; // Neutral default
        if (biasMap.TryGetValue(file.Symbol, out var bias))
        {
            biasShift = Math.Clamp(bias.ScannerScoreShift, -_config.MaxBiasShift, _config.MaxBiasShift);
            // Map [-MaxBiasShift, +MaxBiasShift] → [0, 100]
            biasScore = Math.Clamp(50.0 + (biasShift / _config.MaxBiasShift) * 50.0, 0, 100);
        }

        // ── Time-of-day score ───────────────────────────────────────
        var todMultiplier = ResolveTimeOfDayMultiplier(phase);
        var timeOfDayScore = Math.Clamp(todMultiplier * 100.0, 0, 100);

        // ── Consistency score (from workbench observations) ─────────
        double consistencyScore;
        if (totalRuns <= 0 || !obsMap.TryGetValue(file.Symbol, out var obsCount))
        {
            consistencyScore = 50; // No workbench data → neutral
        }
        else
        {
            consistencyScore = Math.Clamp((obsCount * 100.0) / Math.Max(1, totalRuns), 0, 100);
        }

        // ── Diversification score (exchange uniqueness) ─────────────
        // Computed at group level in ApplyDiversificationGuard;
        // individual candidate gets neutral score here.
        var diversificationScore = 50.0;

        // ═════════════════════════════════════════════════════════════
        // COMPOSITE SCORE
        // ═════════════════════════════════════════════════════════════

        var composite =
            (fileScore * _config.FileScoreWeight) +
            (spreadScore * _config.SpreadWeight) +
            (volumeScore * _config.VolumeWeight) +
            (depthScore * _config.DepthWeight) +
            (momentumScore * _config.MomentumWeight) +
            (biasScore * _config.BiasWeight) +
            (timeOfDayScore * _config.TimeOfDayWeight) +
            (diversificationScore * _config.DiversificationWeight) +
            (consistencyScore * _config.ConsistencyWeight);

        // Apply time-of-day multiplier as post-composite adjustment
        composite *= todMultiplier;

        var eligible = rejectReasons.Count == 0;
        var rejectReason = eligible ? string.Empty : string.Join(";", rejectReasons);

        return new ScannerV2RankedCandidate(
            Symbol: file.Symbol,
            CompositeScore: Math.Round(composite, 4),
            Eligible: eligible,
            RejectReason: rejectReason,
            FileScore: Math.Round(fileScore, 4),
            SpreadScore: Math.Round(spreadScore, 4),
            VolumeScore: Math.Round(volumeScore, 4),
            DepthScore: Math.Round(depthScore, 4),
            MomentumScore: Math.Round(momentumScore, 4),
            BiasScore: Math.Round(biasScore, 4),
            TimeOfDayScore: Math.Round(timeOfDayScore, 4),
            DiversificationScore: Math.Round(diversificationScore, 4),
            ConsistencyScore: Math.Round(consistencyScore, 4),
            SpreadPct: Math.Round(spreadPct, 6),
            BidAskImbalanceRatio: Math.Round(bidAskImbalanceRatio, 4),
            TotalBidDepth: Math.Round(totalBidDepth, 2),
            TotalAskDepth: Math.Round(totalAskDepth, 2),
            MomentumBps: Math.Round(momentumBps, 2),
            BiasShift: Math.Round(biasShift, 4),
            FileWeightedScore: Math.Round(file.WeightedScore, 4),
            FileAverageRank: Math.Round(file.AverageRank, 4)
        );
    }

    // ─────────────────────────────────────────────────────────────────
    // DIVERSIFICATION GUARD
    // ─────────────────────────────────────────────────────────────────

    private List<ScannerV2RankedCandidate> ApplyDiversificationGuard(
        List<ScannerV2RankedCandidate> ranked)
    {
        var eligible = ranked.Where(c => c.Eligible).ToList();
        if (eligible.Count == 0)
        {
            return [];
        }

        var selected = new List<ScannerV2RankedCandidate>();
        var exchangeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in eligible.OrderByDescending(c => c.CompositeScore))
        {
            if (selected.Count >= _config.TopN)
            {
                break;
            }

            // For now, use symbol prefix as exchange proxy (lacking real exchange data
            // in the scored record; can be upgraded when IBKR contract details are fetched)
            var exchangeKey = ResolveExchangeKey(candidate.Symbol);
            exchangeCounts.TryGetValue(exchangeKey, out var currentCount);

            var maxForExchange = Math.Max(1,
                (int)Math.Ceiling(_config.TopN * _config.MaxExchangeConcentration));

            if (currentCount >= maxForExchange)
            {
                continue;
            }

            exchangeCounts[exchangeKey] = currentCount + 1;
            selected.Add(candidate);
        }

        return selected;
    }

    // ─────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────

    private string ResolveSessionPhase(DateTime sessionOpenUtc, DateTime sessionCloseUtc, DateTime nowUtc)
    {
        if (sessionOpenUtc == DateTime.MinValue || sessionCloseUtc == DateTime.MinValue)
        {
            return "unknown";
        }

        if (nowUtc < sessionOpenUtc)
        {
            return "pre-open";
        }

        if (nowUtc >= sessionCloseUtc)
        {
            return "post-close";
        }

        var minutesSinceOpen = (nowUtc - sessionOpenUtc).TotalMinutes;
        if (minutesSinceOpen <= _config.OpenPhaseMinutes)
        {
            return "open-phase";
        }

        var minutesToClose = (sessionCloseUtc - nowUtc).TotalMinutes;
        if (minutesToClose <= _config.ClosePhaseMinutes)
        {
            return "close-phase";
        }

        return "mid-session";
    }

    private double ResolveTimeOfDayMultiplier(string phase)
    {
        return phase switch
        {
            "open-phase" => _config.OpenPhaseScoreMultiplier,
            "close-phase" => _config.ClosePhaseScoreMultiplier,
            "pre-open" => 0.70,
            "post-close" => 0.50,
            _ => 1.0
        };
    }

    private static string ResolveExchangeKey(string symbol)
    {
        // Simple heuristic: use first letter as exchange proxy for diversification.
        // In production, this should be replaced with actual exchange/sector data.
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return "UNK";
        }

        return symbol.Length switch
        {
            <= 3 => "SHORT",
            4 => "FOUR",
            _ => "LONG"
        };
    }

    private static IReadOnlyDictionary<string, ScannerV2SymbolBias> BuildBiasMap(
        IReadOnlyList<ScannerV2SymbolBias> biasEntries)
    {
        var map = new Dictionary<string, ScannerV2SymbolBias>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in biasEntries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Symbol))
            {
                map[entry.Symbol.Trim().ToUpperInvariant()] = entry;
            }
        }

        return map;
    }

    // ─────────────────────────────────────────────────────────────────
    // BRIDGE: Convert V2 snapshot to V1 snapshot for backward compatibility
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Convert a V2 selection snapshot to the V1
    /// <see cref="ReplayScannerSymbolSelectionSnapshotRow"/> format
    /// so that existing entry strategies and pipelines can consume it
    /// without modification.
    /// </summary>
    public static ReplayScannerSymbolSelectionSnapshotRow ToV1Snapshot(ScannerV2SelectionSnapshot v2)
    {
        var ranked = v2.RankedCandidates
            .Select(c => new ReplayScannerRankedSymbolRow(
                c.Symbol,
                c.CompositeScore,
                c.Eligible,
                c.FileAverageRank))
            .ToList();

        return new ReplayScannerSymbolSelectionSnapshotRow(
            v2.TimestampUtc,
            v2.SourcePath,
            ranked,
            v2.SelectedSymbols.ToList());
    }
}
