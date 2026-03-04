using Harvester.App.IBKR.Broker;
using Harvester.App.Monitor;
using IBApi;
using Microsoft.Extensions.Logging;

namespace Harvester.App.IBKR.Runtime;

public sealed partial class SnapshotRuntime
{
    private async Task RunAccountUpdatesMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        var subscriptionAccount = string.IsNullOrWhiteSpace(_options.UpdateAccount) ? _options.Account : _options.UpdateAccount;

        brokerAdapter.RequestAccountUpdates(client, true, subscriptionAccount);
        await AwaitWithTimeout(_wrapper.AccountDownloadEndTask, token, "accountDownloadEnd");
        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        brokerAdapter.RequestAccountUpdates(client, false, subscriptionAccount);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var valuesPath = Path.Combine(outputDir, $"account_updates_values_{timestamp}.json");
        var portfolioPath = Path.Combine(outputDir, $"account_updates_portfolio_{timestamp}.json");
        var timesPath = Path.Combine(outputDir, $"account_updates_time_{timestamp}.json");

        WriteJson(valuesPath, _wrapper.AccountValueUpdates.ToArray());
        WriteJson(portfolioPath, _wrapper.PortfolioUpdates.ToArray());
        WriteJson(timesPath, _wrapper.AccountUpdateTimes.ToArray());

        _logger.LogInformation("Account update values export path={Path} rows={Rows}", valuesPath, _wrapper.AccountValueUpdates.Count);
        _logger.LogInformation("Account update portfolio export path={Path} rows={Rows}", portfolioPath, _wrapper.PortfolioUpdates.Count);
        _logger.LogInformation("Account update times export path={Path} rows={Rows}", timesPath, _wrapper.AccountUpdateTimes.Count);
    }

    private async Task RunPositionsMonitorUiMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        var account = string.IsNullOrWhiteSpace(_options.UpdateAccount) ? _options.Account : _options.UpdateAccount;
        var port = _options.MonitorUiPort > 0 ? _options.MonitorUiPort : 5100;

        _logger.LogInformation("Starting positions monitor UI account={Account} port={Port}", account, port);

        var store = new PositionMonitorStore();
        var poller = new IbkrPositionPoller(_wrapper, brokerAdapter, client, account, store);
        var webServer = new PositionsWebServer(store, port);

        var pollerTask = Task.Run(() => poller.RunAsync(token), token);
        var webTask = Task.Run(() => webServer.RunAsync(token), token);

        _logger.LogInformation("Positions monitor dashboard ready url={Url}", $"http://localhost:{port}");

        await Task.WhenAny(pollerTask, webTask);

        if (!token.IsCancellationRequested)
        {
            _logger.LogWarning("Positions monitor poller or web server exited unexpectedly.");
        }

        await Task.WhenAll(pollerTask, webTask).ConfigureAwait(false);
    }
}
