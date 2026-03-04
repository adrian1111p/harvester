using System.Collections.Concurrent;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Monitor;

/// <summary>
/// Thread-safe in-memory store for live position snapshots.
/// Keyed by Symbol; each update bumps Version for change detection.
/// </summary>
public sealed class PositionMonitorStore
{
    private readonly ConcurrentDictionary<string, MonitorPositionRow> _positions = new(StringComparer.OrdinalIgnoreCase);
    private long _version;
    private readonly ConcurrentDictionary<string, string> _accountValues = new(StringComparer.OrdinalIgnoreCase);

    public long Version => Interlocked.Read(ref _version);

    public event Action? OnChanged;

    /// <summary>
    /// Upsert a position from a PortfolioUpdateRow.
    /// </summary>
    public void Update(PortfolioUpdateRow row)
    {
        var pos = new MonitorPositionRow(
            row.TimestampUtc,
            row.Account,
            row.ConId,
            row.Symbol,
            row.SecurityType,
            row.Currency,
            row.Exchange,
            row.Position,
            row.MarketPrice,
            row.MarketValue,
            row.AverageCost,
            row.UnrealizedPnl,
            row.RealizedPnl
        );

        _positions.AddOrUpdate(row.Symbol, pos, (_, _) => pos);
        Interlocked.Increment(ref _version);
        OnChanged?.Invoke();
    }

    /// <summary>
    /// Update an account-level value (NetLiquidation, TotalCashValue, etc.).
    /// </summary>
    public void UpdateAccountValue(string key, string value, string currency)
    {
        var compositeKey = $"{key}|{currency}";
        _accountValues.AddOrUpdate(compositeKey, value, (_, _) => value);
    }

    /// <summary>
    /// Return all current positions (including zero-quantity closed ones for the session).
    /// </summary>
    public IReadOnlyList<MonitorPositionRow> GetAll()
    {
        return _positions.Values.OrderBy(p => p.Symbol).ToList();
    }

    /// <summary>
    /// Return account-level summary values.
    /// </summary>
    public Dictionary<string, string> GetAccountSummary()
    {
        return _accountValues.ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}

public sealed record MonitorPositionRow(
    DateTime TimestampUtc,
    string Account,
    int ConId,
    string Symbol,
    string SecurityType,
    string Currency,
    string Exchange,
    double Position,
    double MarketPrice,
    double MarketValue,
    double AverageCost,
    double UnrealizedPnl,
    double RealizedPnl
);
