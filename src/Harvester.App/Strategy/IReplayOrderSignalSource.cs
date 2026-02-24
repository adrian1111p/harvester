namespace Harvester.App.Strategy;

public interface IReplayOrderSignalSource
{
    IReadOnlyList<ReplayOrderIntent> GetReplayOrderIntents(StrategyDataSlice dataSlice, StrategyRuntimeContext context);
}
