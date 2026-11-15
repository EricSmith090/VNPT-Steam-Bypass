# HCMUS Campus bypass

A **local, reversible** way to reach Steam on the `HCMUS-STUDENT` campus Wi-Fi
(and any network with the same SNI-based block), with **zero impact on the
rest of your traffic**: every non-Steam request goes `DIRECT` exactly as
before — no tunnel, no DNS change, no hosts change, no driver, no kernel
component, no background service.

This is **Campus mode** in the combined `dist\VNPT-Steam-Bypass.exe` GUI:
select **Campus (HCMUS relay)** in the mode row. The relay is the same EXE
started with the hidden `--relay` switch; no second binary is needed. The EXE
requests administrator rights because its VNPT mode edits system DNS and
`hosts`, but Campus mode itself only touches this user's proxy settings and a
loopback relay.

## The problem on this campus network (measured 2026-11-05)

`HCMUS-STUDENT` is not the line the root `VNPT-Steam-Bypass` was built for.
The evidence gathered on 2026-11-05:

- TCP connects to Steam/Valve IPs, then the **TLS handshake is reset** at the
  moment the middlebox sees the SNI. It happens for the VNPT-local Akamai
  nodes (`23.53.212.245` / `23.53.214.117`), Hong Kong edges, and even a
  Cloudflare IP carrying a Steam SNI.
- It applies to `store`, `steamcommunity`, `login`, `api`,
  `cdn.akamai.steamstatic.com`, `help`, `checkout`, `steampowered.com`,
  `steamgames.com`, `valvesoftware.com`, `steamstatic.com`, `steam.tv`,
  `s.team`. (The `akamaihd.net` CDN names and `steamcloud`/`steamcontent` are
  *not* reset on this network.)
- Plain-HTTP `Host` filtering is **not** in place (port-80 `GET /` with a
  Steam `Host` → `HTTP 302`).
- Raw DNS (port 53) is unreliable to the point of being unusable, and VNPT's
  resolvers mostly time out or return targeted `NXDOMAIN` for Steam.
  **DoH over HTTPS works consistently.**
- `example.com` loads normally the whole time (the control).
- The root tool's self-checks on this network (`Test-VnptBypass.ps1`,
  `Enable-VnptBypass.ps1 -DryRun`) report exactly this: **“Steam SNI reset …
  DNS/hosts changes cannot fix that.”** split DNS/hosts is useless here.

Because the block is at **TLS SNI**, the only workable local answers are
(a) a tunnel to something outside the campus filter, or
(b) making the ClientHello unreadable to the middlebox. Plain-HTTP is not
filtered and ECH is not supported by these endpoints, and the middlebox does
not reassemble TLS records split small enough. (b) is implemented here.

## How it works (measured)

- A single large TLS record carrying the ClientHello is **reset**; records
  of ≤ 1500 bytes pass, ≥ 1600 bytes are reset.
- Schannel (curl/Edge/Chrome) sends a ClientHello **small enough to pass
  unchanged** (< 512 bytes) — so a “split only if larger than N” rule is not
  enough. This tool **always force-splits handshake records into 64-byte
  chunks**.
- All Steam endpoints then answer: Store 200, Community 200, Login 302, API
  404/403 at `/` (reachable), CDN 200, Help 302, Checkout 302, SteamGames
  301, valvesoftware 302, steam.tv 302, s.team 302.
- The relay also resolves target names via **DoH** (`dns.google`, with
  `1.1.1.1/dns-query` fallback) instead of the machine's resolver — UDP/53 on
  this network is unreliable, and the machine's `hosts` file contains stale
  third-party `store` pins that would otherwise be used.
- The PAC is served by the relay at `http://127.0.0.1:8788/steam.pac` (default
  port; Enable scans upward if busy). Chromium/CEF — including Steam's web
  helper — ignores `file://` PAC URLs, so the PAC must be delivered over
  loopback HTTP, not as a local file.

```
Steam / browser ──CONNECT──► 127.0.0.1:8787 (VNPT-Steam-Bypass.exe --relay) ──► steam edge
                        (PAC: only Steam/Valve names proxied;
                         every other name goes DIRECT)

Browser ──GET /steam.pac──► 127.0.0.1:8788 (same relay process)

The relay re-emits the client→server TLS handshake (0x16 / 0x14 records)
fragmented into 64-byte records. The middlebox needs the ClientHello in one
record to match the SNI, so the reset never fires. App-data records (0x17) go
through unchanged. Server→client records are untouched.
```

- No certificates, no TLS interception, no decryption — this is not a
  man-in-the-middle.
