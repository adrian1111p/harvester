using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

/// <summary>
/// Tracks live position state per symbol: open quantity, average cost, peak/trough prices,
/// unrealized and realized PnL, and open risk. Fed from <see cref="StrategyDataSlice"/>
/// positions data and order event callbacks. Provides the data contract that
/// <see cref="V3LiveRiskGuard"/> needs for accurate daily-loss and open-risk checks.
/// </summary>
public sealed class V3LivePositionTracker
{
    private readonly Dictionary<string, V3LiveTrackedPosition> _positions = new(StringComparer.OrdinalIgnoreCase);
    private double _totalRealizedPnlToday;
    private int _totalFilledOrdersToday;

    /// <summary>Current tracked positions keyed by symbol.</summary>
    public IReadOnlyDictionary<string, V3LiveTrackedPosition> Positions => _positions;

    /// <summary>Aggregate realized PnL across all symbols today.</summary>
    public double TotalRealizedPnlToday => _totalRealizedPnlToday;

    /// <summary>Total filled orders today across all symbols.</summary>
    public int TotalFilledOrdersToday => _totalFilledOrdersToday;

    /// <summary>
    /// Sync position state from the IBKR positions snapshot (from StrategyDataSlice).
    /// Called on every data tick to keep positions current.
    /// </summary>
    public void SyncFromPositions(IReadOnlyList<PositionRow> positionRows, string account)
    {
        // Mark all existing positions as potentially stale
        foreach (var pos in _positions.Values)
            pos.LastSyncSeen = false;

        foreach (var row in positionRows)
        {
            if (!string.Equals(row.Account, account, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(row.Symbol)) continue;
            if (!string.Equals(row.SecurityType, "STK", StringComparison.OrdinalIgnoreCase)) continue;

            var symbol = row.Symbol.Trim().ToUpperInvariant();
            if (!_positions.TryGetValue(symbol, out var tracked))
            {
                tracked = new V3LiveTrackedPosition(symbol);
                _positions[symbol] = tracked;
            }

            var previousQty = tracked.Quantity;
            tracked.Quantity = row.Quantity;
            tracked.AverageCost = row.AverageCost;
            tracked.LastSyncSeen = true;
            tracked.LastSyncUtc = DateTime.UtcNow;

            // Detect position close via quantity change: had position → now flat
            if (previousQty != 0 && row.Quantity == 0 && tracked.EntryPrice > 0)
            {
                var estimatedRealizedPnl = tracked.Side == "LONG"
                    ? (tracked.LastMarkPrice - tracked.EntryPrice) * Math.Abs(previousQty)
                    : (tracked.EntryPrice - tracked.LastMarkPrice) * Math.Abs(previousQty);

                tracked.RealizedPnl += estimatedRealizedPnl;
                _totalRealizedPnlToday += estimatedRealizedPnl;
                tracked.OpenRiskDollars = 0;
                tracked.IsFlat = true;
            }
            else if (row.Quantity != 0)
            {
                tracked.IsFlat = false;
                tracked.Side = row.Quantity > 0 ? "LONG" : "SHORT";
            }
        }

        // Positions that weren't seen in the sync are potentially closed externally
        foreach (var kvp in _positions)
        {
            if (!kvp.Value.LastSyncSeen && kvp.Value.Quantity != 0)
            {
                // Position disappeared from IBKR — assume flattened
                kvp.Value.Quantity = 0;
                kvp.Value.OpenRiskDollars = 0;
                kvp.Value.IsFlat = true;
            }
        }
    }

    /// <summary>
    /// Update the mark (last traded) price for a symbol from L1 data.
    /// Also updates peak/trough tracking for exit monitoring.
    /// </summary>
    public void UpdateMarkPrice(string symbol, double markPrice, DateTime timestampUtc)
    {
        if (string.IsNullOrWhiteSpace(symbol) || markPrice <= 0) return;

        var normalized = symbol.Trim().ToUpperInvariant();
        if (!_positions.TryGetValue(normalized, out var tracked)) return;

        tracked.LastMarkPrice = markPrice;
        tracked.LastMarkUtc = timestampUtc;

        if (tracked.Quantity == 0) return;

        // Update peak/trough for trailing stop logic
        if (tracked.Side == "LONG")
        {
            tracked.PeakPriceSinceEntry = Math.Max(tracked.PeakPriceSinceEntry, markPrice);
            tracked.TroughPriceSinceEntry = tracked.TroughPriceSinceEntry > 0
                ? Math.Min(tracked.TroughPriceSinceEntry, markPrice)
                : markPrice;

            tracked.UnrealizedPnl = (markPrice - tracked.EntryPrice) * Math.Abs(tracked.Quantity);
            tracked.UnrealizedPnlPeak = Math.Max(tracked.UnrealizedPnlPeak, tracked.UnrealizedPnl);
        }
        else if (tracked.Side == "SHORT")
        {
            tracked.PeakPriceSinceEntry = tracked.PeakPriceSinceEntry > 0
                ? Math.Min(tracked.PeakPriceSinceEntry, markPrice)
                : markPrice;
            tracked.TroughPriceSinceEntry = Math.Max(tracked.TroughPriceSinceEntry, markPrice);

            tracked.UnrealizedPnl = (tracked.EntryPrice - markPrice) * Math.Abs(tracked.Quantity);
            tracked.UnrealizedPnlPeak = Math.Max(tracked.UnrealizedPnlPeak, tracked.UnrealizedPnl);
        }
    }

    /// <summary>
    /// Record an entry fill. Called when the host acknowledges an order was transmitted and filled.
    /// </summary>
    public void RecordEntry(string symbol, string side, double quantity, double fillPrice, double estimatedRiskDollars, string intentId)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (!_positions.TryGetValue(normalized, out var tracked))
        {
            tracked = new V3LiveTrackedPosition(normalized);
            _positions[normalized] = tracked;
        }

        tracked.IntentId = intentId;
        tracked.Side = side.ToUpperInvariant() == "BUY" ? "LONG" : "SHORT";
        tracked.Quantity = side.ToUpperInvariant() == "BUY" ? quantity : -quantity;
        tracked.EntryPrice = fillPrice;
        tracked.EntryUtc = DateTime.UtcNow;
        tracked.AverageCost = fillPrice;
        tracked.OpenRiskDollars = estimatedRiskDollars;
        tracked.PeakPriceSinceEntry = fillPrice;
        tracked.TroughPriceSinceEntry = fillPrice;
        tracked.LastMarkPrice = fillPrice;
        tracked.UnrealizedPnl = 0;
        tracked.UnrealizedPnlPeak = 0;
        tracked.IsFlat = false;
        _totalFilledOrdersToday++;
    }

