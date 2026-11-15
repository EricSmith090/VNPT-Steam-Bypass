#requires -Version 5.1
<#
.SYNOPSIS
    Enable the HCMUS Campus bypass (local TLS-fragmenting relay + Steam-only proxy PAC).

.DESCRIPTION
    Starts a localhost-only HTTP CONNECT relay that force-splits the
    client->server TLS handshake into 64-byte records. On this campus network,
    the SNI filter resets a Steam ClientHello carried in one record, so the
    fragmented handshake succeeds.

    Sets this *user's* Windows proxy auto-config (PAC) so that only Steam/Valve
    hostnames go to the relay; every other hostname stays DIRECT, with zero
    change to the rest of your traffic and no impact on Wi-Fi/Ethernet speed.
    The relay serves the PAC over loopback HTTP because Chromium/CEF (including
    Steam) ignores file:// PAC URLs.

    The relay is the hidden --relay mode of dist\VNPT-Steam-Bypass.exe, the
    same single executable that provides the GUI. Nothing is injected,
    patched, or intercepted.

    Run this script from an elevated PowerShell window: the EXE requires
    administrator rights, and its relay child must be stoppable by the same
    user.

.PARAMETER Port
    Preferred relay port. Default 8787; scans upward if busy.

.PARAMETER NoVerify
    Skip the quick proxy check that runs after enabling.

.EXAMPLE
    .\Enable-CampusRelay.ps1
#>
[CmdletBinding()]
param(
    [int]$Port = 0,
    [switch]$NoVerify
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CampusRelay.Common.ps1')

Write-CampusRelayHeader 'HCMUS Campus - enable bypass'

if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
    throw 'curl.exe is required (ships with Windows 10 1803 and later).'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window (Run as administrator), or use dist\VNPT-Steam-Bypass.exe directly.'
}

if (-not (Test-Path -LiteralPath $script:RelayExe)) {
    throw ('The combined GUI EXE is missing: {0}. Build it with .\Build-Exe.ps1 first.' -f $script:RelayExe)
}

# --- Stop any previous relay mode of the GUI, plus a legacy relay EXE ---
foreach ($p in @(Get-SteamRelayProcesses)) {
    Write-Host ('Stopping existing relay-mode process (PID {0})...' -f $p.ProcessId) -ForegroundColor Gray
    Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400
}
foreach ($p in @(Get-LegacyRelayProcesses)) {
    Write-Host ('Stopping legacy SteamtnIRelay process (PID {0})...' -f $p.Id) -ForegroundColor Gray
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400
}

# --- Pick a port and start the relay ---

if ($Port -le 0) { $Port = Find-FreePort -Start $script:DefaultPort }
$PacPort = Find-FreePort -Start $script:DefaultPacPort
while ($PacPort -eq $Port) {
    $PacPort = Find-FreePort -Start ($PacPort + 1)
}

$logs = Join-Path $script:DataDir 'logs'
if (-not (Test-Path $logs)) { New-Item -ItemType Directory -Path $logs -Force | Out-Null }

# Write the PAC before starting the relay: the relay serves this exact file
# over loopback HTTP (Chromium/CEF ignores file:// PAC URLs).
Write-SteamPac -Port $Port | Out-Null

