namespace Harvester.App.IBKR.Risk;

public enum PreTradeAction
{
    Warn,
    Reject,
    Halt
}

public sealed record PreTradeViolation(
    string Guard,
    PreTradeAction Action,
    string Message
);

public sealed record PreTradeContext(
    string Route,
    string Symbol,
    string Action,
    double Quantity,
    double LimitPrice,
    double Notional,
    int DailyOrderCountAfter
);

public sealed class PreTradeControlDsl
{
    private static readonly IReadOnlyDictionary<string, PreTradeAction> DefaultPolicy =
        new Dictionary<string, PreTradeAction>(StringComparer.OrdinalIgnoreCase)
        {
            ["max-notional"] = PreTradeAction.Reject,
            ["max-qty"] = PreTradeAction.Reject,
            ["max-daily-orders"] = PreTradeAction.Reject,
            ["session-window"] = PreTradeAction.Halt
        };

    public IReadOnlyList<PreTradeViolation> Evaluate(
        PreTradeContext context,
        string dsl,
        double maxNotional,
        double maxQty,
        int maxDailyOrders,
        TimeOnly? sessionStart,
        TimeOnly? sessionEnd,
        DateTime nowUtc)
    {
        var policy = ParsePolicy(dsl);
        var violations = new List<PreTradeViolation>();

        if (context.Notional > maxNotional)
        {
            violations.Add(new PreTradeViolation(
                "max-notional",
                ResolveAction(policy, "max-notional"),
                $"route={context.Route} symbol={context.Symbol} notional {context.Notional:F2} exceeds {maxNotional:F2}"));
        }

        if (context.Quantity > maxQty)
        {
            violations.Add(new PreTradeViolation(
                "max-qty",
                ResolveAction(policy, "max-qty"),
                $"route={context.Route} symbol={context.Symbol} quantity {context.Quantity:F4} exceeds {maxQty:F4}"));
        }

        if (context.DailyOrderCountAfter > maxDailyOrders)
        {
            violations.Add(new PreTradeViolation(
                "max-daily-orders",
                ResolveAction(policy, "max-daily-orders"),
                $"route={context.Route} daily-order-count {context.DailyOrderCountAfter} exceeds {maxDailyOrders}"));
        }

        if (sessionStart.HasValue && sessionEnd.HasValue)
        {
            var now = TimeOnly.FromDateTime(nowUtc);
            var inWindow = IsWithinWindow(now, sessionStart.Value, sessionEnd.Value);
            if (!inWindow)
            {
                violations.Add(new PreTradeViolation(
                    "session-window",
                    ResolveAction(policy, "session-window"),
                    $"route={context.Route} current UTC time {now:HH\\:mm} outside allowed window {sessionStart:HH\\:mm}-{sessionEnd:HH\\:mm}"));
            }
        }

        return violations;
    }

    public static TimeOnly? ParseTimeOrNull(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TimeOnly.TryParse(value, out var parsed)
            ? parsed
            : throw new ArgumentException($"Invalid time value '{value}'. Use HH:mm (UTC).");
    }

    private static IReadOnlyDictionary<string, PreTradeAction> ParsePolicy(string dsl)
    {
        var policy = new Dictionary<string, PreTradeAction>(DefaultPolicy, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(dsl))
        {
            return policy;
        }

        var pairs = dsl.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=', StringSplitOptions.TrimEntries);
            if (kv.Length != 2)
            {
                throw new ArgumentException($"Invalid pretrade control token '{pair}'. Use guard=warn|reject|halt.");
            }

            var guard = kv[0].Trim().ToLowerInvariant();
            var action = kv[1].Trim().ToLowerInvariant() switch
            {
                "warn" => PreTradeAction.Warn,
                "reject" => PreTradeAction.Reject,
                "halt" => PreTradeAction.Halt,
                _ => throw new ArgumentException($"Invalid pretrade action '{kv[1]}'. Use warn|reject|halt.")
            };

            policy[guard] = action;
        }

        return policy;
    }

    private static PreTradeAction ResolveAction(IReadOnlyDictionary<string, PreTradeAction> policy, string guard)
    {
        return policy.TryGetValue(guard, out var action)
            ? action
            : DefaultPolicy[guard];
    }

    private static bool IsWithinWindow(TimeOnly current, TimeOnly start, TimeOnly end)
    {
        if (start <= end)
        {
            return current >= start && current <= end;
        }

        return current >= start || current <= end;
    }
}
