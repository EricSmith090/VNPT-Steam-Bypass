# Network verification — HCMUS-STUDENT, 5 November 2026

> **Repo note (2026-11-15):** the project was reorganized in a single commit.
> The VNPT PowerShell scripts now live in `scripts\vnpt\` and were renamed
> (`SteamBypass.Common.ps1` -> `VnptBypass.Common.ps1`,
> `Enable-SteamBypass.ps1` -> `Enable-VnptBypass.ps1`,
> `Disable-SteamBypass.ps1` -> `Disable-VnptBypass.ps1`,
> `Test-SteamBypass.ps1` -> `Test-VnptBypass.ps1`).
> The campus toolkit moved from `HCMUSCampus\` to `scripts\campus\` and was
> renamed (`SteamUnblock.Common.ps1` -> `CampusRelay.Common.ps1`,
> `Enable-SteamUnblock.ps1` -> `Enable-CampusRelay.ps1`,
> `Disable-SteamUnblock.ps1` -> `Disable-CampusRelay.ps1`,
> `Test-SteamUnblock.ps1` -> `Test-CampusRelay.ps1`).
> `SteamSnIRelay.cs` and `ViettelMode.cs` (renamed `CampusMode.cs`) were
> moved into `src\`, verification reports into `docs\`. File names cited
> below reflect the layout at the time of writing; names above are current.

**Updated 2026-11-06:** The campus-filter findings in this report still stand.
The `HCMUSCampus` section was re-verified and the tool was fixed: the PAC is now
served over loopback HTTP (Chromium/Steam ignores `file://` PACs), the relay is
running, and the full test passes. See §3.4 and §13. The 5 November state
described below is retained as the historical observation that led to the fix.

The relay is now the hidden `--relay` mode of the combined
`dist\VNPT-Steam-Bypass.exe`; the standalone `SteamSnIRelay.exe` and its build
script were retired, so one EXE provides both VNPT and Viettel/HCMUS modes.

**Status:** Verification report for the access network as measured on 5 November
2026. The 5 November pass made no changes to DNS, NRPT, hosts, proxy, firewall,
router, or Steam configuration. A separate `HCMUSCampus` tool had already enabled
a user proxy PAC during that window; its state was observed and left untouched.

**Verdict:** The current network is **not** the direct VNPT access line from the
4 November report. It is the **HCMUS-STUDENT campus Wi-Fi**, which applies an
explicit content filter at `192.168.94.35` before traffic reaches the ISP. The
filter blocks a targeted set of Steam/Valve hostnames at **both HTTP `Host` and
TLS SNI**, and it does so even when the destination IP is a working VNPT-local
Akamai node. It is **not** an IP-level block and it is **not** a DNS-only block.
For that reason, the VNPT split-DNS + hosts bypass in this repository **cannot
work on this network**; the project's own verification and enable scripts
correctly detect the SNI reset and refuse to make changes.

A **separate** `HCMUSCampus` PAC/relay workaround is also configured. On
5 November its PAC was a `file://` URL and the relay was **not listening**, so
Steam traffic pointed at a closed port. That state was fixed and independently
re-verified on 6 November: the relay runs on `127.0.0.1:8787`, the PAC is served
at `http://127.0.0.1:8788/steam.pac`, and both the relay and PAC checks pass —
see §3.4 and §13.

---

## 1. Test conditions

| Item | Value |
|---|---|
| Date/time | 2026-11-05, 09:27–13:23 ICT (UTC+7) |
| Location | Ho Chi Minh City, Vietnam (ipinfo: District 5) |
| Access network | Wi-Fi SSID **HCMUS-STUDENT**, 802.11ax, **5 GHz** (channel 108), WPA3-Personal |
| Adapter | Intel Wi-Fi 6E AX211 160 MHz |
| Local IPv4 | `10.120.5.106/16` early in the window; `10.124.1.196/16` later |
| Gateway | `10.120.0.1` early; `10.124.0.1` later |
| Configured DNS | `1.1.1.1`, `1.0.0.1` |
| Public egress | Observed on both **AS45899 VNPT** and **AS7552 Viettel** — see §3 |
| OS | Windows 11 Home Single Language 10.0.26200 build 26200 |
| PowerShell | 5.1.26100.9711 |
| Privilege | Standard user; no administrator rights |
| Test type | Read-only DNS, TCP, TLS, HTTP, ICMP, traceroute and local state checks |
| VNPT bypass state | **OFF** — 0 NRPT rules, no managed hosts block |
| Other proxy state | On 5 Nov: HCMUSCampus PAC enabled (`steam.pac`); relay not listening at final check — §3.4. Fixed 6 Nov (§13) |
| Unrelated hosts entries | 7 old `store.steampowered.com` pins from a `# Fix Steam Host` block |

The client roamed between two campus subnets during the test window. The SSID
and the Steam block behaviour were consistent; the final traceroutes show the
same hop-2 filter (`192.168.94.35`).

---

## 2. Executive findings

1. **The block is at the campus edge.** All four traceroutes to Steam and
   non-Steam destinations go `10.124.0.1` → `192.168.94.35` → first public VNPT
   hop. The private filter/portal is hop 2, before the ISP.
2. **HTTP is redirected to a campus block page.** A request with
   `Host: store.steampowered.com` returns `302` to
   `http://192.168.94.35:8183/redirect.cgi?u=store.steampowered.com&c=games`.
   The page title is **“Blocked Site”**, the text says the domain is blocked
   “due to content filtering”, and its script contains `var cat = "games";`.
3. **HTTPS is reset by SNI.** Every core Steam/Valve hostname tested resets the
   TLS handshake, even when connecting to a VNPT-local Akamai IP that serves
   Store/Community. A non-Steam SNI to the same IP succeeds; IP-literal HTTPS
   succeeds; a Steam SNI to a Cloudflare IP still resets.
4. **The filter is targeted, not a blanket Valve block.** Core names such as
   `store.steampowered.com`, `steamcommunity.com`, `login`, `api`, `cdn`, `help`,
   `checkout`, `steampowered.com`, `steamgames.com`, `valvesoftware.com`,
   `steamstatic.com`, `steam.tv` and `s.team` are blocked. Auxiliary names such
   as `steamdeck.com`, `steamcloud.com`, `steamcontent.com`,
   `steamserver.net` and several `*-a.akamaihd.net` names completed TLS; they
   are not consistently blocked.