    /// <summary>
    /// Record a position close (full or partial). Called via <see cref="ILiveOrderSignalSource.AcknowledgePositionClosed"/>.
    /// </summary>
    public void RecordClose(string symbol, double closedQuantity, double realizedPnl)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (!_positions.TryGetValue(normalized, out var tracked)) return;

        tracked.RealizedPnl += realizedPnl;
        _totalRealizedPnlToday += realizedPnl;
        tracked.OpenRiskDollars = 0;

        if (Math.Abs(closedQuantity) >= Math.Abs(tracked.Quantity))
        {
            tracked.Quantity = 0;
            tracked.IsFlat = true;
        }
        else
        {
            tracked.Quantity -= closedQuantity;
        }
    }

    /// <summary>
    /// Check whether a symbol has an open (non-zero) position.
    /// </summary>
    public bool HasOpenPosition(string symbol)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        return _positions.TryGetValue(normalized, out var tracked) && tracked.Quantity != 0;
    }

    /// <summary>
    /// Get the tracked position for a symbol, or null if not tracked.
    /// </summary>
    public V3LiveTrackedPosition? GetPosition(string symbol)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        return _positions.TryGetValue(normalized, out var tracked) ? tracked : null;
    }

    /// <summary>
    /// Get aggregate open risk across all symbols (sum of OpenRiskDollars for non-flat positions).
    /// </summary>
    public double GetTotalOpenRisk()
    {
        return _positions.Values
            .Where(p => !p.IsFlat)
            .Sum(p => p.OpenRiskDollars);
    }

    /// <summary>
    /// Build a <see cref="V3LiveSymbolRiskState"/> for the risk guard, using live tracked data.
    /// </summary>
    public V3LiveSymbolRiskState BuildRiskState(string symbol)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        var totalOpenRisk = GetTotalOpenRisk();

        return new V3LiveSymbolRiskState
        {
            OpenRiskDollars = totalOpenRisk,
            RealizedPnlToday = _totalRealizedPnlToday
        };
    }

    /// <summary>
    /// Reset daily accumulators (call on session-open-reset event).
    /// </summary>
    public void ResetDaily()
    {
        _totalRealizedPnlToday = 0;
        _totalFilledOrdersToday = 0;

        foreach (var pos in _positions.Values)
        {
            pos.RealizedPnl = 0;
            pos.UnrealizedPnlPeak = 0;
        }
    }
}

/// <summary>
/// Live tracked position state for a single symbol.
/// Mutable — updated continuously from market data and order events.
/// </summary>
public sealed class V3LiveTrackedPosition
{
    public V3LiveTrackedPosition(string symbol) => Symbol = symbol;

    public string Symbol { get; }
    public string IntentId { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public double Quantity { get; set; }
    public double AverageCost { get; set; }
    public double EntryPrice { get; set; }
    public DateTime EntryUtc { get; set; }
    public double StopPrice { get; set; }
    public double TakeProfitPrice { get; set; }
    public double LastMarkPrice { get; set; }
    public DateTime LastMarkUtc { get; set; }
    public double PeakPriceSinceEntry { get; set; }
    public double TroughPriceSinceEntry { get; set; }
    public double UnrealizedPnl { get; set; }
    public double UnrealizedPnlPeak { get; set; }
    public double RealizedPnl { get; set; }
    public double OpenRiskDollars { get; set; }
    public bool IsFlat { get; set; } = true;
    public bool LastSyncSeen { get; set; }
    public DateTime LastSyncUtc { get; set; }
}
