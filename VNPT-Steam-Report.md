# Does VNPT block Steam? — consolidated report

**Status:** Final consolidated report. It merges the original test report with its verification and supersedes both.

**Verdict:** Yes. On the VNPT line tested in Ho Chi Minh City on **4 November 2026**, Steam is blocked at two layers:

1. **DNS:** VNPT's resolvers return a targeted false `NXDOMAIN` for a broad set of Valve/Steam names, while control domains resolve normally.
2. **Traffic:** on the international path, TCP connects but the TLS handshake is dropped when the SNI is one of specific Steam/Valve hostnames. The same IP works with `example.com` and with some other Steam hostnames. Plain HTTP with a blocked Steam `Host` header is also dropped. On VNPT-local Akamai IPs, Store and Community work normally.

The block is **partial and inconsistent**, not a total Steam ban.

---

## 1. Test conditions

| Item | Value |
|---|---|
| Date/time | 2026-11-04, 19:37–20:05 ICT (UTC+7); verification re-test 20:08–20:17 |
| Location | Ho Chi Minh City, Vietnam |
| Access network | VNPT, AS45899 VNPT Corp |
| Public IP | `14.169.123.101` (`static.vnpt.vn`) |
| System DNS | `1.1.1.1`, `1.0.0.1` |
| VNPT resolvers tested | `203.162.4.190` (`hcm-server01.vnn.vn`), `203.162.4.191` (`hcm-server02.vnn.vn`) |
| Test type | Read-only DNS, TCP, TLS and HTTP checks; no DNS/hosts/proxy changes |

The public IP, ASN and PTR were confirmed through `ipinfo.io` and DNS. Verification also confirmed the hosts file was clean, WinHTTP/WinINET proxy was off, the firewall had only inbound Steam allow rules, and Cloudflare WARP was disconnected with no tunnel adapter.

---

## 2. DNS result: targeted false NXDOMAIN for Steam

Queries sent directly to VNPT's HCMC resolver:

| Domain | VNPT `.190` | Cloudflare `1.1.1.1` | Google `8.8.8.8` |
|---|---|---|---|
| `store.steampowered.com` | **NXDOMAIN** | `23.15.142.182` | `23.53.214.117` |
| `steamcommunity.com` | **NXDOMAIN** | `23.15.140.216` | `23.53.212.245` |
| `login.steampowered.com` | **NXDOMAIN** | `23.32.61.13`, `23.32.61.48` (raw run; rotates) | `113.171.234.x` |
| `api.steampowered.com` | **NXDOMAIN** | `23.33.126.x` | `113.171.234.x` |
| `cdn.akamai.steamstatic.com` | **NXDOMAIN** | `104.84.150.x` | `113.171.12.x` |
| `help.steampowered.com` | **NXDOMAIN** | resolved | resolved |
| `checkout.steampowered.com` | **NXDOMAIN** | resolved | resolved |
| `steampowered.com` | **NXDOMAIN** | `104.69.32.10` | `23.203.76.5` |
| `steamstatic.com` | **NXDOMAIN** (false) | NOERROR/NODATA; SOA/NS exist | NOERROR/NODATA; SOA/NS exist |
| `steamgames.com` | **NXDOMAIN** | `104.69.32.10` | `23.203.76.5` |
| `valvesoftware.com` | **NXDOMAIN** | `23.49.105.189`, `23.49.105.184` | `113.171.234.140`, `113.171.234.147` |
| `google.com` (control) | resolves | resolves | resolves |
| `reddit.com` (control) | resolves | resolves | resolves |

The second VNPT resolver, `203.162.4.191`, also returned `NXDOMAIN` for `store.steampowered.com` and `steamcommunity.com`. The result reproduced over TCP and with the DNSSEC OK flag. Additional controls (`example.com`, `github.com`, `akamai.com`, `cloudflare.com`, `microsoft.com`) resolved normally.

