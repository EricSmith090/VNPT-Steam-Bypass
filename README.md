[Tiếng Việt](https://github.com/EricSmith090/VNPT-Steam-Bypass/blob/main/README.vn.MD)

# VNPT-Steam-Bypass

A targeted workaround for the VNPT Steam block documented in
[`VNPT-Steam-Report.md`](docs/VNPT-Steam-Report.md), plus the HCMUS campus
TLS-fragmentation relay documented in
[`scripts\campus\README.md`](scripts/campus/README.md).

One executable — `dist\VNPT-Steam-Bypass.exe` — provides both modes, selectable
inside the GUI:

| Mode | How it works | Use it when |
|---|---|---|
| **VNPT (split DNS)** | Windows NRPT rules plus a small managed hosts block | Steam is blocked by DNS on a VNPT line |
| **Campus (HCMUS relay)** | A localhost TLS-fragmenting relay plus a Steam-only HTTP PAC | Steam TLS is reset by SNI filtering on campus Wi-Fi |

Only one mode runs at a time; switching modes turns the active mode off first.
The relay is the same EXE launched with the hidden `--relay` switch, so no
second binary is produced or distributed. The PowerShell scripts remain
available for command-line use.

## VNPT mode: what it changes

| Layer | Change | Scope |
|---|---|---|
| DNS | Windows NRPT rules send only Steam/Valve domains to `8.8.8.8` / `8.8.4.4` | Steam names only |
| Hosts | A small managed block pins the few names Google still resolves to a filtered international edge | Steam names only |
| Everything else | Untouched | All other DNS and all other traffic |

It does **not** install a VPN, proxy, filter driver, route, or background
service. It does not touch the router, so other devices on the Wi-Fi are
unaffected.

## Why this does not degrade Wi-Fi/Ethernet

- No tunnel or overlay: packets still take the direct VNPT path.
- Non-Steam DNS queries still use the existing resolver (`1.1.1.1` here).
- Non-Steam traffic is not proxied, shaped, or inspected by this tool.
- Store and Community are resolved to Akamai nodes **inside VNPT**, which is
  shorter than the blocked Hong Kong path. The fix should reduce latency for
  Steam, not add to it.
- The only overhead is a DNS lookup for Steam names through Google DNS. That is
  typically tens of milliseconds and applies only to Steam domains; the pinned
  Store/Community names skip DNS entirely.

## Campus mode (HCMUS relay)

- Starts a localhost-only HTTP CONNECT relay as a hidden child of the same EXE.
- Force-splits client→server TLS handshake records into 64-byte records, which
  defeats the campus SNI filter without decrypting anything.
- Sets this user's proxy PAC to `http://127.0.0.1:<port>/steam.pac`; only
  Steam/Valve names use the relay, everything else stays `DIRECT`.
- Refuses non-Steam hostnames with `403`, so it cannot be used as an open proxy.
- Backs up the previous proxy settings and restores them on **Turn OFF**.

See [`scripts\campus\README.md`](scripts/campus/README.md) for the campus-specific
measurements and console-script workflow.

## Files

| File | Purpose |
|---|---|
| `dist\VNPT-Steam-Bypass.exe` | The single built EXE: GUI plus hidden `--relay` mode |
| `Build-Exe.ps1` | Builds the combined EXE and embeds the VNPT PowerShell scripts |
| `src\SteamBypassGui.cs` | GUI application source and mode switching |
| `src\CampusMode.cs` | Campus/HCMUS relay/PAC/proxy lifecycle and verification |
| `src\UiControls.cs` | Dark-theme WinForms controls |
| `src\app.manifest` | Requests administrator rights for the EXE |
| `src\SteamSnIRelay.cs` | Relay source compiled into the EXE as `--relay` |
| `scripts\campus\Enable-CampusRelay.ps1` | Starts the combined EXE's relay mode and applies the PAC |
| `scripts\campus\Disable-CampusRelay.ps1` | Stops the relay-mode child and restores proxy settings |
| `scripts\campus\Test-CampusRelay.ps1` | Console verification for Campus mode |
| `scripts\campus\CampusRelay.Common.ps1` | Console helpers: suffix list, PAC writer, HTTP PAC/WinHTTP checks, proxy backup/restore |
| `scripts\vnpt\Enable-VnptBypass.ps1` | Adds the NRPT rules and managed hosts block; backs up `hosts` first |
| `scripts\vnpt\Disable-VnptBypass.ps1` | Removes the rules/block and restores the previous Steam hosts lines |
| `scripts\vnpt\Test-VnptBypass.ps1` | Verifies DNS, NRPT, hosts, and real HTTPS responses |
| `scripts\vnpt\VnptBypass.Common.ps1` | Shared helpers and domain lists |
| `scripts\vnpt\Test-SteamVnpt.ps1` | Original read-only baseline diagnostic from the report |

## GUI (recommended)

`dist\VNPT-Steam-Bypass.exe` is the single standalone Windows executable. It
embeds the four VNPT PowerShell scripts and the HCMUS relay, so the EXE can be
copied anywhere on this PC and run by itself.

1. Double-click the EXE and accept the UAC prompt.
2. In the **MODE** row, select **VNPT (split DNS)** or
   **Campus (HCMUS relay)**. Switching modes turns the active mode off first.
3. The status card shows **VNPT MODE IS ON/OFF/PARTIAL** or
   **CAMPUS MODE IS ON/OFF/PARTIAL**.
4. Click **Turn ON** / **Turn OFF** / **Repair** to change the selected mode.
5. Click **Test connection** to verify the selected mode.
6. Click **Open data folder** to see `%ProgramData%\VNPT-Steam-Bypass`, where
   the EXE extracts the embedded VNPT scripts and writes logs and hosts
   backups. Campus logs and its proxy backup live in
   `%LOCALAPPDATA%\HCMUS-Campus`.

The GUI requires administrator rights because VNPT mode changes NRPT rules and
the `hosts` file. Campus mode itself only writes this user's proxy settings
and starts a loopback relay. The scripts are re-extracted every time the EXE
starts, so the EXE always uses the scripts that were embedded when it was
built.

To rebuild after changing the C# or PowerShell sources:

```powershell
.\Build-Exe.ps1
```

The build produces exactly one EXE. The relay source in `src\SteamSnIRelay.cs`
is compiled into it; do not build a separate `SteamSnIRelay.exe`.

Requirements: Windows 10/11 with .NET Framework 4.x (included with Windows).
The build uses the .NET Framework compiler (`csc.exe`); no SDK is required.

The EXE is not code-signed. If Windows SmartScreen shows a warning, choose
**More info** → **Run anyway**. Windows will also ask for administrator rights
through UAC because the tool changes system DNS and the `hosts` file.

## Command-line usage

Open **PowerShell as Administrator**, then:

```powershell
Set-Location 'C:\Users\MSI VN\Desktop\VNPT-Steam-Bypass'

# Optional: preview the changes without writing anything
.\scripts\vnpt\Enable-VnptBypass.ps1 -DryRun

# Apply
.\scripts\vnpt\Enable-VnptBypass.ps1

# Verify
.\scripts\vnpt\Test-VnptBypass.ps1
```

For Campus/HCMUS mode from a console (also in an elevated PowerShell):

```powershell
Set-Location 'C:\Users\MSI VN\Desktop\VNPT-Steam-Bypass\scripts\campus'
.\Enable-CampusRelay.ps1
.\Test-CampusRelay.ps1
```

Fully quit Steam, including the tray icon, then start it again. If a browser
still shows the old page, run `ipconfig /flushdns` or restart the browser.

## Rollback

Click **Turn OFF** in the GUI, or run PowerShell as Administrator.

VNPT mode:

```powershell
Set-Location 'C:\Users\MSI VN\Desktop\VNPT-Steam-Bypass'
.\scripts\vnpt\Disable-VnptBypass.ps1
```

Campus/HCMUS mode:

```powershell
Set-Location 'C:\Users\MSI VN\Desktop\VNPT-Steam-Bypass\scripts\campus'
.\Disable-CampusRelay.ps1
```

This removes the NRPT rules, removes the managed hosts block, and restores any
Steam hosts entries that were disabled by the enable script. A timestamped
backup of `hosts` is written before every change: next to the scripts when run
from PowerShell, or in `%ProgramData%\VNPT-Steam-Bypass\scripts\backups` when
run from the GUI.

## How it works

1. **NRPT split DNS.** Windows Name Resolution Policy Table rules are added for
   `steampowered.com`, `steamcommunity.com`, `steamstatic.com`,
   `steamgames.com`, `valvesoftware.com`, and the other Steam/Valve suffixes.
   Only those names are sent to Google DNS. Google DNS returns VNPT-local
   Akamai addresses for Store and Community, avoiding the filtered
   international path.
2. **Hosts pins.** A few names (`store.steampowered.com`, `steamcommunity.com`,
   `steampowered.com`, `www.steampowered.com`, `steamgames.com`) are pinned to
   a verified local Akamai IP. This is needed because Google DNS can still
   return an international edge for those names; connecting to that edge with
   the Steam SNI is what VNPT drops.
3. **Stale entries.** Existing `hosts` lines for Steam names are backed up and
   commented out. The enable script is idempotent and can be re-run to refresh
   the pins when CDN IPs rotate.

## Expected result

On the tested VNPT line (HCMC, 4 November 2026):

| Endpoint | Before | After |
|---|---|---|
| `store.steampowered.com` | TLS timeout to Hong Kong edge | HTTP `200` from local Akamai |
| `steamcommunity.com` | TLS timeout to Hong Kong edge | HTTP `200` from local Akamai |
| `login.steampowered.com` | DNS `NXDOMAIN` on VNPT resolver | HTTP `302` |
| `api.steampowered.com` | DNS `NXDOMAIN` on VNPT resolver | HTTP `404` at `/` (reachable) |
| `cdn.akamai.steamstatic.com` | DNS `NXDOMAIN` on VNPT resolver | HTTP `200` |
| `example.com` (non-Steam control) | normal | normal, same resolver and path |

## Refresh and troubleshooting

- **CDN IPs rotate.** If Steam breaks later, click **Repair bypass** in the GUI
  (or run `.\scripts\vnpt\Enable-VnptBypass.ps1`). It revalidates the candidates and
  rewrites the managed block.
- **Check the network context.** Each enable, disable, and verification run of
  the bypass scripts logs the active interface, Wi-Fi SSID, local IP, gateway,
  and DNS servers; enable/verification also record the public IP/ASN. This makes
  it obvious when the PC is on a different network than the one the bypass was
  built for.
- **Watch for `Steam SNI reset`.** If the candidate table reports that note, or
  `scripts\vnpt\Test-VnptBypass.ps1` ends with the Steam TLS reset verdict, the active
  network is resetting Steam TLS handshakes regardless of the destination IP.
  DNS and hosts changes cannot fix that; use a network that allows Steam TLS
  (such as the VNPT line this tool targets). `-Force` will install rules that
  still fail.
- **Leaving VNPT.** The pins point at VNPT-local Akamai nodes. Click **Turn OFF**
  in the GUI (or run `.\scripts\vnpt\Disable-VnptBypass.ps1`) when you move to another ISP.
  Re-running the enable script on another network is only useful if that network
  allows Steam TLS; the VNPT-specific pins cannot help on an SNI-filtering
  campus or corporate network.
- **Check state:**
  ```powershell
  Get-DnsClientNrptRule | Where-Object Comment -eq 'VNPT-Steam-Bypass' |
      Select-Object Namespace, NameServers
  Select-String -Path "$env:SystemRoot\System32\drivers\etc\hosts" -Pattern 'VNPT-Steam-Bypass'
  ```
- **Steam still fails in the client but works in a browser.** Make sure Steam
  is not configured to use a proxy/VPN, fully quit it from the tray, and restart
  it. Some Steam builds cache DNS for the session.
- **A browser still fails.** Turn off the browser's own Secure DNS / DoH
  (Chrome/Edge: Settings → Privacy → Use secure DNS; Firefox: Network Settings
  → Enable DNS over HTTPS). A browser using its own DoH resolver bypasses the
  Windows resolver, NRPT, and the hosts file.
- **A VPN is active.** Disable the bypass first (click **Turn OFF** in the GUI,
  or run `.\scripts\vnpt\Disable-VnptBypass.ps1`), because NRPT rules can override the VPN's
  DNS for Steam names and cause a DNS leak or a broken tunnel.

## Caveats

- This is a DNS/hosts workaround for the block observed on one VNPT line. It
  does not defeat a future IP-level or protocol-level block.
- The tested line had no working IPv6 route; the pins are IPv4. If your line
  has active IPv6, the client may prefer AAAA records and take a different
  path. Test `scripts\vnpt\Test-VnptBypass.ps1` after any IPv6 change.
- It changes only this Windows PC. It does not modify the router or other
  devices.
- It is not a privacy tool and does not encrypt Steam traffic. Steam DNS names
  are sent to Google DNS instead of your current resolver; other names stay on
  the existing resolver. Store/Community are pinned and skip DNS entirely.
- Use it in line with your local law and Steam's terms. The underlying report
  documents the policy context; this tool only changes local name resolution.

## Baseline

The original read-only diagnostic is still available:

```powershell
.\scripts\vnpt\Test-SteamVnpt.ps1 -TimeoutMs 5000 -OutFile .\result.md
```
