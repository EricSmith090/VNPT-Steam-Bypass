#requires -Version 5.1
<#
.SYNOPSIS
    Enable a targeted, zero-tunnel Steam bypass on a VNPT connection.

.DESCRIPTION
    Adds split-DNS rules (NRPT) so only Steam/Valve domains use Google DNS
    (8.8.8.8 / 8.8.4.4), then pins the few hostnames whose Google answer still
    lands on a filtered international edge to a verified VNPT-local Akamai IP.

    It does not install a VPN, proxy, filter driver, or route. Non-Steam DNS
    and all non-Steam traffic are left untouched.

.PARAMETER DryRun
    Show what would change without writing NRPT rules or the hosts file.

.PARAMETER SkipHosts
    Do not touch the hosts file.

.PARAMETER SkipNrpt
    Do not add the NRPT split-DNS rules. Hosts pins alone will still work for
    the pinned hostnames, but other Steam names can still be NXDOMAIN.

.PARAMETER Force
    Continue even if a critical endpoint (Store/Community) cannot be verified.

.PARAMETER HostsPath
    Override the hosts file path. Intended for testing.

.PARAMETER TimeoutSec
    Per-endpoint curl timeout in seconds. Default 8.

.EXAMPLE
    .\Enable-SteamBypass.ps1 -DryRun

.EXAMPLE
    .\Enable-SteamBypass.ps1
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$SkipHosts,
    [switch]$SkipNrpt,
    [switch]$Force,
    [string]$HostsPath = (Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'),
    [int]$TimeoutSec = 8
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SteamBypass.Common.ps1')

Write-SteamBypassHeader 'VNPT Steam bypass - enable'
Write-SteamNetworkContext

if ($SkipHosts -and $SkipNrpt) {
    throw 'Both -SkipHosts and -SkipNrpt were specified; there is nothing to do.'
}

if (-not $DryRun -and -not (Test-IsAdmin)) {
    Write-Host 'Administrator rights are required to change NRPT rules or the hosts file.' -ForegroundColor Red
    Write-Host ''
    Write-Host 'Open PowerShell as Administrator, then run:' -ForegroundColor Yellow
    Write-Host ("  Set-Location '{0}'" -f $PSScriptRoot)
    Write-Host '  .\Enable-SteamBypass.ps1'
    exit 1
}

if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
    throw 'curl.exe is required. It ships with Windows 10 1803 and later.'
}

# --- Preflight: make sure the Google path is not itself NXDOMAIN-blocked. ---
Write-Host ''
Write-Host 'Preflight: checking 8.8.8.8 for Steam DNS...' -ForegroundColor Gray
$dnsCheck = @(Resolve-SteamDns -HostName 'store.steampowered.com' -Server '8.8.8.8')
if ($dnsCheck.Count -eq 0 -and -not $Force) {
    throw '8.8.8.8 returned no A record for store.steampowered.com. The DNS bypass is not viable right now.'
}

# --- Validate candidate pins and choose the fastest working IP per host. ---
Write-Host 'Validating local Akamai candidates (this takes a few seconds)...' -ForegroundColor Gray
$plan = @(Get-SteamPinPlan -TimeoutSec $TimeoutSec)

$plan | ForEach-Object {
    $note = ''
    if (-not $_.Best) {
        if ($_.Control -and $_.Control.Ok) {
            if (@($_.Tested | Where-Object { $_.Kind -eq 'TLS_RESET' }).Count -gt 0) {
                $note = 'Steam SNI reset'
            }
            else {
                $note = 'endpoint failed'
            }
        }
        else {
            $note = 'path/TLS failed'
        }
    }

    [pscustomobject]@{
        Host     = $_.Host
        ChosenIP = $(if ($_.Best) { $_.Best.IP } else { '(none)' })
        HTTP     = $(if ($_.Best) { $_.Best.Status } else { 'FAILED' })
        Ms       = $(if ($_.Best) { $_.Best.Ms } else { '' })
        Note     = $note
    }
} | Format-Table -AutoSize

$critical = @('store.steampowered.com', 'steamcommunity.com')
$missing = foreach ($hostName in $critical) {
    $entry = $plan | Where-Object { $_.Host -eq $hostName }
    if (-not $entry -or -not $entry.Best) { $hostName }
}

