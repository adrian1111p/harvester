param(
    [string]$ExportDir = "exports"
)

$ErrorActionPreference = "Stop"

function Get-LatestTwoFiles {
    param(
        [string]$Path,
        [string]$Pattern
    )

    $files = Get-ChildItem -Path $Path -Filter $Pattern -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 2

    if (-not $files -or $files.Count -lt 2) {
        return $null
    }

    return $files
}

function Read-JsonFile {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return @() }
    $content = Get-Content -Path $Path -Raw
    if ([string]::IsNullOrWhiteSpace($content)) { return @() }
    return $content | ConvertFrom-Json
}

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\$ExportDir"))
if (-not (Test-Path $root)) {
    throw "Export directory not found: $root"
}

Write-Host "Comparing latest snapshots in: $root" -ForegroundColor Cyan

$targets = @(
    @{ Name = "Open Orders"; Pattern = "open_orders_*.json" },
    @{ Name = "Completed Orders"; Pattern = "completed_orders_*.json" },
    @{ Name = "Executions"; Pattern = "executions_*.json" },
    @{ Name = "Account Summary"; Pattern = "account_summary_*.json" },
    @{ Name = "Positions"; Pattern = "positions_*.json" }
)

foreach ($target in $targets) {
    $pair = Get-LatestTwoFiles -Path $root -Pattern $target.Pattern
    if (-not $pair) {
        Write-Host "[$($target.Name)] not enough files to compare." -ForegroundColor Yellow
        continue
    }

    $newFile = $pair[0]
    $oldFile = $pair[1]

    $newRows = @(Read-JsonFile -Path $newFile.FullName)
    $oldRows = @(Read-JsonFile -Path $oldFile.FullName)

    Write-Host "[$($target.Name)]" -ForegroundColor Green
    Write-Host "  New: $($newFile.Name) rows=$($newRows.Count)"
    Write-Host "  Old: $($oldFile.Name) rows=$($oldRows.Count)"
    Write-Host "  Delta rows: $($newRows.Count - $oldRows.Count)"
}
