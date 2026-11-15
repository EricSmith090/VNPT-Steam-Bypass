using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace VnptSteamBypass
{
    internal sealed class CampusStatus
    {
        public bool RelayRunning;
        public bool ProxyConfigured;
        public bool PacAvailable;
        public bool PacContentOk;
        public int RelayPort;
        public int PacPort;
        public string PacUrl = string.Empty;

        public bool On
        {
            get
            {
                return RelayRunning && ProxyConfigured && PacAvailable &&
                    PacContentOk && RelayPort > 0 && PacPort > 0;
            }
        }

        public bool Partial
        {
            get { return (RelayRunning || ProxyConfigured) && !On; }
        }

        public bool AnyState
        {
            get { return RelayRunning || ProxyConfigured; }
        }
    }

    internal sealed class CampusTestResult
    {
        public bool Success;
        public bool Partial;
        public string verdict = string.Empty;
    }

    internal static class CampusMode
    {
        internal const int DefaultRelayPort = 8787;
        internal const int DefaultPacPort = 8788;
        internal const string Marker = "Campus-HCMUS";

        private const string InternetKey =
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        private static readonly string[] SteamSuffixes = new string[]
        {
            "steampowered.com","steamcommunity.com","steamstatic.com","steamgames.com",
            "valvesoftware.com","steam-chat.com","steam.tv","steamdeck.com","steamcloud.com",
            "steamcontent.com","steamusercontent.com","steamserver.net","steamconnect.com",
            "steamcdn-a.akamaihd.net","steamuserimages-a.akamaihd.net","steamstore-a.akamaihd.net",
            "steamcommunity-a.akamaihd.net","steamstatic-a.akamaihd.net","s.team"
        };

        internal static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HCMUS-Campus");

        internal static readonly string PacPath = Path.Combine(DataDir, "steam.pac");
        internal static readonly string BackupPath = Path.Combine(DataDir, "proxy-backup.json");
        internal static readonly string StatePath = Path.Combine(DataDir, "relay-state.json");
        internal static readonly string LogDir = Path.Combine(DataDir, "logs");

        private sealed class RelayState
        {
            public int Pid { get; set; }
            public int RelayPort { get; set; }
            public int PacPort { get; set; }
            public string StartedAt { get; set; }
        }

        private sealed class WinHttpDecision
        {
            public bool Ok;
            public string AccessType = string.Empty;
            public string Proxy = string.Empty;
            public string Bypass = string.Empty;
            public string Error = string.Empty;
        }

        private sealed class ProxyCheck
        {
            public bool Ok;
            public int Code;
            public string Error = string.Empty;
        }

        // ---- public mode operations ---------------------------------------

        internal static CampusStatus GetStatus()
        {
            CampusStatus status = new CampusStatus();
            RelayState state = ReadState();

            status.PacUrl = ReadAutoConfigUrl();
            Match urlMatch = Regex.Match(
                status.PacUrl,
                @"^http://127\.0\.0\.1:(\d+)/steam\.pac(?:[?#].*)?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (urlMatch.Success)
            {
                status.PacPort = ParseInt(urlMatch.Groups[1].Value);
                status.ProxyConfigured = true;
            }

            if (File.Exists(PacPath))
            {
                status.PacAvailable = true;
                try
                {
                    string pac = File.ReadAllText(PacPath);
                    status.PacContentOk = pac.IndexOf(
                        "FindProxyForURL", StringComparison.OrdinalIgnoreCase) >= 0;
                    Match proxyMatch = Regex.Match(
                        pac,
                        @"PROXY\s+127\.0\.0\.1:(\d+)",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (proxyMatch.Success)
                    {
                        status.RelayPort = ParseInt(proxyMatch.Groups[1].Value);
                    }
                }
                catch
                {
                    status.PacContentOk = false;
                }
            }

            if (state != null)
            {
                if (state.RelayPort > 0) status.RelayPort = state.RelayPort;
                if (state.PacPort > 0) status.PacPort = state.PacPort;
                if (state.Pid > 0 && ProcessAlive(state.Pid))
                {
                    status.RelayRunning = true;
                }
            }

            if (!status.RelayRunning && status.RelayPort > 0)
            {
                status.RelayRunning = PortOpen(status.RelayPort);
            }

            if (!status.RelayRunning)
            {
                // Adopt a relay started by an older build if its PAC is gone.
                foreach (int pid in FindRelayProcessIds())
                {
                    if (pid == Process.GetCurrentProcess().Id) continue;
                    status.RelayRunning = true;
                    if (status.RelayPort <= 0) status.RelayPort = DefaultRelayPort;
                    break;
                }
            }

            if (status.RelayPort > 0 && status.PacPort > 0 &&
                status.RelayPort == status.PacPort)
            {
                status.PacContentOk = false;
            }

            return status;
        }

        internal static CampusStatus Enable(Action<string> log)
        {
            if (log == null) log = delegate { };

            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(LogDir);

            CampusStatus before = GetStatus();
            if (before.On)
            {
                log("Campus/HCMUS mode is already active on relay port " +
                    before.RelayPort + " and PAC port " + before.PacPort + ".");
                return before;
            }

            log("Stopping any existing Campus/HCMUS relay...");
            StopRelayProcesses();

            int relayPort = FindFreePort(DefaultRelayPort, 0);
            int pacPort = FindFreePort(DefaultPacPort, relayPort);
            if (relayPort <= 0 || pacPort <= 0)
            {
                throw new InvalidOperationException(
                    "No free loopback ports found for the relay/PAC listener.");
            }

            string pac = BuildPac(relayPort);
            File.WriteAllText(PacPath, pac, new UTF8Encoding(false));
            log("PAC written: " + PacPath);
            log("Starting relay: 127.0.0.1:" + relayPort +
                " (PAC 127.0.0.1:" + pacPort + ")");

            string logPath = Path.Combine(
                LogDir,
                "relay-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");

            Process child = StartRelayProcess(relayPort, pacPort, logPath);
            try
            {
                if (!WaitForPort(relayPort, 10000) || !WaitForPort(pacPort, 10000))
                {
                    throw new InvalidOperationException(
                        "The relay/PAC listener did not start on 127.0.0.1:" +
                        relayPort + " / " + pacPort + ".");
                }

                WriteState(new RelayState
                {
                    Pid = child.Id,
                    RelayPort = relayPort,
                    PacPort = pacPort,
                    StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")
                });

                BackupProxyIfNeeded();
                string pacUrl = "http://127.0.0.1:" + pacPort + "/steam.pac";
                ApplyProxy(pacUrl);

                string served = DownloadPac(pacUrl);
                if (served == null ||
                    served.IndexOf("FindProxyForURL", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidOperationException(
                        "The PAC endpoint did not return a valid PAC file.");
                }

                log("Relay and HTTP PAC are running.");
                log("AutoConfigURL = " + pacUrl);
                log("Steam/Valve names -> 127.0.0.1:" + relayPort +
                    "; everything else DIRECT.");
            }
            catch
            {
                try
                {
                    if (child != null && !child.HasExited) child.Kill();
                }
                catch { }
                try { RestoreProxy(delegate { }); } catch { }
                try { if (File.Exists(StatePath)) File.Delete(StatePath); } catch { }
                throw;
            }

            return GetStatus();
        }

        internal static CampusStatus Disable(Action<string> log)
        {
            if (log == null) log = delegate { };

            log("Stopping Campus/HCMUS relay...");
            StopRelayProcesses();
            RestoreProxy(log);
            log("Campus/HCMUS mode is OFF. Proxy settings restored.");
            return GetStatus();
        }

        internal static CampusTestResult Test(Action<string> log)
        {
            if (log == null) log = delegate { };

            CampusTestResult result = new CampusTestResult();
            CampusStatus status = GetStatus();

            log("Campus/HCMUS mode verification");
            log("Relay port: " + (status.RelayPort > 0 ? status.RelayPort.ToString() : "(none)"));
            log("PAC port:   " + (status.PacPort > 0 ? status.PacPort.ToString() : "(none)"));
            log("AutoConfigURL: " + (status.PacUrl == string.Empty ? "(not set)" : status.PacUrl));
            log("");

            if (!status.On)
            {
                log("FAIL: the relay and HTTP PAC are not both active.");
                if (status.Partial)
                {
                    log("PARTIAL: some Campus/HCMUS state remains; use Repair or Turn ON.");
                }
                result.verdict = "FAIL: Campus/HCMUS relay/PAC is not fully active.";
                return result;
            }

            int failures = 0;

            log("PAC delivery:");
            string pac = null;
            try
            {
                pac = DownloadPac(status.PacUrl);
                bool ok = pac != null &&
                    pac.IndexOf("FindProxyForURL", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    pac.IndexOf("PROXY 127.0.0.1:" + status.RelayPort,
                        StringComparison.OrdinalIgnoreCase) >= 0;
                if (!ok) failures++;
                log("  " + (ok ? "PASS" : "FAIL") + "  HTTP 200, " +
                    (pac == null ? 0 : Encoding.UTF8.GetByteCount(pac)) + " bytes");
            }
            catch (Exception ex)
            {
                failures++;
                log("  FAIL  " + ex.Message);
            }

            log("");
            log("WinHTTP PAC decisions:");
            failures += CheckPacDecision(log, status, "Store",
                "https://store.steampowered.com/", true);
            failures += CheckPacDecision(log, status, "Community",
                "https://steamcommunity.com/", true);
            failures += CheckPacDecision(log, status, "CDN",
                "https://cdn.akamai.steamstatic.com/", true);
            failures += CheckPacDecision(log, status, "s.team",
                "https://s.team/", true);
            failures += CheckPacDecision(log, status, "Control",
                "https://example.com/", false);
            failures += CheckPacDecision(log, status, "Lookalike",
                "https://steampowered.com.evil.com/", false);

            log("");
            log("Via the relay:");
            int steamTotal = 3;
            int steamPass = 0;
            if (CheckRelayEndpoint(log, status.RelayPort, "Store",
                "https://store.steampowered.com/", false) == 0) steamPass++;
            if (CheckRelayEndpoint(log, status.RelayPort, "Community",
                "https://steamcommunity.com/", false) == 0) steamPass++;
            if (CheckRelayEndpoint(log, status.RelayPort, "CDN",
                "https://cdn.akamai.steamstatic.com/", false) == 0) steamPass++;
            failures += CheckRelayEndpoint(log, status.RelayPort, "Control",
                "https://example.com/", true);

            log("");
            if (failures > 0)
            {
                result.verdict = "FAIL: " + failures +
                    " PAC or whitelist check(s) failed. See the activity log.";
                log(result.verdict);
            }
            else if (steamPass == 0)
            {
                result.verdict =
                    "FAIL: the relay started but no Steam endpoint answered. See the activity log.";
                log(result.verdict);
            }
            else if (steamPass < steamTotal)
            {
                result.Success = true;
                result.Partial = true;
                result.verdict = "PARTIAL: relay reaches " + steamPass + "/" +
                    steamTotal + " Steam endpoints; the HTTP PAC routes only Steam/Valve names.";
                log(result.verdict);
            }
            else
            {
                result.Success = true;
                result.verdict =
                    "PASS: Campus/HCMUS relay reaches Steam; HTTP PAC routes only Steam/Valve names.";
                log(result.verdict);
            }

            return result;
        }

        // ---- process management -----------------------------------------

        private static Process StartRelayProcess(int relayPort, int pacPort, string logPath)
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = exe;
            info.Arguments = "--relay -p " + relayPort +
                " -pac-port " + pacPort +
                " -log \"" + logPath + "\"";
            info.WorkingDirectory = Path.GetDirectoryName(exe);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            return Process.Start(info);
        }

        private static void StopRelayProcesses()
        {
            HashSet<int> pids = new HashSet<int>();
            RelayState state = ReadState();
            if (state != null && state.Pid > 0)
            {
                pids.Add(state.Pid);
            }

            foreach (int pid in FindRelayProcessIds())
            {
                pids.Add(pid);
            }

            foreach (int pid in FindProcessIdsByName("SteamSnIRelay"))
            {
                pids.Add(pid);
            }

            int current = Process.GetCurrentProcess().Id;
            foreach (int pid in pids)
            {
                if (pid == current) continue;
                try
                {
                    using (Process process = Process.GetProcessById(pid))
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                    }
                }
                catch { }
            }

            try
            {
                if (File.Exists(StatePath)) File.Delete(StatePath);
            }
            catch { }
        }

        private static List<int> FindRelayProcessIds()
        {
            string exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
            List<int> result = new List<int>();

            try
            {
                string query = "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = '" +
                    exeName.Replace("'", "''") + "'";
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        try
                        {
                            string commandLine = Convert.ToString(item["CommandLine"]);
                            if (commandLine != null &&
                                commandLine.IndexOf("--relay", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                result.Add(Convert.ToInt32(item["ProcessId"]));
                            }
                        }
                        finally
                        {
                            item.Dispose();
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        private static List<int> FindProcessIdsByName(string name)
        {
            List<int> result = new List<int>();
            try
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    try { result.Add(process.Id); }
                    finally { process.Dispose(); }
                }
            }
            catch { }
            return result;
        }

        private static bool ProcessAlive(int pid)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    return !process.HasExited;
                }
            }
            catch
            {
                return false;
            }
        }

        // ---- ports -------------------------------------------------------

        internal static bool PortOpen(int port)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult result = client.BeginConnect(
                        IPAddress.Loopback, port, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(400))
                    {
                        return false;
                    }
                    client.EndConnect(result);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool WaitForPort(int port, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (PortOpen(port)) return true;
                Thread.Sleep(150);
            }
            return false;
        }

        private static int FindFreePort(int start, int exclude)
        {
            for (int offset = 0; offset < 60; offset++)
            {
                int port = start + offset;
                if (port == exclude) continue;
                try
                {
                    TcpListener listener = new TcpListener(IPAddress.Loopback, port);
                    listener.Start();
                    listener.Stop();
                    return port;
                }
                catch { }
            }
            return 0;
        }

        // ---- PAC ---------------------------------------------------------

        private static string BuildPac(int relayPort)
        {
            StringBuilder pac = new StringBuilder();
            pac.AppendLine("// Steam Bypass - Campus/HCMUS mode (managed by the GUI).");
            pac.AppendLine("// Steam/Valve names use the local TLS-fragmenting relay; everything else DIRECT.");
            pac.AppendLine("function FindProxyForURL(url, host) {");
            pac.AppendLine("  var h = (host || \"\").toLowerCase();");
            pac.AppendLine("  var suffixes = [");
            for (int i = 0; i < SteamSuffixes.Length; i++)
            {
                pac.Append("    \"");
                pac.Append(SteamSuffixes[i]);
                pac.Append("\"");
                if (i < SteamSuffixes.Length - 1) pac.Append(",");
                pac.AppendLine();
            }
            pac.AppendLine("  ];");
            pac.AppendLine("  for (var i = 0; i < suffixes.length; i++) {");
            pac.AppendLine("    var s = suffixes[i];");
            pac.AppendLine("    if (h === s || dnsDomainIs(h, \".\" + s)) {");
            pac.Append("      return \"PROXY 127.0.0.1:");
            pac.Append(relayPort);
            pac.AppendLine("\";");
            pac.AppendLine("    }");
            pac.AppendLine("  }");
            pac.AppendLine("  return \"DIRECT\";");
            pac.AppendLine("}");
            return pac.ToString();
        }

        private static string DownloadPac(string url)
        {
            using (WebClient client = new WebClient())
            {
                client.Proxy = null;
                client.Encoding = Encoding.UTF8;
                return client.DownloadString(url);
            }
        }

        // ---- proxy settings ---------------------------------------------

        private static string ReadAutoConfigUrl()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetKey, false))
                {
                    if (key == null) return string.Empty;
                    object value = key.GetValue("AutoConfigURL", null);
                    return value == null ? string.Empty : Convert.ToString(value);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void BackupProxyIfNeeded()
        {
            if (File.Exists(BackupPath)) return;

            Dictionary<string, object> backup = new Dictionary<string, object>();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetKey, false))
                {
                    backup["ProxyEnable"] = ReadKeyvalue(key, "ProxyEnable");
                    backup["ProxyServer"] = ReadKeyvalue(key, "ProxyServer");
                    backup["ProxyOverride"] = ReadKeyvalue(key, "ProxyOverride");
                    backup["AutoConfigURL"] = ReadKeyvalue(key, "AutoConfigURL");
                    backup["AutoDetect"] = ReadKeyvalue(key, "AutoDetect");
                }
                backup["SavedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");

                string json = new JavaScriptSerializer().Serialize(backup);
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(BackupPath, json, new UTF8Encoding(false));
            }
            catch
            {
                // A missing backup still leaves the reset path available.
            }
        }

        private static object ReadKeyvalue(RegistryKey key, string name)
        {
            if (key == null) return null;
            return key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        private static void ApplyProxy(string pacUrl)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(InternetKey))
            {
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", string.Empty, RegistryValueKind.String);
                key.SetValue("ProxyOverride", "<local>", RegistryValueKind.String);
                key.SetValue("AutoConfigURL", pacUrl, RegistryValueKind.String);
                key.SetValue("AutoDetect", 0, RegistryValueKind.DWord);
            }
            NotifyProxyChanged();
        }

        private static void RestoreProxy(Action<string> log)
        {
            if (log == null) log = delegate { };

            Dictionary<string, object> backup = ReadBackup();
            if (backup == null)
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(InternetKey))
                    {
                        key.DeleteValue("AutoConfigURL", false);
                        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                        key.SetValue("ProxyServer", string.Empty, RegistryValueKind.String);
                    }
                    log("No proxy backup found; removed the bypass PAC and disabled the proxy.");
                }
                catch (Exception ex)
                {
                    log("Could not reset proxy settings: " + ex.Message);
                }
                NotifyProxyChanged();
                return;
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(InternetKey))
                {
                    SetOrRemove(key, "ProxyEnable", backup.ContainsKey("ProxyEnable") ? backup["ProxyEnable"] : null);
                    SetOrRemove(key, "ProxyServer", backup.ContainsKey("ProxyServer") ? backup["ProxyServer"] : null);
                    SetOrRemove(key, "ProxyOverride", backup.ContainsKey("ProxyOverride") ? backup["ProxyOverride"] : null);
                    SetOrRemove(key, "AutoConfigURL", backup.ContainsKey("AutoConfigURL") ? backup["AutoConfigURL"] : null);
                    SetOrRemove(key, "AutoDetect", backup.ContainsKey("AutoDetect") ? backup["AutoDetect"] : null);
                }
                log("Proxy settings restored from backup.");
            }
            catch (Exception ex)
            {
                log("Could not restore proxy settings: " + ex.Message);
            }

            try { File.Delete(BackupPath); } catch { }
            NotifyProxyChanged();
        }

        private static Dictionary<string, object> ReadBackup()
        {
            try
            {
                if (!File.Exists(BackupPath)) return null;
                string json = File.ReadAllText(BackupPath);
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            }
            catch
            {
                return null;
            }
        }

        private static void SetOrRemove(RegistryKey key, string name, object value)
        {
            if (value == null)
            {
                key.DeleteValue(name, false);
                return;
            }

            if (value is int || value is long)
            {
                key.SetValue(name, Convert.ToInt32(value), RegistryValueKind.DWord);
            }
            else if (value is bool)
            {
                key.SetValue(name, ((bool)value) ? 1 : 0, RegistryValueKind.DWord);
            }
            else
            {
                key.SetValue(name, Convert.ToString(value), RegistryValueKind.String);
            }
        }

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(
            IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private static void NotifyProxyChanged()
        {
            try
            {
                InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
            }
            catch { }
        }

        // ---- state -------------------------------------------------------

        private static RelayState ReadState()
        {
            try
            {
                if (!File.Exists(StatePath)) return null;
                string json = File.ReadAllText(StatePath);
                return new JavaScriptSerializer().Deserialize<RelayState>(json);
            }
            catch
            {
                return null;
            }
        }

        private static void WriteState(RelayState state)
        {
            string json = new JavaScriptSerializer().Serialize(state);
            File.WriteAllText(StatePath, json, new UTF8Encoding(false));
        }

        private static int ParseInt(string value)
        {
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : 0;
        }

        // ---- verification helpers ---------------------------------------

        private static int CheckPacDecision(
            Action<string> log,
            CampusStatus status,
            string name,
            string url,
            bool expectProxy)
        {
            WinHttpDecision decision = GetWinHttpDecision(status.PacUrl, url);
            bool ok;
            string detail;

            if (!decision.Ok)
            {
                ok = false;
                detail = decision.Error;
            }
            else if (expectProxy)
            {
                ok = decision.AccessType == "PROXY" &&
                    string.Equals(decision.Proxy,
                        "127.0.0.1:" + status.RelayPort,
                        StringComparison.OrdinalIgnoreCase);
                detail = decision.AccessType +
                    (decision.Proxy == string.Empty ? string.Empty : " " + decision.Proxy);
            }
            else
            {
                ok = decision.AccessType == "DIRECT";
                detail = decision.AccessType;
            }

            log("  " + (ok ? "PASS" : "FAIL") + "  " + name + ": " + detail);
            return ok ? 0 : 1;
        }

        private static int CheckRelayEndpoint(
            Action<string> log,
            int relayPort,
            string name,
            string url,
            bool expectRefused)
        {
            ProxyCheck check = RequestThroughProxy(relayPort, url);
            bool ok;
            string detail;

            if (expectRefused)
            {
                ok = !check.Ok || check.Code == 403;
                detail = check.Ok ? ("HTTP " + check.Code) : check.Error;
            }
            else
            {
                ok = check.Ok && check.Code > 0;
                detail = check.Ok ? ("HTTP " + check.Code) : check.Error;
            }

            log("  " + (ok ? "PASS" : "FAIL") + "  " + name + ": " + detail);
            return ok ? 0 : 1;
        }

        private static ProxyCheck RequestThroughProxy(int relayPort, string url)
        {
            ProxyCheck result = new ProxyCheck();
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Proxy = new WebProxy("http://127.0.0.1:" + relayPort, false);
                request.Timeout = 12000;
                request.ReadWriteTimeout = 12000;
                request.AllowAutoRedirect = false;
                request.UserAgent = "SteamBypass/2.0";
                request.Method = "GET";

                try
                {
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        result.Ok = true;
                        result.Code = (int)response.StatusCode;
                    }
                }
                catch (WebException ex)
                {
                    HttpWebResponse response = ex.Response as HttpWebResponse;
                    if (response != null)
                    {
                        using (response)
                        {
                            result.Ok = true;
                            result.Code = (int)response.StatusCode;
                        }
                    }
                    else
                    {
                        result.Error = ex.Status + ": " + ex.Message;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            return result;
        }

        private static WinHttpDecision GetWinHttpDecision(string pacUrl, string url)
        {
            WinHttpDecision result = new WinHttpDecision();
            IntPtr session = IntPtr.Zero;
            try
            {
                session = WinHttpOpen(
                    "SteamBypass-PAC-Test",
                    0, null, null, 0);
                if (session == IntPtr.Zero)
                {
                    result.Error = "WinHttpOpen failed: " +
                        Marshal.GetLastWin32Error();
                    return result;
                }

                AutoProxyOptions options = new AutoProxyOptions();
                options.dwFlags = 2; // WINHTTP_AUTOPROXY_CONFIG_URL
                options.dwAutoDetectFlags = 0;
                options.lpszAutoConfigUrl = pacUrl;
                options.lpiReserved = IntPtr.Zero;
                options.dwReserved = 0;
                options.fAutoLogonIfChallenged = true;

                ProxyInfo info = new ProxyInfo();
                if (!WinHttpGetProxyForUrl(session, url, ref options, ref info))
                {
                    result.Error = "WinHTTP error " + Marshal.GetLastWin32Error();
                    return result;
                }

                result.Ok = true;
                result.AccessType = AccessTypeName(info.dwAccessType);
                result.Proxy = PtrToStringAndFree(info.lpszProxy);
                result.Bypass = PtrToStringAndFree(info.lpszProxyBypass);
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
            finally
            {
                if (session != IntPtr.Zero)
                {
                    WinHttpCloseHandle(session);
                }
            }
        }

        private static string PtrToStringAndFree(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return string.Empty;
            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                try { GlobalFree(pointer); } catch { }
            }
        }

        private static string AccessTypeName(int accessType)
        {
            switch (accessType)
            {
                case 0: return "DEFAULT";
                case 1: return "DIRECT";
                case 3: return "PROXY";
                case 4: return "AUTO";
                default: return "TYPE_" + accessType;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct AutoProxyOptions
        {
            public int dwFlags;
            public int dwAutoDetectFlags;
            public string lpszAutoConfigUrl;
            public IntPtr lpiReserved;
            public int dwReserved;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAutoLogonIfChallenged;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProxyInfo
        {
            public int dwAccessType;
            public IntPtr lpszProxy;
            public IntPtr lpszProxyBypass;
        }

        [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr WinHttpOpen(
            string pszAgentW,
            int dwAccessType,
            string pszProxyW,
            string pszProxyBypassW,
            int dwFlags);

        [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool WinHttpGetProxyForUrl(
            IntPtr hSession,
            string lpcwszUrl,
            ref AutoProxyOptions pAutoProxyOptions,
            ref ProxyInfo pProxyInfo);

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpCloseHandle(IntPtr hInternet);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);
    }
}
