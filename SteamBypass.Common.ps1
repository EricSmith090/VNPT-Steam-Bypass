# Shared helpers for the VNPT Steam bypass scripts.
# This file is dot-sourced by Enable/Disable/Test-SteamBypass.ps1.

$ErrorActionPreference = 'Stop'

$script:SteamBypassMarker     = 'VNPT-Steam-Bypass'
$script:HostsBeginMarker      = '# BEGIN VNPT-Steam-Bypass (managed)'
$script:HostsEndMarker        = '# END VNPT-Steam-Bypass (managed)'
$script:HostsDisabledPrefix   = '# [VNPT-Steam-Bypass disabled] '

# Domains that VNPT resolves falsely or sends down a filtered international path.
# A leading-dot NRPT rule covers all subdomains; the bare rule covers the apex.
$script:SteamSuffixes = @(
    'steampowered.com',
    'steamcommunity.com',
    'steamstatic.com',
    'steamgames.com',
    'valvesoftware.com',
    'steam-chat.com',
    'steam.tv',
    'steamdeck.com',
    'steamcloud.com',
    'steamcontent.com',
    'steamusercontent.com',
    'steamserver.net',
    'steamconnect.com',
    'steamcdn-a.akamaihd.net',
    'steamuserimages-a.akamaihd.net',
    'steamstore-a.akamaihd.net',
    'steamcommunity-a.akamaihd.net',
    'steamstatic-a.akamaihd.net',
    's.team'
)

# Hosts pins are only needed where Google DNS still returns a blocked
# international edge. Values are validated at enable time; the candidates
# are VNPT-local Akamai nodes observed on 2026-11-04.
$script:SteamPinCandidates = [ordered]@{
    'store.steampowered.com' = @('23.53.214.117', '23.53.212.245')
    'steamcommunity.com'     = @('23.53.212.245')
    'steampowered.com'       = @('23.53.212.245')
    'www.steampowered.com'   = @('23.53.212.245')
    'steamgames.com'         = @('23.53.212.245')
}

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-DefaultHostsPath {
    return (Join-Path $env:SystemRoot 'System32\drivers\etc\hosts')
}

function Get-SteamNetworkContext {
    param([switch]$SkipPublicIp)

    $context = [pscustomobject]@{
        Interface = ''
        SSID      = ''
        LocalIP   = ''
        Gateway   = ''
        DNS       = ''
        PublicIP  = ''
        Org       = ''
    }

    # SSID is only exposed by netsh; Get-NetIPConfiguration does not report it.
    try {
        $wifi = & netsh.exe wlan show interfaces 2>$null
        $ssidLine = @($wifi | Where-Object { $_ -match '^\s*SSID\s*:\s*(.+?)\s*$' } | Select-Object -First 1)
        if ($ssidLine.Count -gt 0 -and $ssidLine[0] -match '^\s*SSID\s*:\s*(.+?)\s*$') {
            $context.SSID = $Matches[1]
        }
    }
    catch {
        # Wi-Fi may be disabled, or netsh may be unavailable.
    }

    try {
        $config = Get-NetIPConfiguration -ErrorAction Stop |
            Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } |
            Select-Object -First 1

        if ($config) {
            $context.Interface = [string]$config.InterfaceAlias
            $context.LocalIP = (@($config.IPv4Address.IPAddress) -join ', ')
            $context.Gateway = (@($config.IPv4DefaultGateway.NextHop) -join ', ')
            $context.DNS = (@(
                $config.DNSServer |
                    Where-Object { $_.AddressFamily -eq 2 } |
                    ForEach-Object { $_.ServerAddresses }
            ) -join ', ')
        }
    }
    catch {
        # Keep the context partial rather than failing the operation.
    }

    if (-not $SkipPublicIp) {
        try {
            $info = Invoke-RestMethod -Uri 'https://ipinfo.io/json' -TimeoutSec 3 -ErrorAction Stop
            if ($info.ip) { $context.PublicIP = [string]$info.ip }
            if ($info.org) { $context.Org = [string]$info.org }
        }
        catch {
            # Public IP is best effort; some networks block the lookup.
        }
    }

    return $context
}

