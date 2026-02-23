using System.Collections.Concurrent;

namespace Harvester.App.IBKR.Runtime;

public enum RequestStatus
{
    Started,
    Completed,
    TimedOut,
    Failed,
    Cancelled
}

public sealed record RequestRecord(
    Guid CorrelationId,
    int? RequestId,
    string Type,
    string Origin,
    DateTime StartedAtUtc,
    DateTime DeadlineUtc,
    RequestStatus Status,
    DateTime? EndedAtUtc,
    string? Details
);

public sealed class RequestRegistry
{
    private readonly ConcurrentDictionary<Guid, RequestRecord> _records = new();

    public Guid Register(int? requestId, string type, string origin, DateTime deadlineUtc)
    {
        var correlationId = Guid.NewGuid();
        var record = new RequestRecord(
            correlationId,
            requestId,
            type,
            origin,
            DateTime.UtcNow,
            deadlineUtc,
            RequestStatus.Started,
            null,
            null
        );

        _records[correlationId] = record;
        return correlationId;
    }

    public void Complete(Guid correlationId, string? details = null) => SetStatus(correlationId, RequestStatus.Completed, details);

    public void Timeout(Guid correlationId, string? details = null) => SetStatus(correlationId, RequestStatus.TimedOut, details);

    public void Fail(Guid correlationId, string? details = null) => SetStatus(correlationId, RequestStatus.Failed, details);

    public void Cancel(Guid correlationId, string? details = null) => SetStatus(correlationId, RequestStatus.Cancelled, details);

    public IReadOnlyCollection<RequestRecord> Snapshot()
    {
        return _records.Values.OrderBy(r => r.StartedAtUtc).ToArray();
    }

    public string Describe(Guid correlationId)
    {
        if (!_records.TryGetValue(correlationId, out var record))
        {
            return $"corr={correlationId} <not-found>";
        }

        var reqId = record.RequestId?.ToString() ?? "n/a";
        return $"corr={record.CorrelationId} reqId={reqId} type={record.Type} origin={record.Origin} status={record.Status} started={record.StartedAtUtc:O} deadline={record.DeadlineUtc:O}";
    }

    private void SetStatus(Guid correlationId, RequestStatus status, string? details)
    {
        _records.AddOrUpdate(
            correlationId,
            _ => throw new InvalidOperationException($"Request correlation id not found: {correlationId}"),
            (_, current) => current with
            {
                Status = status,
                EndedAtUtc = DateTime.UtcNow,
                Details = details
            }
        );
    }
}
