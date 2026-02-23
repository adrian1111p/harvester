namespace Harvester.App.Strategy;

public interface IStrategyRuntime
{
    Task InitializeAsync(StrategyRuntimeContext context, CancellationToken cancellationToken);
    Task OnScheduledEventAsync(string eventName, StrategyRuntimeContext context, CancellationToken cancellationToken);
    Task OnDataAsync(StrategyDataSlice dataSlice, CancellationToken cancellationToken);
    Task OnShutdownAsync(StrategyRuntimeContext context, int exitCode, CancellationToken cancellationToken);
}
