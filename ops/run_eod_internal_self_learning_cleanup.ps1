param(
    [string]$Date = "",
    [string]$TempDir = "temp",
    [string]$ExportDir = "exports",
    [string]$MemoryDir = "memory",
    [switch]$ConvertEpisodesToParquet,
    [switch]$DeleteTempAfterSuccess,
    [switch]$DeleteHeavyExportsAfterSuccess,
    [bool]$PreserveSelfLearningExports = $true
)

if ($args -contains '-?' -or $args -contains '/?') {
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host "  .\ops\run_eod_internal_self_learning_cleanup.ps1 [-Date yyyy-MM-dd] [-ConvertEpisodesToParquet] [-DeleteTempAfterSuccess] [-DeleteHeavyExportsAfterSuccess] [-PreserveSelfLearningExports <true|false>]"
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
$journalDir = Join-Path $fullMemoryDir "journal"
if (-not (Test-Path $journalDir)) {
    New-Item -ItemType Directory -Path $journalDir -Force | Out-Null
}

function Get-LatestFile {
    param([Parameter(Mandatory = $true)][string]$Pattern)

    Get-ChildItem -Path $Pattern -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Get-PropValue {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string[]]$Names,
        $Default = $null
    )

    if ($null -eq $Object) {
        return $Default
    }

    foreach ($name in $Names) {
        if ($Object.PSObject.Properties.Name -contains $name) {
            return $Object.$name
        }
    }

    return $Default
}

function Get-DoubleValue {
    param($Value, [double]$Default = 0.0)

    if ($null -eq $Value) {
        return $Default
    }

    $parsed = 0.0
    if ([double]::TryParse("$Value", [ref]$parsed)) {
        return $parsed
    }

    return $Default
}

function Clamp-Double {
    param(
        [double]$Value,
        [double]$Min,
        [double]$Max
    )

    if ($Value -lt $Min) {
        return $Min
    }
    if ($Value -gt $Max) {
        return $Max
    }

    return $Value
}

function Convert-ToUtcDateTimeOrNull {
    param($Value)

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [DateTime]) {
        return [DateTime]::SpecifyKind($Value, [DateTimeKind]::Utc)
    }

    $asInt64 = 0L
    if ([Int64]::TryParse("$Value", [ref]$asInt64)) {
        try {
            return [DateTimeOffset]::FromUnixTimeMilliseconds($asInt64).UtcDateTime
        }
        catch {
            return $null
        }
    }

    $asDate = [DateTime]::MinValue
    if ([DateTime]::TryParse("$Value", [ref]$asDate)) {
        return [DateTime]::SpecifyKind($asDate, [DateTimeKind]::Utc)
    }

    return $null
}

function Get-OrCreateSymbolStats {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Container,
        [Parameter(Mandatory = $true)][string]$Symbol
    )

    if ($Container.ContainsKey($Symbol)) {
        return $Container[$Symbol]
    }

    $obj = [ordered]@{
        trade_count = 0
        wins = 0
        losses = 0
        breakeven = 0
        pnl_usd = 0.0
        volume = 0.0
        r_multiple_sum = 0.0
        mae_usd_sum = 0.0
        mfe_usd_sum = 0.0
        hold_seconds_sum = 0.0
        pre_imb_sum = 0.0
        pre_spread_sum = 0.0
        pre_tape_delta_sum = 0.0
        structure_uptrend = 0
        structure_downtrend = 0
        structure_range = 0
        entry_reasons = @{}
        exit_reasons = @{}
    }

    $Container[$Symbol] = $obj
    return $obj
}

function Increment-Count {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Table,
        [Parameter(Mandatory = $true)][string]$Key
    )

    if ([string]::IsNullOrWhiteSpace($Key)) {
        return
    }

    if ($Table.ContainsKey($Key)) {
        $Table[$Key] = [int]$Table[$Key] + 1
    }
    else {
        $Table[$Key] = 1
    }
}

function Sigmoid {
    param([double]$X)

    if ($X -ge 0.0) {
        $z = [Math]::Exp(-$X)
        return 1.0 / (1.0 + $z)
    }

    $ez = [Math]::Exp($X)
    return $ez / (1.0 + $ez)
}

