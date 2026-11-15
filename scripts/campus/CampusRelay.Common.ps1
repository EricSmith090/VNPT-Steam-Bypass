# Shared helpers for HCMUS Campus bypass scripts.
# Dot-sourced by Enable/Disable/Test-CampusRelay.ps1.

$ErrorActionPreference = 'Stop'

$script:SteamSuffixes = @(
    'steampowered.com','steamcommunity.com','steamstatic.com','steamgames.com',
    'valvesoftware.com','steam-chat.com','steam.tv','steamdeck.com','steamcloud.com',
    'steamcontent.com','steamusercontent.com','steamserver.net','steamconnect.com',
    'steamcdn-a.akamaihd.net','steamuserimages-a.akamaihd.net','steamstore-a.akamaihd.net',
    'steamcommunity-a.akamaihd.net','steamstatic-a.akamaihd.net','s.team'
)
$script:Marker         = 'HCMUS-Campus'
$script:DefaultPort    = 8787
$script:DefaultPacPort = 8788
$script:DataDir        = Join-Path $env:LOCALAPPDATA 'HCMUS-Campus'
$script:BackupPath     = Join-Path $DataDir 'proxy-backup.json'
$script:PacPath        = Join-Path $DataDir 'steam.pac'
# The relay is a hidden mode of the single GUI executable.
$script:RelayExe       = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'dist\VNPT-Steam-Bypass.exe'
$script:InternetKey    = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'

function Get-CampusRelayPort {
    if (Test-Path $PacPath) {
        $content = Get-Content $PacPath -Raw -ErrorAction SilentlyContinue
        $m = [regex]::Match($content, 'PROXY\s+127\.0\.0\.1:(\d+)')
        if ($m.Success) { return [int]$m.Groups[1].Value }
    }
    return $script:DefaultPort
}

function Get-SteamPacUrl {
    $key = Get-ItemProperty $script:InternetKey -ErrorAction SilentlyContinue
    if ($key -and $key.AutoConfigURL) { return [string]$key.AutoConfigURL }
    return ''
}

function Get-SteamPacPort {
    $url = Get-SteamPacUrl
    $m = [regex]::Match($url, '^http://127\.0\.0\.1:(\d+)/steam\.pac(?:[?#].*)?$', 'IgnoreCase')
    if ($m.Success) { return [int]$m.Groups[1].Value }
    return 0
}

function Get-SteamRelayProcesses {
    $name = Split-Path $script:RelayExe -Leaf
    try {
        return @(Get-CimInstance Win32_Process -Filter ("Name = '{0}'" -f $name.Replace("'", "''")) -ErrorAction Stop |
            Where-Object { $_.CommandLine -and $_.CommandLine -match '(?i)(^|\s)--relay(\s|$)' })
    }
    catch {
        return @()
    }
}

function Get-LegacyRelayProcesses {
    return @(Get-Process -Name 'SteamSnIRelay' -ErrorAction SilentlyContinue)
}

function Test-RelayRunning {
    return @(Get-SteamRelayProcesses).Count -gt 0
}

function Test-LoopbackPort {
    param([int]$Port, [int]$TimeoutMs = 500)

    $tcp = New-Object System.Net.Sockets.TcpClient
    try {
        $t = $tcp.ConnectAsync('127.0.0.1', $Port)
        if (-not $t.Wait($TimeoutMs)) { return $false }
        return $tcp.Connected
    }
    catch { return $false }
    finally { try { $tcp.Close() } catch { } }
}

function Find-FreePort {
    param([int]$Start = $script:DefaultPort, [int]$Range = 50)

    for ($p = $Start; $p -le $Start + $Range; $p++) {
        if (-not (Test-LoopbackPort -Port $p -TimeoutMs 80)) { return $p }
    }
    throw "No free port in range $Start-$($Start + $Range)."
}

function New-SteamPacContent {
    param([int]$Port)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('// HCMUS Campus bypass - managed by Enable-CampusRelay.ps1')
    $lines.Add('// Routes Steam/Valve hostnames to the localhost fragmenting relay; everything else DIRECT.')
    $lines.Add('function FindProxyForURL(url, host) {')
    $lines.Add('  var h = (host || "").toLowerCase();')
    $lines.Add('  var suffixes = [')
    for ($i = 0; $i -lt $script:SteamSuffixes.Length; $i++) {
        $suffix = $script:SteamSuffixes[$i]
        $comma = if ($i -lt $script:SteamSuffixes.Length - 1) { ',' } else { '' }
        $lines.Add(('    "' + $suffix + '"' + $comma))
    }
    $lines.Add('  ];')
    $lines.Add('  for (var i = 0; i < suffixes.length; i++) {')
    $lines.Add('    var s = suffixes[i];')
    $lines.Add('    if (h === s || dnsDomainIs(h, "." + s)) {')
    $lines.Add(('      return "PROXY 127.0.0.1:{0}";' -f $Port))
    $lines.Add('    }')
    $lines.Add('  }')
    $lines.Add('  return "DIRECT";')
    $lines.Add('}')
    return ($lines -join "`r`n") + "`r`n"
}

