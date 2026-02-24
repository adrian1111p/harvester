namespace Harvester.App.Strategy;

public sealed record AlphaInsight(
    DateTime TimestampUtc,
    string Symbol,
    string Direction,
    double Confidence,
    TimeSpan Horizon,
    string Source
);

public sealed record PortfolioTarget(
    DateTime TimestampUtc,
    string Symbol,
    double TargetQuantity,
    string Source
);

public interface IAlphaModel
{
    IReadOnlyList<AlphaInsight> Update(StrategyDataSlice dataSlice, StrategyRuntimeContext context);
}

public interface IPortfolioConstructionModel
{
    IReadOnlyList<PortfolioTarget> CreateTargets(
        IReadOnlyList<AlphaInsight> insights,
        ReplayPortfolioRow portfolio,
        StrategyRuntimeContext context);
}

public interface IRiskManagementModel
{
    IReadOnlyList<PortfolioTarget> Apply(
        IReadOnlyList<PortfolioTarget> targets,
        ReplayPortfolioRow portfolio,
        StrategyRuntimeContext context);
}

public interface IExecutionModel
{
    IReadOnlyList<ReplayOrderIntent> Execute(
        IReadOnlyList<PortfolioTarget> targets,
        ReplayPortfolioRow portfolio,
        StrategyRuntimeContext context,
        StrategyDataSlice dataSlice);
}

public sealed class NullAlphaModel : IAlphaModel
{
    public IReadOnlyList<AlphaInsight> Update(StrategyDataSlice dataSlice, StrategyRuntimeContext context) => [];
}

public sealed class NullPortfolioConstructionModel : IPortfolioConstructionModel
{
    public IReadOnlyList<PortfolioTarget> CreateTargets(IReadOnlyList<AlphaInsight> insights, ReplayPortfolioRow portfolio, StrategyRuntimeContext context) => [];
}

public sealed class NullRiskManagementModel : IRiskManagementModel
{
    public IReadOnlyList<PortfolioTarget> Apply(IReadOnlyList<PortfolioTarget> targets, ReplayPortfolioRow portfolio, StrategyRuntimeContext context) => targets;
}

public sealed class NullExecutionModel : IExecutionModel
{
    public IReadOnlyList<ReplayOrderIntent> Execute(IReadOnlyList<PortfolioTarget> targets, ReplayPortfolioRow portfolio, StrategyRuntimeContext context, StrategyDataSlice dataSlice) => [];
}