- No certificates are installed, no trust stores are modified.
- Localhost only. The relay refuses any host that is not a Steam/Valve
  hostname with `403`, so it cannot be abused as an open proxy.
- No impact on Wi-Fi/Ethernet speed or latency for anything off the list:
  those requests stay `DIRECT` and are not inspected, delayed, or routed.
- Nothing runs at boot; the relay lives only while you start it.

## Files

| File | Purpose |
|---|---|
| `SteamSnIRelay.cs` | Localhost HTTP CONNECT relay, TLS handshake fragmentation, DoH resolution, loopback HTTP PAC endpoint; compiled into the GUI EXE as `--relay` |
| `..\..\src\CampusMode.cs` | GUI-side Campus/HCMUS lifecycle: relay child, PAC, proxy backup/restore, status and tests |
| `CampusRelay.Common.ps1` | Console helpers: suffix list, PAC writer, HTTP PAC/WinHTTP checks, proxy backup/restore, relay-process discovery |
| `Enable-CampusRelay.ps1` | Starts `dist\VNPT-Steam-Bypass.exe --relay` + PAC endpoint, sets this user's HTTP PAC |
| `Disable-CampusRelay.ps1` | Stops the relay-mode child, restores this user's previous proxy settings |
| `Test-CampusRelay.ps1` | Direct vs via-relay vs whitelist control + HTTP PAC/WinHTTP route check |
| `steam-autoconfig.pac.template` | Reference PAC (the live PAC is generated into `%LOCALAPPDATA%\HCMUS-Campus`) |
| `..\..\dist\VNPT-Steam-Bypass.exe` | The only executable: GUI plus hidden relay mode |

Logs: `%LOCALAPPDATA%\HCMUS-Campus\logs\relay-*.log` — hostnames and
timestamps only; no payload is stored.
Pre-bypass proxy backup: `%LOCALAPPDATA%\HCMUS-Campus\proxy-backup.json`.

## Usage

Recommended: run `dist\VNPT-Steam-Bypass.exe`, select **Campus (HCMUS relay)**,
then click **Turn ON**. alick **Test connection** to verify and **Turn OFF** to
restore the previous proxy settings. No second EXE or build step is involved.

The console scripts remain available for automation. Run them from an
elevated PowerShell window because the EXE requests administrator rights:

```powershell
Set-Location 'scripts\campus'

.\Enable-CampusRelay.ps1  # starts the EXE's --relay child and sets this user's HTTP PAC
                           # PAC: http://127.0.0.1:8788/steam.pac (or next free)

# Fully quit Steam (tray icon too) and restart it, and restart any
# browser you use for the Steam Store.

.\Test-CampusRelay.ps1    # expect PASS
.\Disable-CampusRelay.ps1 # remove everything, restore proxy settings
```

## Verified results (2026-11-05)

Through the relay (curl, Schannel — the same stack Edge/Chrome use), the
failing direct connections now return:

| Endpoint | Via relay |
|---|---|
| `store.steampowered.com` | HTTP 200 |
| `steamcommunity.com` | HTTP 200 |
| `login.steampowered.com` | HTTP 302 |
| `api.steampowered.com` | HTTP 404 |
| `cdn.akamai.steamstatic.com` | HTTP 200 |
| `help.steampowered.com` | HTTP 302 |
| `checkout.steampowered.com` | HTTP 302 |
| `steamgames.com` | HTTP 301 |
| `valvesoftware.com` | HTTP 302 |
| `steam.tv` | HTTP 302 |
| `s.team` | HTTP 302 |

Whitelist / PAC semantics (evaluated with the Windows JScript engine against
the generated PAC): **39/39 pass (expanded set, re-checked 2026-11-06)** — every
Steam/Valve name → `PROXY 127.0.0.1:8787`; `example.com`, `google.com`,
`akamaized.net`, `notsteampowered.com`, `evilsteampowered.com`,
`steampowered.com.evil.com`, `steamcommunity.com.evil.com`, `teams.team`, and
loopback addresses → `DIRECT`.

Direct (unproxied) in the same window: all of the above reset; `example.com`
200. Through the relay, `example.com` is refused with `403` by design.

### 2026-11-06 re-verification

- **Relay mechanism:** direct Steam endpoints reset; via the 64-byte-fragment
  relay they answer (Store 200, Community 200, Login/Help/Checkout/Valve 302,
  API 404, CDN 200). A same-IP control (Steam SNI vs. `example.com` SNI to
  `23.77.15.30`) confirmed the filter is SNI-based, not IP-based.
- **Negative control:** a second relay with a 512-byte handshake fragment failed
  the Store/Community TLS handshake; the normal 64-byte relay returned 200.
