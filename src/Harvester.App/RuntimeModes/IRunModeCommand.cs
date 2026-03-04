using Harvester.App.IBKR.Runtime;
using Microsoft.Extensions.Logging;

namespace Harvester.App.RuntimeModes;

public interface IRunModeCommand
{
    bool CanHandle(RunMode mode);
    Task<int> ExecuteAsync(AppOptions options, string[] args, ILoggerFactory loggerFactory);
}
