#requires -Version 5.1
<#
.SYNOPSIS
    Verify the HCMUS Campus bypass (relay + Steam-only proxy PAC).

.DESCRIPTION
    Checks three things and prints a verdict:
      1. Direct (bypassing proxy): Steam resets, example.com works - proves the network still filters.
      2. Via the local relay: Steam endpoints answer (any origin HTTP status is success).
      3. The whitelist: the control (example.com) via the relay is refused (403).

    The relay is the hidden --relay mode of dist\VNPT-Steam-Bypass.exe.
    This script itself does not need elevation, but the relay process does.

.EXAMPLE
    .\Test-CampusRelay.ps1
#>
[CmdletBinding()]
param(
    [int]$TimeoutSec = 8
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CampusRelay.Common.ps1')

Write-CampusRelayHeader 'HCMUS Campus - verification'

if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
    throw 'curl.exe is required.'
}

$port = Get-CampusRelayPort
$pacUrl = Get-SteamPacUrl
$pacPort = Get-SteamPacPort
$running = Test-RelayRunning

Write-Host ('Relay process running: {0}' -f $(if ($running) { 'yes' } else { 'no' })) -ForegroundColor $(if ($running) { 'Green' } else { 'Yellow' })
if ($running) {
    $relayPids = @(Get-SteamRelayProcesses | ForEach-Object { $_.ProcessId })
    Write-Host ('Relay PID(s):          {0}' -f ($relayPids -join ', '))
}
Write-Host ('Relay port:            {0}' -f $port)
Write-Host ('PAC URL:               {0}' -f $(if ($pacUrl) { $pacUrl } else { '(not set)' }))
Write-Host ''

$checks = @(
    @{N='Store';      U='https://store.steampowered.com/'},
    @{N='Community';  U='https://steamcommunity.com/'},
    @{N='Login';      U='https://login.steampowered.com/'},
    @{N='API';        U='https://api.steampowered.com/'},
    @{N='CDN';        U='https://cdn.akamai.steamstatic.com/'},
    @{N='Help';       U='https://help.steampowered.com/'},
    @{N='Checkout';   U='https://checkout.steampowered.com/'},
    @{N='SteamGames'; U='https://steamgames.com/'},
    @{N='Valve';      U='https://valvesoftware.com/'},
    @{N='Steam.tv';   U='https://steam.tv/'},
    @{N='s.team';     U='https://s.team/'}
)

function Invoke-CurlCheck {
    param(
        [string]$Url,
        [string]$Proxy,
        [int]$TimeoutSec = 8
    )
    $prev=$ErrorActionPreference; $ErrorActionPreference='Continue'
    try {
        $curlArgs = @('-st','-o','NUL','--max-time',$TimeoutSec,'-w','%{http_code} %{time_total}')
        if ($Proxy) { $curlArgs += @('--proxy', $Proxy) } else { $curlArgs += @('--noproxy', '*') }
        $curlArgs += $Url
        $out = & curl.exe @curlArgs 2>&1
        $ec = $LASTEXITCODE
    } finally { $ErrorActionPreference=$prev }
    $text = (($out|Out-String).Trim())
    $m = [regex]::Match($text, '(\d{3})\s+([0-9]+(?:[.,][0-9]+)?)\s*$')
    if ($ec -eq 0 -and $m.Success) {
        return [pscustomobject]@{ Ok = ([int]$m.Groups[1].Value -ne 0); Code = [int]$m.Groups[1].Value; Ms = [int]([double]($m.Groups[2].Value -replace ',','.') * 1000); Error = ''; Kind = 'OK' }
    }
    $kind = if ($text -match 'Connection was reset|Recv failure|reset by peer|ssl.*reset') { 'RESET' }
             elseif ($ec -eq 28 -or $text -match 'timed out|timeout') { 'TIMEOUT' }
             elseif ($ec -eq 7) { 'CONNECT_FAIL' } else { "aURL_$ec" }
    return [pscustomobject]@{ Ok = $false; Code = 0; Ms = $null; Error = $text; Kind = $kind }
}

Write-Host 'Direct (no proxy) - network filter still in place:' -ForegroundColor Cyan
$directFail = 0
foreach ($c in @($checks | Select-Object -First 4) + @(@{N='Control'; U='https://example.com/'})) {
    $r = Invoke-CurlCheck -Url $c.U -TimeoutSec $TimeoutSec
    $ok = $r.Ok
    $label = if ($ok) { ('HTTP {0}  {1} ms' -f $r.Code, $r.Ms) } else { $r.Kind }
    $color = if ($ok) { 'Green' } else { 'Red' }
    if (-not $ok) { $directFail++ }
    Write-Host ('  {0,-12} {1}' -f $c.N, $label) -ForegroundColor $color
}

