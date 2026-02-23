using IBApi;
using Harvester.App.IBKR.Wrapper;

namespace Harvester.App.IBKR.Connection;

public sealed class IbkrSession : IDisposable
{
    private readonly EReaderSignal _signal;
    private readonly EClientSocket _client;
    private EReader? _reader;
    private Thread? _readerThread;
    private readonly object _stateLock = new();

    public HarvesterEWrapper Wrapper { get; }
    public EClientSocket Client => _client;
    public IbConnectionState State { get; private set; } = IbConnectionState.Disconnected;
    public List<IbConnectionTransition> StateTransitions { get; } = [];

    public IbkrSession(HarvesterEWrapper? wrapper = null)
    {
        Wrapper = wrapper ?? new HarvesterEWrapper();
        _signal = new EReaderMonitorSignal();
        _client = new EClientSocket(Wrapper, _signal);
    }

    public async Task ConnectAsync(string host, int port, int clientId, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        TransitionTo(IbConnectionState.Connecting, "connect requested");

        _client.eConnect(host, port, clientId);
        if (!_client.IsConnected())
        {
            TransitionTo(IbConnectionState.Halting, "socket connection failed");
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

        try
        {
            _ = await AwaitWithTimeout(Wrapper.NextValidIdTask, timeoutCts.Token, "nextValidId");
            _ = await AwaitWithTimeout(Wrapper.ManagedAccountsTask, timeoutCts.Token, "managedAccounts");
            TransitionTo(IbConnectionState.Connected, "session handshake completed");
        }
        catch
        {
            TransitionTo(IbConnectionState.Halting, "session handshake failed");
            throw;
        }
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
        TransitionTo(IbConnectionState.Halting, "disconnect requested");

        if (_client.IsConnected())
        {
            _client.eDisconnect();
        }

        TransitionTo(IbConnectionState.Disconnected, "socket disconnected");
    }

    private void TransitionTo(IbConnectionState next, string reason)
    {
        lock (_stateLock)
        {
            if (State == next)
            {
                return;
            }

            var previous = State;
            State = next;
            StateTransitions.Add(new IbConnectionTransition(DateTime.UtcNow, previous, next, reason));
            Console.WriteLine($"[STATE] {previous} -> {next} reason={reason}");
        }
    }

    public void Dispose()
    {
        Disconnect();
    }
}
