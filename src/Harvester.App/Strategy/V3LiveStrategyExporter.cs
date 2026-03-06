using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harvester.App.Strategy;

/// <summary>
/// Defines the contract for exporting V3Live runtime data on shutdown.
/// Decouples I/O concerns from strategy logic in <see cref="V3LiveRuntime"/>.
/// </summary>
public interface IStrategyExporter
{
    Task ExportAsync(V3LiveExportPayload payload, CancellationToken cancellationToken);
}

/// <summary>
/// All data gathered during a V3Live session, packaged for export on shutdown.
/// </summary>
public sealed record V3LiveExportPayload(
    string OutputDirectory,
    string TimestampStamp,
    V3LiveRuntimeSummary Summary,
    IReadOnlyList<V3LivePositionSummary> Positions,
    V3LiveEvaluationRow[] Evaluations,
    V3LiveSignalRow[] Signals,
    V3LiveExitEventRow[] ExitEvents,
    V3LiveRiskEventRow[] RiskEvents,
    IReadOnlyList<V3LiveExecutionIntentStatus> ExecutionStatuses,
    IReadOnlyList<V3LiveExecutionTransition> ExecutionTransitions,
    TradeJournalEntry[]? TradeJournal = null);

/// <summary>
/// Position summary record for export (avoids anonymous types in serialization).
/// </summary>
public sealed record V3LivePositionSummary(
    string Symbol,
    PositionSide Side,
    double Quantity,
    double EntryPrice,
    double LastMarkPrice,
    double UnrealizedPnl,
    double RealizedPnl,
    double MostFavorablePriceSinceEntry,
    double MostAdversePriceSinceEntry,
    bool IsFlat);

/// <summary>
/// Default JSON file exporter — writes 8 separate JSON files to the output directory.
/// </summary>
public sealed class JsonStrategyExporter : IStrategyExporter
{
    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public async Task ExportAsync(V3LiveExportPayload payload, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(payload.OutputDirectory);
        var stamp = payload.TimestampStamp;
        var dir = payload.OutputDirectory;

        await File.WriteAllTextAsync(
            Path.Combine(dir, $"v3live_runtime_summary_{stamp}.json"),
            JsonSerializer.Serialize(payload.Summary, ExportOptions),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(dir, $"v3live_evaluations_{stamp}.json"),
            JsonSerializer.Serialize(payload.Evaluations, ExportOptions),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(dir, $"v3live_signals_{stamp}.json"),
            JsonSerializer.Serialize(payload.Signals, ExportOptions),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(dir, $"v3live_exit_events_{stamp}.json"),
            JsonSerializer.Serialize(payload.ExitEvents, ExportOptions),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(dir, $"v3live_risk_events_{stamp}.json"),
            JsonSerializer.Serialize(payload.RiskEvents, ExportOptions),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(dir, $"v3live_positions_{stamp}.json"),
            JsonSerializer.Serialize(payload.Positions, ExportOptions),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(dir, $"v3live_execution_intents_{stamp}.json"),
            JsonSerializer.Serialize(payload.ExecutionStatuses, ExportOptions),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(dir, $"v3live_execution_transitions_{stamp}.json"),
            JsonSerializer.Serialize(payload.ExecutionTransitions, ExportOptions),
            cancellationToken);

        if (payload.TradeJournal is { Length: > 0 })
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, $"v3live_trade_journal_{stamp}.json"),
                JsonSerializer.Serialize(payload.TradeJournal, ExportOptions),
                cancellationToken);
        }
    }
}
