using Harvester.App.IBKR.Runtime;
using Harvester.App.RuntimeModes;
using Microsoft.Extensions.Logging;

var options = AppOptions.Parse(args);
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.ClearProviders();
    builder.AddSimpleConsole(console =>
    {
        console.SingleLine = true;
        console.TimestampFormat = "HH:mm:ss ";
    });
    builder.SetMinimumLevel(LogLevel.Information);
});

var commands = new IRunModeCommand[]
{
    new BacktestRunModeCommand(),
    new SnapshotRuntimeCommand()
};

var command = commands.FirstOrDefault(x => x.CanHandle(options.Mode));
if (command is null)
{
    throw new InvalidOperationException($"No run mode command registered for mode '{options.Mode}'.");
}

var exitCode = await command.ExecuteAsync(options, args, loggerFactory);
Environment.Exit(exitCode);
