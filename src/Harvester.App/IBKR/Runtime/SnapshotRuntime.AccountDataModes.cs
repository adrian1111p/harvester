using Harvester.App.IBKR.Broker;
using IBApi;
using Microsoft.Extensions.Logging;

namespace Harvester.App.IBKR.Runtime;

public sealed partial class SnapshotRuntime
{
    private async Task RunManagedAccountsMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        brokerAdapter.RequestManagedAccounts(client);
        var accountsList = await AwaitWithTimeout(_wrapper.ManagedAccountsTask, token, "managedAccounts");

        var accounts = accountsList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => new ManagedAccountRow(DateTime.UtcNow, a))
            .ToArray();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"managed_accounts_{timestamp}.json");
        WriteJson(path, accounts);

        _logger.LogInformation("Managed accounts export path={Path} rows={Rows}", path, accounts.Length);
    }

    private async Task RunFamilyCodesMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        brokerAdapter.RequestFamilyCodes(client);
        await AwaitWithTimeout(_wrapper.FamilyCodesTask, token, "familyCodes");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"family_codes_{timestamp}.json");
        WriteJson(path, _wrapper.FamilyCodesRows.ToArray());

        _logger.LogInformation("Family codes export path={Path} rows={Rows}", path, _wrapper.FamilyCodesRows.Count);
    }

    private async Task RunAccountUpdatesMultiMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        const int reqId = 9701;
        brokerAdapter.RequestAccountUpdatesMulti(client, reqId, _options.AccountUpdatesMultiAccount, _options.ModelCode, true);
        await AwaitWithTimeout(_wrapper.AccountUpdateMultiEndTask, token, "accountUpdateMultiEnd");
        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        brokerAdapter.CancelAccountUpdatesMulti(client, reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"account_updates_multi_{timestamp}.json");
        WriteJson(path, _wrapper.AccountUpdateMultiRows.ToArray());

        _logger.LogInformation("Account updates multi export path={Path} rows={Rows}", path, _wrapper.AccountUpdateMultiRows.Count);
    }

    private async Task RunAccountSummaryOnlyMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        const int reqId = 9702;
        brokerAdapter.RequestAccountSummary(client, reqId, _options.AccountSummaryGroup, _options.AccountSummaryTags);
        await AwaitWithTimeout(_wrapper.AccountSummaryEndTask, token, "accountSummaryEnd");
        brokerAdapter.CancelAccountSummary(client, reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"account_summary_subscription_{timestamp}.json");
        WriteJson(path, _wrapper.AccountSummaryRows.ToArray());

        _logger.LogInformation("Account summary export path={Path} rows={Rows}", path, _wrapper.AccountSummaryRows.Count);
    }

    private async Task RunPositionsMultiMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        const int reqId = 9703;
        brokerAdapter.RequestPositionsMulti(client, reqId, _options.PositionsMultiAccount, _options.ModelCode);
        await AwaitWithTimeout(_wrapper.PositionMultiEndTask, token, "positionMultiEnd");
        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        brokerAdapter.CancelPositionsMulti(client, reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"positions_multi_{timestamp}.json");
        WriteJson(path, _wrapper.PositionMultiRows.ToArray());

        _logger.LogInformation("Positions multi export path={Path} rows={Rows}", path, _wrapper.PositionMultiRows.Count);
    }

    private async Task RunPnlAccountMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        const int reqId = 9704;
        var pnlAccount = string.IsNullOrWhiteSpace(_options.PnlAccount) ? _options.Account : _options.PnlAccount;

        brokerAdapter.RequestPnlAccount(client, reqId, pnlAccount, _options.ModelCode);
        await AwaitWithTimeout(_wrapper.PnlFirstTask, token, "pnl");
        await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        brokerAdapter.CancelPnlAccount(client, reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"pnl_account_{timestamp}.json");
        WriteJson(path, _wrapper.PnlRows.ToArray());

        _logger.LogInformation("PnL account export path={Path} rows={Rows}", path, _wrapper.PnlRows.Count);
    }

    private async Task RunPnlSingleMode(EClientSocket client, IBrokerAdapter brokerAdapter, CancellationToken token)
    {
        if (_options.PnlConId <= 0)
        {
            throw new InvalidOperationException("pnl-single mode requires --pnl-conid > 0.");
        }

        const int reqId = 9705;
        var pnlAccount = string.IsNullOrWhiteSpace(_options.PnlAccount) ? _options.Account : _options.PnlAccount;

        brokerAdapter.RequestPnlSingle(client, reqId, pnlAccount, _options.ModelCode, _options.PnlConId);
        var receivedFirstUpdate = true;
        try
        {
            await AwaitWithTimeout(_wrapper.PnlSingleFirstTask, token, "pnlSingle");
        }
        catch (TimeoutException)
        {
            receivedFirstUpdate = false;
            _logger.LogWarning("PnL single returned no update for conId={ConId} account={Account} during capture window; exporting current rows.", _options.PnlConId, pnlAccount);
        }

        if (receivedFirstUpdate)
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.CaptureSeconds), token);
        }

        brokerAdapter.CancelPnlSingle(client, reqId);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputDir = EnsureOutputDir();
        var path = Path.Combine(outputDir, $"pnl_single_{_options.PnlConId}_{timestamp}.json");
        WriteJson(path, _wrapper.PnlSingleRows.ToArray());

        _logger.LogInformation("PnL single export path={Path} rows={Rows}", path, _wrapper.PnlSingleRows.Count);
    }
}
