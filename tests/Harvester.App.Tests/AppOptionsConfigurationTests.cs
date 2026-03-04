using Harvester.App.IBKR.Runtime;

namespace Harvester.App.Tests;

public sealed class AppOptionsConfigurationTests
{
    [Fact]
    public void Parse_MapsNestedConfigurationKeysToCliOptions()
    {
        var configJson = """
{
  "Harvester": {
    "Replay": {
      "Scanner": {
        "TopN": 9,
        "MinScore": 72.5,
        "OrderType": "LMT"
      }
    }
  }
}
""";

        var (configPath, configDirectory) = WriteTempConfig(configJson);
        try
        {
            var options = AppOptions.Parse(["--config", configPath]);

            Assert.Equal(9, options.ReplayScannerTopN);
            Assert.Equal(72.5, options.ReplayScannerMinScore);
            Assert.Equal("LMT", options.ReplayScannerOrderType);
        }
        finally
        {
            TryDeleteDirectory(configDirectory);
        }
    }

    [Fact]
    public void Parse_MapsNestedArrayConfigurationToCommaSeparatedCliOption()
    {
        var configJson = """
{
  "Harvester": {
    "Allowed": {
      "Symbols": ["AAPL", "TSLA", "NVDA"]
    }
  }
}
""";

        var (configPath, configDirectory) = WriteTempConfig(configJson);
        try
        {
            var options = AppOptions.Parse(["--config", configPath]);

            Assert.Equal(new[] { "AAPL", "TSLA", "NVDA" }, options.AllowedSymbols);
        }
        finally
        {
            TryDeleteDirectory(configDirectory);
        }
    }

    private static (string ConfigPath, string DirectoryPath) WriteTempConfig(string content)
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "harvester-appoptions-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);

        var configPath = Path.Combine(directoryPath, "appsettings.test.json");
        File.WriteAllText(configPath, content);

        return (configPath, directoryPath);
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
        }
    }
}