function Remove-HeavyExportsForDate {
    param(
        [Parameter(Mandatory = $true)][string]$ExportDir,
        [Parameter(Mandatory = $true)][string]$DateStamp,
        [string[]]$KeepPrefixes = @(),
        [string[]]$KeepFileNames = @()
    )

    if (-not (Test-Path $ExportDir)) {
        return 0
    }

    $heavyPrefixes = @(
        "top_data_",
        "depth_data_",
        "l2_candles_",
        "l2_strategy_signals_",
        "strategy_replay_slices_",
        "strategy_replay_symbol_events_",
        "strategy_replay_borrow_locate_events_",
        "strategy_replay_financing_applied_",
        "strategy_replay_locate_rejections_",
        "strategy_replay_margin_rejections_",
        "strategy_replay_margin_events_",
        "strategy_replay_cash_settlements_",
        "strategy_replay_cash_rejections_",
        "strategy_replay_order_activations_",
        "strategy_replay_order_updates_",
        "strategy_replay_combo_events_",
        "strategy_replay_trailing_stop_updates_",
        "strategy_replay_order_triggers_",
        "strategy_replay_order_cancellations_",
        "strategy_replay_fee_breakdown_",
        "strategy_replay_cost_deltas_",
        "strategy_replay_partial_fill_events_",
        "strategy_replay_portfolio_",
        "strategy_replay_benchmark_",
        "strategy_replay_performance_packets_",
        "strategy_replay_historical_candles_",
        "strategy_replay_scanner_historical_evaluation_",
        "strategy_replay_limit_order_case_matrix_",
        "adapter_trace_strategyreplay_",
        "strategy_scheduler_events_strategyreplay_",
        "resilience_drill_acceptance_strategyreplay_",
        "lean_zipline_parity_checklist_"
    )

    $targets = Get-ChildItem -Path $ExportDir -File -ErrorAction SilentlyContinue |
        Where-Object {
            $name = $_.Name
            if ($name -notlike "*_${DateStamp}*") {
                return $false
            }

            if ($KeepFileNames -contains $name) {
                return $false
            }

            foreach ($keepPrefix in $KeepPrefixes) {
                if (-not [string]::IsNullOrWhiteSpace($keepPrefix) -and $name.StartsWith($keepPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    return $false
                }
            }

            foreach ($prefix in $heavyPrefixes) {
                if ($name.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    return $true
                }
            }

            return $false
        }

    $removed = 0
    foreach ($target in @($targets)) {
        Remove-Item -Path $target.FullName -Force -ErrorAction SilentlyContinue
        $removed++
    }

    return $removed
}

$episodeFiles = @()
if (Test-Path $episodeDir) {
    $episodeFiles = @(Get-ChildItem -Path $episodeDir -Recurse -File -Filter *.json)
}

$latestSummary = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_self_learning_summary_*.json")
$latestSamples = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_self_learning_samples_*.json")
$latestPredictions = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_self_learning_predictions_*.json")
$latestLifecycle = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_self_learning_lifecycle_*.json")
$latestRegistry = Get-LatestFile -Pattern (Join-Path $fullExportDir "strategy_replay_self_learning_model_registry_*.json")

$episodeCount = 0
$episodeParseFailures = 0
$pnlSum = 0.0
$wins = 0
$losses = 0
$breakeven = 0

$globalEntryReasons = @{}
$globalExitReasons = @{}
$globalStructureCounts = @{
    UPTREND = 0
    DOWNTREND = 0
    RANGE = 0
}
$symbolStats = @{}

$featureNames = @(
    "bias",
    "side_sign",
    "pre_imbalance",
    "pre_tape_delta",
    "pre_spread",
    "pre_volatility_proxy",
    "log_qty"
)
$weights = [double[]]@(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0)
$learningRate = 0.03
$l2 = 0.0001
$modelSamples = 0
$modelCorrect = 0
$modelLogLoss = 0.0
$tradeJournalRows = @()
$fillJournalRows = @()

foreach ($file in $episodeFiles) {
    $episode = $null
    try {
        $episode = Get-Content $file.FullName -Raw | ConvertFrom-Json
    }
    catch {
        $episodeParseFailures++
        Write-Warning "Unable to parse episode file: $($file.FullName)"
        continue
    }

    if ($null -eq $episode) {
        $episodeParseFailures++
        continue
    }

    $symbolRaw = Get-PropValue -Object $episode -Names @("symbol", "Symbol") -Default ""
    $symbol = "$symbolRaw".Trim().ToUpperInvariant()
    if ([string]::IsNullOrWhiteSpace($symbol)) {
        $symbol = "UNKNOWN"
    }

    $sideRaw = Get-PropValue -Object $episode -Names @("side", "Side") -Default ""
    $side = "$sideRaw".Trim().ToUpperInvariant()

    $qty = Get-DoubleValue -Value (Get-PropValue -Object $episode -Names @("qty", "quantity", "Quantity") -Default 0.0)

    $labels = Get-PropValue -Object $episode -Names @("labels", "Labels") -Default $null
    $decisionTrace = Get-PropValue -Object $episode -Names @("decision_trace", "DecisionTrace") -Default $null
    $entry = Get-PropValue -Object $episode -Names @("entry", "Entry") -Default $null
    $exit = Get-PropValue -Object $episode -Names @("exit", "Exit") -Default $null

    $pnl = Get-DoubleValue -Value (Get-PropValue -Object $labels -Names @("pnl_usd", "PnlUsd") -Default 0.0)
    $rMultiple = Get-DoubleValue -Value (Get-PropValue -Object $labels -Names @("r_multiple", "RMultiple") -Default 0.0)
    $maeUsd = Get-DoubleValue -Value (Get-PropValue -Object $labels -Names @("mae_usd", "MaeUsd") -Default 0.0)
    $mfeUsd = Get-DoubleValue -Value (Get-PropValue -Object $labels -Names @("mfe_usd", "MfeUsd") -Default 0.0)

    $winLoss = ""
    $winLossRaw = Get-PropValue -Object $labels -Names @("win_loss", "WinLoss") -Default ""
    if (-not [string]::IsNullOrWhiteSpace("$winLossRaw")) {
        $winLoss = "$winLossRaw".Trim().ToUpperInvariant()
    }
    if ([string]::IsNullOrWhiteSpace($winLoss)) {
        if ($pnl -gt 0.005) {
            $winLoss = "WIN"
        }
        elseif ($pnl -lt -0.005) {
            $winLoss = "LOSS"
        }
        else {
            $winLoss = "BREAKEVEN"
        }
    }

    $entryReason = ""
    $entryReasonRaw = Get-PropValue -Object $decisionTrace -Names @("entry_reason", "EntryReason") -Default ""
    if (-not [string]::IsNullOrWhiteSpace("$entryReasonRaw")) {
        $entryReason = "$entryReasonRaw".Trim().ToUpperInvariant()
    }

    $exitReasonRaw = Get-PropValue -Object $labels -Names @("exit_reason", "ExitReason") -Default ""
    if ([string]::IsNullOrWhiteSpace("$exitReasonRaw")) {
        $exitReasonRaw = Get-PropValue -Object $decisionTrace -Names @("exit_reason", "ExitReason") -Default ""
    }
    $exitReason = ""
    if (-not [string]::IsNullOrWhiteSpace("$exitReasonRaw")) {
        $exitReason = "$exitReasonRaw".Trim().ToUpperInvariant()
    }

    $entryTs = Convert-ToUtcDateTimeOrNull (Get-PropValue -Object $entry -Names @("ts", "timestamp", "TimestampUtc") -Default $null)
    $exitTs = Convert-ToUtcDateTimeOrNull (Get-PropValue -Object $exit -Names @("ts", "timestamp", "TimestampUtc") -Default $null)
    $entryPrice = Get-DoubleValue -Value (Get-PropValue -Object $entry -Names @("price", "Price") -Default 0.0)
    $exitPrice = Get-DoubleValue -Value (Get-PropValue -Object $exit -Names @("price", "Price") -Default 0.0)
    $tradeIdRaw = Get-PropValue -Object $episode -Names @("trade_id", "tradeId", "TradeId") -Default ""
    $tradeId = "$tradeIdRaw".Trim()
    if ([string]::IsNullOrWhiteSpace($tradeId)) {
        $tradeId = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    }
    $holdSeconds = 0.0
    if ($entryTs -and $exitTs -and $exitTs -gt $entryTs) {
        $holdSeconds = [Math]::Max(0.0, ($exitTs - $entryTs).TotalSeconds)
    }

    $fills = Get-PropValue -Object $episode -Names @("fills", "Fills") -Default @()
    $volume = 0.0
    foreach ($fill in @($fills)) {
        $fillQty = Get-DoubleValue -Value (Get-PropValue -Object $fill -Names @("qty", "quantity", "Quantity") -Default 0.0)
        $volume += [Math]::Abs($fillQty)
    }
    if ($volume -le 0.0) {
        $volume = [Math]::Abs($qty)
    }

    $tradeJournalRows += [pscustomobject]@{
        date = $Date
        trade_id = $tradeId
        symbol = $symbol
        side = $side
        quantity = [Math]::Round($qty, 6)
        volume = [Math]::Round($volume, 6)
        entry_ts_utc = $(if ($entryTs) { $entryTs.ToString("o") } else { $null })
        entry_price = [Math]::Round($entryPrice, 6)
        exit_ts_utc = $(if ($exitTs) { $exitTs.ToString("o") } else { $null })
        exit_price = [Math]::Round($exitPrice, 6)
        hold_seconds = [Math]::Round($holdSeconds, 3)
        pnl_usd = [Math]::Round($pnl, 6)
        r_multiple = [Math]::Round($rMultiple, 6)
        mae_usd = [Math]::Round($maeUsd, 6)
        mfe_usd = [Math]::Round($mfeUsd, 6)
        win_loss = $winLoss
        entry_reason = $entryReason
        exit_reason = $exitReason
        source_file = $file.FullName
    }

    foreach ($fill in @($fills)) {
        $fillTs = Convert-ToUtcDateTimeOrNull (Get-PropValue -Object $fill -Names @("ts", "timestamp", "TimestampUtc") -Default $null)
        $fillPrice = Get-DoubleValue -Value (Get-PropValue -Object $fill -Names @("price", "fill_price", "FillPrice") -Default 0.0)
        $fillQty = Get-DoubleValue -Value (Get-PropValue -Object $fill -Names @("qty", "quantity", "Quantity") -Default 0.0)
        $fillSideRaw = Get-PropValue -Object $fill -Names @("side", "Side") -Default ""
        $fillSide = "$fillSideRaw".Trim().ToUpperInvariant()
        $fillSourceRaw = Get-PropValue -Object $fill -Names @("source", "Source") -Default ""
        $fillSource = "$fillSourceRaw".Trim()

        $fillJournalRows += [pscustomobject]@{
            date = $Date
            trade_id = $tradeId
            symbol = $symbol
            ts_utc = $(if ($fillTs) { $fillTs.ToString("o") } else { $null })
            side = $fillSide
            quantity = [Math]::Round($fillQty, 6)
            price = [Math]::Round($fillPrice, 6)
            source = $fillSource
            source_file = $file.FullName
        }
    }

    $featuresPre = @((Get-PropValue -Object $episode -Names @("features_pre", "FeaturesPre") -Default @()))
    $preImbSum = 0.0
    $preSpreadSum = 0.0
    $preVolSum = 0.0
    $preTapeDeltaSum = 0.0
    $preCount = 0
    foreach ($point in $featuresPre) {
        $imb = Get-DoubleValue -Value (Get-PropValue -Object $point -Names @("imb", "l2_imbalance_top_n", "L2ImbalanceTopN") -Default 0.0)
        $spread = Get-DoubleValue -Value (Get-PropValue -Object $point -Names @("spread", "Spread") -Default 0.0)
        $vol = Get-DoubleValue -Value (Get-PropValue -Object $point -Names @("atr1m", "volatility_proxy", "VolatilityProxy") -Default 0.0)
        $tapeBuy = Get-DoubleValue -Value (Get-PropValue -Object $point -Names @("tape_buy", "tape_buy_volume", "TapeBuyVolume") -Default 0.0)
        $tapeSell = Get-DoubleValue -Value (Get-PropValue -Object $point -Names @("tape_sell", "tape_sell_volume", "TapeSellVolume") -Default 0.0)
        $tapeDenom = [Math]::Abs($tapeBuy) + [Math]::Abs($tapeSell)
        $tapeDelta = if ($tapeDenom -gt 0) { ($tapeBuy - $tapeSell) / $tapeDenom } else { 0.0 }

        $preImbSum += $imb
        $preSpreadSum += $spread
        $preVolSum += $vol
        $preTapeDeltaSum += $tapeDelta
        $preCount++
    }

    $preImbAvg = if ($preCount -gt 0) { $preImbSum / $preCount } else { 0.0 }
    $preSpreadAvg = if ($preCount -gt 0) { $preSpreadSum / $preCount } else { 0.0 }
    $preVolAvg = if ($preCount -gt 0) { $preVolSum / $preCount } else { 0.0 }
    $preTapeDeltaAvg = if ($preCount -gt 0) { $preTapeDeltaSum / $preCount } else { 0.0 }

    $series = @((Get-PropValue -Object $episode -Names @("series", "Series") -Default @()))
    $firstMark = 0.0
    $lastMark = 0.0
    foreach ($point in $series) {
        $mark = Get-DoubleValue -Value (Get-PropValue -Object $point -Names @("mark", "mark_price", "MarkPrice") -Default 0.0)
        if ($mark -le 0.0) {
            continue
        }

        if ($firstMark -le 0.0) {
            $firstMark = $mark
        }
        $lastMark = $mark
    }

    $structure = "RANGE"
    if ($firstMark -gt 0.0 -and $lastMark -gt 0.0) {
        $ret = ($lastMark - $firstMark) / $firstMark
        if ($ret -gt 0.0015) {
            $structure = "UPTREND"
        }
        elseif ($ret -lt -0.0015) {
            $structure = "DOWNTREND"
        }
    }

    $episodeCount++
    $pnlSum += $pnl
    switch ($winLoss) {
        "WIN" { $wins++ }
        "LOSS" { $losses++ }
        default { $breakeven++ }
    }

    Increment-Count -Table $globalEntryReasons -Key $entryReason
    Increment-Count -Table $globalExitReasons -Key $exitReason
    Increment-Count -Table $globalStructureCounts -Key $structure

    $s = Get-OrCreateSymbolStats -Container $symbolStats -Symbol $symbol
    $s.trade_count = [int]$s.trade_count + 1
    switch ($winLoss) {
        "WIN" { $s.wins = [int]$s.wins + 1 }
        "LOSS" { $s.losses = [int]$s.losses + 1 }
        default { $s.breakeven = [int]$s.breakeven + 1 }
    }
    $s.pnl_usd = [double]$s.pnl_usd + $pnl
    $s.volume = [double]$s.volume + $volume
    $s.r_multiple_sum = [double]$s.r_multiple_sum + $rMultiple
    $s.mae_usd_sum = [double]$s.mae_usd_sum + $maeUsd
    $s.mfe_usd_sum = [double]$s.mfe_usd_sum + $mfeUsd
    $s.hold_seconds_sum = [double]$s.hold_seconds_sum + $holdSeconds
    $s.pre_imb_sum = [double]$s.pre_imb_sum + $preImbAvg
    $s.pre_spread_sum = [double]$s.pre_spread_sum + $preSpreadAvg
    $s.pre_tape_delta_sum = [double]$s.pre_tape_delta_sum + $preTapeDeltaAvg

    switch ($structure) {
        "UPTREND" { $s.structure_uptrend = [int]$s.structure_uptrend + 1 }
        "DOWNTREND" { $s.structure_downtrend = [int]$s.structure_downtrend + 1 }
        default { $s.structure_range = [int]$s.structure_range + 1 }
    }

    Increment-Count -Table $s.entry_reasons -Key $entryReason
    Increment-Count -Table $s.exit_reasons -Key $exitReason

    $label = if ($pnl -gt 0.0) { 1.0 } else { 0.0 }
    $sideSign = if ($side -eq "LONG") { 1.0 } elseif ($side -eq "SHORT") { -1.0 } else { 0.0 }

    $x = [double[]]@(
        1.0,
        $sideSign,
        (Clamp-Double -Value $preImbAvg -Min -1.0 -Max 1.0),
        (Clamp-Double -Value $preTapeDeltaAvg -Min -1.0 -Max 1.0),
        (Clamp-Double -Value ($preSpreadAvg * 100.0) -Min 0.0 -Max 10.0),
        (Clamp-Double -Value ($preVolAvg * 100.0) -Min 0.0 -Max 25.0),
        (Clamp-Double -Value ([Math]::Log(1.0 + [Math]::Max(0.0, [Math]::Abs($qty)))) -Min 0.0 -Max 10.0)
    )

    $dot = 0.0
    for ($i = 0; $i -lt $weights.Length; $i++) {
        $dot += $weights[$i] * $x[$i]
    }

    $prob = Sigmoid -X $dot
    $pred = if ($prob -ge 0.5) { 1.0 } else { 0.0 }
    if ($pred -eq $label) {
        $modelCorrect++
    }

    $clampedProb = Clamp-Double -Value $prob -Min 1e-6 -Max (1.0 - 1e-6)
    if ($label -ge 0.5) {
        $modelLogLoss += -[Math]::Log($clampedProb)
    }
    else {
        $modelLogLoss += -[Math]::Log(1.0 - $clampedProb)
    }

    $error = $label - $prob
    for ($j = 0; $j -lt $weights.Length; $j++) {
        $weights[$j] = $weights[$j] + ($learningRate * (($error * $x[$j]) - ($l2 * $weights[$j])))
    }

    $modelSamples++
}

$symbolProfiles = @()
foreach ($key in ($symbolStats.Keys | Sort-Object)) {
    $s = $symbolStats[$key]
    $count = [Math]::Max(1, [int]$s.trade_count)

    $topEntry = @($s.entry_reasons.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 5 | ForEach-Object {
        [pscustomobject]@{ reason = $_.Key; count = $_.Value }
    })
    $topExit = @($s.exit_reasons.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 5 | ForEach-Object {
        [pscustomobject]@{ reason = $_.Key; count = $_.Value }
    })

    $symbolProfiles += [pscustomobject]@{
        symbol = $key
        trade_count = [int]$s.trade_count
        win_rate = [Math]::Round(([double]$s.wins / $count), 6)
        pnl_usd = [Math]::Round([double]$s.pnl_usd, 6)
        volume = [Math]::Round([double]$s.volume, 6)
        avg_r_multiple = [Math]::Round(([double]$s.r_multiple_sum / $count), 6)
        avg_mae_usd = [Math]::Round(([double]$s.mae_usd_sum / $count), 6)
        avg_mfe_usd = [Math]::Round(([double]$s.mfe_usd_sum / $count), 6)
        avg_hold_seconds = [Math]::Round(([double]$s.hold_seconds_sum / $count), 3)
        avg_pre_imbalance = [Math]::Round(([double]$s.pre_imb_sum / $count), 6)
        avg_pre_spread = [Math]::Round(([double]$s.pre_spread_sum / $count), 6)
        avg_pre_tape_delta = [Math]::Round(([double]$s.pre_tape_delta_sum / $count), 6)
        structure_counts = [pscustomobject]@{
            uptrend = [int]$s.structure_uptrend
            downtrend = [int]$s.structure_downtrend
            range = [int]$s.structure_range
        }
        top_entry_reasons = $topEntry
        top_exit_reasons = $topExit
    }
}

$globalWinRate = if ($episodeCount -gt 0) { [double]$wins / $episodeCount } else { 0.0 }
$modelAccuracy = if ($modelSamples -gt 0) { [double]$modelCorrect / $modelSamples } else { 0.0 }
$modelAvgLogLoss = if ($modelSamples -gt 0) { $modelLogLoss / $modelSamples } else { 0.0 }

$focusSymbols = @($symbolProfiles |
    Where-Object { $_.trade_count -ge 5 -and $_.win_rate -ge 0.55 -and $_.pnl_usd -gt 0 } |
    Sort-Object -Property @{ Expression = "pnl_usd"; Descending = $true }, @{ Expression = "win_rate"; Descending = $true } |
    Select-Object -First 10)
$reduceSymbols = @($symbolProfiles |
    Where-Object { $_.trade_count -ge 5 -and $_.win_rate -lt 0.45 } |
    Sort-Object -Property @{ Expression = "pnl_usd"; Descending = $false }, @{ Expression = "win_rate"; Descending = $true } |
    Select-Object -First 10)

$tradeJournalJsonPath = Join-Path $journalDir ("trades_summary_{0}.json" -f $Date)
$tradeJournalCsvPath = Join-Path $journalDir ("trades_summary_{0}.csv" -f $Date)
$fillJournalJsonPath = Join-Path $journalDir ("fills_{0}.json" -f $Date)
$fillJournalCsvPath = Join-Path $journalDir ("fills_{0}.csv" -f $Date)

if ($tradeJournalRows.Count -gt 0) {
    ($tradeJournalRows | ConvertTo-Json -Depth 8) | Set-Content -Path $tradeJournalJsonPath -Encoding UTF8
}
else {
    "[]" | Set-Content -Path $tradeJournalJsonPath -Encoding UTF8
}

if ($fillJournalRows.Count -gt 0) {
    ($fillJournalRows | ConvertTo-Json -Depth 8) | Set-Content -Path $fillJournalJsonPath -Encoding UTF8
}
else {
    "[]" | Set-Content -Path $fillJournalJsonPath -Encoding UTF8
}

if ($tradeJournalRows.Count -gt 0) {
    $tradeJournalRows | Export-Csv -Path $tradeJournalCsvPath -NoTypeInformation -Encoding UTF8
}
else {
    "date,trade_id,symbol,side,quantity,volume,entry_ts_utc,entry_price,exit_ts_utc,exit_price,hold_seconds,pnl_usd,r_multiple,mae_usd,mfe_usd,win_loss,entry_reason,exit_reason,source_file" | Set-Content -Path $tradeJournalCsvPath -Encoding UTF8
}

if ($fillJournalRows.Count -gt 0) {
    $fillJournalRows | Export-Csv -Path $fillJournalCsvPath -NoTypeInformation -Encoding UTF8
}
else {
    "date,trade_id,symbol,ts_utc,side,quantity,price,source,source_file" | Set-Content -Path $fillJournalCsvPath -Encoding UTF8
}

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
$dateStamp = $Date.Replace("-", "")

$trainingSucceeded = ($episodeFiles.Count -eq 0) -or ($episodeCount -gt 0)
$memorySaveSucceeded = $false
$cleanupSucceeded = $false
$heavyExportCleanupSucceeded = $false
$heavyExportRemovedCount = 0

$memoryPayload = [pscustomobject]@{
    generated_utc = (Get-Date).ToUniversalTime().ToString("o")
    date = $Date
    mode = "internal-self-learning-eod"
    training = [pscustomobject]@{
        succeeded = $trainingSucceeded
        episode_files_detected = $episodeFiles.Count
        episodes_processed = $episodeCount
        parse_failures = $episodeParseFailures
        global_win_rate = [Math]::Round($globalWinRate, 6)
        pnl_sum_usd = [Math]::Round($pnlSum, 6)
        outcomes = [pscustomobject]@{
            wins = $wins
            losses = $losses
            breakeven = $breakeven
        }
    }
    ai_model = [pscustomobject]@{
        type = "online_logistic_v1"
        feature_names = $featureNames
        learning_rate = $learningRate
        l2 = $l2
        sample_count = $modelSamples
        accuracy = [Math]::Round($modelAccuracy, 6)
        avg_logloss = [Math]::Round($modelAvgLogLoss, 6)
        weights = @($weights | ForEach-Object { [Math]::Round([double]$_, 8) })
    }
    intelligence = [pscustomobject]@{
        symbol_profiles = $symbolProfiles
        chart_structure_counts = [pscustomobject]@{
            uptrend = [int]$globalStructureCounts.UPTREND
            downtrend = [int]$globalStructureCounts.DOWNTREND
            range = [int]$globalStructureCounts.RANGE
        }
        top_entry_reasons = @($globalEntryReasons.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 12 | ForEach-Object {
            [pscustomobject]@{ reason = $_.Key; count = $_.Value }
        })
        top_exit_reasons = @($globalExitReasons.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 12 | ForEach-Object {
            [pscustomobject]@{ reason = $_.Key; count = $_.Value }
        })
        recommendations = [pscustomobject]@{
            focus_symbols = $focusSymbols
            reduce_symbols = $reduceSymbols
        }
    }
    parquet_path = $parquetPath
    sources = [pscustomobject]@{
        episode_dir = $(if (Test-Path $episodeDir) { $episodeDir } else { $null })
        self_learning_summary = $(if ($latestSummary) { $latestSummary.FullName } else { $null })
        self_learning_samples = $(if ($latestSamples) { $latestSamples.FullName } else { $null })
        self_learning_predictions = $(if ($latestPredictions) { $latestPredictions.FullName } else { $null })
        self_learning_lifecycle = $(if ($latestLifecycle) { $latestLifecycle.FullName } else { $null })
        self_learning_registry = $(if ($latestRegistry) { $latestRegistry.FullName } else { $null })
        trades_summary_json = $tradeJournalJsonPath
        trades_summary_csv = $tradeJournalCsvPath
        fills_json = $fillJournalJsonPath
        fills_csv = $fillJournalCsvPath
    }
    safety = [pscustomobject]@{
        delete_requires_training_success = $true
        delete_requires_memory_save_success = $true
    }
    notes = @(
        "EOD training from compact trade episodes",
        "Streaming ingestion for performance without DB",
        "Delete temp only after successful training and memory write"
    )
}

$memoryJson = $memoryPayload | ConvertTo-Json -Depth 20
$hashAlgo = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $hashAlgo.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($memoryJson))
$hashHex = -join ($hashBytes | ForEach-Object { $_.ToString("x2") })
$shortHash = $hashHex.Substring(0, 12)
$versionPath = Join-Path $versionsDir ("memory_{0}_{1}.json" -f $Date, $shortHash)

