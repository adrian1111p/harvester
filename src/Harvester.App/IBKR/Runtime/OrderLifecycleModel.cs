using Harvester.App.IBKR.Broker;
using Harvester.App.IBKR.Wrapper;

namespace Harvester.App.IBKR.Runtime;

public enum HarvesterOrderLifecycleState
{
    Unknown,
    Accepted,
    Working,
    PartiallyFilled,
    Filled,
    UpdatePending,
    CancelPending,
    Canceled,
    Rejected,
    Inactive
}

public enum ApiErrorDisposition
{
    Ignored,
    Warning,
    Retryable,
    NonBlocking,
    Blocking
}

public sealed record ApiErrorClassification(
    ApiErrorDisposition Disposition,
    IbErrorAction PolicyAction,
    string Reason
);

public sealed record OrderLifecycleTransitionArtifactRow(
    DateTime TimestampUtc,
    int OrderId,
    int PermId,
    string Symbol,
    string EventType,
    string RawStatus,
    HarvesterOrderLifecycleState PreviousState,
    HarvesterOrderLifecycleState NextState,
    bool TransitionAllowed,
    string TransitionReason
);

public sealed record OrderLifecycleSummaryRow(
    DateTime TimestampUtc,
    string Mode,
    int OrdersObserved,
    int TransitionCount,
    int InvalidTransitionCount,
    int ActiveOrderCount,
    int TerminalFilledCount,
    int TerminalCanceledCount,
    int TerminalRejectedCount,
    int TerminalInactiveCount
);

public sealed record ApiErrorNormalizationRow(
    DateTime TimestampUtc,
    int? Id,
    int? Code,
    string Message,
    ApiErrorDisposition Disposition,
    IbErrorAction PolicyAction,
    bool Blocking,
    string Reason
);