function Write-SteamNetworkContext {
    param([switch]$SkipPublicIp)

    $context = Get-SteamNetworkContext -SkipPublicIp:$SkipPublicIp

    Write-Host ''
    Write-Host 'Network context:' -ForegroundColor Cyan
    Write-Host ("  Interface:  {0}" -f $(if ($context.Interface) { $context.Interface } else { 'unknown' }))
    if ($context.SSID) {
        Write-Host ("  SSID:       {0}" -f $context.SSID)
    }
    Write-Host ("  Local IP:   {0}" -f $(if ($context.LocalIP) { $context.LocalIP } else { 'unknown' }))
    Write-Host ("  Gateway:    {0}" -f $(if ($context.Gateway) { $context.Gateway } else { 'unknown' }))
    Write-Host ("  DNS:        {0}" -f $(if ($context.DNS) { $context.DNS } else { 'unknown' }))
    if (-not $SkipPublicIp) {
        if ($context.PublicIP) {
            $public = if ($context.Org) { "$($context.PublicIP)  $($context.Org)" } else { $context.PublicIP }
            Write-Host ("  Public IP:  {0}" -f $public)
        }
        else {
            Write-Host '  Public IP:  unavailable' -ForegroundColor DarkYellow
        }
    }
}

function Test-SteamHostName {
    param([string]$HostName)

    $name = $HostName.Trim().TrimEnd('.').ToLowerInvariant()
    foreach ($suffix in $script:SteamSuffixes) {
        if ($name -eq $suffix -or $name.EndsWith('.' + $suffix)) {
            return $true
        }
    }
    return $false
}

function Resolve-SteamDns {
    param(
        [string]$HostName,
        [string]$Server = '8.8.8.8'
    )

    # Resolve-DnsName on current Windows builds still consults the hosts file
    # even with -NoHostsFile, which would make a refresh trust the old pin.
    # nslookup talks to the specified resolver directly and bypasses hosts.
    #
    # The trailing dot is important: without it nslookup appends the adapter's
    # DNS suffix (for example store.steampowered.com.edu.vn) and validates the
    # wrong name.
    if (-not (Get-Command nslookup.exe -ErrorAction SilentlyContinue)) {
        return @()
    }

    $fqdn = $HostName.Trim().TrimEnd('.') + '.'
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & nslookup.exe -timeout=3 -type=A $fqdn $Server 2>&1
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    $text = ($output | Out-String)
    if ($text -match "can't find|NXDOMAIN|Non-existent domain") {
        return @()
    }

    # Pick every IPv4 literal in the answer, excluding the resolver's own
    # address (nslookup prints "Server: ... / Address: <resolver>" first).
    $ips = [regex]::Matches($text, '\b([0-9]{1,3}(?:\.[0-9]{1,3}){3})\b') |
        ForEach-Object { $_.Groups[1].Value } |
        Where-Object { $_ -ne $Server -and $_ -ne '127.0.0.1' } |
        Select-Object -Unique

    return @($ips)
}

function Get-CurlFailureKind {
    param(
        [int]$ExitCode,
        [string]$Text
    )

    if ($Text -match '(?i)connection was reset|recv failure|reset by peer|ssl.*reset') {
        return 'TLS_RESET'
    }
    if ($ExitCode -eq 28 -or $Text -match '(?i)timed out|timeout') {
        return 'TIMEOUT'
    }
    if ($ExitCode -eq 6) {
        return 'DNS'
    }
    if ($ExitCode -eq 7) {
        return 'CONNECT'
    }
    if ($ExitCode -eq 35) {
        return 'TLS'
    }
    return ("CURL_{0}" -f $ExitCode)
}

function Test-HttpsViaIp {
    param(
        [string]$HostName,
        [string]$IP,
        [int]$TimeoutSec = 8,
        [switch]$Insecure
    )

    if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
        return [pscustomobject]@{
            Ok = $false
            Status = 'curl.exe not found'
            Ms = $null
            Kind = 'NO_CURL'
        }
    }

    # PowerShell 5.1 turns native stderr into a terminating error when
    # $ErrorActionPreference is Stop. curl writes timeouts to stderr, so
    # temporarily relax it and capture both streams.
    $curlArgs = @(
        '-sS', '-o', 'NUL', '-w', '%{http_code} %{time_total}',
        '--noproxy', '*', '--max-time', $TimeoutSec,
        '--resolve', "${HostName}:443:${IP}", "https://${HostName}/"
    )
    if ($Insecure) { $curlArgs = @('-k') + $curlArgs }

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & curl.exe @curlArgs 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    $text = ($output | Out-String).Trim()

    # curl's time_total uses a dot in the C locale, but tolerate a comma.
    $match = [regex]::Match($text, '(\d{3})\s+([0-9]+(?:[.,][0-9]+)?)\s*$')
    if ($exitCode -eq 0 -and $match.Success) {
        $status = [int]$match.Groups[1].Value
        $seconds = [double]($match.Groups[2].Value -replace ',', '.')
        return [pscustomobject]@{
            Ok = ($status -ne 0)
            Status = $status
            Ms = [int]($seconds * 1000)
            Kind = 'OK'
        }
    }

    return [pscustomobject]@{
        Ok = $false
        Status = $text
        Ms = $null
        Kind = (Get-CurlFailureKind -ExitCode $exitCode -Text $text)
    }
}