if ($trainingSucceeded) {
    $memoryJson | Set-Content -Path $memoryLatestPath -Encoding UTF8
    $memoryJson | Set-Content -Path $versionPath -Encoding UTF8
    $memorySaveSucceeded = $true
}
else {
    Write-Warning "Training did not produce valid results; memory files were not updated."
}

if ($DeleteTempAfterSuccess) {
    if ($trainingSucceeded -and $memorySaveSucceeded) {
        try {
            if (Test-Path $episodeDir) {
                Remove-Item -Path $episodeDir -Recurse -Force
            }
            if (Test-Path $rawDir) {
                Remove-Item -Path $rawDir -Recurse -Force
            }
            $cleanupSucceeded = $true
        }
        catch {
            $cleanupSucceeded = $false
            Write-Warning "Temp cleanup failed: $($_.Exception.Message)"
        }
    }
    else {
        $cleanupSucceeded = $false
        Write-Warning "Temp cleanup skipped because training and/or memory save was not successful."
    }
}

if ($DeleteHeavyExportsAfterSuccess) {
    if ($trainingSucceeded -and $memorySaveSucceeded) {
        try {
            $keepPrefixes = @()
            if ($PreserveSelfLearningExports) {
                $keepPrefixes += @(
                    "strategy_replay_self_learning_",
                    "promotion_readiness_strategyreplay_"
                )
            }

            $keepFileNames = @(
                $(if ($latestSummary) { [System.IO.Path]::GetFileName($latestSummary.FullName) } else { $null }),
                $(if ($latestSamples) { [System.IO.Path]::GetFileName($latestSamples.FullName) } else { $null }),
                $(if ($latestPredictions) { [System.IO.Path]::GetFileName($latestPredictions.FullName) } else { $null }),
                $(if ($latestLifecycle) { [System.IO.Path]::GetFileName($latestLifecycle.FullName) } else { $null }),
                $(if ($latestRegistry) { [System.IO.Path]::GetFileName($latestRegistry.FullName) } else { $null })
            ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

            $heavyExportRemovedCount = Remove-HeavyExportsForDate -ExportDir $fullExportDir -DateStamp $dateStamp -KeepPrefixes $keepPrefixes -KeepFileNames $keepFileNames
            $heavyExportCleanupSucceeded = $true
        }
        catch {
            $heavyExportCleanupSucceeded = $false
            Write-Warning "Heavy export cleanup failed: $($_.Exception.Message)"
        }
    }
    else {
        $heavyExportCleanupSucceeded = $false
        Write-Warning "Heavy export cleanup skipped because training and/or memory save was not successful."
    }
}

Write-Host "`n=== EOD Internal Self-Learning Complete ===" -ForegroundColor Green
Write-Host "Date: $Date"
Write-Host "Episode files detected: $($episodeFiles.Count)"
Write-Host "Episodes processed: $episodeCount"
Write-Host "Parse failures: $episodeParseFailures"
Write-Host "Global win rate: $([Math]::Round($globalWinRate * 100.0, 2))%"
Write-Host "Episode PnL sum USD: $([Math]::Round($pnlSum, 6))"
Write-Host "Model samples: $modelSamples"
Write-Host "Model accuracy: $([Math]::Round($modelAccuracy * 100.0, 2))%"
Write-Host "Training succeeded: $trainingSucceeded"
Write-Host "Memory save succeeded: $memorySaveSucceeded"
Write-Host "Memory latest: $memoryLatestPath"
Write-Host "Memory version: $versionPath"
Write-Host "Trade journal (JSON): $tradeJournalJsonPath"
Write-Host "Trade journal (CSV): $tradeJournalCsvPath"
Write-Host "Fill journal (JSON): $fillJournalJsonPath"
Write-Host "Fill journal (CSV): $fillJournalCsvPath"
Write-Host "Trade journal rows: $($tradeJournalRows.Count)"
Write-Host "Fill journal rows: $($fillJournalRows.Count)"
if ($parquetPath) { Write-Host "Parquet dataset: $parquetPath" }
if ($DeleteTempAfterSuccess) {
    Write-Host "Temp cleanup succeeded: $cleanupSucceeded"
}
if ($DeleteHeavyExportsAfterSuccess) {
    Write-Host "Heavy export cleanup succeeded: $heavyExportCleanupSucceeded"
    Write-Host "Heavy export files removed: $heavyExportRemovedCount"
    Write-Host "Preserve self-learning exports: $PreserveSelfLearningExports"
}

Write-Host "`nLaunch command:" -ForegroundColor Yellow
Write-Host ".\ops\run_eod_internal_self_learning_cleanup.ps1 -Date $Date -ConvertEpisodesToParquet -DeleteTempAfterSuccess -DeleteHeavyExportsAfterSuccess -PreserveSelfLearningExports $PreserveSelfLearningExports"
