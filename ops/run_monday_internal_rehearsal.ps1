param(
    [string]$GatewayHost = "127.0.0.1",
    [int]$Port = 7496,
    [int]$ClientId = 9970,
    [string]$Account = "U22462030",
    [string]$Symbol = "SIRI",
    [string]$PrimaryExchange = "NSDQ",
    [string]$ExportDir = "exports",
    [int]$TimeoutSeconds = 90,
    [int]$ScannerTopN = 5,
    [double]$ScannerMinScore = 60,
    [double]$OrderQuantity = 1,
    [string]$OrderSide = "BUY",
    [string]$OrderType = "LMT",
    [string]$OrderTif = "DAY",
    [double]$LimitOffsetBps = 10,
    [string]$ReplayOrdersInputPath = "",
    [int]$MarketDataType = 3,
    [int]$L1L2CaptureSeconds = 120,
    [int]$DepthRows = 10,
    [string]$RealtimeBarsWhat = "TRADES",
    [switch]$SkipScannerWorkbench,
    [switch]$SkipMarketDataCapture,
    [switch]$SkipHistoricalBars
)

if ($args -contains '-?' -or $args -contains '/?') {
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host "  .\\ops\\run_monday_internal_rehearsal.ps1 [-GatewayHost <host>] [-Port <port>] [-ClientId <id>] [-Account <account>] [-Symbol <symbol>]"
    Write-Host "  Optional: -PrimaryExchange <exchange> -ReplayOrdersInputPath <path> -MarketDataType <1|2|3|4> -L1L2CaptureSeconds <sec> -DepthRows <rows> -RealtimeBarsWhat <TRADES|MIDPOINT|BID|ASK>"
    Write-Host "            -SkipScannerWorkbench -SkipMarketDataCapture -SkipHistoricalBars"
    return
}

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-LatestFile {
    param(
        [Parameter(Mandatory = $true)][string]$Pattern
    )

    $file = Get-ChildItem -Path $Pattern -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    return $file
}

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

function Convert-HistoricalBarsToReplayInput {
    param(
        [Parameter(Mandatory = $true)][string]$HistoricalBarsPath,
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][string]$FallbackSymbol
    )

    $rows = Get-Content $HistoricalBarsPath -Raw | ConvertFrom-Json
    if (-not $rows -or $rows.Count -eq 0) {
        throw "Historical bars file is empty: $HistoricalBarsPath"
    }

    $mapped = @()
    foreach ($row in $rows) {
        $ts = $null
        if ($row.TimestampUtc) {
            $ts = [DateTime]$row.TimestampUtc
        }
        elseif ($row.Time) {
            $ts = [DateTime]::Parse($row.Time)
        }

        if (-not $ts) {
            continue
        }

        $mapped += [pscustomobject]@{
            TimestampUtc = ([DateTime]::SpecifyKind($ts, [DateTimeKind]::Utc).ToString("o"))
            Symbol       = $FallbackSymbol
            Open         = [double]$row.Open
            High         = [double]$row.High
            Low          = [double]$row.Low
            Close        = [double]$row.Close
            Volume       = [decimal]$row.Volume
        }
    }

    if ($mapped.Count -eq 0) {
        throw "No valid historical rows were mapped from: $HistoricalBarsPath"
    }

    $mapped | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputPath -Encoding UTF8
}

$fullExportDir = [System.IO.Path]::GetFullPath($ExportDir)
if (-not (Test-Path $fullExportDir)) {
    New-Item -ItemType Directory -Path $fullExportDir | Out-Null
}

$runStamp = Get-Date -Format "yyyyMMdd_HHmmss"
$replayInputPath = Join-Path $fullExportDir "replay_input_monday_internal_${runStamp}.json"
$scannerCandidatesPath = ""

