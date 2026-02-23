param(
    [string]$TwsRoot = 'C:\Jts',
    [int[]]$Ports = @(7496, 7497, 4001, 4002)
)

$ErrorActionPreference = 'Stop'

function Get-TwsApiSettings {
    param([string]$Root)

    $twsXml = Get-ChildItem -Path $Root -Recurse -Filter 'tws.xml' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $twsXml) {
        return [PSCustomObject]@{
            TwsXmlPath = $null
            SocketClient = $null
            Port = $null
            ReadOnlyApi = $null
            AllowOnlyLocalhost = $null
        }
    }

    [xml]$xml = Get-Content -Path $twsXml.FullName -Raw
    $apiNode = $xml.SelectSingleNode('//ApiSettings')

    [PSCustomObject]@{
        TwsXmlPath = $twsXml.FullName
        SocketClient = $apiNode.socketClient
        Port = $apiNode.port
        ReadOnlyApi = $apiNode.readOnlyApi
        AllowOnlyLocalhost = $apiNode.allowOnlyLocalhost
    }
}

function Get-PortListeners {
    param([int[]]$PortList)

    foreach ($p in $PortList) {
        $listener = Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue
        if ($listener) {
            [PSCustomObject]@{
                Port = $p
                Listening = $true
                OwningProcess = ($listener | Select-Object -First 1 -ExpandProperty OwningProcess)
            }
        }
        else {
            [PSCustomObject]@{
                Port = $p
                Listening = $false
                OwningProcess = $null
            }
        }
    }
}

$tws = Get-Process -Name 'tws' -ErrorAction SilentlyContinue
$apiSettings = Get-TwsApiSettings -Root $TwsRoot
$listeners = Get-PortListeners -PortList $Ports

Write-Host '=== TWS Process ==='
if ($tws) {
    $tws | Select-Object ProcessName, Id, StartTime | Format-Table -AutoSize
}
else {
    Write-Host 'TWS process not running.'
}

Write-Host "`n=== API Settings (from tws.xml) ==="
$apiSettings | Format-List

Write-Host "`n=== Port Listeners ==="
$listeners | Format-Table -AutoSize

$effectivePort = [int]($apiSettings.Port)
$effectiveListener = $listeners | Where-Object { $_.Port -eq $effectivePort -and $_.Listening }

Write-Host "`n=== Result ==="
if ($tws -and $effectiveListener) {
    Write-Host "READY: TWS API socket appears reachable on port $effectivePort." -ForegroundColor Green
    if ($apiSettings.SocketClient -ne 'true') {
        Write-Host "NOTE: Runtime is reachable, but saved profile shows socketClient=$($apiSettings.SocketClient)." -ForegroundColor Yellow
        Write-Host '- In TWS, re-open API settings and click Apply/OK to persist the current runtime setting.'
    }
    exit 0
}

Write-Host 'NOT READY: TWS API is not fully reachable yet.' -ForegroundColor Yellow
if (-not $tws) {
    Write-Host '- Start TWS and log in.'
}
if ($apiSettings.SocketClient -ne 'true') {
    Write-Host '- In TWS: Global Configuration > API > Settings > Enable ActiveX and Socket Clients.'
}
if (-not $effectiveListener) {
    Write-Host "- Ensure configured API port ($effectivePort) is enabled and TWS is fully logged in."
}

exit 1