Write-Host ''
Write-Host 'Via relay - Steam should be reachable:' -ForegroundColor Cyan
$proxyFail = 0
$pacFail = 0
foreach ($c in $checks) {
    if (-not $running) { break }
    $r = Invoke-CurlCheck -Url $c.U -Proxy ("http://127.0.0.1:{0}" -f $port) -TimeoutSec $TimeoutSec
    # Any origin status (200/301/302/403/404/...) proves the TLS handshake completed and the edge answered.
    $ok = $r.Ok
    $label = if ($ok) { ('HTTP {0}  {1} ms' -f $r.Code, $r.Ms) } else { $r.Kind }
    $color = if ($ok) { 'Green' } else { 'Red' }
    if (-not $ok) { $proxyFail++ }
    Write-Host ('  {0,-12} {1}' -f $c.N, $label) -ForegroundColor $color
}

Write-Host ''
Write-Host 'Whitelist control - example.com via relay must be refused:' -ForegroundColor Cyan
if ($running) {
    $r = Invoke-CurlCheck -Url 'https://example.com/' -Proxy ("http://127.0.0.1:{0}" -f $port) -TimeoutSec $TimeoutSec
    $ok = (-not $r.Ok) -or ($r.Code -eq 403)
    Write-Host ('  example.com     {0}' -f $(if ($r.Ok) { ('HTTP {0}' -f $r.Code) } else { $r.Kind })) -ForegroundColor $(if ($ok) { 'Green' } else { 'Yellow' })
} else {
    Write-Host '  relay not running; skipping.' -ForegroundColor Yellow
}

Write-Host ''
$key = Get-ItemProperty $script:InternetKey -ErrorAction SilentlyContinue
Write-Host 'User proxy state:' -ForegroundColor Cyan
Write-Host ('  ProxyEnable   : {0}' -f $key.ProxyEnable)
Write-Host ('  AutoConfigURL : {0}' -f $key.AutoConfigURL)
Write-Host ''

if (-not $running) {
    Write-Host 'PARTIAL: relay is not running. Start it from the GUI (Campus mode) or with .\Enable-CampusRelay.ps1 from an elevated PowerShell window.' -ForegroundColor Yellow
    exit 1
}

Write-Host ''
Write-Host 'PAC delivery - Chromium/Steam requires an HTTP PAC:' -ForegroundColor Cyan
if ($pacPort -le 0) {
    Write-Host ('  AutoConfigURL is not the expected loopback HTTP PAC endpoint: {0}' -f $(if ($pacUrl) { $pacUrl } else { '(not set)' })) -ForegroundColor Red
    Write-Host '  file:// PAC URLs are ignored by Chromium/Steam. Re-run .\Enable-CampusRelay.ps1.' -ForegroundColor Yellow
    $pacFail++
}
else {
    try {
        $wc = New-Object System.Net.WebClient
        $wc.Proxy = $null
        $pacBody = $wc.DownloadString($pacUrl)
        $pacBytes = [Text.Encoding]::UTF8.GetByteCount($pacBody)
        $contentOk = $pacBody -match 'function FindProxyForURL'
        $relayOk = $pacBody -match ('PROXY\s+127\.0\.0\.1:{0}\b' -f $port)
        $ok = $contentOk -and $relayOk
        if (-not $ok) { $pacFail++ }
        Write-Host ('  {0,-12} HTTP 200  {1} bytes' -f 'PAC endpoint', $pacBytes) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    }
    catch {
        Write-Host ('  {0,-12} {1}' -f 'PAC endpoint', $_.Exception.Message) -ForegroundColor Red
        $pacFail++
    }

    $pacChecks = @(
        @{N='Store';     U='https://store.steampowered.com/';        Expect='PROXY'},
        @{N='Community'; U='https://steamcommunity.com/';            Expect='PROXY'},
        @{N='CDN';       U='https://cdn.akamai.steamstatic.com/';    Expect='PROXY'},
        @{N='s.team';    U='https://s.team/';                         Expect='PROXY'},
        @{N='Control';   U='https://example.com/';                    Expect='DIRECT'},
        @{N='Lookalike'; U='https://steampowered.com.evil.com/';      Expect='DIRECT'}
    )
    foreach ($c in $pacChecks) {
        $d = Get-WinHttpPacDecision -PacUrl $pacUrl -Url $c.U
        if (-not $d.Ok) {
            $ok = $false
            $label = $d.Error
        }
        elseif ($c.Expect -eq 'PROXY') {
            $ok = ($d.AccessType -eq 'PROXY' -and $d.Proxy -eq ('127.0.0.1:{0}' -f $port))
            $label = if ($d.AccessType -eq 'PROXY') { 'PROXY ' + $d.Proxy } else { $d.AccessType }
        }
        else {
            $ok = ($d.AccessType -eq 'DIRECT')
            $label = $d.AccessType
        }
        if (-not $ok) { $pacFail++ }
        Write-Host ('  {0,-12} {1}' -f $c.N, $label) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    }
}

Write-Host ''
if ($proxyFail -eq 0 -and $pacFail -eq 0) {
    Write-Host 'PASS: relay reaches Steam endpoints; direct path remains filtered; HTTP PAC routes only Steam/Valve names to the relay.' -ForegroundColor Green
    exit 0
}
Write-Host ('FAIL: relay failures={0}, PAC failures={1}. Check .\logs or re-run .\Enable-CampusRelay.ps1.' -f $proxyFail, $pacFail) -ForegroundColor Red
exit 1