5. **DNS is not the cause here.** `1.1.1.1` and `8.8.8.8` resolve all core Steam
   names. VNPT's `203.162.4.190/191` resolvers are reachable but erratic: .190
   mixed `NXDOMAIN` with control timeouts; .191 later resolved controls and
   returned `NXDOMAIN` for Store/Community/Login. That is a targeted DNS signal,
   but it is not what breaks Steam at the TLS layer. The current failure is fully
   explained by the hop-2 HTTP/SNI filter.
6. **The project's guardrail works.** `Test-SteamBypass.ps1` returns
   `FAIL: Steam TLS is being reset while the non-Steam control works`. The
   `Enable-SteamBypass.ps1 -DryRun` reports `Steam SNI reset` for every critical
   host and exits with “No changes were made.”
7. **The old hosts pins are not helping.** Four of the seven old
   `store.steampowered.com` pins are dead at TCP level. The three live ones reset
   Steam/Community SNI. They are unrelated to this tool and can slow or fail
   Store resolution.
8. **On 5 November, a separate HCMUSCampus PAC was active but its relay was
   down.** The PAC routed Steam/Valve hostnames to `127.0.0.1:8787`; port 8787
   was closed at the final check. This is not the VNPT tool. The HCMUSCampus
   README claims its relay works when running, but the 5 November verification
   did not start it; the PAC alone pointed PAC-aware Steam traffic at a closed
   port. This was fixed and re-verified on 6 November: HTTP PAC on port 8788,
   relay on 8787, full test passing — see §13.

---

## 3. Network identity and state

### 3.1 Public egress is multi-homed

Public IP observations during the window:

| Time (ICT) | Public IP | ASN / organisation |
|---|---|---|
| 09:27 | `14.161.23.125` | AS45899 VNPT Corp |
| 13:20 | `14.241.254.209` | AS45899 VNPT Corp |
| 13:21 (project test) | `27.74.254.72` | AS7552 Viettel Group |
| 13:21 (dry-run) | `14.161.23.204` | AS45899 VNPT Corp |
| 13:22 (5 quick checks) | `14.161.23.204` | AS45899 VNPT Corp |
| 13:22:51 | `115.73.217.121` | AS7552 Viettel Group |

The campus path is therefore multi-homed or load-balanced between VNPT and
Viettel. **ASN is not a stable attribution point for this network.** The
hop-2 filter is independent of which upstream is used.

### 3.2 Local state

| Item | Result |
|---|---|
| NRPT rules (`Get-DnsClientNrptRule`) | 0 |
| NRPT registry subkeys (`DnsPolicyConfig`) | 0 |
| Managed `hosts` block (`VNPT-Steam-Bypass`) | absent |
| WinHTTP proxy | direct, no proxy |
| WinINET proxy | `ProxyEnable = 1`; `AutoConfigURL` points to `HCMUS-Campus\steam.pac`; `ProxyServer` empty |
| HCMUSCampus relay | At the 5 Nov final check: **not listening** on `127.0.0.1:8787`; last log 10:49:43. Fixed 6 Nov (§13) |
| VPN/WARP/TAP/TUN adapters | none |
| VirtualBox host-only adapter | present, up, no default gateway; not the active path |
| IPv6 global address / default route | none |
| Steam AAAA records | none for Store/Community; `example.com` has AAAA but no IPv6 route |
| Administrator | false |

The VNPT tool is **off**. On 5 November the separate HCMUSCampus PAC/relay was
**configured** but its relay was not running (§3.4). It was fixed and enabled on
6 November (§13). The old `# Fix Steam Host` block is a separate, pre-existing
modification.

### 3.3 Old hosts pins

The hosts file contains seven `store.steampowered.com` addresses:

`23.40.36.77`, `184.51.97.157`, `23.36.252.78`, `23.53.225.65`,
`23.35.101.126`, `23.46.197.62`, `118.68.80.100`.

| IP | TCP 443 | ICMP | Steam/Community SNI | `example.com` SNI |
|---|---|---|---|---|
| `23.40.36.77` | timeout | no reply | not tested (dead) | not tested |
| `184.51.97.157` | timeout | no reply | not tested (dead) | not tested |
| `23.36.252.78` | OK | reply | **RESET** | TLS OK, HTTP 400 |
| `23.53.225.65` | timeout | no reply | not tested (dead) | not tested |
| `23.35.101.126` | OK | reply | **RESET** | TLS error |
| `23.46.197.62` | timeout | no reply | not tested (dead) | not tested |
| `118.68.80.100` | OK | reply | **RESET** | TLS OK, HTTP 400 |

Four are dead; three connect but are still blocked by SNI. These entries are not
created by this repository and were left untouched.

### 3.4 Separate HCMUSCampus PAC/relay

The repository also contains a second, independent tool under `HCMUSCampus\`.
It is not the VNPT split-DNS/hosts tool. Its enable script starts a localhost
TLS-fragmenting relay and sets a user proxy PAC for Steam/Valve hostnames only.

Observed state at the final check on 5 November (historical):

| Item | Result |
|---|---|
| WinINET `ProxyEnable` | `1` |
| `AutoConfigURL` | `file:///C:/Users/MSI%20VN/AppData/Local/HCMUS-Campus/steam.pac` |
| `ProxyServer` | empty |
| PAC rule | Steam/Valve suffixes → `PROXY 127.0.0.1:8787`; all else `DIRECT` |
| Relay on `127.0.0.1:8787` | **not listening** (TCP connect fails) |
| Proxy backup | `%LOCALAPPDATA%\HCMUS-Campus\proxy-backup.json`: prior `ProxyEnable = 0`, no PAC, saved 2026-11-05 10:49:37 |
| Last relay log | `relay-20261105-104936.log`; ends 10:49:43; logs show `SteamSnIRelay 1.0.0` and `client->server TLS fragmentation applied` |

Consequences of that 5 November state:

