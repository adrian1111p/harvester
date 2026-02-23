using Harvester.App.IBKR.Runtime;

var options = AppOptions.Parse(args);
var runtime = new SnapshotRuntime(options);
var exitCode = await runtime.RunAsync();
Environment.Exit(exitCode);
