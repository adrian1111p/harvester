namespace Harvester.App.Strategy;

public interface IStrategyEventScheduler
{
    IReadOnlyList<string> GetDueEvents(StrategyRuntimeContext context, DateTime utcNow);
}