- The PAC was active but the relay was down, so PAC-aware applications that
  requested a Steam/Valve hostname were sent to `127.0.0.1:8787` and failed with
  connection refused. This was independent of the campus filter and was fixed on
  6 November.
- The VNPT tool's checks use `curl --noproxy '*'`, so they bypass the PAC and
  measure the direct path. Their `FAIL / Steam SNI reset` verdict is about that
  direct path, not about the PAC.
- The HCMUSCampus logs show the relay was running earlier in the window
  (10:15–10:49) and fragmenting ClientHello records. The 5 November report did
  not start the relay, so those results were not independently re-tested then.
  They were re-tested on 6 November — see §13.
- The HCMUSCampus README's earlier claim that plain-HTTP `Host` filtering is
  “not in place” was incomplete: the port-80 `302` is the campus block portal
  (`192.168.94.35:8183`), so plain-HTTP Steam requests are also redirected. This
  does not affect the relay's HTTPS path.
- The PAC and relay were left untouched on 5 November. The current fixed state
  and its verification are documented in §13; `Disable-SteamUnblock.ps1` still
  restores the 5 November backup (`ProxyEnable = 0`).

---

## 4. DNS results

### 4.1 Public and system resolvers

Snapshot from the clean key matrix. DNS answers rotate; these are representative
values, not invariants.

| Domain | system (`1.1.1.1`) | `1.1.1.1` | `8.8.8.8` |
|---|---|---|---|
| `store.steampowered.com` | `23.15.142.182` | `23.15.142.182` | `23.53.214.117` |
| `steamcommunity.com` | `23.15.140.216` | `23.15.140.216` | `23.41.37.9` |
| `login.steampowered.com` | `184.24.154.161`, `.142` | same | `113.171.234.149`, `.142` |
| `api.steampowered.com` | `23.54.118.52`, `.36`, `.37` | `23.35.148.153`–`.162` (8 addresses) | `113.171.234.139`, `.142` |
| `cdn.akamai.steamstatic.com` | `2.17.106.206`, `.213` | `23.200.230.174`, `.169` | `113.171.234.48`, `.43` |
| `help.steampowered.com` | `184.87.106.51` | `184.87.106.51` | `184.87.106.51` |
| `checkout.steampowered.com` | `23.63.225.250` | `23.63.225.250` | `23.222.128.240` |
| `steampowered.com` | `23.222.131.51` | `23.203.76.5` | `23.222.131.51` |
| `steamstatic.com` | NO_A | NO_A | NO_A |
| `steamgames.com` | `23.222.131.51` | `23.219.113.41` | `104.69.32.10` |
| `valvesoftware.com` | `23.54.118.53`, `.41` | `184.24.154.170`, `.175` | `113.171.234.147`, `.140` |
| `google.com` (control) | `172.253.118.x` | `142.250.199.238` | `142.250.199.238` |
| `example.com` (control) | `104.20.23.154`, `172.66.147.243` | same | same |
| `reddit.com` (control) | `151.101.x` | `151.101.x` | `151.101.x` |
| `github.com` (control) | `20.205.243.166` | resolved early; timeout later | `20.205.243.166` |
| `microsoft.com` (control) | `150.171.110.70` | `150.171.110.108` | `150.171.110.108` |
| `cloudflare.com` (control) | `104.16.132/133.229` | same | same |

Key points:

- The Windows resolver is configured with `1.1.1.1` and `1.0.0.1`. Some answers
  match the direct `1.1.1.1` query; others differ because of anycast/CDN
  rotation. A direct-query timeout in this table is not evidence that a name is
  blocked.
- `8.8.8.8` returns VNPT-local/Vietnam edges for several Steam names:
  `23.53.214.117` for Store, `113.171.234.x` for Login/API/CDN/Valve. This is
  exactly the structural difference the 4 November bypass relied on: Google DNS
  steers to the local Akamai node instead of an international edge.
- `steamstatic.com` correctly returns **NO_A** (the apex has no A record), not
  `NXDOMAIN`. `Resolve-DnsName` returned its SOA (`ns1.valvesoftware.com`,
  serial `2025072252`) against both public resolvers; `nslookup` SOA/NS probes
  were intermittent.
- Direct-query reliability changed during the window. Early repeated UDP checks
  mostly succeeded (for example, `1.1.1.1` answered 5/5 for Store/Community/CDN;
  `8.8.8.8` answered 4–5/5). A later recheck timed out on all five attempts for
  both resolvers, including for `github.com`, while `Resolve-DnsName` with
  DNSSEC-OK still returned answers. This is consistent with rate limiting or
  intermittent filtering of direct DNS queries; it is not domain-specific
  evidence.
- A random nonexistent name returned `NXDOMAIN` on the system resolver,
  `1.1.1.1`, and `8.8.8.8`; there is no DNS hijack.
- DNSSEC-OK queries worked for `login.steampowered.com` against both public
  resolvers.

### 4.2 VNPT resolvers from the campus path

Controls-first sequence against `203.162.4.190` (1 s spacing):

| Domain | Result |
|---|---|
| `google.com` (control) | TIMEOUT |
| `example.com` (control) | TIMEOUT |
| `reddit.com` (control) | `151.101.x` |
| `microsoft.com` (control) | `150.171.110.103` |
| `cloudflare.com` (control) | TIMEOUT |
| `github.com` (control) | TIMEOUT |
| `store.steampowered.com` | **NXDOMAIN** |
| `steamcommunity.com` | **NXDOMAIN** |
| `login.steampowered.com` | **NXDOMAIN** |
| `api.steampowered.com` | TIMEOUT |
| `cdn.akamai.steamstatic.com` | TIMEOUT |
| `help.steampowered.com` | **NXDOMAIN** |
| `checkout.steampowered.com` | TIMEOUT |
| `steampowered.com` | TIMEOUT |
| `steamstatic.com` | **NXDOMAIN** |
| `steamgames.com` | **NXDOMAIN** |
| `valvesoftware.com` | **NXDOMAIN** |