if ($missing -and -not $Force) {
    $failed = @($plan | Where-Object { $critical -contains $_.Host -and -not $_.Best })
    $sniBlocked = @($failed | Where-Object {
        $_.Control -and $_.Control.Ok -and
        @($_.Tested | Where-Object { $_.Kind -eq 'TLS_RESET' }).Count -gt 0
    })

    $hint = if ($sniBlocked.Count -gt 0) {
        'The active network resets Steam TLS handshakes (SNI filtering); DNS/hosts changes cannot fix that. Retry on the VNPT network this tool targets, or another network that allows Steam TLS.'
    }
    else {
        'Check the active network and retry; no changes were made.'
    }

    throw ("Could not verify: {0}. No changes were made. {1} Use -Force only if you accept the risk." -f ($missing -join ', '), $hint)
}

# --- Show or apply NRPT rules. ---
Write-Host ''
if ($SkipNrpt) {
    Write-Host 'NRPT: skipped (-SkipNrpt).' -ForegroundColor Yellow
}
else {
    Write-Host 'NRPT split-DNS:' -ForegroundColor Cyan
    Install-SteamNrptRules -DryRun:$DryRun
}

# --- Show or apply hosts pins. ---
Write-Host ''
if ($SkipHosts) {
    Write-Host 'Hosts pins: skipped (-SkipHosts).' -ForegroundColor Yellow
}
else {
    $hostsContent = [System.IO.File]::ReadAllText($HostsPath)
    $updatedContent = Get-UpdatedHostsContent -Content $hostsContent -Pins $plan

    if ($DryRun) {
        Write-Host 'Hosts preview (no file written):' -ForegroundColor Cyan
        $block = [regex]::Match(
            $updatedContent,
            '(?ms)^# BEGIN VNPT-Steam-Bypass \(managed\).*?^# END VNPT-Steam-Bypass \(managed\)'
        ).Value
        Write-Host $block
    }
    else {
        $backupDir = Join-Path $PSScriptRoot 'backups'
        $backup = Set-SteamHostsFile -Path $HostsPath `
            -Content $updatedContent -BackupDirectory $backupDir
        Write-Host ("Hosts updated: {0}" -f $HostsPath) -ForegroundColor Green
        Write-Host ("Backup:        {0}" -f $backup) -ForegroundColor Gray
    }
}

# --- Apply cache flush and verify. ---
if (-not $DryRun) {
    Clear-SteamDnsCache
    Start-Sleep -Milliseconds 500

    Write-Host ''
    Write-Host 'Verification through the system resolver:' -ForegroundColor Cyan
    $checks = @('store.steampowered.com', 'steamcommunity.com', 'login.steampowered.com',
                'api.steampowered.com', 'cdn.akamai.steamstatic.com')
    foreach ($hostName in $checks) {
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $output = & curl.exe -sS -o NUL -w '%{http_code} %{time_total}' `
                --noproxy '*' --max-time $TimeoutSec "https://${hostName}/" 2>&1
            $curlExit = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousPreference
        }
        $text = ($output | Out-String).Trim()
        $m = [regex]::Match($text, '(\d{3})\s+([0-9]+(?:[.,][0-9]+)?)\s*$')
        if ($curlExit -eq 0 -and $m.Success) {
            Write-Host ("  {0,-30} HTTP {1}  {2:N0} ms" -f $hostName, $m.Groups[1].Value,
                ([double]($m.Groups[2].Value -replace ',', '.') * 1000)) -ForegroundColor Green
        }
        else {
            Write-Host ("  {0,-30} FAILED: {1}" -f $hostName, $text) -ForegroundColor Red
        }
    }
}

Write-Host ''
if ($DryRun) {
    Write-Host 'Dry run complete. No changes were made.' -ForegroundColor Yellow
}
else {
    Write-Host 'Steam bypass enabled.' -ForegroundColor Green
    Write-Host 'Restart Steam completely (including the tray icon) and run .\Test-SteamBypass.ps1.' -ForegroundColor Gray
    Write-Host 'To undo: .\Disable-SteamBypass.ps1' -ForegroundColor Gray
}
