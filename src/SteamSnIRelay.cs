using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// SteamSnIRelay - localhost-only HTTP CONNECT proxy that re-emits client->server
// TLS records split into small fragments. The campus middlebox resets a Steam
// ClientHello when it sits inside one large TLS record (observed: reset at
// >=1600 bytes, pass at <=1500). Splitting records defeats the SNI filter
// without any tunnel, driver, or kernel component, and without touching
// non-Steam traffic.
//
// It also serves the generated PAC over loopback HTTP when -pac-port is given.
// Chromium/CEF (including the Steam web helper) ignores file:// PAC URLs, so
// the PAC must be delivered over http://127.0.0.1:<port>/steam.pac.
//
// It does NOT decrypt, modify, or inspect beyond TLS record boundaries.
// It is not a TLS-terminating proxy and needs no certificates.
// Only Steam/Valve hostnames are accepted (whitelist enforced twice: in the
// client PAC and in this relay). Otherwise it refuses with 403.
//
// Usage:
//   SteamSnIRelay.exe                 listen on 127.0.0.1:8787, handshake fragment 64
//   SteamSnIRelay.exe -p 9000 -f 64   choose port / handshake fragment size
//   SteamSnIRelay.exe -pac-port 8788  also serve %LOCALAPPDATA%\HCMUS-Campus\steam.pac
//   SteamSnIRelay.exe -log <path>     log file (default: stdout + logs\relay-<stamp>.log next to exe)
//
// Build with the inbox .NET Framework compiler; no SDK required.

internal static class SteamSnIRelayProgram
{
    private const string Version = "1.2.0";
    private const int DefaultPort = 8787;
    private const int DefaultSplit = 512;
    private const int DefaultForce = 64;
    private const int MaxRecord = 16384;          // TLS record payload limit
    private const int MaxHeaderLine = 8192;

    private static readonly string[] SteamSuffixes = new string[]
    {
        "steampowered.com","steamcommunity.com","steamstatic.com","steamgames.com",
        "valvesoftware.com","steam-chat.com","steam.tv","steamdeck.com","steamcloud.com",
        "steamcontent.com","steamusercontent.com","steamserver.net","steamconnect.com",
        "steamcdn-a.akamaihd.net","steamuserimages-a.akamaihd.net","steamstore-a.akamaihd.net",
        "steamcommunity-a.akamaihd.net","steamstatic-a.akamaihd.net","s.team"
    };

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HCMUS-Campus");
    private static readonly string PacPath = Path.Combine(DataDir, "steam.pac");

    private static StreamWriter _log;
    private static readonly object _logLock = new object();
    private static int _split = DefaultSplit;
    private static int _force = DefaultForce;
    private static int _connectCount;
    private static TcpListener _pacListener;
    private static volatile bool _quit;

