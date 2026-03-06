using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harvester.App.Strategy;

/// <summary>
/// Unified per-trade journal that chains the full lifecycle:
///   Signal → Order → Fill → Monitoring → Exit → P&amp;L
/// Each trade gets a single <see cref="TradeJournalEntry"/> keyed by IntentId.
/// Entries are written incrementally to a JSONL file (one JSON line per event),
/// and the complete trades are exported on shutdown.
/// </summary>
public sealed class V3LiveTradeJournal
{
    private readonly Dictionary<string, TradeJournalEntry> _trades = new(StringComparer.Ordinal);
    private readonly List<TradeJournalEntry> _completedTrades = [];
    private readonly object _lock = new();
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private string? _journalFilePath;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        WriteIndented = false,
    };

    public V3LiveTradeJournal(TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Initialize the incremental JSONL file path. Call once at session start.</summary>
    public void Initialize(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd_HHmmss");
        _journalFilePath = Path.Combine(outputDirectory, $"v3live_trade_journal_{stamp}.jsonl");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Lifecycle hooks — called from V3LiveRuntime at each stage
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Record an entry signal that passed all gates and was accepted.</summary>
    public void RecordEntry(
        string intentId,
        DateTime timestampUtc,
        string symbol,
        string side,
        string setup,
        V3LiveFeatureSnapshot features,
        V3LiveProposedOrder order,
        string regime)
    {
        lock (_lock)
        {
            var entry = new TradeJournalEntry
            {
                IntentId = intentId,
                Symbol = symbol,
                Side = side,
                Setup = setup,
                Regime = regime,

                // Timing
                EntryTimestampUtc = timestampUtc,

                // Signal features at entry
                EntryPrice = order.EntryPrice,
                StopPrice = order.StopPrice,
                TakeProfitPrice = order.TakeProfitPrice,
                Quantity = order.Quantity,
                EstimatedRiskDollars = order.EstimatedRiskDollars,
                Atr14AtEntry = features.Atr14,
                Rsi14AtEntry = features.Rsi14,
                BbPctBAtEntry = features.BbPctB,
                DistFromVwapAtrAtEntry = features.DistFromVwapAtr,
                StochKAtEntry = features.StochK,
                Adx14AtEntry = features.Adx14,
                RvolAtEntry = features.Rvol,
                OfiSignalAtEntry = features.OfiSignal,
                SpreadPctAtEntry = features.L1.SpreadPct,
                ImbalanceRatioAtEntry = features.L2.ImbalanceRatio,
                SqueezeOnAtEntry = features.SqueezeOn,
                BbBandwidthAtEntry = features.BbBandwidth,
                AtrRatioAtEntry = features.AtrRatio,
            };

            _trades[intentId] = entry;
            AppendToJournal("entry", entry);
        }
    }

    /// <summary>Record a partial exit (e.g. TP1 50% close).</summary>
    public void RecordPartialExit(
        string intentId,
        DateTime timestampUtc,
        double exitPrice,
        int partialQuantity,
        string reason,
        string detail,
        double unrealizedPnl,
        double unrealizedPnlPeak)
    {
        lock (_lock)
        {
            if (!_trades.TryGetValue(intentId, out var trade))
                return;

            trade.PartialExits.Add(new PartialExitRecord(
                TimestampUtc: timestampUtc,
                ExitPrice: exitPrice,
                Quantity: partialQuantity,
                Reason: reason,
                Detail: detail,
                UnrealizedPnl: unrealizedPnl,
                UnrealizedPnlPeak: unrealizedPnlPeak));

            AppendToJournal("partial-exit", trade);
        }
    }

    /// <summary>Record the final full exit that closes the trade.</summary>
    public void RecordExit(
        string intentId,
        DateTime timestampUtc,
        double exitPrice,
        int exitQuantity,
        string reason,
        string detail,
        double unrealizedPnl,
        double unrealizedPnlPeak,
        double holdSeconds,
        double entryPrice,
        double mfe,
        double mae,
        double realizedPnl)
    {
        lock (_lock)
        {
            if (!_trades.TryGetValue(intentId, out var trade))
                return;

            trade.ExitTimestampUtc = timestampUtc;
            trade.ExitPrice = exitPrice;
            trade.ExitQuantity = exitQuantity;
            trade.ExitReason = reason;
            trade.ExitDetail = detail;
            trade.HoldSeconds = holdSeconds;
            trade.UnrealizedPnlAtExit = unrealizedPnl;
            trade.UnrealizedPnlPeak = unrealizedPnlPeak;
            trade.MostFavorableExcursion = mfe;
            trade.MostAdverseExcursion = mae;
            trade.RealizedPnl = realizedPnl;
            trade.IsComplete = true;

            _completedTrades.Add(trade);
            _trades.Remove(intentId);

            AppendToJournal("exit", trade);
        }
    }

    /// <summary>Record a risk guard rejection that prevented an entry.</summary>
    public void RecordRejection(
        string symbol,
        DateTime timestampUtc,
        string side,
        string setup,
        string regime,
        string rejectReason,
        double proposedRisk,
        double currentRisk)
    {
        lock (_lock)
        {
            var entry = new TradeJournalEntry
            {
                IntentId = $"REJECTED-{symbol}-{timestampUtc:yyyyMMddHHmmssfff}",
                Symbol = symbol,
                Side = side,
                Setup = setup,
                Regime = regime,
                EntryTimestampUtc = timestampUtc,
                ExitReason = $"rejected:{rejectReason}",
                ExitDetail = $"proposedRisk={proposedRisk:F2} currentRisk={currentRisk:F2}",
                IsComplete = true,
                IsRejected = true,
            };

            _completedTrades.Add(entry);
            AppendToJournal("rejected", entry);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Export
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Returns all completed + still-open trades (open trades have IsComplete=false).</summary>
    public TradeJournalEntry[] Snapshot()
    {
        lock (_lock)
        {
            var all = new List<TradeJournalEntry>(_completedTrades);
            all.AddRange(_trades.Values); // still-open trades
            return all.ToArray();
        }
    }

    /// <summary>Reset journal for a new session.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _trades.Clear();
            _completedTrades.Clear();
        }
    }

    /// <summary>Try to find the IntentId of the open position for a given symbol.</summary>
    public string? FindOpenIntentId(string symbol)
    {
        lock (_lock)
        {
            foreach (var (id, trade) in _trades)
            {
                if (string.Equals(trade.Symbol, symbol, StringComparison.OrdinalIgnoreCase) && !trade.IsComplete)
                    return id;
            }
            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────────────

    private void AppendToJournal(string eventType, TradeJournalEntry entry)
    {
        if (_journalFilePath is null) return;

        try
        {
            var line = JsonSerializer.Serialize(new
            {
                @event = eventType,
                ts = _timeProvider.GetUtcNow().UtcDateTime,
                entry
            }, JsonOpts);
            File.AppendAllText(_journalFilePath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append trade journal entry");
        }
    }
}

/// <summary>
/// Unified per-trade record carrying the full lifecycle from signal to close.
/// Mutable because fields are filled incrementally (entry → partial → exit).
/// </summary>
public sealed class TradeJournalEntry
{
    // ── Identity ──────────────────────────────────────────────────────────
    public string IntentId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public string Setup { get; init; } = string.Empty;
    public string Regime { get; init; } = string.Empty;

    // ── Entry ─────────────────────────────────────────────────────────────
    public DateTime EntryTimestampUtc { get; init; }
    public double EntryPrice { get; init; }
    public double StopPrice { get; init; }
    public double TakeProfitPrice { get; init; }
    public int Quantity { get; init; }
    public double EstimatedRiskDollars { get; init; }

    // ── Features at entry (signal snapshot) ───────────────────────────────
    public double Atr14AtEntry { get; init; }
    public double Rsi14AtEntry { get; init; }
    public double BbPctBAtEntry { get; init; }
    public double DistFromVwapAtrAtEntry { get; init; }
    public double StochKAtEntry { get; init; }
    public double Adx14AtEntry { get; init; }
    public double RvolAtEntry { get; init; }
    public double OfiSignalAtEntry { get; init; }
    public double SpreadPctAtEntry { get; init; }
    public double ImbalanceRatioAtEntry { get; init; }
    public bool SqueezeOnAtEntry { get; init; }
    public double BbBandwidthAtEntry { get; init; }
    public double AtrRatioAtEntry { get; init; }

    // ── Partial exits ─────────────────────────────────────────────────────
    public List<PartialExitRecord> PartialExits { get; init; } = [];

    // ── Exit ──────────────────────────────────────────────────────────────
    public DateTime? ExitTimestampUtc { get; set; }
    public double ExitPrice { get; set; }
    public int ExitQuantity { get; set; }
    public string? ExitReason { get; set; }
    public string? ExitDetail { get; set; }
    public double HoldSeconds { get; set; }

    // ── P&L & Excursion ──────────────────────────────────────────────────
    public double RealizedPnl { get; set; }
    public double UnrealizedPnlAtExit { get; set; }
    public double UnrealizedPnlPeak { get; set; }
    public double MostFavorableExcursion { get; set; }
    public double MostAdverseExcursion { get; set; }

    // ── Status ────────────────────────────────────────────────────────────
    public bool IsComplete { get; set; }
    public bool IsRejected { get; set; }
}

/// <summary>Record of a partial exit within a trade (e.g., TP1 50% close).</summary>
public sealed record PartialExitRecord(
    DateTime TimestampUtc,
    double ExitPrice,
    int Quantity,
    string Reason,
    string Detail,
    double UnrealizedPnl,
    double UnrealizedPnlPeak);
