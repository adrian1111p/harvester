param(
    [string]$Date = "",
    [string]$TempDir = "temp",
    [string]$ExportDir = "exports",
    [string]$MemoryDir = "memory",
    [switch]$ConvertEpisodesToParquet,
    [switch]$DeleteTempAfterSuccess
)

if ($args -contains '-?' -or $args -contains '/?') {
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host "  .\ops\run_eod_internal_self_learning_cleanup.ps1 [-Date yyyy-MM-dd] [-ConvertEpisodesToParquet] [-DeleteTempAfterSuccess]"
    return
}

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($Date)) {
    $Date = (Get-Date).ToString("yyyy-MM-dd")
}

$fullTempDir = [System.IO.Path]::GetFullPath($TempDir)
$fullExportDir = [System.IO.Path]::GetFullPath($ExportDir)
$fullMemoryDir = [System.IO.Path]::GetFullPath($MemoryDir)
$episodeDir = Join-Path $fullTempDir (Join-Path "episodes" $Date)
$rawDir = Join-Path $fullTempDir (Join-Path "raw" $Date)

if (-not (Test-Path $fullMemoryDir)) {
    New-Item -ItemType Directory -Path $fullMemoryDir -Force | Out-Null
}
$versionsDir = Join-Path $fullMemoryDir "versions"
if (-not (Test-Path $versionsDir)) {
    New-Item -ItemType Directory -Path $versionsDir -Force | Out-Null
}
$datasetsDir = Join-Path $fullMemoryDir "datasets"
if (-not (Test-Path $datasetsDir)) {
    New-Item -ItemType Directory -Path $datasetsDir -Force | Out-Null
}

function Get-LatestFile {
    param([Parameter(Mandatory = $true)][string]$Pattern)

    Get-ChildItem -Path $Pattern -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

$episodeFiles = @()
if (Test-Path $episodeDir) {
    $episodeFiles = @(Get-ChildItem -Path $episodeDir -Recurse -File -Filter *.json)
}

$episodes = @()
foreach ($file in $episodeFiles) {
    try {
        $episodes += @(Get-Content $file.FullName -Raw | ConvertFrom-Json)
    }
    catch {
        Write-Warning "Unable to parse episode file: $($file.FullName)"
    }
}

$episodeCount = $episodes.Count
$pnlSum = 0.0
foreach ($ep in $episodes) {
    if ($ep -and $ep.PSObject.Properties.Name -contains "labels" -and $ep.labels -and $ep.labels.PSObject.Properties.Name -contains "pnl_usd") {
        $pnlSum += [double]$ep.labels.pnl_usd
    }
}

$latestSummary = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_self_learning_summary_*.json")
$latestSamples = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_self_learning_samples_*.json")
$latestPredictions = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_self_learning_predictions_*.json")
$latestLifecycle = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_self_learning_lifecycle_*.json")
$latestRegistry = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_self_learning_model_registry_*.json")

$parquetPath = $null
if ($ConvertEpisodesToParquet -and $episodeFiles.Count -gt 0) {
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) {
        $targetDir = Join-Path $datasetsDir $Date
        if (-not (Test-Path $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        }

        $jsonlPath = Join-Path $targetDir "episodes.jsonl"
        $episodeFiles | ForEach-Object { Get-Content $_.FullName -Raw } | Set-Content -Path $jsonlPath -Encoding UTF8
        $parquetPath = Join-Path $targetDir "episodes.parquet"

        $pyScript = @"
import json
import sys
from pathlib import Path

jsonl = Path(sys.argv[1])
parquet = Path(sys.argv[2])

try:
    import pyarrow as pa
    import pyarrow.parquet as pq
except Exception:
    print("PYARROW_NOT_AVAILABLE")
    sys.exit(3)

rows = []
with jsonl.open("r", encoding="utf-8") as f:
    for line in f:
        line = line.strip()
        if not line:
            continue
        rows.append(json.loads(line))

if not rows:
    print("NO_ROWS")
    sys.exit(0)

table = pa.Table.from_pylist(rows)
pq.write_table(table, parquet)
print("OK")
"@

        $pyFile = Join-Path $targetDir "_episodes_to_parquet.py"
        Set-Content -Path $pyFile -Value $pyScript -Encoding UTF8

        $result = & python $pyFile $jsonlPath $parquetPath 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Parquet conversion skipped: $result"
            $parquetPath = $null
        }
        Remove-Item $pyFile -ErrorAction SilentlyContinue
    }
    else {
        Write-Warning "Parquet conversion skipped: python not found."
    }
}

$memoryLatestPath = Join-Path $fullMemoryDir "memory_latest.json"
$versionPath = Join-Path $versionsDir ("memory_{0}_{1}.json" -f $Date, (Get-Date -Format "yyyyMMdd_HHmmss"))

$memoryPayload = [pscustomobject]@{
    generated_utc = (Get-Date).ToUniversalTime().ToString("o")
    date = $Date
    mode = "internal-self-learning-eod"
    episode_count = $episodeCount
    episode_pnl_sum_usd = [Math]::Round($pnlSum, 6)
    parquet_path = $parquetPath
    sources = [pscustomobject]@{
        episode_dir = $(if (Test-Path $episodeDir) { $episodeDir } else { $null })
        self_learning_summary = $(if ($latestSummary) { $latestSummary.FullName } else { $null })
        self_learning_samples = $(if ($latestSamples) { $latestSamples.FullName } else { $null })
        self_learning_predictions = $(if ($latestPredictions) { $latestPredictions.FullName } else { $null })
        self_learning_lifecycle = $(if ($latestLifecycle) { $latestLifecycle.FullName } else { $null })
        self_learning_registry = $(if ($latestRegistry) { $latestRegistry.FullName } else { $null })
    }
    notes = @(
        "RAM-first intraday workflow",
        "No database required",
        "Temp episodes can be deleted after successful memory write"
    )
}

$memoryPayload | ConvertTo-Json -Depth 12 | Set-Content -Path $memoryLatestPath -Encoding UTF8
$memoryPayload | ConvertTo-Json -Depth 12 | Set-Content -Path $versionPath -Encoding UTF8

$cleanupSucceeded = $true
if ($DeleteTempAfterSuccess) {
    try {
        if (Test-Path $episodeDir) {
            Remove-Item -Path $episodeDir -Recurse -Force
        }
        if (Test-Path $rawDir) {
            Remove-Item -Path $rawDir -Recurse -Force
        }
    }
    catch {
        $cleanupSucceeded = $false
        Write-Warning "Temp cleanup failed: $($_.Exception.Message)"
    }
}

Write-Host "`n=== EOD Internal Self-Learning Complete ===" -ForegroundColor Green
Write-Host "Date: $Date"
Write-Host "Episodes: $episodeCount"
Write-Host "Episode PnL sum USD: $([Math]::Round($pnlSum, 6))"
Write-Host "Memory latest: $memoryLatestPath"
Write-Host "Memory version: $versionPath"
if ($parquetPath) { Write-Host "Parquet dataset: $parquetPath" }
if ($DeleteTempAfterSuccess) {
    Write-Host "Temp cleanup attempted: $cleanupSucceeded"
}

Write-Host "`nLaunch command:" -ForegroundColor Yellow
Write-Host ".\ops\run_eod_internal_self_learning_cleanup.ps1 -Date $Date -ConvertEpisodesToParquet -DeleteTempAfterSuccess"