Against `203.162.4.191`, the pattern was mixed but targeted. A later
`Resolve-DnsName` check (Windows resolver path) returned `google.com`,
`microsoft.com` and `cloudflare.com` normally, while
`store.steampowered.com`, `steamcommunity.com` and
`login.steampowered.com` returned `NXDOMAIN`. An earlier `nslookup` sequence on
the same resolver had `store`/`login`/`api`/`steamstatic.com` time out and
`community`/`cdn` return `NXDOMAIN`.

**Interpretation:** the observed Steam `NXDOMAIN`s are false in the sense that
the names exist and resolve on public DNS. The .191 later check is a cleaner
targeted signal — controls resolve, Steam names do not — while .190 and the
direct `nslookup` path were intermittently timing out. Confidence in the DNS
layer is therefore **moderate**, not high. The current Steam failure does not
depend on this DNS layer; the hop-2 HTTP/SNI filter explains it on its own.

### 4.3 DNS transport

| Test | Result |
|---|---|
| TCP 53 connect to `1.1.1.1`, `8.8.8.8`, `203.162.4.190`, `203.162.4.191` | succeeds |
| ICMP to all four resolvers | replies |
| `nslookup -vc` (DNS over TCP) to `1.1.1.1` / `8.8.8.8` | timed out for controls and Steam names |
| UDP DNS | sometimes works; later all five direct `nslookup` attempts timed out for both public resolvers, including controls |

TCP/53 handshakes succeed, but DNS-over-TCP queries time out even to public
resolvers. UDP DNS is also not reliably available for direct queries; the
Windows resolver path (`Resolve-DnsName`) may still answer from cache or via a
different resolver path. Direct resolver conclusions are therefore limited.

---

## 5. HTTP layer: campus block page

### 5.1 The redirect is a campus filter, not an origin response

A plain-HTTP request to the local Akamai IP `23.53.214.117` with
`Host: store.steampowered.com` returned:

```http
HTTP/1.1 302 Found
Server: nginx
Content-Length: 0
Location: http://192.168.94.35:8183/redirect.cgi?u=store.steampowered.com&c=games
```

The `Location` is a private RFC1918 address on the campus path, not a Steam or
Akamai host. Following it returns a page with:

```html
<title>Blocked Site</title>
This domain is blocked due to content filtering.
If you think this shouldn't be blocked, please contact your network administrator.
...
var cat = "games";
```

The `c=games` query parameter and `var cat = "games"` identify the filter
category. `steam.tv` is categorized as `c=ads` instead. The portal root at
`http://192.168.94.35:8183/` returns `301 redirect.cgi` and is served by nginx.

### 5.2 HTTP Host scope on a local Akamai IP

Requests were sent to `23.53.214.117:80` with different `Host` headers:

| `Host` header | Result |
|---|---|
| `store.steampowered.com` | CAMPUS_BLOCK (`c=games`) |
| `steamcommunity.com` | CAMPUS_BLOCK (`c=games`) |
| `login.steampowered.com` | CAMPUS_BLOCK (`c=games`) |
| `api.steampowered.com` | CAMPUS_BLOCK (`c=games`) |
| `cdn.akamai.steamstatic.com` | CAMPUS_BLOCK (`c=games`) |
| `help.steampowered.com` | CAMPUS_BLOCK (`c=games`) |
| `checkout.steampowered.com` | CAMPUS_BLOCK (`c=games`) |
| `steampowered.com` | CAMPUS_BLOCK (`c=games`) |
| `steamgames.com` | CAMPUS_BLOCK (`c=games`) |
| `valvesoftware.com` | CAMPUS_BLOCK (`c=games`) |
| `steamstatic.com` | CAMPUS_BLOCK (`c=games`) |
| `steam.tv` | CAMPUS_BLOCK (`c=ads`) |
| `s.team` | CAMPUS_BLOCK (`c=games`) |
| `steamdeck.com` | origin, `302 https://steamdeck.com/` |
| `steamcloud.com` | origin, HTTP 400 |
| `steamserver.net` | origin, HTTP 400 |
| `example.com` | origin, HTTP 400 |
| `github.com` | origin, HTTP 400 |

This matches the SNI block list almost exactly: the core Steam/Valve names are
redirected to the campus block page; `steamdeck.com`, `steamcloud.com` and
`steamserver.net` are not.

### 5.3 Host header is checked independently of destination IP

The same `Host` headers were sent to the Cloudflare `example.com` IP
`104.20.23.154`:

| `Host` header | Result |
|---|---|
| `store.steampowered.com` | CAMPUS_BLOCK (`c=games`) |
| `steamcommunity.com` | CAMPUS_BLOCK (`c=games`) |
| `help.steampowered.com` | CAMPUS_BLOCK (`c=games`) |
| `steamdeck.com` | HTTP 409 (not the portal) |
| `example.com` | HTTP 200 |
| `github.com` | HTTP 409 |

And the reverse control, sending a non-Steam `Host` to the Steam/Akamai IP,
returned origin responses (`example.com` → 400; `github.com` → 400), not the
portal. This is a clean hostname-based HTTP filter, not an IP-based one.

---

## 6. TLS layer: SNI reset

### 6.1 Direct application path

`curl` to the normal application path (hosts + system resolver):

| Name | URL | Result |
|---|---|---|
| Store | `https://store.steampowered.com/` | **TLS reset** (`curl 35`, “Recv failure: Connection was reset”) |
| Community | `https://steamcommunity.com/` | **TLS reset** |
| Login | `https://login.steampowered.com/` | **TLS reset** |
| API | `https://api.steampowered.com/` | **TLS reset** |
| CDN | `https://cdn.akamai.steamstatic.com/` | **TLS reset** |
| Help | `https://help.steampowered.com/` | **TLS reset** |
| Checkout | `https://checkout.steampowered.com/` | **TLS reset** |
| SteamGames | `https://steamgames.com/` | **TLS reset** |
| Control | `https://example.com/` | HTTP 200 |

Every endpoint in the project test failed at TLS; the non-Steam control
succeeded. `valvesoftware.com` was separately tested in the SNI matrix (§6.3)
and also reset.

### 6.2 Same-IP / different-SNI controls