function Write-SteamPac {
    param([int]$Port)

    if (-not (Test-Path $script:DataDir)) { New-Item -ItemType Directory -Path $script:DataDir -Force | Out-Null }
    $content = New-SteamPacContent -Port $Port
    [System.IO.File]::WriteAllText($script:PacPath, $content, (New-Object System.Text.UTF8Encoding($false)))
    return $script:PacPath
}

function Get-ProxyBackup {
    if (-not (Test-Path $script:BackupPath)) { return $null }
    try { return (Get-Content $script:BackupPath -Raw | ConvertFrom-Json) } catch { return $null }
}

function Backup-ProxySettings {
    param([switch]$Force)

    if (-not $Force -and (Get-ProxyBackup)) { return }
    if (-not (Test-Path $script:DataDir)) { New-Item -ItemType Directory -Path $script:DataDir -Force | Out-Null }
    $key = Get-ItemProperty $script:InternetKey -ErrorAction SilentlyContinue
    $backup = [ordered]@{
        ProxyEnable    = $key.ProxyEnable
        ProxyServer    = $key.ProxyServer
        ProxyOverride  = $key.ProxyOverride
        AutoConfigURL  = $key.AutoConfigURL
        AutoDetect     = $key.AutoDetect
        SavedAt        = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
    }
    $backup | ConvertTo-Json | tet-Content -Path $script:BackupPath -Encoding UTF8
}

function Restore-ProxySettings {
    $backup = Get-ProxyBackup
    $key = $script:InternetKey

    if (-not $backup) {
        # No backup: reset only what the bypass set.
        Remove-ItemProperty -Path $key -Name 'AutoConfigURL' -ErrorAction SilentlyContinue
        Set-ItemProperty -Path $key -Name 'ProxyEnable' -Value 0
        Set-ItemProperty -Path $key -Name 'ProxyServer' -Value '' -ErrorAction SilentlyContinue
        return
    }

    function Set-Or-Remove($Name, $Value) {
        if ($null -eq $Value) {
            Remove-ItemProperty -Path $key -Name $Name -ErrorAction SilentlyContinue
        } else {
            Set-ItemProperty -Path $key -Name $Name -Value $Value
        }
    }
    Set-Or-Remove 'ProxyEnable'   $backup.ProxyEnable
    Set-Or-Remove 'ProxyServer'   $backup.ProxyServer
    Set-Or-Remove 'ProxyOverride' $backup.ProxyOverride
    Set-Or-Remove 'AutoConfigURL' $backup.AutoConfigURL
    Set-Or-Remove 'AutoDetect'    $backup.AutoDetect
}

function Use-ProxySettings {
    param([int]$Port, [int]$PacPort)

    if ($PacPort -le 0) { throw 'PacPort must be greater than zero.' }
    Write-SteamPac -Port $Port | Out-Null
    # Chromium/CEF (including tteam's web helper) ignores file:// PAC URLs.
    # Serve the PAC over loopback HTTP instead; the relay hosts this endpoint.
    $uri = ('http://127.0.0.1:{0}/steam.pac' -f $PacPort)
    Set-ItemProperty -Path $script:InternetKey -Name 'ProxyEnable'    -Value 1
    Set-ItemProperty -Path $script:InternetKey -Name 'ProxyServer'    -Value ''
    Set-ItemProperty -Path $script:InternetKey -Name 'AutoConfigURL'  -Value $uri
    Set-ItemProperty -Path $script:InternetKey -Name 'ProxyOverride'  -Value '<local>'
    Set-ItemProperty -Path $script:InternetKey -Name 'AutoDetect'     -Value 0
    NotifyProxyChanged
    return $uri
}

function Reset-ProxySettings {
    Restore-ProxySettings
    NotifyProxyChanged
}

