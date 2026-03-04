using Harvester.App.IBKR.Broker;
using Harvester.App.IBKR.Runtime;
using IBApi;

namespace Harvester.App.Monitor;

/// <summary>
/// Background poller that subscribes to IBKR account updates,
/// drains the wrapper queues into <see cref="PositionMonitorStore"/>,
/// then unsubscribes and sleeps before the next cycle.
/// </summary>
public sealed class IbkrPositionPoller
{
    private readonly SnapshotEWrapper _wrapper;
    private readonly IBrokerAdapter _brokerAdapter;
    private readonly EClientSocket _client;
    private readonly string _account;
    private readonly PositionMonitorStore _store;
    private readonly TimeSpan _pollInterval;

    public IbkrPositionPoller(
        SnapshotEWrapper wrapper,
        IBrokerAdapter brokerAdapter,
        EClientSocket client,
        string account,
        PositionMonitorStore store,
        TimeSpan? pollInterval = null)
    {
        _wrapper = wrapper;
        _brokerAdapter = brokerAdapter;
        _client = client;
        _account = account;
        _store = store;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>
    /// Runs a continuous subscribe → drain → unsubscribe loop until cancelled.
    /// </summary>
    public async Task RunAsync(CancellationToken token)
    {
        Console.WriteLine($"[Monitor] Poller started – account={_account} interval={_pollInterval.TotalSeconds}s");

        while (!token.IsCancellationRequested)
        {
            try
            {
                // Reset the TCS so we can await the next download-end signal.
                _wrapper.ResetAccountDownloadEnd();

                // Subscribe
                _brokerAdapter.RequestAccountUpdates(_client, true, _account);

                // Wait for IBKR to signal all data has been sent
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

                try
                {
                    await _wrapper.AccountDownloadEndTask.WaitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    Console.WriteLine("[Monitor] AccountDownloadEnd timeout – retrying next cycle.");
                }

                // Drain portfolio updates
                while (_wrapper.PortfolioUpdates.TryDequeue(out var row))
                {
                    _store.Update(row);
                }

                // Drain account value updates
                while (_wrapper.AccountValueUpdates.TryDequeue(out var av))
                {
                    _store.UpdateAccountValue(av.Key, av.Value, av.Currency);
                }

                // Unsubscribe
                _brokerAdapter.RequestAccountUpdates(_client, false, _account);

                await Task.Delay(_pollInterval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Monitor] Poller error: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
        }

        Console.WriteLine("[Monitor] Poller stopped.");
    }
}