| Target IP | Steam SNI | Non-Steam SNI | IP-literal HTTPS |
|---|---|---|---|
| `23.53.214.117` (VNPT-local Akamai) | **RESET** | `example.com` → TLS OK, HTTP 400 | TLS OK, HTTP 400 |
| `23.53.212.245` (VNPT-local Akamai) | **RESET** | `example.com` → TLS OK, HTTP 400 | TLS OK, HTTP 400 |
| `23.219.113.41` (live Akamai) | **RESET** | `example.com` → TLS OK, HTTP 400 | TLS OK, HTTP 400 |
| `23.15.142.182` (international edge) | **RESET** | `example.com` → TLS OK, HTTP 400 | TLS OK, HTTP 400 |
| `104.20.23.154` (Cloudflare `example.com`) | Store **RESET** | `example.com` → HTTP 200 | n/a |

The 2×2 control:

| SNI | Destination | Result |
|---|---|---|
| Steam | Steam/Akamai IP | **RESET** |
| Steam | non-Steam Cloudflare IP | **RESET** |
| `example.com` | Steam/Akamai IP | TLS OK (HTTP 400) |
| `example.com` | Cloudflare IP | TLS OK (HTTP 200) |

This rules out an IP-level block and shows that the reset follows the **SNI**,
not the destination address.

### 6.3 Block scope

Full Steam/Valve name set tested against live Steam/Akamai IPs
(`23.53.214.117` and `23.219.113.41`):

**Consistently reset (core block list):**

`store.steampowered.com`, `steamcommunity.com`, `login.steampowered.com`,
`api.steampowered.com`, `cdn.akamai.steamstatic.com`,
`help.steampowered.com`, `checkout.steampowered.com`, `steampowered.com`,
`steamgames.com`, `valvesoftware.com`, `steamstatic.com`, `steam.tv`, `s.team`.

**Completed TLS (not consistently blocked):**

`steamdeck.com` (HTTP 403), `steamcloud.com` (400), `steamcontent.com` (400),
`steamconnect.com` (400), `steamserver.net` (400 on one edge; timeout on the
other), `steamusercontent.com` (400 on one edge; timeout on the other),
`steam-chat.com` (403 on one edge; timeout on the other),
`steamcdn-a.akamaihd.net` (403), `steamcommunity-a.akamaihd.net` (403),
`steamstore-a.akamaihd.net` (403 on one edge; timeout on the other),
`steamstatic-a.akamaihd.net` (400), `steamuserimages-a.akamaihd.net` (403 on
one edge; timeout on the other).

The auxiliary timeouts are not counted as confirmed blocks; they vary by edge
and are likely path/rate-limit effects. The consistent, repeatable signal is
the core block list above.

### 6.4 Repeatability and TLS version

- Store SNI to `104.20.23.154`: **3/3 RESET**.
- `example.com` SNI to the same IP: **3/3 HTTP 200**.
- TLS 1.2 (`--tlsv1.2 --tls-max 1.2`): **RESET**.
- TLS 1.3 (`--tlsv1.3`): **RESET**.

The reset happens before HTTP and applies to both TLS versions, consistent with
ClientHello/SNI inspection.

### 6.5 TCP, ICMP and IP-literal

- Live Steam/Akamai IPs: TCP 443 connect succeeds (6–70 ms), ICMP replies.
- Dead old hosts pins: TCP 443 times out, no ICMP reply.
- IP-literal HTTPS to `23.219.113.41`, `23.53.214.117`, `23.53.212.245` and
  `23.15.142.182`: TLS OK, HTTP 400.

So the network is not dropping TCP or ICMP to Steam. It accepts the TCP
connection and then resets the TLS handshake when the SNI is on the block list.

---

## 7. Path and attribution

Traceroutes during the final window:

| Target | Hop 1 | Hop 2 | Hop 3 | Endpoint |
|---|---|---|---|---|
| `23.53.214.117` (VNPT-local Store) | `10.124.0.1` | **`192.168.94.35`** | `125.235.249.189` | `23.53.214.117` |
| `23.219.113.41` (live Akamai) | `10.124.0.1` | **`192.168.94.35`** | `125.235.249.189` | `23.219.113.41` |
| `23.15.142.182` (international edge) | `10.124.0.1` | **`192.168.94.35`** | `125.235.249.189` | `23.15.142.182` |
| `104.20.23.154` (Cloudflare) | `10.124.0.1` | **`192.168.94.35`** | `125.235.249.189` | `104.20.23.154` |

`tracert 192.168.94.35` completes in two hops:

```text
1     4 ms     2 ms     1 ms  10.124.0.1
2    26 ms    42 ms    39 ms  192.168.94.35
```

The same private device is hop 2 on every path, before the first public VNPT
hop. That is the content filter/portal. Combined with the SSID
**HCMUS-STUDENT**, the private portal address, and the block page, the most
parsimonious attribution is the **campus access network**, not the Steam edge
and not the public ISP.

The public egress varied between VNPT and Viettel during the test, but the hop-2
filter was constant. This is why ASN-based attribution is unreliable here.

A traceroute cannot prove where a TCP RST is injected, and this test did not
capture packets. The HTTP portal, the hop-2 position, and the same-IP
Host/SNI controls together make the campus filter the strong explanation.

---

## 8. What the project's own tools report

### 8.1 `Test-SteamBypass.ps1`

```text
Configuration:
  NRPT rules:          0
  Hosts block:         absent
  Disabled old lines:  0

HTTPS through the system resolver:
Store      ... FAIL ... curl: (35) Recv failure: Connection was reset
Community  ... FAIL ... curl: (35) Recv failure: Connection was reset
Login      ... FAIL ... curl: (35) Recv failure: Connection was reset
API        ... FAIL ... curl: (35) Recv failure: Connection was reset
CDN        ... FAIL ... curl: (35) Recv failure: Connection was reset
Help       ... FAIL ... curl: (35) Recv failure: Connection was reset
Checkout   ... FAIL ... curl: (35) Recv failure: Connection was reset
SteamGames ... FAIL ... curl: (35) Recv failure: Connection was reset
Control    ... 200

FAIL: Steam TLS is being reset while the non-Steam control works.
This network filters Steam SNI; DNS/hosts changes cannot fix it.
```

Exit code: **1**.

### 8.2 `Enable-SteamBypass.ps1 -DryRun`