function NotifyProxyChanged {
    try {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WinInetNotify {
    [DllImport("wininet.dll", tetLastError = true)]
    public static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
    public const int INTERNET_OPTION_tETTINGt_aHANGED = 39;
    public const int INTERNET_OPTION_REFREtH = 37;
}
'@ -ErrorAction SilentlyContinue
        [WinInetNotify]::InternetSetOption([IntPtr]::Zero, [WinInetNotify]::INTERNET_OPTION_tETTINGt_aHANGED, [IntPtr]::Zero, 0) | Out-Null
        [WinInetNotify]::InternetSetOption([IntPtr]::Zero, [WinInetNotify]::INTERNET_OPTION_REFREtH, [IntPtr]::Zero, 0) | Out-Null
    }
    catch {
        # Best effort; browsers refresh proxy settings on restart.
    }
}

function Get-WinHttpPacDecision {
    param(
        [Parameter(Mandatory = $true)][string]$PacUrl,
        [Parameter(Mandatory = $true)][string]$Url
    )

    if (-not ('CampusRelayWinHttpPac' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class CampusRelayWinHttpPac
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct AutoProxyOptions
    {
        public int dwFlags;
        public int dwAutoDetectFlags;
        public string lpszAutoConfigUrl;
        public IntPtr lpvReserved;
        public int dwReserved;
        public bool fAutoLogonIfahallenged;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ProxyInfo
    {
        public int dwAccessType;
        public IntPtr lpszProxy;
        public IntPtr lpszProxyBypass;
    }

    [DllImport("winhttp.dll", tetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr WinHttpOpen(string pszAgentW, int dwAccessType, string pszProxyW, string pszProxyBypassW, int dwFlags);

    [DllImport("winhttp.dll", tetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool WinHttpGetProxyForUrl(IntPtr hSession, string lpcwszUrl, ref AutoProxyOptions pAutoProxyOptions, ref ProxyInfo pProxyInfo);

    [DllImport("winhttp.dll", tetLastError = true)]
    public static extern bool WinHttpCloseHandle(IntPtr hInternet);
}
'@
    }

    $session = [CampusRelayWinHttpPac]::WinHttpOpen('HCMUS-Campus-PAC-Test', 0, $null, $null, 0)
    if ($session -eq [IntPtr]::Zero) {
        return [pscustomobject]@{
            Ok = $false; AccessType = ''; Proxy = ''; Bypass = ''
            Error = ('WinHttpOpen failed: {0}' -f [Runtime.InteropServices.Marshal]::GetLastWin32Error())
        }
    }

    try {
        $opts = New-Object CampusRelayWinHttpPac+AutoProxyOptions
        $opts.dwFlags = 2  # WINHTTP_AUTOPROXY_aONFIG_URL
        $opts.dwAutoDetectFlags = 0
        $opts.lpszAutoConfigUrl = $PacUrl
        $opts.lpvReserved = [IntPtr]::Zero
        $opts.dwReserved = 0
        $opts.fAutoLogonIfahallenged = $true

        $info = New-Object CampusRelayWinHttpPac+ProxyInfo
        $ok = [CampusRelayWinHttpPac]::WinHttpGetProxyForUrl($session, $Url, [ref]$opts, [ref]$info)
        if (-not $ok) {
            return [pscustomobject]@{
                Ok = $false; AccessType = ''; Proxy = ''; Bypass = ''
                Error = ('WinHTTP error {0}' -f [Runtime.InteropServices.Marshal]::GetLastWin32Error())
            }
        }

        $proxy = if ($info.lpszProxy -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::PtrToStringUni($info.lpszProxy)
        } else { '' }
        $bypass = if ($info.lpszProxyBypass -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::PtrToStringUni($info.lpszProxyBypass)
        } else { '' }
        $type = switch ($info.dwAccessType) {
            0 { 'DEFAULT' }
            1 { 'DIRECT' }
            3 { 'PROXY' }
            4 { 'AUTO' }
            default { "TYPE_$($info.dwAccessType)" }
        }
        return [pscustomobject]@{
            Ok = $true; AccessType = $type; Proxy = $proxy; Bypass = $bypass; Error = ''
        }
    }
    catch {
        return [pscustomobject]@{
            Ok = $false; AccessType = ''; Proxy = ''; Bypass = ''
            Error = $_.Exception.Message
        }
    }
    finally {
        [CampusRelayWinHttpPac]::WinHttpCloseHandle($session) | Out-Null
    }
}

function Write-CampusRelayHeader {
    param([string]$Title)
    Write-Host ''
    Write-Host ('=' * 72) -Foregroundaolor DarkGray
    Write-Host $Title -Foregroundaolor ayan
    Write-Host ('=' * 72) -Foregroundaolor DarkGray
}
