<#
.SYNOPSIS
  Read-only Steam connectivity check for a VNPT (AS45899) connection.

.DESCRIPTION
  Checks:
    1. Public IP / ASN.
    2. DNS answers for Steam domains from the system resolver, 1.1.1.1,
       8.8.8.8 and VNPT's HCMC resolver (203.162.4.190).
    3. TCP + TLS handshake to the IPs returned by 1.1.1.1 and 8.8.8.8,
       using the real Steam SNI and an example.com control SNI.

  It does not change DNS, hosts, firewall or proxy settings.

.PARAMETER TimeoutMs
  Per TCP/TLS operation timeout. Default 6000 ms.

.PARAMETER OutFile
  Optional path for a Markdown copy of the result.

.EXAMPLE
  .\Test-SteamVnpt.ps1
  .\Test-SteamVnpt.ps1 -TimeoutMs 4000 -OutFile .\result.md
#>
[CmdletBinding()]
param(
    [int]$TimeoutMs = 6000,
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'

# A managed callback avoids the "no Runspace" error that a PowerShell
# scriptblock callback hits when the TLS handshake runs on a worker thread.
if (-not ('TrustAllCert' -as [type])) {
    Add-Type -TypeDefinition @"
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
public static class TrustAllCert {
    public static readonly RemoteCertificateValidationCallback Callback = Validate;
    public static bool Validate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) { return true; }
}
"@
}

function Get-DnsResult {
    param(
        [string]$Name,
        [string]$Server
    )

    $params = @{
        Name         = $Name
        Type         = 'A'
        DnsOnly      = $true
        QuickTimeout = $true
        ErrorAction  = 'Stop'
    }
    if ($Server) { $params['Server'] = $Server }

    try {
        $records = Resolve-DnsName @params
        $ips = @($records | Where-Object { $_.Type -eq 'A' } | ForEach-Object { $_.IPAddress })
        if ($ips.Count -gt 0) {
            return [pscustomobject]@{ Status = ($ips -join ', '); IPs = $ips }
        }
        return [pscustomobject]@{ Status = 'NO_A'; IPs = @() }
    }
    catch {
        $message = $_.Exception.Message
        if ($message -match 'does not exist|Non-existent') {
            return [pscustomobject]@{ Status = 'NXDOMAIN'; IPs = @() }
        }
        if ($message -match 'timed out|timeout') {
            return [pscustomobject]@{ Status = 'TIMEOUT'; IPs = @() }
        }
        return [pscustomobject]@{ Status = "ERROR: $message"; IPs = @() }
    }
}

function Test-Tls {
    param(
        [string]$IP,
        [string]$SNI,
        [int]$TimeoutMs = 6000
    )

    $tcp = New-Object System.Net.Sockets.TcpClient
    $ssl = $null
    $timer = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $connect = $tcp.ConnectAsync($IP, 443)
        if (-not $connect.Wait($TimeoutMs)) {
            return [pscustomobject]@{
                Result = 'TCP_TIMEOUT'; Protocol = ''; Ms = $timer.ElapsedMilliseconds
            }
        }

        $ssl = New-Object System.Net.Security.SslStream(
            $tcp.GetStream(),
            $false,
            ([System.Net.Security.RemoteCertificateValidationCallback][TrustAllCert]::Callback)
        )
        $ssl.ReadTimeout = $TimeoutMs
        $ssl.WriteTimeout = $TimeoutMs

        $auth = $ssl.AuthenticateAsClientAsync($SNI)
        if (-not $auth.Wait($TimeoutMs)) {
            return [pscustomobject]@{
                Result = 'TLS_TIMEOUT'; Protocol = ''; Ms = $timer.ElapsedMilliseconds
            }
        }

        return [pscustomobject]@{
            Result = 'TLS_OK'; Protocol = [string]$ssl.SslProtocol; Ms = $timer.ElapsedMilliseconds
        }
    }
    catch {
        return [pscustomobject]@{
            Result = 'ERROR'; Protocol = ''; Ms = $timer.ElapsedMilliseconds
        }
    }
    finally {
        if ($ssl) { try { $ssl.Dispose() } catch { } }
        try { $tcp.Close() } catch { }
    }
}