    private static void Log(string message)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;
        try { Console.WriteLine(line); } catch { }
        StreamWriter w = _log;
        if (w != null)
        {
            lock (_logLock) { try { w.WriteLine(line); w.Flush(); } catch { } }
        }
    }

    internal static int Run(string[] args)
    {
        int port = DefaultPort;
        int split = DefaultSplit;
        int force = DefaultForce;
        int pacPort = 0;
        string logPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].ToLowerInvariant();
            if (a == "-p" && i + 1 < args.Length) { port = int.Parse(args[++i]); }
            else if (a == "-s" && i + 1 < args.Length) { split = int.Parse(args[++i]); }
            else if (a == "-f" && i + 1 < args.Length) { force = int.Parse(args[++i]); }
            else if (a == "-pac-port" && i + 1 < args.Length) { pacPort = int.Parse(args[++i]); }
            else if (a == "-log" && i + 1 < args.Length) { logPath = args[++i]; }
            else if (a == "-quit") { return 0; }
            else if (a == "-v" || a == "--version") { Console.WriteLine("SteamSnIRelay " + Version); return 0; }
            else if (a == "-h" || a == "--help") { Usage(); return 0; }
        }

        if (split < 512) split = 512;
        if (split > 1500) split = 1500;    // measured filter threshold; never exceed
        if (force < 16) force = 16;
        if (force > 512) force = 512;
        if (pacPort < 0 || pacPort > 65535)
        {
            try { Console.Error.WriteLine("Invalid PAC port: " + pacPort); } catch { }
            return 2;
        }
        if (pacPort > 0 && !File.Exists(PacPath))
        {
            try { Console.Error.WriteLine("PAC file not found: " + PacPath); } catch { }
            return 2;
        }
        _split = split;
        _force = force;

        try
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        }
        catch { /* best effort; DoH will fail over to system DNS */ }

        try
        {
            Directory.CreateDirectory(DataDir);
            if (logPath == null)
            {
                string logs = Path.Combine(DataDir, "logs");
                Directory.CreateDirectory(logs);
                logPath = Path.Combine(logs, "relay-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            }
            _log = new StreamWriter(logPath, true, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine("Could not open log: " + ex.Message); } catch { }
        }

        TcpListener listener = null;
        for (int offset = 0; offset <= 50; offset++)
        {
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port + offset);
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
                break;
            }
            catch
            {
                listener = null;
            }
        }

        if (listener == null)
        {
            try { Console.Error.WriteLine("No free port found in range 8787-8837."); } catch { }
            return 2;
        }

        TcpListener pacListener = null;
        if (pacPort > 0)
        {
            try
            {
                pacListener = new TcpListener(IPAddress.Loopback, pacPort);
                pacListener.Start();
                pacPort = ((IPEndPoint)pacListener.LocalEndpoint).Port;
                _pacListener = pacListener;
            }
            catch (Exception ex)
            {
                listener.Stop();
                try { Console.Error.WriteLine("Could not start PAC server on 127.0.0.1:" + pacPort + ": " + ex.Message); } catch { }
                return 2;
            }
        }

        try
        {
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                Log("Shutdown requested.");
                _quit = true;
                listener.Stop();
                if (_pacListener != null) _pacListener.Stop();
            };
        }
        catch { }
        AppDomain.CurrentDomain.ProcessExit += delegate { Log("Process exit."); };

        Log(string.Format("SteamSnIRelay {0} listening on 127.0.0.1:{1} (handshake fragment {2} bytes, split {3}).", Version, port, _force, _split));
        Log("Steam/Valve hostnames only. Non-Steam requests get 403.");
        if (pacListener != null)
        {
            Log(string.Format("PAC: http://127.0.0.1:{0}/steam.pac -> {1}", pacPort, PacPath));
            TcpListener pac = pacListener;
            Task.Run(delegate { AcceptPacClients(pac); });
        }
        Log("Log: " + (_log != null ? logPath : "(none)"));

        while (!_quit)
        {
            TcpClient accepted;
            try { accepted = listener.AcceptTcpClient(); }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { Log("Accept error: " + ex.Message); continue; }
            Task ignored = Task.Run(async delegate
            {
                try { await HandleClient(accepted).ConfigureAwait(false); }
                catch (Exception ex) { Log("Worker error: " + ex.Message); }
            });
        }

        return 0;
    }

    private static void Usage()
    {
        Console.WriteLine("SteamSnIRelay - local TLS-fragmenting CONNECT relay for Steam");
        Console.WriteLine("  -p <port>        listen port (default 8787, scans up to +50)");
        Console.WriteLine("  -s <bytes>       TLS fragment size 512..1500 (default 512)");
        Console.WriteLine("  -f <bytes>       handshake fragment size 16..512 (default 64)");
        Console.WriteLine("  -pac-port <port> also serve " + PacPath + " at /steam.pac");
        Console.WriteLine("  -log <path>      log file path");
        Console.WriteLine("  -v | -h          version / help");
    }

    private static bool IsSteamHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        string h = host.Trim().TrimEnd('.').ToLowerInvariant();
        for (int i = 0; i < SteamSuffixes.Length; i++)
        {
            string s = SteamSuffixes[i];
            if (h == s || h.EndsWith("." + s, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private sealed class Request
    {
        public string Method;
        public string Target;
        public string Host;
        public int Port;
    }

    private static Request ReadRequestHeader(NetworkStream client)
    {
        StringBuilder sb = new StringBuilder(256);
        byte[] b = new byte[1];
        int days = 0;
        while (true)
        {
            if (client.Read(b, 0, 1) != 1) throw new EndOfStreamException("Connection closed before request.");
            sb.Append((char)b[0]);
            days++;
            if (days > MaxHeaderLine) throw new InvalidDataException("Request header too large.");
            string s = sb.ToString();
            if (s.EndsWith("\r\n\r\n")) break;
        }

        string[] lines = sb.ToString().Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) throw new InvalidDataException("Empty request.");
        string[] parts = lines[0].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) throw new InvalidDataException("Malformed request line.");

        Request req = new Request();
        req.Method = parts[0].ToUpperInvariant();
        req.Target = parts[1];
        if (!req.Method.Equals("CONNECT", StringComparison.Ordinal))
        {
            // This is a localhost-only whitelist proxy; plain HTTP is never
            // useful or safe here, so refuse it explicitly.
            throw new NotSupportedException("Only CONNECT is supported.");
        }

        string target = req.Target;
        int colon = target.LastIndexOf(':');
        if (colon > 0 && target.IndexOf(']') == -1)
        {
            req.Host = target.Substring(0, colon);
            req.Port = int.Parse(target.Substring(colon + 1));
        }
        else if (target.StartsWith("["))
        {
            int end = target.IndexOf(']');
            req.Host = target.Substring(1, end - 1);
            int c = target.LastIndexOf(':');
            req.Port = c > end ? int.Parse(target.Substring(c + 1)) : 443;
        }
        else
        {
            req.Host = target;
            req.Port = 443;
        }
        return req;
    }

    private static async Task HandleClient(TcpClient client)
    {
        try
        {
            client.NoDelay = true;
            client.ReceiveTimeout = 30000;
            client.SendTimeout = 30000;
            NetworkStream clientStream = client.GetStream();

            Request req = ReadRequestHeader(clientStream);
            Interlocked.Increment(ref _connectCount);

            if (!IsSteamHost(req.Host))
            {
                Log("DENY " + req.Host + " (not a Steam/Valve host)");
                WriteStatus(clientStream, "403 Forbidden", "Host not allowed.");
                client.Close();
                return;
            }

            List<IPAddress> candidates = ResolveAddresses(req.Host);
            TcpClient remote = null;
            foreach (IPAddress ip in candidates)
            {
                try
                {
                    TcpClient t = new TcpClient();
                    t.NoDelay = true;
                    Task connectTask = t.ConnectAsync(ip, req.Port);
                    if (!connectTask.Wait(2500) || !t.Connected)
                    {
                        try { t.Close(); } catch { }
                        continue;
                    }
                    remote = t;
                    Log(string.Format("RESOLVE {0} -> {1}", req.Host, ip));
                    break;
                }
                catch
                {
                    continue;
                }
            }

            if (remote == null)
            {
                Log("502 " + req.Host + ":" + req.Port + " unreachable (tried " + candidates.Count + " address(es))");
                WriteStatus(clientStream, "502 Bad Gateway", "Upstream unreachable.");
                client.Close();
                return;
            }

            Log("OPEN " + req.Host + ":" + req.Port);
            WriteStatus(clientStream, "200 Connection Established", null);
            NetworkStream remoteStream = remote.GetStream();

            Task c2r = PumpClientToRemoteAsync(clientStream, remoteStream);
            Task r2c = PumpRemoteToClientAsync(remoteStream, clientStream);
            await Task.WhenAny(c2r, r2c).ConfigureAwait(false);
            TryShutdown(remote.Client);
            TryShutdown(client.Client);
            await Task.Delay(2000).ConfigureAwait(false);
            Log("CLOSE " + req.Host);
            client.Close();
            remote.Close();
        }
        catch (NotSupportedException ex)
        {
            try { WriteStatus(client.GetStream(), "405 Method Not Allowed", ex.Message); } catch { }
            Log("405: " + ex.Message);
            client.Close();
        }
        catch (Exception ex)
        {
            if (!(ex is EndOfStreamException)) Log("ERROR: " + ex.Message);
            try { client.Close(); } catch { }
        }
    }

    private static void AcceptPacClients(TcpListener listener)
    {
        while (!_quit)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                if (!_quit) Log("PAC accept error: " + ex.Message);
                continue;
            }

            Task.Run(delegate
            {
                try { HandlePacClient(client); }
                catch (Exception ex)
                {
                    Log("PAC worker error: " + ex.Message);
                    try { client.Close(); } catch { }
                }
            });
        }
    }

    private static void HandlePacClient(TcpClient client)
    {
        try
        {
            client.NoDelay = true;
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            NetworkStream stream = client.GetStream();

            string requestLine = ReadHttpLine(stream);
            if (string.IsNullOrEmpty(requestLine)) return;

            string[] parts = requestLine.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string method = parts.Length > 0 ? parts[0].ToUpperInvariant() : "";
            string target = parts.Length > 1 ? parts[1] : "";

            int headerBytes = 0;
            while (true)
            {
                string line = ReadHttpLine(stream);
                if (string.IsNullOrEmpty(line)) break;
                headerBytes += line.Length;
                if (headerBytes > MaxHeaderLine) throw new InvalidDataException("PAC request header too large.");
            }

            string path = target;
            int query = path.IndexOf('?');
            if (query >= 0) path = path.Substring(0, query);

            if ((method != "GET" && method != "HEAD") ||
                !path.Equals("/steam.pac", StringComparison.OrdinalIgnoreCase))
            {
                byte[] notFound = Encoding.UTF8.GetBytes("Not found.\r\n");
                WriteHttpResponse(stream, "404 Not Found", "text/plain; charset=utf-8", notFound, method == "HEAD");
                Log("PAC " + method + " " + path + " -> 404");
                return;
            }

            byte[] body;
            try
            {
                body = File.ReadAllBytes(PacPath);
            }
            catch (Exception ex)
            {
                byte[] unavailable = Encoding.UTF8.GetBytes("PAC file unavailable.\r\n");
                WriteHttpResponse(stream, "503 Service Unavailable", "text/plain; charset=utf-8", unavailable, method == "HEAD");
                Log("PAC read error: " + ex.Message);
                return;
            }

            WriteHttpResponse(stream, "200 OK", "application/x-ns-proxy-autoconfig", body, method == "HEAD");
            Log("PAC " + method + " /steam.pac -> 200 (" + body.Length + " bytes)");
        }
        finally
        {
            try { client.Close(); } catch { }
        }
    }

    private static string ReadHttpLine(NetworkStream stream)
    {
        StringBuilder sb = new StringBuilder(128);
        byte[] b = new byte[1];
        int total = 0;
        while (true)
        {
            int read;
            try { read = stream.Read(b, 0, 1); }
            catch { return null; }
            if (read != 1) return null;
            total++;
            if (total > MaxHeaderLine) throw new InvalidDataException("HTTP request header too large.");
            char c = (char)b[0];
            if (c == '\n') break;
            if (c != '\r') sb.Append(c);
        }
        return sb.ToString();
    }

    private static void WriteHttpResponse(NetworkStream stream, string status, string contentType, byte[] body, bool headOnly)
    {
        string header = "HTTP/1.1 " + status + "\r\n" +
            "Content-Type: " + contentType + "\r\n" +
            "Content-Length: " + body.Length + "\r\n" +
            "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
            "Pragma: no-cache\r\n" +
            "Connection: close\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        stream.Write(headerBytes, 0, headerBytes.Length);
        if (!headOnly && body.Length > 0)
        {
            stream.Write(body, 0, body.Length);
        }
        stream.Flush();
    }

    private static List<IPAddress> ResolveAddresses(string host)
    {
        List<IPAddress> ips = new List<IPAddress>();

        // Prefer DoH over HTTPS to dns.google: the campus network's UDP/53 and
        // TCP/53 paths are unreliable, and HTTPS works consistently. This also
        // bypasses the machine's stale third-party hosts pins for Steam.
        try { ips.AddRange(ResolveViaDoh("https://dns.google/resolve?name=" + host + "&type=A")); } catch { }
        if (ips.Count == 0) { try { ips.AddRange(ResolveViaDoh("https://1.1.1.1/dns-query?name=" + host + "&type=A")); } catch { } }

        if (ips.Count == 0)
        {
            try
            {
                IPAddress[] sys = Dns.GetHostAddresses(host);
                foreach (IPAddress a in sys)
                {
                    if (a.AddressFamily == AddressFamily.InterNetwork && !ips.Contains(a)) ips.Add(a);
                }
            }
            catch { }
        }

        List<IPAddress> result = new List<IPAddress>();
        foreach (IPAddress a in ips) { if (!result.Contains(a)) result.Add(a); }
        return result;
    }

    private static List<IPAddress> ResolveViaDoh(string url)
    {
        List<IPAddress> ips = new List<IPAddress>();
        try
        {
            HttpWebRequest http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = "GET";
            http.Accept = "application/dns-json";
            http.Timeout = 5000;
            http.ReadWriteTimeout = 5000;
            http.UserAgent = "SteamSnIRelay/1.0";
            http.AllowAutoRedirect = false;

            using (HttpWebResponse resp = (HttpWebResponse)http.GetResponse())
            using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string json = reader.ReadToEnd();
                Match status = Regex.Match(json, "\"Status\"\\s*:\\s*(\\d+)");
                if (status.Success && status.Groups[1].Value != "0") { return ips; }
                foreach (Match m in Regex.Matches(json, "\\b([0-9]{1,3}(?:\\.[0-9]{1,3}){3})\\b"))
                {
                    IPAddress a;
                    if (IPAddress.TryParse(m.Groups[1].Value, out a) &&
                        a.AddressFamily == AddressFamily.InterNetwork &&
                        !ips.Contains(a))
                    {
                        ips.Add(a);
                    }
                }
            }
        }
        catch { }
        return ips;
    }

    private static void TryShutdown(Socket socket)
    {
        try { socket.Shutdown(SocketShutdown.Send); } catch { }
    }

    private static void WriteStatus(NetworkStream stream, string status, string body)
    {
        // A successful CONNECT response must be a bare tunnel header. Sending
        // Content-Length/Connection headers here makes some clients (notably
        // .NET's HttpWebRequest) treat the tunnel as a closed HTTP response.
        if (body == null && status.StartsWith("200", StringComparison.Ordinal))
        {
            byte[] established = Encoding.ASCII.GetBytes(
                "HTTP/1.1 " + status + "\r\n\r\n");
            stream.Write(established, 0, established.Length);
            stream.Flush();
            return;
        }

        string payload = (body == null) ? "" : ("{\"error\":\"" + body.Replace("\"", "'") + "\"}");
        string headers = "HTTP/1.1 " + status + "\r\n" +
            "Proxy-agent: SteamSnIRelay/" + Version + "\r\n" +
            "Content-Type: application/json\r\n" +
            "Content-Length: " + Encoding.ASCII.GetByteCount(payload) + "\r\n" +
            "Connection: close\r\n\r\n" + payload;
        byte[] bytes = Encoding.ASCII.GetBytes(headers);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    // ---- TLS record handling ------------------------------------------

    // A minimal parser. If the client->server bytes do not look like a TLS
    // record stream (e.g. plain HTTP despite a proxy CONNECT), they are passed
    // through unchanged and logged once.
    private sealed class TlsRecordParser
    {
        private readonly int _split;
        private readonly int _force;
        private bool _appDataSeen;
        private readonly List<byte> _pending = new List<byte>();
        private bool _announcedNonTls;
        private int _recordsSplit;

        public TlsRecordParser(int split, int force)
        {
            _split = split;
            _force = force;
        }

        public IEnumerable<byte[]> Feed(byte[] data, int offset, int count)
        {
            for (int i = 0; i < count; i++) _pending.Add(data[offset + i]);

            List<byte[]> output = new List<byte[]>();
            while (true)
            {
                if (_pending.Count >= 5)
                {
                    bool tls = IsTlsRecordStart(_pending[0], _pending[1], _pending[2]);
                    if (!tls)
                    {
                        if (!_announcedNonTls) Log("client->server stream is not TLS; passing through unchanged.");
                        _announcedNonTls = true;
                        byte[] all = _pending.ToArray();
                        _pending.Clear();
                        output.Add(all);
                        break;
                    }

                    int length = (_pending[3] << 8) | _pending[4];
                    if (length > MaxRecord + 2048)
                    {
                        _announcedNonTls = true;
                        byte[] all = _pending.ToArray();
                        _pending.Clear();
                        output.Add(all);
                        break;
                    }

                    if (_pending.Count >= 5 + length)
                    {
                        byte[] header = _pending.GetRange(0, 5).ToArray();
                        byte[] payload = _pending.GetRange(5, length).ToArray();
                        _pending.RemoveRange(0, 5 + length);

                        if (header[0] == 0x17) { _appDataSeen = true; }
                        bool handshake = (!_appDataSeen && (header[0] == 0x16 || header[0] == 0x14));
                        // Handshake phase records are always force-split into
                        // small chunks: Schannel's ClientHello can be under a
                        // larger threshold and would otherwise pass untouched
                        // and be reset by the middlebox. App data passes through.
                        int maxChunk = handshake ? Math.Min(_force, _split)
                            : (_appDataSeen ? int.MaxValue : _split);

                        if (length > maxChunk)
                        {
                            _recordsSplit++;
                            for (int p = 0; p < payload.Length; p += maxChunk)
                            {
                                int chunk = Math.Min(maxChunk, payload.Length - p);
                                byte[] rec = new byte[5 + chunk];
                                rec[0] = header[0];
                                rec[1] = header[1];
                                rec[2] = header[2];
                                rec[3] = (byte)((chunk >> 8) & 0xff);
                                rec[4] = (byte)(chunk & 0xff);
                                Buffer.BlockCopy(payload, p, rec, 5, chunk);
                                output.Add(rec);
                            }
                        }
                        else
                        {
                            byte[] rec = new byte[5 + length];
                            Buffer.BlockCopy(header, 0, rec, 0, 5);
                            Buffer.BlockCopy(payload, 0, rec, 5, length);
                            output.Add(rec);
                        }
                    }
                    else
                    {
                        // Wait for the rest of this record.
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            return output;
        }

        public IEnumerable<byte[]> Flush()
        {
            List<byte[]> output = new List<byte[]>();
            if (_pending.Count > 0)
            {
                output.Add(_pending.ToArray());
                _pending.Clear();
            }
            if (_recordsSplit > 0) Log("client->server TLS fragmentation applied (" + _recordsSplit + " record(s) split).");
            return output;
        }

        private static bool IsTlsRecordStart(byte b0, byte b1, byte b2)
        {
            if (b0 < 0x14 || b0 > 0x17) return false;          // change_cipher / alert / handshake / appdata
            if (b1 != 0x03) return false;                       // legacy record version major
            if (b2 < 0x00 || b2 > 0x04) return false;           // minor: ssl3..tls1.3
            return true;
        }
    }

    private static async Task PumpClientToRemoteAsync(NetworkStream client, NetworkStream remote)
    {
        TlsRecordParser parser = new TlsRecordParser(_split, _force);
        byte[] buffer = new byte[16384];
        try
        {
            while (true)
            {
                int read = await client.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read <= 0) break;
                foreach (byte[] rec in parser.Feed(buffer, 0, read))
                {
                    await remote.WriteAsync(rec, 0, rec.Length).ConfigureAwait(false);
                }
                await remote.FlushAsync().ConfigureAwait(false);
            }
            foreach (byte[] rec in parser.Flush())
            {
                await remote.WriteAsync(rec, 0, rec.Length).ConfigureAwait(false);
            }
        }
        catch (IOException) { /* client went away */ }
        catch (SocketException) { }
    }

    private static async Task PumpRemoteToClientAsync(NetworkStream remote, NetworkStream client)
    {
        byte[] buffer = new byte[16384];
        try
        {
            while (true)
            {
                int read = await remote.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read <= 0) break;
                await client.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                await client.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (IOException) { }
        catch (SocketException) { }
    }
}