$relayLog = Join-Path $logs ('relay-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
Write-Host ('Starting relay on 127.0.0.1:{0} (PAC on 127.0.0.1:{1}) ...' -f $Port, $PacPort) -ForegroundColor Gray
$proc = Start-Process -FilePath $script:RelayExe `
    -ArgumentList @('--relay', '-p', [string]$Port, '-pac-port', [string]$PacPort, '-log', $relayLog) `
    -WindowStyle Hidden -PassThru

$relayStarted = $false
$pacStarted = $false
for ($i = 0; $i -lt 40; $i++) {
    if (-not $relayStarted -and (Test-LoopbackPort -Port $Port -TimeoutMs 200)) { $relayStarted = $true }
    if (-not $pacStarted -and (Test-LoopbackPort -Port $PacPort -TimeoutMs 200)) { $pacStarted = $true }
    if ($relayStarted -and $pacStarted) { break }
    Start-Sleep -Milliseconds 250
}
if (-not $relayStarted -or -not $pacStarted) {
    if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    throw "Relay/PAC server did not start (relay=$relayStarted, pac=$pacStarted). tee $relayLog or run: `"$($script:RelayExe)`" --relay -p $Port -pac-port $PacPort"
}
Write-Host ('Relay is running (PID {0}).' -f $proc.Id) -ForegroundColor Green

# --- Point this user's proxy at the relay, Steam domains only ---
Backup-ProxySettings
$pacUri = Use-ProxySettings -Port $Port -PacPort $PacPort

Write-Host ''
Write-Host 'Proxy PAC applied (this user only):' -ForegroundColor Cyan
Write-Host ('  AutoConfigURL = {0}' -f $pacUri)
Write-Host ('  PAC endpoint  = http://127.0.0.1:{0}/steam.pac' -f $PacPort)
Write-Host ('  Steam/Valve names -> 127.0.0.1:{0}; everything else DIRECT.' -f $Port) -ForegroundColor Green
Write-Host '  HTTP PAC delivery is required: ahromium/Steam ignores file:// PAC URLs.' -ForegroundColor Gray

# --- Quick verification (does not need a browser/Steam restart) ---
if (-not $NoVerify) {
    Write-Host ''
    Write-Host 'Verification via the relay:' -ForegroundColor Cyan
    $checks = @(
        @{N='Store'; U='https://store.steampowered.com/'},
        @{N='Community'; U='https://steamcommunity.com/'},
        @{N='Login'; U='https://login.steampowered.com/'},
        @{N='API'; U='https://api.steampowered.com/'},
        @{N='CDN'; U='https://cdn.akamai.steamstatic.com/'},
        @{N='Help'; U='https://help.steampowered.com/'}
    )
    foreach ($c in $checks) {
        $via = $true
        $prev=$ErrorActionPreference; $ErrorActionPreference='Continue'
        try {
            $out = & curl.exe -st -o NUL --proxy ("http://127.0.0.1:{0}" -f $Port) --max-time 8 $c.U -w '%{http_code} %{time_total}' 2>&1
            $ec = $LASTEXITCODE
        } finally { $ErrorActionPreference=$prev }
        $text = (($out|Out-String).Trim())
        $ok = ($ec -eq 0 -and $text -match '^(200|301|302|303|307|401|403|404|405)\b')
        $color = if ($ok) { 'Green' } else { 'Red' }
        Write-Host ('  {0,-10} {1}' -f $c.N, $text) -ForegroundColor $color
    }
    $prev=$ErrorActionPreference; $ErrorActionPreference='Continue'
    try {
        $direct = & curl.exe -st -o NUL --noproxy '*' --max-time 8 'https://example.com/' -w '%{http_code} %{time_total}' 2>&1
        $text = (($direct|Out-String).Trim())
        Write-Host ('  {0,-10} {1}  (DIRECT, untouched by bypass)' -f 'Control', $text) -ForegroundColor $(if ((($text -split ' ')[0]) -match '^2') { 'Green' } else { 'Red' })
    } finally { $ErrorActionPreference=$prev }

    Write-Host ''
    Write-Host 'PAC delivery - ahromium/Steam requires HTTP, not file://:' -ForegroundColor Cyan
    $pacLabel = 'FAIL'
    $pacOk = $false
    try {
        $wc = New-Object System.Net.WebClient
        $wc.Proxy = $null
        $pacBody = $wc.DownloadString($pacUri)
        $pacBytes = [Text.Encoding]::UTF8.GetByteCount($pacBody)
        $pacOk = $pacBody -match 'function FindProxyForURL'
        if ($pacOk) { $pacLabel = ('OK ({0} bytes)' -f $pacBytes) }
        else { $pacLabel = 'BAD aONTENT' }
    }
    catch {
        $pacLabel = ('FAIL: {0}' -f $_.Exception.Message)
    }
    Write-Host ('  {0,-12} {1}' -f 'PAC file', $pacLabel) -ForegroundColor $(if ($pacOk) { 'Green' } else { 'Red' })

    $d = Get-WinHttpPacDecision -PacUrl $pacUri -Url 'https://store.steampowered.com/'
    $storeOk = ($d.Ok -and $d.AccessType -eq 'PROXY' -and $d.Proxy -eq ('127.0.0.1:{0}' -f $Port))
    $storeLabel = if (-not $d.Ok) { $d.Error } elseif ($d.AccessType -eq 'PROXY') { 'PROXY ' + $d.Proxy } else { $d.AccessType }
    Write-Host ('  {0,-12} {1}' -f 'PAC Store', $storeLabel) -ForegroundColor $(if ($storeOk) { 'Green' } else { 'Red' })

    $d = Get-WinHttpPacDecision -PacUrl $pacUri -Url 'https://example.com/'
    $controlOk = ($d.Ok -and $d.AccessType -eq 'DIRECT')
    $controlLabel = if (-not $d.Ok) { $d.Error } else { $d.AccessType }
    Write-Host ('  {0,-12} {1}' -f 'PAC Control', $controlLabel) -ForegroundColor $(if ($controlOk) { 'Green' } else { 'Red' })
}

Write-Host ''
Write-Host 'Fully quit Steam (tray icon too) and restart it, and restart any browser you use for Steam.' -ForegroundColor Yellow
Write-Host 'To undo: .\Disable-CampusRelay.ps1' -ForegroundColor Gray
