param(
    [string]$GatewayHost = "127.0.0.1",
    [int]$Port = 7497,
    [int]$ClientId = 9980,
    [string]$Account = "",
    [string]$Symbol = "SIRI",
    [string]$PrimaryExchange = "NSDQ",
    [string]$ExportDir = "exports",
    [int]$TimeoutSeconds = 45,
    [int]$MarketDataType = 1,
    [int]$CaptureSeconds = 12,
    [int]$DepthRows = 5,
    [string]$OrderAction = "BUY",
    [double]$OrderQuantity = 1,
    [double]$OrderLimit = 5.00,
    [double]$MaxNotional = 100,
    [double]$MaxShares = 10,
    [double]$MaxPrice = 10,
    [string]$AllowedSymbols = "SIRI,SOFI,F,PLTR,PLTK",
    [switch]$RelaxLiveQuoteSanity,
    [switch]$DisableLiveMomentumGuard,
    [switch]$PreTradeSessionWindowWarn,
    [switch]$RunOrderPlacement,
    [switch]$RunCancel,
    [int]$CancelOrderId = 0,
    [switch]$AllowNonPaperPort,
    [switch]$AllowNonPaperAccount
)

if ($args -contains '-?' -or $args -contains '/?') {
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host "  .\\ops\\run_paper_trading_activation.ps1 -Account <DU...> [options]"
    Write-Host "  Optional placement: -RunOrderPlacement"
    Write-Host "    Relaxed placement flags: -RelaxLiveQuoteSanity -DisableLiveMomentumGuard -PreTradeSessionWindowWarn"
    Write-Host "  Optional cancel: -RunCancel -CancelOrderId <id>"
    Write-Host "  Safety overrides: -AllowNonPaperPort -AllowNonPaperAccount"
    return
}

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-HarvesterMode {
    param(
        [Parameter(Mandatory = $true)][string[]]$Args,
        [Parameter(Mandatory = $true)][string]$Title
    )

    Write-Host "`n=== $Title ===" -ForegroundColor Cyan
    $cmd = @("dotnet", "run", "--project", "src/Harvester.App", "--") + $Args
    Write-Host ($cmd -join " ") -ForegroundColor DarkGray
    & dotnet run --project src/Harvester.App -- @Args
}

if ([string]::IsNullOrWhiteSpace($Account)) {
    throw "Paper activation requires -Account (expected paper account like DUxxxxxxx)."
}

$normalizedAccount = $Account.Trim().ToUpperInvariant()
if (-not $AllowNonPaperAccount -and -not $normalizedAccount.StartsWith("DU")) {
    throw "Safety gate: account '$normalizedAccount' does not look like a paper account (DU...). Use -AllowNonPaperAccount to override intentionally."
}

if (-not $AllowNonPaperPort -and $Port -eq 7496) {
    throw "Safety gate: port 7496 is typically live TWS. Use paper port (usually 7497 for TWS Paper or 4002 for IB Gateway Paper), or pass -AllowNonPaperPort intentionally."
}

$fullExportDir = [System.IO.Path]::GetFullPath($ExportDir)
if (-not (Test-Path $fullExportDir)) {
    New-Item -ItemType Directory -Path $fullExportDir | Out-Null
}

$normalizedAction = $OrderAction.Trim().ToUpperInvariant()
if ($normalizedAction -notin @("BUY", "SELL")) {
    throw "-OrderAction must be BUY or SELL."
}

Invoke-HarvesterMode -Title "Paper endpoint connectivity check" -Args @(
    "--mode", "connect",
    "--host", $GatewayHost,
    "--port", "$Port",
    "--client-id", "$ClientId",
    "--account", $normalizedAccount,
    "--timeout", "$TimeoutSeconds",
    "--export-dir", $fullExportDir
)

Invoke-HarvesterMode -Title "Paper market data smoke (L1/L2)" -Args @(
    "--mode", "market-data-all",
    "--host", $GatewayHost,
    "--port", "$Port",
    "--client-id", "$($ClientId + 1)",
    "--account", $normalizedAccount,
    "--timeout", "$TimeoutSeconds",
    "--export-dir", $fullExportDir,
    "--symbol", $Symbol,
    "--primary-exchange", $PrimaryExchange,
    "--depth-exchange", $PrimaryExchange,
    "--depth-rows", "$DepthRows",
    "--market-data-type", "$MarketDataType",
    "--capture-seconds", "$CaptureSeconds",
    "--rtb-what", "TRADES"
)

Invoke-HarvesterMode -Title "Paper what-if order preview" -Args @(
    "--mode", "orders-whatif",
    "--host", $GatewayHost,
    "--port", "$Port",
    "--client-id", "$($ClientId + 2)",
    "--account", $normalizedAccount,
    "--timeout", "$TimeoutSeconds",
    "--export-dir", $fullExportDir,
    "--live-symbol", $Symbol,
    "--live-action", $normalizedAction,
    "--live-qty", "$OrderQuantity",
    "--live-limit", "$OrderLimit",
    "--whatif-template", "lmt"
)

