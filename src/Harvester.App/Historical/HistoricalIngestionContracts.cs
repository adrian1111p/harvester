using System.Text.Json;

namespace Harvester.App.Historical;

public interface IHistoricalExtractor<out TRaw>
{
    IReadOnlyList<TRaw> Extract();
}

public interface IHistoricalNormalizer<in TRaw, out TCanonical>
{
    IReadOnlyList<TCanonical> Normalize(IReadOnlyList<TRaw> rows);
}

public interface IHistoricalWriter<in TCanonical>
{
    void Write(string outputPath, IReadOnlyList<TCanonical> rows);
}

public sealed class InMemoryHistoricalExtractor<TRaw> : IHistoricalExtractor<TRaw>
{
    private readonly IReadOnlyList<TRaw> _rows;

    public InMemoryHistoricalExtractor(IReadOnlyList<TRaw> rows)
    {
        _rows = rows;
    }

    public IReadOnlyList<TRaw> Extract() => _rows;
}

public sealed class JsonHistoricalWriter<TCanonical> : IHistoricalWriter<TCanonical>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public void Write(string outputPath, IReadOnlyList<TCanonical> rows)
    {
        var payload = JsonSerializer.Serialize(rows, JsonOptions);
        File.WriteAllText(outputPath, payload);
    }
}

public sealed class HistoricalIngestionPipeline<TRaw, TCanonical>
{
    private readonly IHistoricalExtractor<TRaw> _extractor;
    private readonly IHistoricalNormalizer<TRaw, TCanonical> _normalizer;
    private readonly IHistoricalWriter<TCanonical> _writer;

    public HistoricalIngestionPipeline(
        IHistoricalExtractor<TRaw> extractor,
        IHistoricalNormalizer<TRaw, TCanonical> normalizer,
        IHistoricalWriter<TCanonical> writer)
    {
        _extractor = extractor;
        _normalizer = normalizer;
        _writer = writer;
    }

    public IReadOnlyList<TCanonical> Run(string outputPath)
    {
        var raw = _extractor.Extract();
        var normalized = _normalizer.Normalize(raw);
        _writer.Write(outputPath, normalized);
        return normalized;
    }
}

public sealed record CanonicalHistoricalBar(
    DateTime CapturedAtUtc,
    string Source,
    string Symbol,
    string SecurityType,
    string Exchange,
    string Currency,
    string Time,
    double Open,
    double High,
    double Low,
    double Close,
    decimal Volume,
    double Wap,
    int Count,
    bool IsUpdate
);