if (-not $SkipMarketDataCapture) {
    Invoke-HarvesterMode -Title "L1/L2 market data capture (internal evidence only)" -Args @(
        "--mode", "market-data-all",
        "--host", $GatewayHost,
        "--port", "$Port",
        "--client-id", "$($ClientId + 1)",
        "--account", $Account,
        "--timeout", "$TimeoutSeconds",
        "--export-dir", $fullExportDir,
        "--symbol", $Symbol,
        "--primary-exchange", $PrimaryExchange,
        "--depth-exchange", $PrimaryExchange,
        "--depth-rows", "$DepthRows",
        "--market-data-type", "$MarketDataType",
        "--capture-seconds", "$L1L2CaptureSeconds",
        "--rtb-what", $RealtimeBarsWhat
    )
}

if (-not $SkipScannerWorkbench) {
    Invoke-HarvesterMode -Title "Scanner workbench (candidate universe)" -Args @(
        "--mode", "scanner-workbench",
        "--host", $GatewayHost,
        "--port", "$Port",
        "--client-id", "$($ClientId + 2)",
        "--account", $Account,
        "--timeout", "$TimeoutSeconds",
        "--export-dir", $fullExportDir,
        "--scanner-instrument", "STK",
        "--scanner-location", "STK.US.MAJOR",
        "--scanner-rows", "20",
        "--scanner-workbench-codes", "TOP_PERC_GAIN,HOT_BY_VOLUME,MOST_ACTIVE",
        "--scanner-workbench-runs", "2",
        "--scanner-workbench-capture-seconds", "8",
        "--scanner-workbench-min-rows", "1"
    )

    $latestCandidates = Get-LatestFile -Pattern (Join-Path $fullExportDir "scanner_workbench_candidates_*.json")
    if (-not $latestCandidates) {
        throw "No scanner_workbench_candidates_*.json found in $fullExportDir"
    }
    $scannerCandidatesPath = $latestCandidates.FullName
}

if (-not $SkipHistoricalBars) {
    Invoke-HarvesterMode -Title "Historical bars capture (replay data source)" -Args @(
        "--mode", "historical-bars",
        "--host", $GatewayHost,
        "--port", "$Port",
        "--client-id", "$($ClientId + 3)",
        "--account", $Account,
        "--timeout", "$TimeoutSeconds",
        "--export-dir", $fullExportDir,
        "--symbol", $Symbol,
        "--primary-exchange", $PrimaryExchange,
        "--hist-duration", "1 D",
        "--hist-barsize", "1 min",
        "--hist-what", "TRADES",
        "--hist-use-rth", "1",
        "--hist-format-date", "1"
    )

    $latestBars = Get-LatestFile -Pattern (Join-Path $fullExportDir ("historical_bars_" + $Symbol + "_*.json"))
    if (-not $latestBars) {
        throw "No historical_bars_${Symbol}_*.json found in $fullExportDir"
    }

    Convert-HistoricalBarsToReplayInput -HistoricalBarsPath $latestBars.FullName -OutputPath $replayInputPath -FallbackSymbol $Symbol
}
else {
    $latestReplay = Get-LatestFile -Pattern (Join-Path $fullExportDir "replay_input_*.json")
    if (-not $latestReplay) {
        throw "SkipHistoricalBars set but no replay_input_*.json found in $fullExportDir"
    }
    $replayInputPath = $latestReplay.FullName
}

if ([string]::IsNullOrWhiteSpace($scannerCandidatesPath)) {
    $latestCandidates = Get-LatestFile -Pattern (Join-Path $fullExportDir "scanner_workbench_candidates_*.json")
    if (-not $latestCandidates) {
        throw "No scanner candidates found. Provide scanner file by running scanner-workbench first or remove -SkipScannerWorkbench."
    }
    $scannerCandidatesPath = $latestCandidates.FullName
}

$env:SCN_001_REQUIRE_MTF_ALIGNMENT = "true"
$env:EOD_001_ENABLED = "false"

