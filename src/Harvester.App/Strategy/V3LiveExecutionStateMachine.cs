namespace Harvester.App.Strategy;

public enum V3LiveExecutionState
{
    Created,
    Dequeued,
    Transmitted,
    Closed
}

public sealed record V3LiveExecutionIntentStatus(
    string IntentId,
    string Symbol,
    OrderSide Side,
    int Quantity,
    V3LiveExecutionState State,
    DateTime CreatedUtc,
    DateTime LastUpdatedUtc,
    double LastKnownPrice,
    double RealizedPnl,
    string Notes
);

public sealed record V3LiveExecutionTransition(
    DateTime TimestampUtc,
    string IntentId,
    string Symbol,
    V3LiveExecutionState From,
    V3LiveExecutionState To,
    string Reason,
    double Quantity,
    double Price,
    double RealizedPnl
);

public sealed class V3LiveExecutionStateMachine
{
    private readonly object _sync = new();
    private readonly Dictionary<string, V3LiveExecutionIntentStatus> _statusByIntentId = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<V3LiveExecutionTransition> _transitions = [];
    private readonly TimeProvider _timeProvider;

    public V3LiveExecutionStateMachine(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Reset()
    {
        lock (_sync)
        {
            _statusByIntentId.Clear();
            _transitions.Clear();
        }
    }

    public void OnIntentCreated(LiveOrderIntent intent)
    {
        if (intent is null || string.IsNullOrWhiteSpace(intent.IntentId))
            return;

        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var created = new V3LiveExecutionIntentStatus(
                IntentId: intent.IntentId,
                Symbol: (intent.Symbol ?? string.Empty).Trim().ToUpperInvariant(),
                Side: intent.Side,
                Quantity: intent.Quantity,
                State: V3LiveExecutionState.Created,
                CreatedUtc: now,
                LastUpdatedUtc: now,
                LastKnownPrice: intent.EntryPrice,
                RealizedPnl: 0.0,
                Notes: "intent-created");

            _statusByIntentId[intent.IntentId] = created;
        }
    }

    public void OnIntentDequeued(string intentId)
    {
        if (string.IsNullOrWhiteSpace(intentId))
            return;

        lock (_sync)
        {
            if (!_statusByIntentId.TryGetValue(intentId, out var status))
                return;

            if (status.State == V3LiveExecutionState.Created)
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var updated = status with
                {
                    State = V3LiveExecutionState.Dequeued,
                    LastUpdatedUtc = now,
                    Notes = "intent-dequeued"
                };
                _statusByIntentId[intentId] = updated;
                _transitions.Add(new V3LiveExecutionTransition(
                    TimestampUtc: now,
                    IntentId: intentId,
                    Symbol: status.Symbol,
                    From: V3LiveExecutionState.Created,
                    To: V3LiveExecutionState.Dequeued,
                    Reason: "consume-order-intents",
                    Quantity: status.Quantity,
                    Price: status.LastKnownPrice,
                    RealizedPnl: 0.0));
            }
        }
    }

    public void OnIntentTransmitted(string intentId, string symbol, double quantity, double fillPrice)
    {
        if (string.IsNullOrWhiteSpace(intentId))
            return;

        lock (_sync)
        {
            if (!_statusByIntentId.TryGetValue(intentId, out var status))
            {
                var fallbackNow = _timeProvider.GetUtcNow().UtcDateTime;
                status = new V3LiveExecutionIntentStatus(
                    IntentId: intentId,
                    Symbol: (symbol ?? string.Empty).Trim().ToUpperInvariant(),
                    Side: OrderSide.Buy, // default when intent not found
                    Quantity: (int)Math.Round(Math.Abs(quantity)),
                    State: V3LiveExecutionState.Transmitted,
                    CreatedUtc: fallbackNow,
                    LastUpdatedUtc: fallbackNow,
                    LastKnownPrice: fillPrice,
                    RealizedPnl: 0.0,
                    Notes: "transmitted-without-create");

                _statusByIntentId[intentId] = status;
                return;
            }

            var from = status.State;
            if (from == V3LiveExecutionState.Transmitted || from == V3LiveExecutionState.Closed)
                return;

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var updated = status with
            {
                State = V3LiveExecutionState.Transmitted,
                LastUpdatedUtc = now,
                LastKnownPrice = fillPrice > 0 ? fillPrice : status.LastKnownPrice,
                Notes = "intent-transmitted"
            };
            _statusByIntentId[intentId] = updated;

            _transitions.Add(new V3LiveExecutionTransition(
                TimestampUtc: now,
                IntentId: intentId,
                Symbol: updated.Symbol,
                From: from,
                To: V3LiveExecutionState.Transmitted,
                Reason: "broker-transmit-ack",
                Quantity: quantity,
                Price: fillPrice,
                RealizedPnl: 0.0));
        }
    }

    public void OnPositionClosed(string symbol, double closedQuantity, double realizedPnl)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        lock (_sync)
        {
            var candidate = _statusByIntentId.Values
                .Where(x => string.Equals(x.Symbol, normalized, StringComparison.OrdinalIgnoreCase)
                            && x.State == V3LiveExecutionState.Transmitted)
                .OrderByDescending(x => x.LastUpdatedUtc)
                .FirstOrDefault();

            if (candidate is null)
                return;

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var updated = candidate with
            {
                State = V3LiveExecutionState.Closed,
                LastUpdatedUtc = now,
                RealizedPnl = candidate.RealizedPnl + realizedPnl,
                Notes = "position-close-ack"
            };
            _statusByIntentId[candidate.IntentId] = updated;

            _transitions.Add(new V3LiveExecutionTransition(
                TimestampUtc: now,
                IntentId: candidate.IntentId,
                Symbol: normalized,
                From: V3LiveExecutionState.Transmitted,
                To: V3LiveExecutionState.Closed,
                Reason: "position-closed",
                Quantity: closedQuantity,
                Price: updated.LastKnownPrice,
                RealizedPnl: realizedPnl));
        }
    }

    public (IReadOnlyList<V3LiveExecutionIntentStatus> Statuses, IReadOnlyList<V3LiveExecutionTransition> Transitions) Snapshot()
    {
        lock (_sync)
        {
            var statuses = _statusByIntentId.Values
                .OrderBy(x => x.CreatedUtc)
                .ToArray();
            var transitions = _transitions
                .OrderBy(x => x.TimestampUtc)
                .ToArray();
            return (statuses, transitions);
        }
    }
}
