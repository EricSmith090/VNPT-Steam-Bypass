#requires -Version 5.1
<#
.SYNOPSIS
    Verify the targeted VNPT Steam bypass.

.DESCRIPTION
    Checks the NRPT rules, the managed hosts block, DNS resolution through the
    Windows resolver (which honors hosts and NRPT), and real HTTPS responses.
    Also measures the non-Steam control path to show that ordinary traffic is
    not being routed through a tunnel.

.EXAMPLE
    .\Test-SteamBypass.ps1
#>
[CmdletBinding()]
param(
    [int]$TimeoutSec = 8
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SteamBypass.Common.ps1')

function Invoke-CurlCheck {
    param(
        [string]$Url,
        [int]$TimeoutSec = 8
    )

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & curl.exe -sS -o NUL -w '%{http_code} %{time_total}' `
            --noproxy '*' --max-time $TimeoutSec $Url 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    $text = ($output | Out-String).Trim()
    $match = [regex]::Match($text, '(\d{3})\s+([0-9]+(?:[.,][0-9]+)?)\s*$')

    if ($exitCode -eq 0 -and $match.Success) {
        return [pscustomobject]@{
            Ok = ([int]$match.Groups[1].Value -ne 0)
            Status = [int]$match.Groups[1].Value
            Ms = [int]([double]($match.Groups[2].Value -replace ',', '.') * 1000)
            Error = $null
        }
    }

    return [pscustomobject]@{
        Ok = $false
        Status = $null
        Ms = $null
        Error = $text
    }
}

Write-SteamBypassHeader 'VNPT Steam bypass - verification'

# --- Configuration state. ---
$rules = @(Get-DnsClientNrptRule | Where-Object {
    $_.Comment -eq $script:SteamBypassMarker
})

$hostsPath = Get-DefaultHostsPath
$hostsContent = [System.IO.File]::ReadAllText($hostsPath)
$hasBlock = $hostsContent.Contains($script:HostsBeginMarker)
$disabledCount = ([regex]::Matches($hostsContent, [regex]::Escape($script:HostsDisabledPrefix))).Count

Write-Host ''
Write-Host 'Configuration:' -ForegroundColor Cyan
Write-Host ("  NRPT rules:          {0}" -f $rules.Count)
Write-Host ("  Hosts block:         {0}" -f $(if ($hasBlock) { 'present' } else { 'absent' }))
Write-Host ("  Disabled old lines:  {0}" -f $disabledCount)

# --- Resolution checks. ---
$dnsHosts = @(
    'store.steampowered.com',
    'steamcommunity.com',
    'login.steampowered.com',
    'api.steampowered.com',
    'cdn.akamai.steamstatic.com'
)

Write-Host ''
Write-Host 'Resolution (app path vs raw Google DNS):' -ForegroundColor Cyan
$dnsRows = foreach ($hostName in $dnsHosts) {
    $resolved = ''
    try {
        $addresses = [System.Net.Dns]::GetHostAddresses($hostName) |
            Where-Object { $_.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork } |
            ForEach-Object { $_.IPAddressToString }
        $resolved = @($addresses) -join ', '
    }
    catch {
        $resolved = 'ERROR'
    }

    $google = @(Resolve-SteamDns -HostName $hostName -Server '8.8.8.8') -join ', '

    [pscustomobject]@{
        Host = $hostName
        'App path' = $(if ($resolved) { $resolved } else { 'none' })
        'Google DNS' = $(if ($google) { $google } else { 'none' })
    }
}
$dnsRows | Format-Table -AutoSize

# --- HTTPS checks. ---
$checks = @(
    @{ Url = 'https://store.steampowered.com/';          Name = 'Store';       Expect = 200 },
    @{ Url = 'https://steamcommunity.com/';              Name = 'Community';   Expect = 200 },
    @{ Url = 'https://login.steampowered.com/';          Name = 'Login';       Expect = 302 },
    @{ Url = 'https://api.steampowered.com/';            Name = 'API';         Expect = 404 },
    @{ Url = 'https://cdn.akamai.steamstatic.com/';      Name = 'CDN';         Expect = 200 },
    @{ Url = 'https://help.steampowered.com/';           Name = 'Help';        Expect = 302 },
    @{ Url = 'https://checkout.steampowered.com/';       Name = 'Checkout';    Expect = 302 },
    @{ Url = 'https://steamgames.com/';                  Name = 'SteamGames';  Expect = 301 },
    @{ Url = 'https://example.com/';                     Name = 'Control';     Expect = 200 }
)

Write-Host ''
Write-Host 'HTTPS through the system resolver:' -ForegroundColor Cyan
$results = foreach ($check in $checks) {
    $result = Invoke-CurlCheck -Url $check.Url -TimeoutSec $TimeoutSec
    [pscustomobject]@{
        Name = $check.Name
        URL = $check.Url
        HTTP = $(if ($result.Ok) { $result.Status } else { 'FAIL' })
        'Expected' = $check.Expect
        Ms = $(if ($result.Ok) { $result.Ms } else { '' })
        Error = $(if ($result.Ok) { '' } else { $result.Error })
    }
}
$results | Format-Table -AutoSize

# --- Verdict. ---
$store = $results | Where-Object { $_.Name -eq 'Store' }
$community = $results | Where-Object { $_.Name -eq 'Community' }
$control = $results | Where-Object { $_.Name -eq 'Control' }

$storeOk = $store -and $store.HTTP -eq 200
$communityOk = $community -and $community.HTTP -eq 200
$controlOk = $control -and $control.HTTP -eq 200

Write-Host ''
if ($storeOk -and $communityOk -and $controlOk) {
    Write-Host 'PASS: Store and Community load directly; the non-Steam control is unaffected.' -ForegroundColor Green
    exit 0
}
if ($storeOk -or $communityOk) {
    Write-Host 'PARTIAL: at least one Steam endpoint works, but the bypass is incomplete.' -ForegroundColor Yellow
    exit 0
}
Write-Host 'FAIL: Steam is still blocked. Re-run Enable-SteamBypass.ps1 or check the NRPT/hosts state.' -ForegroundColor Red
exit 1
