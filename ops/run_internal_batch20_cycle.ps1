param(
    [string]$GatewayHost = "127.0.0.1",
    [int]$Port = 7496,
    [int]$ClientId = 9960,
    [string]$Account = "U22462030",
    [string]$PrimaryExchange = "NSDQ",
    [string]$ExportDir = "exports",
    [int]$TimeoutSeconds = 90,
    [int]$BatchSize = 20,
    [int]$FocusSize = 5,
    [string]$FallbackSymbol = "SIRI",
    [double]$ScannerMinScore = 0,
    [int]$MarketDataType = 1,
    [int]$FocusCaptureSeconds = 45,
    [int]$DepthRows = 10,
    [string]$HistDuration = "1 D",
    [string]$HistBarSize = "1 min",
    [string]$HistWhat = "TRADES",
    [switch]$SkipScannerWorkbench,
    [switch]$SkipHistoricalBackfill,
    [switch]$SkipFocusLiveCapture,
    [switch]$SkipStrategyReplay,
    [switch]$RequireMtfAlignment
)

if ($args -contains '-?' -or $args -contains '/?') {
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host "  .\ops\run_internal_batch20_cycle.ps1 [options]"
    Write-Host "  This is internal-only simulation/data collection (no paper/live order transmission)."
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

function Get-LatestFile {
    param([Parameter(Mandatory = $true)][string]$Pattern)

    Get-ChildItem -Path $Pattern -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Convert-HistoricalBarsToReplayInput {
    param(
        [Parameter(Mandatory = $true)][string]$HistoricalBarsPath,
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][string]$Symbol
    )

    $rows = Get-Content $HistoricalBarsPath -Raw | ConvertFrom-Json
    $mapped = @()

    foreach ($row in @($rows)) {
        $ts = $null
        if ($row.PSObject.Properties.Name -contains "TimestampUtc" -and $row.TimestampUtc) {
            $ts = [DateTime]$row.TimestampUtc
        }
        elseif ($row.PSObject.Properties.Name -contains "Time" -and $row.Time) {
            $ts = [DateTime]::Parse($row.Time)
        }

        if (-not $ts) { continue }

        $mapped += [pscustomobject]@{
            TimestampUtc = ([DateTime]::SpecifyKind($ts, [DateTimeKind]::Utc).ToString("o"))
            Symbol       = $Symbol
            Open         = [double]$row.Open
            High         = [double]$row.High
            Low          = [double]$row.Low
            Close        = [double]$row.Close
            Volume       = [decimal]$row.Volume
        }
    }

    if ($mapped.Count -eq 0) {
        throw "No valid rows mapped from historical bars: $HistoricalBarsPath"
    }

    $mapped | ConvertTo-Json -Depth 6 | Set-Content -Path $OutputPath -Encoding UTF8
}

$fullExportDir = [System.IO.Path]::GetFullPath($ExportDir)
if (-not (Test-Path $fullExportDir)) {
    New-Item -ItemType Directory -Path $fullExportDir | Out-Null
}

$runStamp = Get-Date -Format "yyyyMMdd_HHmmss"
$runDate = Get-Date -Format "yyyy-MM-dd"
$tempRoot = Join-Path (Get-Location) "temp"
$replayInputDir = Join-Path $tempRoot (Join-Path "replay_inputs" $runDate)
if (-not (Test-Path $replayInputDir)) {
    New-Item -ItemType Directory -Path $replayInputDir -Force | Out-Null
}

$scannerCandidatesPath = $null
if (-not $SkipScannerWorkbench) {
    Invoke-HarvesterMode -Title "Scanner workbench (candidate pool)" -Args @(
        "--mode", "scanner-workbench",
        "--host", $GatewayHost,
        "--port", "$Port",
        "--client-id", "$ClientId",
        "--account", $Account,
        "--timeout", "$TimeoutSeconds",
        "--export-dir", $fullExportDir,
        "--scanner-instrument", "STK",
        "--scanner-location", "STK.US.MAJOR",
        "--scanner-rows", "25",
        "--scanner-workbench-codes", "TOP_PERC_GAIN,HOT_BY_VOLUME,MOST_ACTIVE",
        "--scanner-workbench-runs", "2",
        "--scanner-workbench-capture-seconds", "8",
        "--scanner-workbench-min-rows", "1"
    )
}

$latestCandidates = Get-LatestFile -Pattern (Join-Path $fullExportDir "scanner_workbench_candidates_*.json")
if (-not $latestCandidates) {
    throw "No scanner_workbench_candidates_*.json found in $fullExportDir"
}
$scannerCandidatesPath = $latestCandidates.FullName

$candidateRows = @(Get-Content $scannerCandidatesPath -Raw | ConvertFrom-Json)
$ranked = @(
    $candidateRows |
        Where-Object { $_.PSObject.Properties.Name -contains "Symbol" -and -not [string]::IsNullOrWhiteSpace($_.Symbol) } |
        ForEach-Object {
            $eligible = $true
            if ($_.PSObject.Properties.Name -contains "Eligible") { $eligible = [bool]$_.Eligible }
            $weightedScore = 0.0
            if ($_.PSObject.Properties.Name -contains "WeightedScore") { $weightedScore = [double]$_.WeightedScore }
            $avgRank = 0.0
            if ($_.PSObject.Properties.Name -contains "AverageRank") { $avgRank = [double]$_.AverageRank }

            [pscustomobject]@{
                Symbol        = ([string]$_.Symbol).Trim().ToUpperInvariant()
                WeightedScore = $weightedScore
                Eligible      = $eligible
                AverageRank   = $avgRank
            }
        } |
        Where-Object { $_.Eligible -and $_.WeightedScore -ge $ScannerMinScore } |
        Group-Object Symbol |
        ForEach-Object {
            $_.Group |
            Sort-Object -Property WeightedScore -Descending |
            Select-Object -First 1
        } |
        Sort-Object -Property WeightedScore, Symbol -Descending
)

if ($ranked.Count -eq 0) {
    Write-Warning "No eligible scanner symbols found above ScannerMinScore=$ScannerMinScore. Falling back to $FallbackSymbol."
    $ranked = @(
        [pscustomobject]@{
            Symbol = $FallbackSymbol.Trim().ToUpperInvariant()
            WeightedScore = 0.0
            Eligible = $true
            AverageRank = 0.0
        }
    )
}

$batch = @($ranked | Select-Object -First ([Math]::Max(1, $BatchSize)))
$focus = @($batch | Select-Object -First ([Math]::Max(1, [Math]::Min($FocusSize, $batch.Count))))

$batchPath = Join-Path $fullExportDir ("internal_batch_20_{0}.json" -f $runStamp)
$focusPath = Join-Path $fullExportDir ("internal_focus_5_{0}.json" -f $runStamp)
$batch | ConvertTo-Json -Depth 6 | Set-Content -Path $batchPath -Encoding UTF8
$focus | ConvertTo-Json -Depth 6 | Set-Content -Path $focusPath -Encoding UTF8

$historicalBySymbol = @{}
if (-not $SkipHistoricalBackfill) {
    $offset = 10
    foreach ($row in $batch) {
        $symbol = $row.Symbol
        Invoke-HarvesterMode -Title "Historical backfill 1m/24h ($symbol)" -Args @(
            "--mode", "historical-bars",
            "--host", $GatewayHost,
            "--port", "$Port",
            "--client-id", "$($ClientId + $offset)",
            "--account", $Account,
            "--timeout", "$TimeoutSeconds",
            "--export-dir", $fullExportDir,
            "--symbol", $symbol,
            "--primary-exchange", $PrimaryExchange,
            "--hist-duration", $HistDuration,
            "--hist-barsize", $HistBarSize,
            "--hist-what", $HistWhat,
            "--hist-use-rth", "0",
            "--hist-format-date", "1"
        )

        $latestBars = Get-LatestFile -Pattern (Join-Path $fullExportDir ("historical_bars_{0}_*.json" -f $symbol))
        if (-not $latestBars) {
            Write-Warning "No historical bars found for $symbol, skipping replay input creation."
            $offset++
            continue
        }

        $replayInputPath = Join-Path $replayInputDir ("replay_input_{0}_{1}.json" -f $symbol, $runStamp)
        Convert-HistoricalBarsToReplayInput -HistoricalBarsPath $latestBars.FullName -OutputPath $replayInputPath -Symbol $symbol
        $historicalBySymbol[$symbol] = $replayInputPath
        $offset++
    }
}

if (-not $SkipFocusLiveCapture) {
    $offset = 200
    foreach ($row in $focus) {
        $symbol = $row.Symbol
        Invoke-HarvesterMode -Title "Focus live capture L1+L2 ($symbol)" -Args @(
            "--mode", "market-data-all",
            "--host", $GatewayHost,
            "--port", "$Port",
            "--client-id", "$($ClientId + $offset)",
            "--account", $Account,
            "--timeout", "$TimeoutSeconds",
            "--export-dir", $fullExportDir,
            "--symbol", $symbol,
            "--primary-exchange", $PrimaryExchange,
            "--depth-exchange", $PrimaryExchange,
            "--depth-rows", "$DepthRows",
            "--market-data-type", "$MarketDataType",
            "--capture-seconds", "$FocusCaptureSeconds",
            "--rtb-what", "TRADES"
        )
        $offset++
    }
}

if (-not $SkipStrategyReplay) {
    $offset = 300
    $env:SCN_001_REQUIRE_MTF_ALIGNMENT = $(if ($RequireMtfAlignment) { "true" } else { "false" })
    $env:EOD_001_ENABLED = "false"

    foreach ($row in $focus) {
        $symbol = $row.Symbol
        if (-not $historicalBySymbol.ContainsKey($symbol)) {
            Write-Warning "No replay input available for $symbol; skipping strategy-replay."
            continue
        }

        Invoke-HarvesterMode -Title "Internal strategy replay ($symbol)" -Args @(
            "--mode", "strategy-replay",
            "--host", $GatewayHost,
            "--port", "$Port",
            "--client-id", "$($ClientId + $offset)",
            "--account", $Account,
            "--timeout", "$TimeoutSeconds",
            "--export-dir", $fullExportDir,
            "--symbol", $symbol,
            "--replay-input", $historicalBySymbol[$symbol],
            "--replay-scanner-candidates-input", $scannerCandidatesPath,
            "--replay-scanner-top-n", "$FocusSize",
            "--replay-scanner-min-score", "$ScannerMinScore",
            "--replay-scanner-order-qty", "1",
            "--replay-scanner-order-side", "BUY",
            "--replay-scanner-order-type", "LMT",
            "--replay-scanner-order-tif", "DAY",
            "--replay-scanner-limit-offset-bps", "10"
        )
        $offset++
    }
}

Write-Host "`n=== Internal Batch/Focus Cycle Complete ===" -ForegroundColor Green
Write-Host "Batch file: $batchPath"
Write-Host "Focus file: $focusPath"
Write-Host "Scanner candidates: $scannerCandidatesPath"
Write-Host "Replay input directory: $replayInputDir"
Write-Host "`nLaunch command:" -ForegroundColor Yellow
Write-Host ".\ops\run_internal_batch20_cycle.ps1 -GatewayHost $GatewayHost -Port $Port -ClientId $ClientId -Account $Account -PrimaryExchange $PrimaryExchange -ExportDir `"$fullExportDir`" -TimeoutSeconds $TimeoutSeconds -BatchSize $BatchSize -FocusSize $FocusSize -FallbackSymbol $FallbackSymbol -ScannerMinScore $ScannerMinScore -MarketDataType $MarketDataType -FocusCaptureSeconds $FocusCaptureSeconds -DepthRows $DepthRows -HistDuration `"$HistDuration`" -HistBarSize `"$HistBarSize`" -HistWhat $HistWhat"
Write-Host "`nSafety: Internal-only data collection + replay. No paper/live transmission is performed by this script."