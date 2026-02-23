using IBApi;
using Harvester.App.IBKR.Wrapper;

namespace Harvester.App.IBKR.Connection;

public sealed class IbkrSession : IDisposable
{
    private readonly EReaderSignal _signal;
    private readonly EClientSocket _client;
    private EReader? _reader;
    private Thread? _readerThread;

    public HarvesterEWrapper Wrapper { get; }
    public EClientSocket Client => _client;

    public IbkrSession(HarvesterEWrapper? wrapper = null)
    {
        Wrapper = wrapper ?? new HarvesterEWrapper();
        _signal = new EReaderMonitorSignal();
        _client = new EClientSocket(Wrapper, _signal);
    }

    public async Task ConnectAsync(string host, int port, int clientId, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        _client.eConnect(host, port, clientId);
        if (!_client.IsConnected())
        {
            throw new InvalidOperationException("IBKR socket connection failed.");
        }

        _reader = new EReader(_client, _signal);
        _reader.Start();

        _readerThread = new Thread(ProcessMessages)
        {
            IsBackground = true,
            Name = "IBKR-EReader"
        };
        _readerThread.Start();

        _client.startApi();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        _ = await AwaitWithTimeout(Wrapper.NextValidIdTask, timeoutCts.Token, "nextValidId");
        _ = await AwaitWithTimeout(Wrapper.ManagedAccountsTask, timeoutCts.Token, "managedAccounts");
    }

    private void ProcessMessages()
    {
        while (_client.IsConnected())
        {
            _signal.waitForSignal();
            _reader?.processMsgs();
        }
    }

    private static async Task<T> AwaitWithTimeout<T>(Task<T> task, CancellationToken cancellationToken, string stage)
    {
        var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var winner = await Task.WhenAny(task, timeoutTask);
        if (winner == task)
        {
            return await task;
        }

        throw new TimeoutException($"Timed out waiting for {stage}.");
    }

    public void Disconnect()
    {
        if (_client.IsConnected())
        {
            _client.eDisconnect();
        }
    }

    public void Dispose()
    {
        Disconnect();
    }
}