**Interpretation:** this is not a general DNS failure or DNSSEC problem. VNPT is returning `NXDOMAIN` for names that exist, including `steamstatic.com`, which has SOA/NS records but no A record. That is a **targeted false NXDOMAIN**, consistent with deliberate DNS blocking. Intent is inferred: the resolver configuration itself was not observed.

A VNPT subscriber using VNPT-issued DNS would fail to resolve even `login.steampowered.com`, `api.steampowered.com` or `cdn.akamai.steamstatic.com`.

---

## 3. Traffic result: hostname filtering on the international path

### 3.1 Store and Community

Because the test machine used `1.1.1.1`, the NXDOMAIN layer was bypassed. The Store still failed on the IP returned by Cloudflare:

| Target | DNS used | IP | Result |
|---|---|---|---|
| `store.steampowered.com` | `1.1.1.1` | `23.15.142.182` | TCP connects (~34 ms); TLS handshake times out (raw run 5,060 ms; repeated checks 3/3 at ~19.2 s) |
| `steamcommunity.com` | `1.1.1.1` | `23.15.140.216` | TCP connects; TLS handshake times out (raw run 5,043 ms; repeated checks also time out) |
| `store.steampowered.com` | `8.8.8.8` | `23.53.214.117` | HTTP `200`, ~0.4 s, title **“Welcome to Steam”** |
| `steamcommunity.com` | `8.8.8.8` | `23.53.212.245` | HTTP `200`, title **“Steam Community”** |

The failure is **after TCP connect**, at the TLS handshake, not at TCP connection setup.

### 3.2 Same-IP, different-hostname control

On the blocked Hong Kong edge `23.15.142.182`:

| Hostname / SNI | Result |
|---|---|
| `store.steampowered.com` | TLS timeout |
| `steamcommunity.com` | TLS timeout |
| `www.steampowered.com` | TLS timeout |
| `steamgames.com` | TLS timeout |
| `valvesoftware.com` | TLS timeout |
| `help.steampowered.com` | HTTP `403` (reachable) |
| `checkout.steampowered.com` | HTTP `403` (reachable) |
| `login.steampowered.com` | HTTP `403` (reachable) |
| `api.steampowered.com` | HTTP `403` (reachable) |
| `cdn.akamai.steamstatic.com` | HTTP `403` (reachable) |
| `example.com` (control) | TLS OK, TLS 1.3, 3/3 attempts; ~77–725 ms depending on run |

A pure IP null-route would fail for every SNI. A pure DNS block would not affect a connection made to an IP with `--resolve`. The observed pattern — same IP works with a hostname not on the blocklist, fails with specific Steam/Valve hostnames — points to **hostname-based filtering**, not a generic “any Steam SNI” rule. A Steam hostname such as `cdn.akamai.steamstatic.com` works on the same IP.

### 3.3 The HTTP `Host` header is filtered too

A plain HTTP request on port 80 to `23.15.142.182` behaved the same way:

| HTTP `Host` header | Result |
|---|---|
| `store.steampowered.com` | timeout |
| `steamcommunity.com` | timeout |
| `help.steampowered.com` | HTTP `302` |
| `cdn.akamai.steamstatic.com` | HTTP `200` |
| `example.com` | HTTP `400` |

So the filter is **hostname/DPI filtering on TLS SNI or HTTP Host**, not strictly TLS-SNI filtering. The exact middlebox mechanism is inferred from behaviour; no packet capture was taken.

### 3.4 The same hostnames work on VNPT-local Akamai IPs

On `23.53.212.245`, the Google-resolved VNPT-local Akamai node:

- `store.steampowered.com` and `steamcommunity.com` return `200`;
- `www.steampowered.com` and `steamgames.com` return `301`;
- `valvesoftware.com` returns `302`.

This rules out a simple “the Hong Kong edge does not serve those names” explanation and shows that the block is path/IP-dependent, not a universal hostname block.

### 3.5 Other Steam endpoints via public DNS

