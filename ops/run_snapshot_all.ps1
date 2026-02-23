param(
    [string]$TwsHost = "127.0.0.1",
    [int]$TwsPort = 7496,
    [int]$ClientId = 9201,
    [string]$Account = "U22462030",
    [int]$TimeoutSeconds = 35,
    [string]$ExportDir = "exports"
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\src\Harvester.App"
$project = [System.IO.Path]::GetFullPath($project)

Write-Host "Running snapshot-all..." -ForegroundColor Cyan

dotnet run --project $project -- `
    --mode snapshot-all `
    --host $TwsHost `
    --port $TwsPort `
    --client-id $ClientId `
    --account $Account `
    --timeout $TimeoutSeconds `
    --export-dir $ExportDir

if ($LASTEXITCODE -ne 0) {
    throw "snapshot-all failed with exit code $LASTEXITCODE"
}

Write-Host "Snapshot-all completed." -ForegroundColor Green