$lines = New-Object System.Collections.Generic.List[string]
$now = Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'

$lines.Add('# Steam / VNPT connectivity check')
$lines.Add('')
$lines.Add("- Run at: $now")
$lines.Add("- Per-operation timeout: $TimeoutMs ms")
$lines.Add('')

try {
    $ipinfo = Invoke-RestMethod -Uri 'https://ipinfo.io/json' -TimeoutSec 10
    $lines.Add("- Public IP: $($ipinfo.ip)")
    $lines.Add("- Network: $($ipinfo.org)")
    $lines.Add("- Location: $($ipinfo.city), $($ipinfo.region), $($ipinfo.country)")
}
catch {
    $lines.Add('- Public IP: lookup failed')
}
$lines.Add('')

$domains = @(
    'store.steampowered.com',
    'steamcommunity.com',
    'login.steampowered.com',
    'api.steampowered.com',
    'cdn.akamai.steamstatic.com'
)
$servers = @(
    @{ Label = 'system'; Server = $null },
    @{ Label = '1.1.1.1'; Server = '1.1.1.1' },
    @{ Label = '8.8.8.8'; Server = '8.8.8.8' },
    @{ Label = 'VNPT 203.162.4.190'; Server = '203.162.4.190' }
)

$lines.Add('## DNS')
$lines.Add('')
$lines.Add('| Domain | system | 1.1.1.1 | 8.8.8.8 | VNPT resolver |')
$lines.Add('|---|---|---|---|---|')

foreach ($domain in $domains) {
    $cells = foreach ($server in $servers) {
        $result = Get-DnsResult -Name $domain -Server $server.Server
        $result.Status
    }
    $lines.Add("| $domain | $($cells -join ' | ') |")
}
$lines.Add('')

$lines.Add('## TLS by IP and SNI')
$lines.Add('')
$lines.Add('| Domain | DNS used | IP | SNI | Result | Protocol | ms |')
$lines.Add('|---|---|---|---|---|---|---|')

$pairs = @(
    @{ Domain = 'store.steampowered.com'; Dns = '1.1.1.1' },
    @{ Domain = 'store.steampowered.com'; Dns = '8.8.8.8' },
    @{ Domain = 'steamcommunity.com'; Dns = '1.1.1.1' },
    @{ Domain = 'steamcommunity.com'; Dns = '8.8.8.8' }
)

foreach ($pair in $pairs) {
    $dns = Get-DnsResult -Name $pair.Domain -Server $pair.Dns
    $ip = @($dns.IPs) | Select-Object -First 1
    if (-not $ip) {
        $lines.Add("| $($pair.Domain) | $($pair.Dns) | - | $($pair.Domain) | NO_IP | - | - |")
        continue
    }

    $test = Test-Tls -IP $ip -SNI $pair.Domain -TimeoutMs $TimeoutMs
    $lines.Add("| $($pair.Domain) | $($pair.Dns) | $ip | $($pair.Domain) | $($test.Result) | $($test.Protocol) | $($test.Ms) |")

    if ($test.Result -ne 'TLS_OK') {
        $control = Test-Tls -IP $ip -SNI 'example.com' -TimeoutMs $TimeoutMs
        $lines.Add("| $($pair.Domain) | $($pair.Dns) | $ip | example.com (control) | $($control.Result) | $($control.Protocol) | $($control.Ms) |")
    }
}

$lines.Add('')
$text = $lines -join [Environment]::NewLine
$text | Write-Output

if ($OutFile) {
    Set-Content -Path $OutFile -Value $text -Encoding UTF8
}
