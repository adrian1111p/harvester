namespace Harvester.App.Strategy;

public sealed class DeterministicReplayClock
{
    private DateTime _currentUtc;

    public DeterministicReplayClock(DateTime startUtc)
    {
        _currentUtc = startUtc;
    }

    public DateTime UtcNow => _currentUtc;

    public void AdvanceTo(DateTime nextUtc)
    {
        if (nextUtc < _currentUtc)
        {
            throw new InvalidOperationException("Replay clock cannot move backwards.");
        }

        _currentUtc = nextUtc;
    }
}
