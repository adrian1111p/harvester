using System.Collections.Concurrent;
using IBApi;

namespace Harvester.App.IBKR.Wrapper;

public class HarvesterEWrapper : DefaultEWrapper
{
    private readonly TaskCompletionSource<int> _nextValidIdTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _managedAccountsTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _contractDetailsEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConcurrentQueue<string> Errors { get; } = new();
    public ConcurrentQueue<ContractDetails> ContractDetailsRows { get; } = new();
    public ConcurrentQueue<string> OrderStatusRows { get; } = new();

    public Task<int> NextValidIdTask => _nextValidIdTcs.Task;
    public Task<string> ManagedAccountsTask => _managedAccountsTcs.Task;
    public Task<bool> ContractDetailsEndTask => _contractDetailsEndTcs.Task;

    public override void nextValidId(int orderId)
    {
        _nextValidIdTcs.TrySetResult(orderId);
    }

    public override void managedAccounts(string accountsList)
    {
        _managedAccountsTcs.TrySetResult(accountsList);
    }

    public override void contractDetails(int reqId, ContractDetails contractDetails)
    {
        ContractDetailsRows.Enqueue(contractDetails);
    }

    public override void contractDetailsEnd(int reqId)
    {
        _contractDetailsEndTcs.TrySetResult(true);
    }

    public override void orderStatus(
        int orderId,
        string status,
        double filled,
        double remaining,
        double avgFillPrice,
        int permId,
        int parentId,
        double lastFillPrice,
        int clientId,
        string whyHeld,
        double mktCapPrice)
    {
        var row =
            $"orderId={orderId} status={status} filled={filled} remaining={remaining} avgFillPrice={avgFillPrice} permId={permId} parentId={parentId} lastFillPrice={lastFillPrice} clientId={clientId} whyHeld={whyHeld} mktCapPrice={mktCapPrice}";
        OrderStatusRows.Enqueue(row);
    }

    public override void openOrder(int orderId, Contract contract, Order order, OrderState orderState)
    {
        var row =
            $"openOrder id={orderId} symbol={contract.Symbol} secType={contract.SecType} action={order.Action} type={order.OrderType} qty={order.TotalQuantity} status={orderState.Status}";
        OrderStatusRows.Enqueue(row);
    }

    public override void error(int id, int errorCode, string errorMsg)
    {
        Errors.Enqueue($"[ERROR] id={id} code={errorCode} msg={errorMsg}");
    }

    public override void error(Exception e)
    {
        Errors.Enqueue($"[ERROR] exception={e.Message}");
    }

    public override void error(string str)
    {
        Errors.Enqueue($"[ERROR] raw={str}");
    }
}
