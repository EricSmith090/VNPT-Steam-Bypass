#requires -Version 5.1
<#
.SYNOPSIS
    Undo the targeted VNPT Steam bypass.

.DESCRIPTION
    Removes the NRPT split-DNS rules created by Enable-SteamBypass.ps1, removes
    the managed hosts block, and restores any Steam hosts entries that the
    enable script commented out. A timestamped hosts backup is written first.

.PARAMETER DryRun
    Show what would be removed without changing anything.

.PARAMETER HostsPath
    Override the hosts file path. Intended for testing.
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [string]$HostsPath = (Join-Path $env:SystemRoot 'System32\drivers\etc\hosts')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SteamBypass.Common.ps1')

Write-SteamBypassHeader 'VNPT Steam bypass - disable'

if (-not $DryRun -and -not (Test-IsAdmin)) {
    Write-Host 'Administrator rights are required to remove NRPT rules or edit the hosts file.' -ForegroundColor Red
    Write-Host ''
    Write-Host 'Open PowerShell as Administrator, then run:' -ForegroundColor Yellow
    Write-Host ("  Set-Location '{0}'" -f $PSScriptRoot)
    Write-Host '  .\Disable-SteamBypass.ps1'
    exit 1
}

Write-Host ''
Write-Host 'Removing NRPT split-DNS rules...' -ForegroundColor Cyan
$removed = Remove-SteamNrptRules -DryRun:$DryRun
Write-Host ("  Rules matched: {0}" -f $removed) -ForegroundColor Gray

Write-Host ''
Write-Host 'Restoring hosts file...' -ForegroundColor Cyan
$content = [System.IO.File]::ReadAllText($HostsPath)
$restored = Get-HostsContentWithoutBlock -Content $content
$disabledCount = ([regex]::Matches($content, [regex]::Escape($script:HostsDisabledPrefix))).Count
$hasBlock = $content.Contains($script:HostsBeginMarker)

if ($restored -eq $content) {
    Write-Host '  No managed block or disabled Steam entries found; nothing to change.' -ForegroundColor Gray
}
elseif ($DryRun) {
    Write-Host ("  Would remove the managed block: {0}" -f $hasBlock) -ForegroundColor Gray
    Write-Host ("  Would restore {0} disabled hosts line(s)." -f $disabledCount) -ForegroundColor Gray
}
else {
    $backupDir = Join-Path $PSScriptRoot 'backups'
    $backup = Set-SteamHostsFile -Path $HostsPath `
        -Content $restored -BackupDirectory $backupDir
    Write-Host ("  Restored {0} hosts line(s)." -f $disabledCount) -ForegroundColor Green
    Write-Host ("  Backup: {0}" -f $backup) -ForegroundColor Gray
}

if (-not $DryRun) {
    Clear-SteamDnsCache
    Start-Sleep -Milliseconds 300
}

Write-Host ''
if ($DryRun) {
    Write-Host 'Dry run complete. No changes were made.' -ForegroundColor Yellow
}
else {
    Write-Host 'Steam bypass disabled. DNS and hosts are back to their previous behavior.' -ForegroundColor Green
}
