#requires -Version 5.1
<#
.SYNOPSIS
    Disable the HCMUS Campus bypass and restore this user's proxy settings.

.DESCRIPTION
    Stops the local relay (including its loopback HTTP PAC endpoint) and
    restores the proxy configuration that was in effect before
    Enable-CampusRelay.ps1 was run (registry backup), or resets only the
    bypass's own values if no backup exists. Run this from an elevated
    PowerShell window because the relay is a child of the GUI EXE.

.PARAMETER KeepBackup
    Keep the saved pre-bypass proxy backup instead of deleting it.

.EXAMPLE
    .\Disable-CampusRelay.ps1
#>
[CmdletBinding()]
param(
    [switch]$KeepBackup
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CampusRelay.Common.ps1')

Write-CampusRelayHeader 'HCMUS Campus - disable bypass'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window (Run as administrator), or use dist\VNPT-Steam-Bypass.exe directly.'
}

$killed = 0
foreach ($p in @(Get-SteamRelayProcesses)) {
    try {
        Stop-Process -Id $p.ProcessId -Force
        $killed++
    }
    catch {
        Write-Host ('Could not stop relay PID {0}: {1}' -f $p.ProcessId, $_.Exception.Message) -ForegroundColor Red
    }
}
foreach ($p in @(Get-LegacyRelayProcesses)) {
    try {
        Stop-Process -Id $p.Id -Force
        $killed++
    }
    catch {
        Write-Host ('Could not stop legacy relay PID {0}: {1}' -f $p.Id, $_.Exception.Message) -ForegroundColor Red
    }
}
Write-Host ('Relay/PAC processes stopped: {0}' -f $killed) -ForegroundColor Gray

$statePath = Join-Path $script:DataDir 'relay-state.json'
if (Test-Path -LiteralPath $statePath) {
    Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
}

Reset-ProxySettings
if (-not $KeepBackup -and (Test-Path $script:BackupPath)) {
    Remove-Item -LiteralPath $script:BackupPath -Force -ErrorAction SilentlyContinue
    Write-Host 'Proxy backup consumed and removed.' -ForegroundColor Gray
}
else {
    Write-Host ('Proxy backup kept at {0}' -f $script:BackupPath) -ForegroundColor Gray
}

$key = Get-ItemProperty $script:InternetKey -ErrorAction SilentlyContinue
Write-Host ''
Write-Host 'aurrent user proxy state:' -ForegroundColor Cyan
Write-Host ('  ProxyEnable   : {0}' -f $key.ProxyEnable)
Write-Host ('  ProxyServer   : {0}' -f $key.ProxyServer)
Write-Host ('  AutoConfigURL : {0}' -f $key.AutoConfigURL)
Write-Host ('  AutoDetect    : {0}' -f $key.AutoDetect)

Write-Host ''
Write-Host 'Bypass disabled. Restart Steam/browser for the change to take effect.' -ForegroundColor Green