These were reachable during the same session, so the block is not a blanket “all Steam traffic” block:

| Endpoint | Result via normal public-DNS IP |
|---|---|
| `login.steampowered.com` | HTTP `302` (reachable) |
| `help.steampowered.com` | HTTP `302` (reachable) |
| `checkout.steampowered.com` | HTTP `302` (reachable) |
| `api.steampowered.com` | HTTP `404` at `/` (reachable; 404 is the expected root response) |
| `cdn.akamai.steamstatic.com` | HTTP `200` (reachable) |

On the blocked Hong Kong edge these hostnames return `403` rather than timing out, which confirms they are not on the drop list. With VNPT DNS, all of these names return `NXDOMAIN`; the table above used public DNS to bypass the DNS layer.

### 3.6 Why changing DNS can “fix” Steam — systematic, not incidental

The split is deterministic and geographic/ASN-based:

| Resolver | Repeated Store answer | ASN / location |
|---|---|---|
| Cloudflare `1.1.1.1` | `23.15.142.182` (5/5 queries) | AS16625 Akamai, Tseung Kwan O, Hong Kong |
| Google `8.8.8.8` | `23.53.214.117` (5/5 queries) | AS45899 VNPT, Ho Chi Minh City |

The blocked IPs are Akamai Hong Kong edges. The working IPs are Akamai nodes announced by VNPT itself. A traceroute confirms the difference: the working IP is reached in 7 hops inside VNPT, while the blocked IP goes through VNPT's international path (`113.171.x`, `23.37.x`) and Akamai Hong Kong.

The practical DNS “fix” therefore works because Google DNS directs the client to the **local VNPT-hosted Akamai cache**, not because a random unfiltered IP happened to be returned. This is a structural difference, not incidental rotation. It is still not permanent: DNS answers and CDN IPs rotate.

---

## 4. What this means

- **VNPT does block Steam**, but the block is layered and uneven:
  - **DNS layer:** broad false `NXDOMAIN` for Valve/Steam domains on VNPT resolvers.
  - **Traffic layer:** hostname-based dropping (TLS SNI or HTTP Host) for specific Steam/Valve hostnames, mainly on the international path.
- **It is not a total IP block.** Some VNPT-local Akamai IPs serve the Store and Community normally, which is why changing DNS sometimes “fixes” Steam. That fix is systematic but can change as DNS/CDN rotation changes the IP.
- **Results vary by ISP, region, CDN IP and time.** Public reports cover Viettel and FPT and describe regional variation; this test adds direct evidence for a VNPT access line. The three ISPs were not measured together in one controlled test.
- **Installed games and other Steam services may still work** while Store/Community fail; the October 2026 reports also mention downloads, updates and Cloud being affected for some users.

**Confidence:**

| Layer | Confidence | Basis |
|---|---|---|
| DNS blocking | High | Direct queries; controls resolve; TCP and DNSSEC OK checks behave the same; false `NXDOMAIN` for an existing domain |
| Hostname filtering on the international path | High | Same-IP/different-SNI and HTTP Host controls; hostname matrix; ASN and traceroute difference |
| Attribution to VNPT | Moderate | Same-IP control strongly favours a network middlebox, but an upstream/transit or Akamai edge policy cannot be ruled out without a packet capture or a same-IP test from another ISP |

---

## 5. Policy context

Vietnam has restricted Steam since **May 2024**. The Authority of Broadcasting, Television and Electronic Information (then under the Ministry of Information and Communications) told VnExpress that Steam distributed games without Vietnamese licences, did not respond to repeated contacts, and was therefore blocked through telecom providers:

> “Most platforms cooperate with authorities and comply with local laws. However, our agency has contacted Steam many times but the platform never responded, and therefore it was blocked.”
>
> — Le Quang Tu Do, Director, Authority of Broadcasting, Television and Electronic Information (VnExpress, 1 June 2024)