```text
Host                   ChosenIP HTTP   Ms Note
----                   -------- ----   -- ----
steamcommunity.com     (none)   FAILED    Steam SNI reset
steamgames.com         (none)   FAILED    Steam SNI reset
steampowered.com       (none)   FAILED    Steam SNI reset
store.steampowered.com (none)   FAILED    Steam SNI reset
www.steampowered.com   (none)   FAILED    Steam SNI reset

Could not verify: store.steampowered.com, steamcommunity.com.
No changes were made. The active network resets Steam TLS handshakes
(SNI filtering); DNS/hosts changes cannot fix that.
```

Exit code: **1**.

This is the correct guardrail. Installing the rules with `-Force` would still
leave Steam broken, because the reset happens after DNS/hosts resolution. Both
checks use `curl --noproxy '*'`; they measure the direct path and are not
affected by the HCMUSCampus PAC.

This verification made no changes. Final state: VNPT tool **OFF** (0 NRPT rules,
no managed hosts block); separate HCMUSCampus PAC **enabled**, relay **not
listening** on `127.0.0.1:8787`.

---

## 9. Comparison with the 4 November VNPT report

| Aspect | 4 Nov direct VNPT line | 5 Nov HCMUS-STUDENT campus |
|---|---|---|
| Access | VNPT AS45899 direct | Campus Wi-Fi; VNPT/Viettel multi-homed egress |
| DNS | VNPT false `NXDOMAIN`; Google DNS returned local Akamai | Public DNS resolves; VNPT resolvers erratic from campus |
| Block layer | DNS + hostname filtering on international path | HTTP `Host` redirect to campus portal + TLS SNI reset |
| HTTP `Host` | Some Hosts timed out on the international edge | `302` to campus “Blocked Site” (`games`/`ads` categories) |
| Local Akamai | Store/Community worked | Core SNI is reset even on local Akamai |
| IP-level block | No | No |
| VNPT tool | Could apply and verify | Refuses; DNS/hosts cannot fix |
| Separate PAC/relay | none observed | HCMUSCampus PAC enabled; relay not listening (fixed 6 Nov, §13) |
| Attribution | VNPT path (moderate confidence) | Campus hop-2 filter (high confidence) |

This verification **does not confirm or refute** the 4 November VNPT findings.
It verifies a different path, with a campus filter in front of the ISP. The
original report's caveat applies exactly: “use a network that allows Steam TLS
… DNS/hosts changes cannot fix that.”

---

## 10. Confidence

| Claim | Confidence | Basis |
|---|---|---|
| Campus-side hostname filtering | **High** | Private portal at hop 2; HTTP `Host` redirect; SNI reset; same-IP controls |
| Not an IP-level block | **High** | TCP/ICMP OK; IP-literal HTTPS works; non-Steam SNI works on Steam IPs |
| Core Steam SNI reset | **High** | 3/3 repeats; multiple live IPs; TLS 1.2 and 1.3; tool's own verdict |
| HTTP `Host` redirect to campus filter | **High** | `Location: 192.168.94.35:8183`; “Blocked Site”; `var cat = "games"`; Host controls |
| Targeted, not blanket Valve block | **High** | Auxiliary names complete TLS; block list is stable and specific |
| Attribution to the campus network (`HCMUS-STUDENT`) | **High** | SSID; private portal at hop 2; before first public hop |
| VNPT DNS false `NXDOMAIN` from this path | **Moderate** | .191 later resolved controls and returned `NXDOMAIN` for Store/Community/Login; .190 and direct `nslookup` were intermittent |
| VNPT DNS/hosts bypass cannot work on this network | **High** | SNI reset on every candidate pin; project test and dry-run both refuse |
| Separate HCMUSCampus PAC state (5 Nov) | **High** | Registry `ProxyEnable`/`AutoConfigURL`, PAC content, proxy backup, port 8787 closed, logs |
| HCMUSCampus relay works when running | **High (re-tested 6 Nov)** | Direct endpoints reset; via 64-byte-fragment relay all Steam endpoints answer; 512-byte-fragment negative control fails; non-Steam via relay returns 403 |
| HTTP PAC delivery required for Chromium/Steam | **High (6 Nov)** | Chromium ignored `file://` PAC (normal and headless); same PAC over HTTP loaded Store and routed CDN; WinHTTP returns 12006 for `file://` |
| HCMUSCampus PAC routes only Steam/Valve | **High (6 Nov)** | 39/39 JScript cases plus WinHTTP decisions; HTTP PAC endpoint 200/956 bytes |
| Steam client CEF uses the HTTP PAC | **High (6 Nov)** | Steam fetched `/steam.pac`, opened Store/Community, displayed “Special Offers”, and logged on via local `127.0.0.1` relay address |

---

## 11. Caveats

- This is **one campus network at one point in time**. The client roamed between
  `10.120.x` and `10.124.x`; the SSID, hop-2 filter, and block behaviour were
  consistent, but DNS/CDN answers and public egress changed.
- DNS answers rotate. The tables are snapshots, not permanent addresses.
- No packet capture was taken. The HTTP portal, hop-2 position, Host/SNI
  controls, and IP-literal controls are strong behavioural evidence, but the
  exact filter implementation was not observed.
- The test ran without administrator rights and made no changes. NRPT state was
  read through both the cmdlet and the registry.
- VNPT's resolvers are reachable at TCP/53 but their DNS answers are erratic from
  this campus path. A later `Resolve-DnsName` check against .191 resolved
  controls and returned `NXDOMAIN` for Store/Community/Login, which is a targeted
  signal; .190 and direct `nslookup` were less clean. The DNS layer is not the
  current cause of the Steam failure, and confidence in it is moderate, not high.
- Direct queries to public resolvers also became unreliable late in the window:
  all five attempts to `1.1.1.1` and `8.8.8.8` timed out, including for
  `github.com`, while `Resolve-DnsName` with DNSSEC-OK still answered. The early
  DNS snapshots are therefore not a stable guarantee.
- The HCMUSCampus PAC was discovered during the 5 November final state check.
  The initial observation was `ProxyEnable = 0`; the PAC backup shows the
  separate tool enabled it at 10:49:37. That pass did not create or change
  either state. The 6 November re-verification did change it deliberately: it
  enabled the fixed HTTP PAC and left it enabled pending the final Steam check.
