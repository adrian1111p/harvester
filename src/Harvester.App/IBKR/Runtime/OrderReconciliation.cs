namespace Harvester.App.IBKR.Runtime;

public static class OrderReconciliation
{
    public static ReconciliationResult Reconcile(
        IReadOnlyCollection<OpenOrderRow> openOrders,
        IReadOnlyCollection<CompletedOrderRow> completedOrders,
        IReadOnlyCollection<ExecutionRow> executions)
    {
        var rows = new Dictionary<string, CanonicalOrderLedgerRow>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<ReconciliationDiagnosticRow>();

        var orderIdByPermId = executions
            .Where(e => e.PermId > 0 && e.OrderId > 0)
            .GroupBy(e => e.PermId)
            .ToDictionary(g => g.Key, g => g.First().OrderId);

        foreach (var order in openOrders)
        {
            var key = KeyByOrderId(order.OrderId);
            rows[key] = new CanonicalOrderLedgerRow(
                CanonicalOrderKey: key,
                OrderId: order.OrderId,
                PermId: null,
                Account: order.Account,
                Symbol: order.Symbol,
                SecurityType: order.SecurityType,
                Action: order.Action,
                OrderType: order.OrderType,
                Status: order.Status,
                TotalQuantity: order.TotalQuantity,
                FilledQuantity: 0,
                AverageFillPrice: null,
                ExecutionCount: 0,
                Commission: null,
                Sources: ["open-order"]
            );
        }

        foreach (var completed in completedOrders)
        {
            var mappedOrderId = 0;
            var key = completed.PermId > 0 && orderIdByPermId.TryGetValue(completed.PermId, out mappedOrderId)
                ? KeyByOrderId(mappedOrderId)
                : KeyByPermId(completed.PermId);

            if (!rows.TryGetValue(key, out var existing))
            {
                rows[key] = new CanonicalOrderLedgerRow(
                    CanonicalOrderKey: key,
                    OrderId: mappedOrderId > 0 ? mappedOrderId : null,
                    PermId: completed.PermId,
                    Account: completed.Account,
                    Symbol: completed.Symbol,
                    SecurityType: completed.SecurityType,
                    Action: completed.Action,
                    OrderType: completed.OrderType,
                    Status: completed.Status,
                    TotalQuantity: completed.TotalQuantity,
                    FilledQuantity: 0,
                    AverageFillPrice: null,
                    ExecutionCount: 0,
                    Commission: null,
                    Sources: ["completed-order"]
                );
                diagnostics.Add(new ReconciliationDiagnosticRow(
                    "completed_without_open",
                    key,
                    $"Completed order found without prior open-order row (permId={completed.PermId})."));
                continue;
            }

            rows[key] = existing with
            {
                PermId = completed.PermId > 0 ? completed.PermId : existing.PermId,
                Status = completed.Status,
                Sources = MergeSources(existing.Sources, "completed-order")
            };
        }

        foreach (var executionGroup in executions.GroupBy(e => ResolveExecutionKey(e)))
        {
            var key = executionGroup.Key;
            var execs = executionGroup.ToArray();

            var totalShares = execs.Sum(e => Math.Abs(e.Shares));
            var weightedNotional = execs.Sum(e => Math.Abs(e.Shares) * e.Price);
            var avgPrice = totalShares > 0 ? weightedNotional / totalShares : (double?)null;
            var netFilled = execs.Sum(e => e.Side.Equals("BOT", StringComparison.OrdinalIgnoreCase) || e.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase)
                ? Math.Abs(e.Shares)
                : -Math.Abs(e.Shares));

            if (!rows.TryGetValue(key, out var existing))
            {
                var first = execs[0];
                rows[key] = new CanonicalOrderLedgerRow(
                    CanonicalOrderKey: key,
                    OrderId: first.OrderId > 0 ? first.OrderId : null,
                    PermId: first.PermId > 0 ? first.PermId : null,
                    Account: first.Account,
                    Symbol: first.Symbol,
                    SecurityType: first.SecurityType,
                    Action: netFilled >= 0 ? "BUY" : "SELL",
                    OrderType: "UNKNOWN",
                    Status: "EXECUTION_ONLY",
                    TotalQuantity: Math.Abs(netFilled),
                    FilledQuantity: Math.Abs(netFilled),
                    AverageFillPrice: avgPrice,
                    ExecutionCount: execs.Length,
                    Commission: null,
                    Sources: ["execution"]
                );

                diagnostics.Add(new ReconciliationDiagnosticRow(
                    "execution_without_order",
                    key,
                    $"Execution rows found without matching open/completed order metadata (execCount={execs.Length})."));
                continue;
            }

            rows[key] = existing with
            {
                PermId = existing.PermId ?? execs[0].PermId,
                FilledQuantity = Math.Abs(netFilled),
                AverageFillPrice = avgPrice,
                ExecutionCount = execs.Length,
                Sources = MergeSources(existing.Sources, "execution")
            };
        }

        foreach (var row in rows.Values)
        {
            if (row.Sources.Contains("open-order") && !row.Sources.Contains("execution") && !row.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new ReconciliationDiagnosticRow(
                    "open_without_execution",
                    row.CanonicalOrderKey,
                    "Open order has no matched execution rows in current snapshot window."));
            }
        }

        return new ReconciliationResult(rows.Values.OrderBy(r => r.CanonicalOrderKey).ToArray(), diagnostics.ToArray());
    }

    private static string ResolveExecutionKey(ExecutionRow execution)
    {
        if (execution.OrderId > 0)
        {
            return KeyByOrderId(execution.OrderId);
        }

        if (execution.PermId > 0)
        {
            return KeyByPermId(execution.PermId);
        }

        return $"SYM:{execution.Symbol}|ACC:{execution.Account}";
    }

    private static string KeyByOrderId(int orderId) => $"OID:{orderId}";
    private static string KeyByPermId(int permId) => $"PID:{permId}";

    private static string[] MergeSources(string[] sources, string source)
    {
        if (sources.Contains(source, StringComparer.OrdinalIgnoreCase))
        {
            return sources;
        }

        return [.. sources, source];
    }
}

public sealed record CanonicalOrderLedgerRow(
    string CanonicalOrderKey,
    int? OrderId,
    int? PermId,
    string Account,
    string Symbol,
    string SecurityType,
    string Action,
    string OrderType,
    string Status,
    double TotalQuantity,
    double FilledQuantity,
    double? AverageFillPrice,
    int ExecutionCount,
    double? Commission,
    string[] Sources
);

public sealed record ReconciliationDiagnosticRow(
    string Kind,
    string CanonicalOrderKey,
    string Message
);

public sealed record ReconciliationResult(
    CanonicalOrderLedgerRow[] Ledger,
    ReconciliationDiagnosticRow[] Diagnostics
);