**Decree 147/2024/ND-CP**, effective **25 December 2024**, requires online game providers serving Vietnamese users — including cross-border providers — to establish a Vietnamese entity and complete licensing/notification procedures. Reporting through October 2026 continued to describe Steam as unlicensed; no evidence was found that it has obtained a Vietnamese licence.

In **September–October 2026**, reports widened from the Store/payment pages to downloads, updates, Community and Steam Cloud. No new official blocking order was announced, and no authority or ISP published a technical explanation. As of **4 November 2026**, the vpnAlert monitor still lists Steam as blocked in Vietnam; its tests cover Viettel and FPT. vpnAlert is independent of the ISPs but is an affiliate-funded commercial monitor, so its commercial interest should be disclosed alongside its measurements. This test adds direct evidence for a VNPT access line.

---

## 6. Caveats

- This is **one VNPT line in HCMC at one point in time**. The block is known to vary by region, ISP, CDN IP and time; repeat the test on the affected line.
- The DNS result is direct evidence of DNS-level blocking. The traffic result is strong circumstantial evidence of DPI/hostname filtering, but it is not a packet capture or a view of VNPT's filter configuration.
- Attribution to VNPT is moderate: the same-IP control favours a network middlebox, but an upstream/transit or Akamai edge policy is not excluded.
- DNS and CDN IPs rotate. A resolver that works now may return a filtered IP later.
- The test used public DNS only to isolate the layers; it did not modify the machine's DNS, hosts file, firewall or proxy.
- The raw scripted run used a 5,000 ms timeout and recorded 5,060 ms / 5,043 ms TLS timeouts. The 20-second and 3/3-attempt figures come from separate repeated ad-hoc checks, not from the raw run in Appendix A.

---

## 7. Reproduce

The read-only PowerShell test is in `Test-SteamVnpt.ps1`:

```powershell
.\Test-SteamVnpt.ps1 -TimeoutMs 5000 -OutFile .\result.md
```

It checks public IP/ASN, DNS answers from the system resolver plus `1.1.1.1`, `8.8.8.8` and VNPT's HCMC resolver, and TCP/TLS handshakes with the real Steam SNI and an `example.com` control.

**Scope note:** the script covers the core DNS/TLS checks only. It tests 5 DNS names and 4 DNS/TLS pairs, with one TLS attempt per pair and an `example.com` control only on failure. The endpoint table, HTTP titles, 3× repeats, 20-second timeouts and HTTP `Host`-header tests came from separate ad-hoc checks. The raw scripted output is reproduced in Appendix A.

---

## Appendix A: Raw scripted run (2026-11-04 20:05:37 +07:00)

```markdown
# Steam / VNPT connectivity check

- Run at: 2026-11-04 20:05:37 +07:00
- Per-operation timeout: 5000 ms

- Public IP: 14.169.123.101
- Network: AS45899 VNPT Corp
- Location: Ho Chi Minh City, Ho Chi Minh City (HCMC), VN

## DNS

| Domain | system | 1.1.1.1 | 8.8.8.8 | VNPT resolver |
|---|---|---|---|---|
| store.steampowered.com | 23.15.142.182 | 23.15.142.182 | 23.53.214.117 | NXDOMAIN |
| steamcommunity.com | 23.15.140.216 | 23.15.140.216 | 23.53.212.245 | NXDOMAIN |
| login.steampowered.com | 23.33.126.29, 23.33.126.26 | 23.32.61.13, 23.32.61.48 | 113.171.234.149, 113.171.234.142 | NXDOMAIN |
| api.steampowered.com | 23.33.126.20, 23.33.126.15, 23.33.126.22, 23.33.126.7, 23.33.126.21, 23.33.126.12, 23.33.126.9, 23.33.126.19 | 23.33.126.28, 23.33.126.61, 23.33.126.9, 23.33.126.19, 23.33.126.52, 23.33.126.12 | 113.171.234.139, 113.171.234.142 | NXDOMAIN |
| cdn.akamai.steamstatic.com | 23.49.104.184, 23.49.104.181 | 104.84.150.165, 104.84.150.167 | 113.171.12.163, 113.171.12.138 | NXDOMAIN |

## TLS by IP and SNI

| Domain | DNS used | IP | SNI | Result | Protocol | ms |
|---|---|---|---|---|---|---|
| store.steampowered.com | 1.1.1.1 | 23.15.142.182 | store.steampowered.com | TLS_TIMEOUT |  | 5060 |
| store.steampowered.com | 1.1.1.1 | 23.15.142.182 | example.com (control) | TLS_OK | Tls13 | 77 |
| store.steampowered.com | 8.8.8.8 | 23.53.214.117 | store.steampowered.com | TLS_OK | Tls13 | 17 |
| steamcommunity.com | 1.1.1.1 | 23.15.140.216 | steamcommunity.com | TLS_TIMEOUT |  | 5043 |
| steamcommunity.com | 1.1.1.1 | 23.15.140.216 | example.com (control) | TLS_OK | Tls13 | 103 |
| steamcommunity.com | 8.8.8.8 | 23.53.212.245 | steamcommunity.com | TLS_OK | Tls13 | 23 |
```