- The HCMUSCampus relay was not running at the 5 November final check, so that
  report did not independently confirm its TLS-fragmentation path. It was
  re-tested on 6 November and works; see §13. The Steam client's own CEF path
  was still not exercised at the time of writing.
- The earlier HCMUSCampus README claim that plain-HTTP `Host` filtering is
  “not in place” was inconsistent with the observed `Location` header; the 302
  is the campus block portal. The HTTPS relay path is unaffected, but HTTP Steam
  requests are also filtered/redirected.
- Some auxiliary Steam names timed out on one Akamai edge and succeeded on
  another. Those timeouts are not counted as confirmed blocks.
- The Cloudflare `104.20.23.154` reverse test is corroborating, not primary:
  that IP serves `example.com`, not arbitrary non-Steam names. A Steam SNI reset
  there is still meaningful because `example.com` SNI succeeds on the same IP,
  but the cleanest control is the Steam/Akamai IP with a non-Steam SNI.
- The old `# Fix Steam Host` block is unrelated to this repository. Four of its
  seven pins are dead; the live ones are still SNI-blocked.
- IPv6 is not in play: no global IPv6 address or default route, and Store/
  Community have no AAAA records. `example.com` has AAAA but no IPv6 route.
- The portal category is inferred from `c=games`/`c=ads` and
  `var cat = "games"`; campus IT has not independently confirmed the policy.
- This is a diagnostic report. It is not an instruction to bypass campus
  network policy; use it in line with local law and the network's acceptable-use
  rules.

---

## 12. Recommendations

1. **Do not enable the VNPT bypass on this network.** Do not use `-Force`. The
   project's own dry-run correctly refuses because DNS/hosts changes cannot fix
   a TLS SNI reset. Use the separate HCMUSCampus relay instead (see §13).
2. **Use the fixed HCMUSCampus tool.** `Enable-SteamUnblock.ps1` now serves the
   PAC over loopback HTTP and starts both listeners; `Test-SteamUnblock.ps1`
   verifies the relay, the PAC endpoint, and WinHTTP decisions. The tool is
   currently enabled. To return to the clean 5 November state, run
   `HCMUSCampus\Disable-SteamUnblock.ps1`, which restores the saved backup
   (`ProxyEnable = 0`, no PAC). This is a separate tool; it does not change the
   VNPT verdict.
3. **Use a network that permits Steam TLS** if Steam access is required, or ask
   the campus network administrator about the `games`/`ads` content-filter
   categories. The 4 November VNPT line is a separate environment; re-test there
   if you need to confirm the original findings.
4. **Clean up the old `hosts` pins with the user's approval.** Four are dead and
   the other three are still blocked. They can slow or fail Store resolution
   even on a network where the bypass would otherwise work. The enable script
   would comment them out only after a successful run, which cannot happen here;
   a deliberate manual cleanup is safer. They were not modified by this test.
5. **Re-run `Test-SteamBypass.ps1` after any network change.** A verdict of
   “Steam TLS is being reset” means the problem is above DNS; stop before
   applying NRPT/hosts changes.
6. **Keep the dry-run guardrail.** It correctly distinguishes a DNS problem from
   an SNI-filtering network and avoids installing rules that cannot help.

---

## Appendix A — raw evidence excerpts

### A.1 Campus portal

```http
HTTP/1.1 302 Found
Server: nginx
Content-Length: 0
Connection: keep-alive
Location: http://192.168.94.35:8183/redirect.cgi?u=store.steampowered.com&c=games
```

```html
<title>Blocked Site</title>
This domain is blocked due to content filtering.
If you think this shouldn't be blocked, please contact your network administrator.
This site was categorized in:
...
var cat = "games";
```

### A.2 Project verification

```text
FAIL: Steam TLS is being reset while the non-Steam control works.
This network filters Steam SNI; DNS/hosts changes cannot fix it.
```

```text
Could not verify: store.steampowered.com, steamcommunity.com.
No changes were made. The active network resets Steam TLS handshakes
(SNI filtering); DNS/hosts changes cannot fix that.
```

### A.3 Path

```text
Tracing route to 23.53.214.117 over a maximum of 12 hops
  1     4 ms     1 ms     1 ms  10.124.0.1
  2    18 ms    19 ms    19 ms  192.168.94.35
  3    24 ms     *       36 ms  125.235.249.189
  ...
  8    97 ms    79 ms    24 ms  23.53.214.117
```

### A.4 Tooling

- `nslookup.exe` for direct resolver queries (trailing dot; avoids the adapter
  DNS suffix and the hosts file).
- `curl.exe` with `--resolve`, `--noproxy '*'`, `--max-time`, and `-k` for
  Host/SNI controls; exit 35 with “Recv failure: Connection was reset” is a TLS
  reset; exit 28 is a timeout.
- .NET `TcpClient` for TCP 443 checks; `ping.exe` for ICMP.
- `tracert.exe -d` for paths.
- `Get-NetIPConfiguration`, `netsh wlan show interfaces`, `Get-DnsClientNrptRule`,
  and the `DnsPolicyConfig` registry key for local state.
- `ipinfo.io` for public IP/ASN observations.
- `HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings` and
  `netsh winhttp show proxy` for the separate HCMUSCampus PAC state.

### A.5 Separate HCMUSCampus PAC/relay (5 November snapshot)

PAC excerpt:

```javascript
// HCMUS Campus bypass - managed by Enable-SteamUnblock.ps1
function FindProxyForURL(url, host) {
  var suffixes = [
    "steampowered.com", "steamcommunity.com", "steamstatic.com",
    "steamgames.com", "valvesoftware.com", "steam-chat.com",
    "steam.tv", "steamdeck.com", "steamcloud.com", "steamcontent.com",
    "steamusercontent.com", "steamserver.net", "steamconnect.com",
    "steamcdn-a.akamaihd.net", "steamuserimages-a.akamaihd.net",
    "steamstore-a.akamaihd.net", "steamcommunity-a.akamaihd.net",
    "steamstatic-a.akamaihd.net", "s.team"
  ];
  // ...suffix match...
  return "PROXY 127.0.0.1:8787";
}
```