function Get-BestSteamEndpoint {
    param(
        [string]$HostName,
        [string[]]$Candidates = @(),
        [int]$TimeoutSec = 8
    )

    $ips = New-Object System.Collections.Generic.List[string]
    foreach ($ip in (Resolve-SteamDns -HostName $HostName)) {
        if (-not $ips.Contains($ip)) { $ips.Add($ip) }
    }
    foreach ($ip in $Candidates) {
        if (-not $ips.Contains($ip)) { $ips.Add($ip) }
    }

    $results = foreach ($ip in $ips) {
        $test = Test-HttpsViaIp -HostName $HostName -IP $ip -TimeoutSec $TimeoutSec
        [pscustomobject]@{
            IP = $ip
            Ok = $test.Ok
            Status = $test.Status
            Ms = $test.Ms
            Kind = $test.Kind
        }
    }

    $best = @($results | Where-Object { $_.Ok } |
        Sort-Object -Property Ms | Select-Object -First 1)

    # If every Steam attempt failed, probe a known non-Steam name against the
    # first pin. A working control means the path is up but Steam SNI is being
    # filtered, which is useful in the error message and GUI log.
    $control = $null
    if (-not $best) {
        $controlIp = $null
        if (@($Candidates).Count -gt 0) {
            $controlIp = $Candidates[0]
        }
        elseif ($ips.Count -gt 0) {
            $controlIp = $ips[0]
        }

        if ($controlIp) {
            # The control only needs to prove that a non-Steam TLS handshake
            # reaches the edge; certificate validation is deliberately skipped
            # because the probe IP is not a real example.com edge.
            $control = Test-HttpsViaIp -HostName 'example.com' -IP $controlIp `
                -TimeoutSec $TimeoutSec -Insecure
        }
    }

    return [pscustomobject]@{
        Host = $HostName
        Endpoint = ($best | Select-Object -First 1)
        Best = ($best | Select-Object -First 1)
        Tested = @($results)
        Control = $control
    }
}

function Get-SteamPinPlan {
    param([int]$TimeoutSec = 8)

    $plan = foreach ($hostName in $script:SteamPinCandidates.Keys) {
        $candidates = $script:SteamPinCandidates[$hostName]
        Get-BestSteamEndpoint -HostName $hostName `
            -Candidates $candidates -TimeoutSec $TimeoutSec
    }

    return @($plan | Sort-Object -Property Host)
}

function Get-SteamNrptNamespaces {
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($suffix in $script:SteamSuffixes) {
        if (-not $names.Contains($suffix)) { $names.Add($suffix) }
        $dotted = '.' + $suffix
        if (-not $names.Contains($dotted)) { $names.Add($dotted) }
    }
    return @($names)
}

function Install-SteamNrptRules {
    param([switch]$DryRun)

    $existing = @(Get-DnsClientNrptRule | Where-Object {
        $_.Comment -eq $script:SteamBypassMarker -or
        $_.DisplayName -like 'Steam bypass:*'
    })

    foreach ($rule in $existing) {
        if ($DryRun) {
            Write-Host ("[dry-run] Would remove NRPT rule {0} ({1})" -f $rule.Name, ($rule.Namespace -join ','))
        }
        else {
            Remove-DnsClientNrptRule -Name $rule.Name -Force
        }
    }

    foreach ($ns in (Get-SteamNrptNamespaces)) {
        if ($DryRun) {
            Write-Host ("[dry-run] Would add NRPT {0} -> 8.8.8.8, 8.8.4.4" -f $ns)
            continue
        }

        Add-DnsClientNrptRule -Namespace $ns `
            -NameServers @('8.8.8.8', '8.8.4.4') `
            -Comment $script:SteamBypassMarker `
            -DisplayName ("Steam bypass: {0}" -f $ns) | Out-Null
    }
}

function Remove-SteamNrptRules {
    param([switch]$DryRun)

    $rules = @(Get-DnsClientNrptRule | Where-Object {
        $_.Comment -eq $script:SteamBypassMarker -or
        $_.DisplayName -like 'Steam bypass:*'
    })

    foreach ($rule in $rules) {
        if ($DryRun) {
            Write-Host ("[dry-run] Would remove NRPT rule {0} ({1})" -f $rule.Name, ($rule.Namespace -join ','))
        }
        else {
            Remove-DnsClientNrptRule -Name $rule.Name -Force
        }
    }

    return $rules.Count
}

function Get-UpdatedHostsContent {
    param(
        [string]$Content,
        [object[]]$Pins,
        [string]$Timestamp = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
    )

    $newline = if ($Content -match "`r`n") { "`r`n" } else { "`n" }
    $normalized = $Content -replace "`r`n", "`n"

    # Remove a previous managed block, if any.
    $normalized = [regex]::Replace(
        $normalized,
        '(?ms)^# BEGIN VNPT-Steam-Bypass \(managed\).*?^# END VNPT-Steam-Bypass \(managed\)[^\n]*\n?',
        ''
    )

    $lines = $normalized -split "`n", -1
    $output = New-Object System.Collections.Generic.List[string]

    foreach ($line in $lines) {
        $trimmed = $line.Trim()

        # Leave blank lines and existing comments (including our disabled
        # markers) alone.
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) {
            $output.Add($line)
            continue
        }

        $body = ($line -split '#', 2)[0].Trim()
        $parts = $body -split '\s+'
        if ($parts.Count -lt 2) {
            $output.Add($line)
            continue
        }

        $isSteam = $false
        for ($i = 1; $i -lt $parts.Count; $i++) {
            if (Test-SteamHostName $parts[$i]) {
                $isSteam = $true
                break
            }
        }

        if ($isSteam) {
            $output.Add($script:HostsDisabledPrefix + $line)
        }
        else {
            $output.Add($line)
        }
    }

    $result = ($output -join "`n").TrimEnd("`n")

    $block = New-Object System.Collections.Generic.List[string]
    $block.Add('')
    $block.Add($script:HostsBeginMarker)
    $block.Add("# Generated: $Timestamp")
    $pinCount = 0
    foreach ($pin in $Pins) {
        if ($pin.Endpoint -and $pin.Endpoint.IP) {
            $block.Add(("{0} {1}  # verified local Akamai" -f $pin.Endpoint.IP, $pin.Host))
            $pinCount++
        }
    }
    if ($pinCount -eq 0) {
        $block.Add('# No verified pins; DNS-only mode.')
    }
    $block.Add($script:HostsEndMarker)

    $result = $result + "`n" + ($block -join "`n") + "`n"

    if ($newline -eq "`r`n") {
        $result = $result -replace "`n", "`r`n"
    }

    return $result
}

function Get-HostsContentWithoutBlock {
    param([string]$Content)

    $newline = if ($Content -match "`r`n") { "`r`n" } else { "`n" }
    $normalized = $Content -replace "`r`n", "`n"

    $normalized = [regex]::Replace(
        $normalized,
        '(?ms)^# BEGIN VNPT-Steam-Bypass \(managed\).*?^# END VNPT-Steam-Bypass \(managed\)[^\n]*\n?',
        ''
    )

    $lines = $normalized -split "`n", -1
    $output = foreach ($line in $lines) {
        if ($line.StartsWith($script:HostsDisabledPrefix)) {
            $line.Substring($script:HostsDisabledPrefix.Length)
        }
        else {
            $line
        }
    }

    $result = (($output -join "`n").TrimEnd("`n")) + "`n"
    if ($newline -eq "`r`n") {
        $result = $result -replace "`n", "`r`n"
    }

    return $result
}

function Set-SteamHostsFile {
    param(
        [string]$Path,
        [string]$Content,
        [string]$BackupDirectory
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Hosts file not found: $Path"
    }

    if (-not (Test-Path -LiteralPath $BackupDirectory)) {
        New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backup = Join-Path $BackupDirectory ("hosts.{0}.bak" -f $stamp)
    Copy-Item -LiteralPath $Path -Destination $backup -Force

    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.Encoding]::ASCII)

    return $backup
}

function Clear-SteamDnsCache {
    param([switch]$DryRun)

    if ($DryRun) {
        Write-Host '[dry-run] Would run Clear-DnsClientCache'
    }
    else {
        Clear-DnsClientCache
    }
}

function Write-SteamBypassHeader {
    param([string]$Title)

    Write-Host ''
    Write-Host ('=' * 72) -ForegroundColor DarkGray
    Write-Host $Title -ForegroundColor Cyan
    Write-Host ('=' * 72) -ForegroundColor DarkGray
}