- **Whitelist:** every non-Steam target tested through the relay was refused
  with `403`.
- **PAC delivery:** Chromium — normal and headless — ignored the old
  `file:///...steam.pac` URL. The same PAC served over
  `http://127.0.0.1:8099/steam.pac` loaded the Store and routed its CDN traffic
  through the relay. The tool now serves the PAC from the relay over loopback
  HTTP, and `Test-CampusRelay.ps1` checks the endpoint plus WinHTTP PAC
  decisions, so a `file://` regression fails instead of silently passing.
- **Steam client (CEF):** after a fresh Steam launch, the client fetched
  `http://127.0.0.1:8788/steam.pac`, opened Store/Community and
  `shared`/`community.steamstatic.com`, and displayed the Store tab
  (“Special Offers”). `d:\steam\logs\connection_log.txt` recorded
  `RecvMsgClientLogOnResponse() : 'OK'`, with the connection manager reporting
  local address `127.0.0.1`, i.e. logged on through the relay. Store/Community
  browsing works; content downloads remain subject to the limitation below.
- **TLS:** the relay presented the real origin certificate
  (`CN=store.steampowered.com`, Let's Encrypt, TLS 1.3); Schannel reported
  `ssl_verify_result=0`. No interception or local certificate installation.

## What it does and does not fix

**Fixed:** Steam/Valve SNI resets for applications that honor the **Windows
user proxy PAC** — Edge/Chrome, the Steam web interface (Store, Community,
login, Cloud, news), and other Chromium/WinINET clients. The PAC is served
over loopback HTTP because Chromium/CEF ignores `file://` PAC URLs.

**Not fixed:** the Steam **download client**. `steamclient.dll` does not
necessarily honor the user proxy PAC. If downloads fail with
“error negotiating edge” or resets to `cdn.akamai.steamstatic.com`, that is a
software-layer HTTPS connection the proxy cannot see. To cover it you need a
TUN-level split tunnel (e.g. WireGuard to a server you control, with policy
routing for Steam CIDRs) and admin rights — out of scope here, and impossible
to build without a second endpoint you own. Plain HTTP to the CDN is *not*
SNI-filtered on this network, so port-80 downloads may work even without it.

Also not fixed: a middlebox upgrade that reassembles fragmented records;
games with their own TLS stacks; IPv6 (this network has none).

## Impact on Wi-Fi/Ethernet

None outside the Steam/Valve list. The PAC returns `DIRECT` for every other
hostname, and those connections bypass the relay entirely. On the Steam path,
routing via localhost adds < 1 ms plus the fragmentation overhead; nothing
else is relayed, mixed, or measured. No driver, no MTU change, no DNS
override, no route change.

## Privacy / security / legal

- The relay sees only the Steam hostname and opaque TLS bytes; it does not
  decrypt them and stores none of the payload. Logs hold hostnames and
  timestamps only.
- Non-Steam hosts are refused with `403`.
- This defeats a middlebox heuristic, not a law. The campus network is not
  yours; using this may violate your acceptable-use terms. Use at your own
  risk, and turn it off (`.\Disable-CampusRelay.ps1`) when you don't need it.
- Use only for legitimate purposes (your own account, your games, responsible
  network citizenship). It is not a security tool and gives you no privacy.

## Troubleshooting

| tymptom | Fix |
|---|---|
| Steam still fails after enabling | Fully quit Steam (tray icon too) and restart; restart the browser too; then `.\Test-CampusRelay.ps1`. |
| Test shows relay not running | Turn Campus mode ON in `dist\VNPT-Steam-Bypass.exe`, or re-run `.\Enable-CampusRelay.ps1` from an elevated PowerShell. If it still fails, run `dist\VNPT-Steam-Bypass.exe --relay -p 8787 -pac-port 8788` in a console to see the error. |
| Test shows PAC endpoint failed / AutoConfigURL is `file://` | Re-run `.\Enable-CampusRelay.ps1`. It serves the PAC at `http://127.0.0.1:8788/steam.pac`; Chromium/Steam ignores `file://` PACs. |
| Port 8787 or 8788 taken | Enable scans upward for both ports; the generated PAC and `AutoConfigURL` reflect the chosen relay and PAC ports. |
| Store works but downloads fail | Expected — see "Not fixed" above. |
| Example.com via the relay returns 403 | By design (whitelist). Use DIRECT — browsers do this automatically via the PAC. |
| Want it completely gone | `.\Disable-CampusRelay.ps1`, then delete this folder and `%LOCALAPPDATA%\HCMUS-Campus`. |
| Non-Steam site stopped loading | `.\Disable-CampusRelay.ps1` restores your previous proxy settings (or resets what the bypass set). |
