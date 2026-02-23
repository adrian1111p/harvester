using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harvester.App.IBKR.Connection;

namespace Harvester.App.IBKR.Runtime;

public sealed class RuntimeStateStore
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _baseDir;
    private readonly string _latestStatePath;
    private readonly string _latestChecksumPath;
    private readonly string _versionsDir;
    private readonly string _quarantineDir;

    public RuntimeStateStore(string exportDir)
    {
        _baseDir = Path.Combine(exportDir, "runtime_state");
        _latestStatePath = Path.Combine(_baseDir, "runtime_state_latest.json");
        _latestChecksumPath = Path.Combine(_baseDir, "runtime_state_latest.sha256");
        _versionsDir = Path.Combine(_baseDir, "versions");
        _quarantineDir = Path.Combine(_baseDir, "quarantine");

        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(_versionsDir);
        Directory.CreateDirectory(_quarantineDir);
    }

    public bool TryLoadLatest(out RuntimeStateCheckpoint? checkpoint, out string? message)
    {
        checkpoint = null;
        message = null;

        if (!File.Exists(_latestStatePath))
        {
            return false;
        }

        try
        {
            var payload = File.ReadAllText(_latestStatePath);
            var computedChecksum = ComputeChecksum(payload);
            var expectedChecksum = File.Exists(_latestChecksumPath)
                ? File.ReadAllText(_latestChecksumPath).Trim()
                : string.Empty;

            if (!string.Equals(computedChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
            {
                QuarantineCorruptFiles("checksum_mismatch");
                message = "Runtime state checksum mismatch detected; latest checkpoint moved to quarantine.";
                return false;
            }

            checkpoint = JsonSerializer.Deserialize<RuntimeStateCheckpoint>(payload, JsonOptions);
            if (checkpoint is null)
            {
                QuarantineCorruptFiles("deserialize_failed");
                message = "Runtime state deserialize failed; latest checkpoint moved to quarantine.";
                return false;
            }

            if (checkpoint.SchemaVersion != SchemaVersion)
            {
                message = $"Runtime state schema mismatch (found={checkpoint.SchemaVersion}, expected={SchemaVersion}).";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            QuarantineCorruptFiles("load_exception");
            message = $"Runtime state load failed; latest checkpoint moved to quarantine ({ex.Message}).";
            return false;
        }
    }

    public void Save(RuntimeStateSnapshot snapshot)
    {
        var checkpoint = new RuntimeStateCheckpoint(
            SchemaVersion,
            DateTime.UtcNow,
            snapshot
        );

        var payload = JsonSerializer.Serialize(checkpoint, JsonOptions);
        var checksum = ComputeChecksum(payload);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var versionPath = Path.Combine(_versionsDir, $"runtime_state_{timestamp}.json");
        var versionChecksumPath = Path.Combine(_versionsDir, $"runtime_state_{timestamp}.sha256");

        File.WriteAllText(versionPath, payload);
        File.WriteAllText(versionChecksumPath, checksum);
        File.WriteAllText(_latestStatePath, payload);
        File.WriteAllText(_latestChecksumPath, checksum);
    }

    public static RuntimeStateSnapshot BuildSnapshot(
        AppOptions options,
        IbkrSession session,
        IReadOnlyCollection<RequestRecord> requests,
        int apiErrorCount,
        bool hasConnectivityFailure,
        bool hasReconciliationQualityFailure,
        int exitCode,
        DateTime runStartedUtc,
        RuntimeLifecycleStage lifecycleStage,
        IReadOnlyList<RuntimeLifecycleTransition> lifecycleTransitions)
    {
        var requestArray = requests.ToArray();
        var timedOutRequests = requestArray.Count(r => r.Status == RequestStatus.TimedOut);
        var failedRequests = requestArray.Count(r => r.Status == RequestStatus.Failed);

        return new RuntimeStateSnapshot(
            options.Mode.ToString(),
            options.Host,
            options.Port,
            options.ClientId,
            options.Account,
            runStartedUtc,
            DateTime.UtcNow,
            exitCode,
            session.State.ToString(),
            session.StateTransitions.ToArray(),
            requestArray.Length,
            timedOutRequests,
            failedRequests,
            apiErrorCount,
            hasConnectivityFailure,
            hasReconciliationQualityFailure,
            lifecycleStage.ToString(),
            lifecycleTransitions
        );
    }

    private void QuarantineCorruptFiles(string reason)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        if (File.Exists(_latestStatePath))
        {
            var target = Path.Combine(_quarantineDir, $"runtime_state_latest_{reason}_{stamp}.json");
            File.Move(_latestStatePath, target, overwrite: true);
        }

        if (File.Exists(_latestChecksumPath))
        {
            var target = Path.Combine(_quarantineDir, $"runtime_state_latest_{reason}_{stamp}.sha256");
            File.Move(_latestChecksumPath, target, overwrite: true);
        }
    }

    private static string ComputeChecksum(string payload)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}

public enum RuntimeLifecycleStage
{
    Startup,
    SteadyState,
    Shutdown,
    Halted
}

public sealed record RuntimeLifecycleTransition(
    DateTime TimestampUtc,
    RuntimeLifecycleStage From,
    RuntimeLifecycleStage To,
    string Reason
);

public sealed record RuntimeStateCheckpoint(
    int SchemaVersion,
    DateTime CheckpointUtc,
    RuntimeStateSnapshot Snapshot
);

public sealed record RuntimeStateSnapshot(
    string Mode,
    string Host,
    int Port,
    int ClientId,
    string Account,
    DateTime RunStartedUtc,
    DateTime RunCompletedUtc,
    int ExitCode,
    string ConnectionState,
    IReadOnlyList<IbConnectionTransition> StateTransitions,
    int RequestCount,
    int TimedOutRequestCount,
    int FailedRequestCount,
    int ApiErrorCount,
    bool ConnectivityHaltTriggered,
    bool ReconciliationGateFailed,
    string LifecycleStage,
    IReadOnlyList<RuntimeLifecycleTransition> LifecycleTransitions
);