public static class OrderLifecycleModel
{
    public static HarvesterOrderLifecycleState NormalizeStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "PENDINGSUBMIT" or "PRESUBMITTED" or "APIPENDING" => HarvesterOrderLifecycleState.Accepted,
            "SUBMITTED" => HarvesterOrderLifecycleState.Working,
            "PARTIALLYFILLED" => HarvesterOrderLifecycleState.PartiallyFilled,
            "FILLED" => HarvesterOrderLifecycleState.Filled,
            "PENDINGREPLACE" or "PENDINGMODIFY" => HarvesterOrderLifecycleState.UpdatePending,
            "PENDINGCANCEL" => HarvesterOrderLifecycleState.CancelPending,
            "CANCELLED" or "CANCELED" or "APICANCELLED" => HarvesterOrderLifecycleState.Canceled,
            "REJECTED" => HarvesterOrderLifecycleState.Rejected,
            "INACTIVE" => HarvesterOrderLifecycleState.Inactive,
            _ => HarvesterOrderLifecycleState.Unknown
        };
    }

    public static bool IsTransitionAllowed(HarvesterOrderLifecycleState previous, HarvesterOrderLifecycleState next)
    {
        if (previous == HarvesterOrderLifecycleState.Unknown || previous == next)
        {
            return true;
        }

        return previous switch
        {
            HarvesterOrderLifecycleState.Accepted => next is HarvesterOrderLifecycleState.Working
                or HarvesterOrderLifecycleState.PartiallyFilled
                or HarvesterOrderLifecycleState.Filled
                or HarvesterOrderLifecycleState.CancelPending
                or HarvesterOrderLifecycleState.Canceled
                or HarvesterOrderLifecycleState.UpdatePending
                or HarvesterOrderLifecycleState.Inactive
                or HarvesterOrderLifecycleState.Rejected,
            HarvesterOrderLifecycleState.Working => next is HarvesterOrderLifecycleState.PartiallyFilled
                or HarvesterOrderLifecycleState.Filled
                or HarvesterOrderLifecycleState.CancelPending
                or HarvesterOrderLifecycleState.Canceled
                or HarvesterOrderLifecycleState.UpdatePending
                or HarvesterOrderLifecycleState.Inactive
                or HarvesterOrderLifecycleState.Rejected,
            HarvesterOrderLifecycleState.PartiallyFilled => next is HarvesterOrderLifecycleState.PartiallyFilled
                or HarvesterOrderLifecycleState.Filled
                or HarvesterOrderLifecycleState.CancelPending
                or HarvesterOrderLifecycleState.Canceled
                or HarvesterOrderLifecycleState.UpdatePending
                or HarvesterOrderLifecycleState.Inactive,
            HarvesterOrderLifecycleState.UpdatePending => next is HarvesterOrderLifecycleState.Working
                or HarvesterOrderLifecycleState.PartiallyFilled
                or HarvesterOrderLifecycleState.Filled
                or HarvesterOrderLifecycleState.CancelPending
                or HarvesterOrderLifecycleState.Canceled
                or HarvesterOrderLifecycleState.Inactive
                or HarvesterOrderLifecycleState.Rejected,
            HarvesterOrderLifecycleState.CancelPending => next is HarvesterOrderLifecycleState.Canceled
                or HarvesterOrderLifecycleState.Working
                or HarvesterOrderLifecycleState.PartiallyFilled
                or HarvesterOrderLifecycleState.Filled
                or HarvesterOrderLifecycleState.Inactive,
            HarvesterOrderLifecycleState.Filled
                or HarvesterOrderLifecycleState.Canceled
                or HarvesterOrderLifecycleState.Rejected
                or HarvesterOrderLifecycleState.Inactive => false,
            _ => false
        };
    }

    public static OrderLifecycleTransitionArtifactRow[] BuildTransitions(IReadOnlyCollection<CanonicalOrderEvent> events)
    {
        var rows = new List<OrderLifecycleTransitionArtifactRow>(events.Count);
        var latestStateByOrderId = new Dictionary<int, HarvesterOrderLifecycleState>();

        foreach (var evt in events.OrderBy(x => x.TimestampUtc).ThenBy(x => x.OrderId))
        {
            var previous = latestStateByOrderId.TryGetValue(evt.OrderId, out var state)
                ? state
                : HarvesterOrderLifecycleState.Unknown;
            var next = NormalizeStatus(evt.Status);
            var allowed = IsTransitionAllowed(previous, next);
            var reason = allowed
                ? "ok"
                : $"invalid transition: {previous} -> {next}";

            rows.Add(new OrderLifecycleTransitionArtifactRow(
                evt.TimestampUtc,
                evt.OrderId,
                evt.PermId,
                evt.Symbol,
                evt.EventType,
                evt.Status,
                previous,
                next,
                allowed,
                reason));

            if (next != HarvesterOrderLifecycleState.Unknown)
            {
                latestStateByOrderId[evt.OrderId] = next;
            }
        }

        return rows.ToArray();
    }

    public static OrderLifecycleSummaryRow BuildSummary(string mode, IReadOnlyCollection<OrderLifecycleTransitionArtifactRow> transitions)
    {
        var latest = transitions
            .GroupBy(x => x.OrderId)
            .Select(g => g.OrderByDescending(x => x.TimestampUtc).First())
            .ToArray();

        var activeOrderCount = latest.Count(x => x.NextState is HarvesterOrderLifecycleState.Accepted
            or HarvesterOrderLifecycleState.Working
            or HarvesterOrderLifecycleState.PartiallyFilled
            or HarvesterOrderLifecycleState.UpdatePending
            or HarvesterOrderLifecycleState.CancelPending);

        return new OrderLifecycleSummaryRow(
            DateTime.UtcNow,
            mode,
            latest.Length,
            transitions.Count,
            transitions.Count(x => !x.TransitionAllowed),
            activeOrderCount,
            latest.Count(x => x.NextState == HarvesterOrderLifecycleState.Filled),
            latest.Count(x => x.NextState == HarvesterOrderLifecycleState.Canceled),
            latest.Count(x => x.NextState == HarvesterOrderLifecycleState.Rejected),
            latest.Count(x => x.NextState == HarvesterOrderLifecycleState.Inactive));
    }

    public static ApiErrorClassification ClassifyApiError(IbApiError error, AppOptions options, IbErrorPolicy errorPolicy)
    {
        if (IsExchangeDeferralWarning(error))
        {
            return new ApiErrorClassification(
                ApiErrorDisposition.NonBlocking,
                IbErrorAction.Warn,
                "exchange deferral warning (order accepted for next session window)");
        }

        if (IsSoftenedCancelNotFoundError(error, options))
        {
            return new ApiErrorClassification(
                ApiErrorDisposition.NonBlocking,
                IbErrorAction.Warn,
                "idempotent cancel not-found confirmation");
        }

        if (IsCancelSuccessConfirmation(error, options))
        {
            return new ApiErrorClassification(
                ApiErrorDisposition.NonBlocking,
                IbErrorAction.Warn,
                "cancel success confirmation callback");
        }

        var decision = errorPolicy.Evaluate(error, options.Mode, options.OptionGreeksAutoFallback);
        var disposition = decision.Action switch
        {
            IbErrorAction.Ignore => ApiErrorDisposition.Ignored,
            IbErrorAction.Warn => ApiErrorDisposition.Warning,
            IbErrorAction.Retry => ApiErrorDisposition.Retryable,
            IbErrorAction.HardFail => ApiErrorDisposition.Blocking,
            _ => ApiErrorDisposition.Warning
        };

        return new ApiErrorClassification(disposition, decision.Action, decision.Reason);
    }

    private static bool IsExchangeDeferralWarning(IbApiError error)
    {
        if (error.Code != 399)
        {
            return false;
        }

        return error.Message.Contains("will not be placed at the exchange until", StringComparison.OrdinalIgnoreCase)
            || error.Message.Contains("outside regular trading hours", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSoftenedCancelNotFoundError(IbApiError error, AppOptions options)
    {
        if (!options.CancelOrderIdempotent || options.Mode != RunMode.OrdersCancelSim)
        {
            return false;
        }

        if (error.Code != 10147)
        {
            return false;
        }

        if (options.CancelOrderId > 0 && error.Id.HasValue && error.Id.Value != options.CancelOrderId)
        {
            return false;
        }

        return error.Message.Contains("needs to be cancelled is not found", StringComparison.OrdinalIgnoreCase)
            || error.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCancelSuccessConfirmation(IbApiError error, AppOptions options)
    {
        if (options.Mode != RunMode.OrdersCancelSim)
        {
            return false;
        }

        if (error.Code != 202)
        {
            return false;
        }

        if (options.CancelOrderId > 0 && error.Id.HasValue && error.Id.Value != options.CancelOrderId)
        {
            return false;
        }

        return error.Message.Contains("order canceled", StringComparison.OrdinalIgnoreCase)
            || error.Message.Contains("order cancelled", StringComparison.OrdinalIgnoreCase);
    }
}