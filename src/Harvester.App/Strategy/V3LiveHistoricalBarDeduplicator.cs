using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Strategy;

public sealed class V3LiveHistoricalBarDeduplicator
{
    private readonly Dictionary<string, HistoricalBarWatermark> _watermarkBySymbol = new(StringComparer.OrdinalIgnoreCase);

    public void Reset() => _watermarkBySymbol.Clear();

    public bool ShouldAccept(string symbol, HistoricalBarRow bar)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        _watermarkBySymbol.TryGetValue(normalized, out var watermark);

        if (watermark is not null &&
            bar.TimestampUtc == watermark.TimestampUtc &&
            bar.Open == watermark.Open &&
            bar.High == watermark.High &&
            bar.Low == watermark.Low &&
            bar.Close == watermark.Close &&
            bar.Volume == watermark.Volume)
        {
            return false;
        }

        if (watermark is not null && bar.TimestampUtc < watermark.TimestampUtc)
        {
            return false;
        }

        _watermarkBySymbol[normalized] = new HistoricalBarWatermark(
            bar.TimestampUtc,
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            bar.Volume);

        return true;
    }

    private sealed record HistoricalBarWatermark(
        DateTime TimestampUtc,
        double Open,
        double High,
        double Low,
        double Close,
        decimal Volume);
}
