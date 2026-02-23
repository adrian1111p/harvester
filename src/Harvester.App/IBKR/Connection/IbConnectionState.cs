namespace Harvester.App.IBKR.Connection;

public enum IbConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Degraded,
    Halting
}

public sealed record IbConnectionTransition(
    DateTime UtcTimestamp,
    IbConnectionState From,
    IbConnectionState To,
    string Reason
);