$strategyArgs = @(
    "--mode", "strategy-replay",
    "--host", $GatewayHost,
    "--port", "$Port",
    "--client-id", "$($ClientId + 4)",
    "--account", $Account,
    "--timeout", "$TimeoutSeconds",
    "--export-dir", $fullExportDir,
    "--symbol", $Symbol,
    "--replay-input", $replayInputPath,
    "--replay-scanner-candidates-input", $scannerCandidatesPath,
    "--replay-scanner-top-n", "$ScannerTopN",
    "--replay-scanner-min-score", "$ScannerMinScore",
    "--replay-scanner-order-qty", "$OrderQuantity",
    "--replay-scanner-order-side", $OrderSide,
    "--replay-scanner-order-type", $OrderType,
    "--replay-scanner-order-tif", $OrderTif,
    "--replay-scanner-limit-offset-bps", "$LimitOffsetBps"
)

if (-not [string]::IsNullOrWhiteSpace($ReplayOrdersInputPath) -and (Test-Path $ReplayOrdersInputPath)) {
    $strategyArgs += @("--replay-orders-input", [System.IO.Path]::GetFullPath($ReplayOrdersInputPath))
}

Invoke-HarvesterMode -Title "Internal order-and-strategy rehearsal (no paper/live transmission)" -Args $strategyArgs

$latestHistoricalCandles = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_historical_candles_*.json")
$latestScannerEval = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_scanner_historical_evaluation_*.json")
$latestSelection = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_scanner_symbol_selection_*.json")
$latestSelfLearning = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_self_learning_summary_*.json")
$latestSelfLearningSamples = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_self_learning_samples_*.json")
$latestSelfLearningPredictions = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_self_learning_predictions_*.json")
$latestL2Candles = Get-LatestFile -Pattern (Join-Path $fullExportDir "l2_candles_*.json")
$latestL2Signals = Get-LatestFile -Pattern (Join-Path $fullExportDir "l2_strategy_signals_*.json")
$latestTopData = Get-LatestFile -Pattern (Join-Path $fullExportDir "top_data_*.json")
$latestDepthData = Get-LatestFile -Pattern (Join-Path $fullExportDir "depth_data_*.json")

Write-Host "`n=== Monday Internal Rehearsal Complete ===" -ForegroundColor Green
Write-Host "Replay input: $replayInputPath"
Write-Host "Scanner candidates: $scannerCandidatesPath"
if ($latestTopData) { Write-Host "L1 top data: $($latestTopData.FullName)" }
if ($latestDepthData) { Write-Host "L2 depth data: $($latestDepthData.FullName)" }
if ($latestL2Candles) { Write-Host "L2 candles: $($latestL2Candles.FullName)" }
if ($latestL2Signals) { Write-Host "L2 strategy signals: $($latestL2Signals.FullName)" }
if ($latestHistoricalCandles) { Write-Host "Historical candles: $($latestHistoricalCandles.FullName)" }
if ($latestScannerEval) { Write-Host "Scanner historical evaluation: $($latestScannerEval.FullName)" }
if ($latestSelection) { Write-Host "Scanner selection snapshot: $($latestSelection.FullName)" }
if ($latestSelfLearning) { Write-Host "Self-learning summary: $($latestSelfLearning.FullName)" }
if ($latestSelfLearningSamples) { Write-Host "Self-learning samples: $($latestSelfLearningSamples.FullName)" }
if ($latestSelfLearningPredictions) { Write-Host "Self-learning predictions: $($latestSelfLearningPredictions.FullName)" }

Write-Host "`nLaunch command:" -ForegroundColor Yellow
Write-Host ".\ops\run_monday_internal_rehearsal.ps1 -GatewayHost $GatewayHost -Port $Port -ClientId $ClientId -Account $Account -Symbol $Symbol -PrimaryExchange $PrimaryExchange -ExportDir `"$fullExportDir`" -TimeoutSeconds $TimeoutSeconds -MarketDataType $MarketDataType -L1L2CaptureSeconds $L1L2CaptureSeconds -DepthRows $DepthRows -ScannerTopN $ScannerTopN -ScannerMinScore $ScannerMinScore -OrderQuantity $OrderQuantity -OrderSide $OrderSide -OrderType $OrderType -OrderTif $OrderTif -LimitOffsetBps $LimitOffsetBps"

Write-Host "`nSafety:" -ForegroundColor Yellow
Write-Host "  This script uses scanner + market-data capture + strategy-replay only; it does NOT place live or paper orders."