---

## Sources

- VnExpress International, “Gaming platform Steam blocked in Vietnam for refusal to cooperate with authorities”, 1 June 2024 — https://e.vnexpress.net/news/business/companies/gaming-platform-steam-blocked-in-vietnam-for-refusal-to-cooperate-with-authorities-4752391.html
- VnExpress, “Lý do nền tảng game Steam bị chặn ở Việt Nam”, 30 May 2024 (quote dated 29 May) — https://vnexpress.net/ly-do-nen-tang-game-steam-bi-chan-o-viet-nam-4751902.html
- VietnamNet Global, “Vietnam gaming community turns against unlicensed PUBG PC”, 25 September 2026 — https://vietnamnet.vn/en/vietnam-gaming-community-turns-against-unlicensed-pubg-pc-2558783.html
- Inven Global, “Steam Effectively Blocked in Vietnam? Issues Extend to Downloads and Updates”, 3 October 2026 — https://www.invenglobal.com/articles/26798/steam-effectively-blocked-in-vietnam-issues-extend-to-downloads-and-updates
- GameK, “Nhiều game thủ gặp vấn đề khi đăng nhập Steam”, 2 October 2026 — https://gamek.vn/nhieu-game-thu-gap-van-de-khi-dang-nhap-steam-178261002155949895.chn
- Techz, “Nền tảng Steam gặp sự cố kết nối tại Việt Nam…”, 5 October 2026 — https://www.techz.vn/223-1026-1-nen-tang-steam-gap-su-co-ket-noi-tai-viet-nam-nguyen-nhan-thuc-su-toi-tu-dau-ylt712028.html
- vpnAlert censorship monitor, “Where is Steam banned? Live test in 24 countries” (SEO title: “Where Is Steam Blocked? Live Test in 24 Countries”), last test 4 November 2026 — https://vpnalert.com/tools/censorship-monitor/steam/
- vpnAlert Vietnam monitor, “What’s blocked in Vietnam right now” (SEO title: “What’s Blocked in Vietnam? Live List of Blocked Sites & Apps”), updated 4 November 2026 — https://vpnalert.com/tools/censorship-monitor/vietnam/
- DFDL, “Vietnam: More Requirements on Internet Service and Online Information” (Decree 147/2024/ND-CP summary), 26 November 2024 — https://www.dfdl.com/insights/legal-and-tax-updates/vietnam-more-requirements-on-internet-service-and-online-information/
- Tuổi Trẻ, “Game PUBG PC chưa được cấp phép, người chơi có bị phạt?”, 24 September 2026 — https://tuoitre.vn/print/game-pubg-pc-chua-duoc-cap-phep-nguoi-choi-co-bi-phat-100260924080229647.htm
