using Harvester.App.Historical;
using Harvester.App.IBKR.Runtime;

namespace Harvester.App.IBKR.Broker;

public sealed class IbHistoricalBarNormalizer : IHistoricalNormalizer<HistoricalBarRow, CanonicalHistoricalBar>
{
    private readonly string _symbol;
    private readonly string _securityType;
    private readonly string _exchange;
    private readonly string _currency;

    public IbHistoricalBarNormalizer(string symbol, string securityType, string exchange, string currency)
    {
        _symbol = symbol;
        _securityType = securityType;
        _exchange = exchange;
        _currency = currency;
    }

    public IReadOnlyList<CanonicalHistoricalBar> Normalize(IReadOnlyList<HistoricalBarRow> rows)
    {
        return rows
            .Select(row => new CanonicalHistoricalBar(
                row.TimestampUtc,
                "IBKR",
                _symbol,
                _securityType,
                _exchange,
                _currency,
                row.Time,
                row.Open,
                row.High,
                row.Low,
                row.Close,
                row.Volume,
                row.Wap,
                row.Count,
                false))
            .ToArray();
    }
}

public sealed class IbHistoricalBarUpdateNormalizer : IHistoricalNormalizer<HistoricalBarUpdateRow, CanonicalHistoricalBar>
{
    private readonly string _symbol;
    private readonly string _securityType;
    private readonly string _exchange;
    private readonly string _currency;

    public IbHistoricalBarUpdateNormalizer(string symbol, string securityType, string exchange, string currency)
    {
        _symbol = symbol;
        _securityType = securityType;
        _exchange = exchange;
        _currency = currency;
    }

    public IReadOnlyList<CanonicalHistoricalBar> Normalize(IReadOnlyList<HistoricalBarUpdateRow> rows)
    {
        return rows
            .Select(row => new CanonicalHistoricalBar(
                row.TimestampUtc,
                "IBKR",
                _symbol,
                _securityType,
                _exchange,
                _currency,
                row.Time,
                row.Open,
                row.High,
                row.Low,
                row.Close,
                row.Volume,
                row.Wap,
                row.Count,
                true))
            .ToArray();
    }
}