Proxy backup:

```json
{
  "ProxyEnable": 0,
  "ProxyServer": null,
  "ProxyOverride": null,
  "AutoConfigURL": null,
  "AutoDetect": null,
  "SavedAt": "2026-11-05 10:49:37 +07:00"
}
```

Relay log excerpt (last run):

```text
2026-11-05 10:49:36  SteamSnIRelay 1.0.0 listening on 127.0.0.1:8787 (handshake fragment 64 bytes, split 512).
2026-11-05 10:49:38  RESOLVE store.steampowered.com -> 23.53.214.117
2026-11-05 10:49:38  OPEN store.steampowered.com:443
2026-11-05 10:49:38  client->server TLS fragmentation applied (1 record(s) split).
...
2026-11-05 10:49:43  CLOSE help.steampowered.com
```

At the 5 November final check, `TcpClient` to `127.0.0.1:8787` failed and
`Get-NetTCPConnection -LocalPort 8787` returned no listener.

6 November fixed state (for comparison):

```text
AutoConfigURL = http://127.0.0.1:8788/steam.pac
Relay         = 127.0.0.1:8787 (PID 13376)
PAC endpoint  = 127.0.0.1:8788 (same PID)
GET /steam.pac -> 200, 956 bytes
WinHTTP store.steampowered.com -> PROXY 127.0.0.1:8787
WinHTTP example.com           -> DIRECT
```

---

## 13. Addendum — 6 November 2026 re-verification and fix

The 5 November report identified two separate issues in the `HCMUSCampus` tool:
the relay was not running, and the PAC was a `file://` URL. Re-verification on
6 November found that the campus filter was unchanged and that the original
relay mechanism works, but Chromium/CEF ignores `file://` PACs. The tool was
fixed to serve the PAC over loopback HTTP and the whole stack was re-tested.

### 13.1 Campus filter unchanged

| Path | Result |
|---|---|
| Direct `store.steampowered.com` | TLS reset |
| Direct `steamcommunity.com` | TLS reset |
| Direct `login.steampowered.com` | TLS reset |
| Direct `api.steampowered.com` | TLS reset |
| Direct `example.com` | HTTP 200 |
| Same IP `23.77.15.30`, Steam SNI | TLS reset |
| Same IP `23.77.15.30`, `example.com` SNI | TLS completed (HTTP 400) |
| Via 64-byte-fragment relay | Store 200, Community 200, Login 302, API 404, CDN 200, Help 302, Checkout 302, Valve 302, Steam.tv 302, s.team 302, SteamGames 301 |
| 512-byte-fragment negative control | Store/Community handshake failed |
| Non-Steam target via relay | `403` |

The same-IP control confirms the filter is SNI-based, not IP-based. The
512-byte negative control confirms that handshake fragmentation is the active
mechanism, not a change on the network side.

### 13.2 PAC delivery fix

- The old `AutoConfigURL` was
  `file:///C:/Users/MSI%20VN/AppData/Local/HCMUS-Campus/steam.pac`.
- Chromium — normal and headless — ignored that URL and failed to load the
  Store. The same PAC served over `http://127.0.0.1:8099/steam.pac` loaded the
  Store and routed its CDN traffic through the relay.
- WinHTTP refuses `file://` PAC URLs with error `12006`
  (`ERROR_WINHTTP_UNRECOGNIZED_SCHEME`), so the old URL could not be verified
  with WinHTTP either.
- `SteamSnIRelay.exe` 1.1.0 now serves the PAC at `/steam.pac` from the same
  process (`-pac-port`, default `8788`) and returns `200`, `404`, or `503`
  appropriately.
- `Enable-SteamUnblock.ps1` writes the PAC before starting the relay, starts
  both listeners (`8787` relay, `8788` PAC), applies
  `AutoConfigURL = http://127.0.0.1:8788/steam.pac`, and checks the PAC endpoint
  plus WinHTTP decisions before reporting success.
- `Test-SteamUnblock.ps1` now fails if `AutoConfigURL` is not the loopback HTTP
  endpoint, if the endpoint is unreachable, or if any WinHTTP decision is wrong.

### 13.3 Current verified state

- Relay PID 13376 on `127.0.0.1:8787`; PAC endpoint on `127.0.0.1:8788`.
- `AutoConfigURL = http://127.0.0.1:8788/steam.pac`; `ProxyEnable = 1`.
- `Test-SteamUnblock.ps1 -TimeoutSec 8`: **PASS**.
- PAC endpoint: HTTP 200, 956 bytes. WinHTTP: Store/Community/CDN/s.team →
  `PROXY 127.0.0.1:8787`; `example.com` and lookalike domains → `DIRECT`.
- PAC JScript semantics: 39/39 cases pass.
- Normal Chrome using the actual system PAC loaded “Welcome to Steam”; the
  relay log shows Store and CDN connections.
- Steam client, launched fresh after the fix: fetched
  `http://127.0.0.1:8788/steam.pac` (3 requests), opened Store, Community, and
  `shared`/`community.steamstatic.com`, and displayed the Store tab
  (“Special Offers”). `d:\steam\logs\connection_log.txt` recorded
  `RecvMsgClientLogOnResponse() : 'OK'`; the connection manager reported local
  address `127.0.0.1`, confirming logon through the relay.
- TLS: the relay presents the real origin certificate
  (`CN=store.steampowered.com`, Let's Encrypt, TLS 1.3), `ssl_verify_result=0`.
  No interception or local certificate installation.
- The relay still refuses non-Steam hosts with `403`.

**Confirmed:** both Chromium and the Steam client's CEF helper use the HTTP PAC.
The Steam Store and Community load, and Steam logs on through the relay.
Content downloads are a separate, known limitation: `steamclient.dll` does not
reliably honor the user PAC, and a depot manifest fetch in `content_log.txt`
still failed during this check. This does not affect Store/Community/login.

**Rollback:** `HCMUSCampus\Disable-SteamUnblock.ps1` stops the relay/PAC process
and restores the saved clean state (`ProxyEnable = 0`, no PAC).