if ($RunOrderPlacement) {
    $placementArgs = @(
        "--mode", "orders-place-sim",
        "--host", $GatewayHost,
        "--port", "$Port",
        "--client-id", "$($ClientId + 3)",
        "--account", $normalizedAccount,
        "--timeout", "$TimeoutSeconds",
        "--export-dir", $fullExportDir,
        "--enable-live", "true",
        "--live-symbol", $Symbol,
        "--live-action", $normalizedAction,
        "--live-qty", "$OrderQuantity",
        "--live-limit", "$OrderLimit",
        "--max-notional", "$MaxNotional",
        "--max-shares", "$MaxShares",
        "--max-price", "$MaxPrice",
        "--allowed-symbols", $AllowedSymbols
    )

    if ($RelaxLiveQuoteSanity) {
        $placementArgs += @("--live-price-sanity-require-quote", "false")
    }

    if ($DisableLiveMomentumGuard) {
        $placementArgs += @("--live-momentum-guard", "false")
    }

    if ($PreTradeSessionWindowWarn) {
        $placementArgs += @("--pretrade-controls", "max-notional=reject;max-qty=reject;max-daily-orders=reject;session-window=warn")
    }

    Invoke-HarvesterMode -Title "Paper order placement (guarded)" -Args $placementArgs
}

if ($RunCancel) {
    if ($CancelOrderId -le 0) {
        throw "-RunCancel requires -CancelOrderId > 0."
    }

    Invoke-HarvesterMode -Title "Paper order cancel (guarded)" -Args @(
        "--mode", "orders-cancel-sim",
        "--host", $GatewayHost,
        "--port", "$Port",
        "--client-id", "$($ClientId + 4)",
        "--account", $normalizedAccount,
        "--timeout", "$TimeoutSeconds",
        "--export-dir", $fullExportDir,
        "--enable-live", "true",
        "--cancel-order-id", "$CancelOrderId",
        "--cancel-idempotent", "true"
    )
}

Write-Host "`n=== Paper Trading Activation Complete ===" -ForegroundColor Green
Write-Host "Account: $normalizedAccount"
Write-Host "Host/Port: ${GatewayHost}:$Port"
Write-Host "Exports: $fullExportDir"
Write-Host "Order placement executed: $RunOrderPlacement"
Write-Host "Cancel executed: $RunCancel"
Write-Host "Relax quote sanity: $RelaxLiveQuoteSanity"
Write-Host "Disable momentum guard: $DisableLiveMomentumGuard"
Write-Host "Session-window warn mode: $PreTradeSessionWindowWarn"

Write-Host "`nRuntime notes:" -ForegroundColor Yellow
Write-Host "- What-if may complete with ApiPending and no margin rows; this is treated as non-fatal fallback when no blocking API errors are present."
Write-Host "- IBKR code 399 (exchange deferral outside session) is treated as informational/non-blocking."
Write-Host "- Inspect exports for details: whatif_status_*.json, whatif_errors_*.json, api_error_normalization_*.json."

Write-Host "`nLaunch command (safe baseline, no placement):" -ForegroundColor Yellow
Write-Host ".\ops\run_paper_trading_activation.ps1 -GatewayHost $GatewayHost -Port $Port -ClientId $ClientId -Account $normalizedAccount -Symbol $Symbol -PrimaryExchange $PrimaryExchange -ExportDir `"$fullExportDir`" -TimeoutSeconds $TimeoutSeconds -MarketDataType $MarketDataType -CaptureSeconds $CaptureSeconds -DepthRows $DepthRows -OrderAction $normalizedAction -OrderQuantity $OrderQuantity -OrderLimit $OrderLimit"

Write-Host "`nTo place one guarded paper order:" -ForegroundColor Yellow
Write-Host ".\ops\run_paper_trading_activation.ps1 -GatewayHost $GatewayHost -Port $Port -ClientId $ClientId -Account $normalizedAccount -Symbol $Symbol -PrimaryExchange $PrimaryExchange -ExportDir `"$fullExportDir`" -RunOrderPlacement -OrderAction $normalizedAction -OrderQuantity $OrderQuantity -OrderLimit $OrderLimit -MaxNotional $MaxNotional -MaxShares $MaxShares -MaxPrice $MaxPrice -AllowedSymbols `"$AllowedSymbols`""

Write-Host "`nTo place with relaxed local guards (market-close/deferral validation):" -ForegroundColor Yellow
Write-Host ".\ops\run_paper_trading_activation.ps1 -GatewayHost $GatewayHost -Port $Port -ClientId $ClientId -Account $normalizedAccount -Symbol $Symbol -PrimaryExchange $PrimaryExchange -ExportDir `"$fullExportDir`" -RunOrderPlacement -OrderAction $normalizedAction -OrderQuantity $OrderQuantity -OrderLimit $OrderLimit -MaxNotional $MaxNotional -MaxShares $MaxShares -MaxPrice $MaxPrice -AllowedSymbols `"$AllowedSymbols`" -RelaxLiveQuoteSanity -DisableLiveMomentumGuard -PreTradeSessionWindowWarn"