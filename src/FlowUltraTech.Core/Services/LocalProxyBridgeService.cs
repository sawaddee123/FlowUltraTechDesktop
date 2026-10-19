using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

/// <summary>
/// Native Local Loopback Proxy Bridge (127.0.0.1:{port}) that allows Chromium / TikTok LIVE Studio
/// to route traffic through HTTP, HTTPS, or SOCKS5 proxies (including Username/Password authentication)
/// natively via --proxy-server="http://127.0.0.1:{port}" without requiring external Proxifier software.
/// </summary>
public sealed class LocalProxyBridgeService : IDisposable
{
    public static LocalProxyBridgeService Shared { get; } = new();

    private readonly ConcurrentDictionary<Guid, BridgeSession> _sessions = new();
    private bool _disposed;

    private sealed class BridgeSession : IDisposable
    {
        public Guid InstanceId { get; }
        public ProxyItem Upstream { get; set; }
        public TcpListener Listener { get; }
        public int LocalPort { get; }
        public CancellationTokenSource Cts { get; } = new();

        public BridgeSession(Guid instanceId, ProxyItem upstream, TcpListener listener, int localPort)
        {
            InstanceId = instanceId;
            Upstream = upstream;
            Listener = listener;
            LocalPort = localPort;
        }

        public void Dispose()
        {
            try { Cts.Cancel(); } catch { }
            try { Listener.Stop(); } catch { }
            try { Cts.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Resolves the effective ProxyItem for an instance (either its custom per-instance proxy or an assigned pool proxy).
    /// Returns null if no proxy is enabled for this instance.
    /// </summary>
    public static ProxyItem? ResolveEffectiveProxy(StudioInstanceProfile profile, AppSettings settings)
    {
        if (profile == null) return null;

        if (profile.UseCustomProxy && !string.IsNullOrWhiteSpace(profile.ProxyHost) && profile.ProxyPort > 0)
        {
            return new ProxyItem
            {
                Id = profile.Id,
                Name = $"{profile.Name}-CustomProxy",
                Protocol = profile.ProxyProtocol,
                Host = profile.ProxyHost.Trim(),
                Port = profile.ProxyPort,
                Username = profile.ProxyUsername?.Trim() ?? string.Empty,
                Password = profile.ProxyPassword ?? string.Empty,
                IsActive = true
            };
        }

        if (profile.ProxyId.HasValue && settings?.Proxies != null)
        {
            var matched = settings.Proxies.Find(p => p.Id == profile.ProxyId.Value && p.IsActive);
            if (matched != null && !string.IsNullOrWhiteSpace(matched.Host) && matched.Port > 0)
            {
                return matched;
            }
        }

        return null;
    }

    /// <summary>
    /// Starts or updates the local loopback HTTP proxy bridge for the given instance and returns the local port.
    /// </summary>
    public int EnsureBridgeForInstance(Guid instanceId, ProxyItem upstreamProxy)
    {
        if (upstreamProxy == null) throw new ArgumentNullException(nameof(upstreamProxy));

        if (_sessions.TryGetValue(instanceId, out var existing))
        {
            existing.Upstream = upstreamProxy;
            return existing.LocalPort;
        }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int localPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        var session = new BridgeSession(instanceId, upstreamProxy, listener, localPort);
        if (_sessions.TryAdd(instanceId, session))
        {
            _ = Task.Run(() => AcceptLoopAsync(session));
            return localPort;
        }
        else
        {
            session.Dispose();
            return _sessions[instanceId].LocalPort;
        }
    }

    public void StopBridgeForInstance(Guid instanceId)
    {
        if (_sessions.TryRemove(instanceId, out var session))
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// Tests the given proxy configuration by routing an HTTP/HTTPS IP check through a temporary local bridge
    /// and measuring the round-trip latency and exit IP / Country / ISP.
    /// </summary>
    public async Task<ProxyTestResult> TestProxyAsync(ProxyItem proxy, CancellationToken ct = default)
    {
        if (proxy == null || string.IsNullOrWhiteSpace(proxy.Host) || proxy.Port <= 0 || proxy.Port > 65535)
        {
            return new ProxyTestResult
            {
                Success = false,
                Message = "กรุณาระบุ Host / IP และ Port (1-65535) ให้ถูกต้อง"
            };
        }

        var tempId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        try
        {
            int localPort = EnsureBridgeForInstance(tempId, proxy);
            using var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"http://127.0.0.1:{localPort}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(8)
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FlowUltraTech-ProxyTester/1.6");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            // Primary check: ip-api.com (returns IP, Country, ISP)
            try
            {
                var resp = await client.GetAsync("http://ip-api.com/json/?fields=status,message,country,countryCode,isp,query", cts.Token);
                if (resp.IsSuccessStatusCode)
                {
                    sw.Stop();
                    string json = await resp.Content.ReadAsStringAsync(cts.Token);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    string ip = root.TryGetProperty("query", out var qEl) ? qEl.GetString() ?? "" : "";
                    string country = root.TryGetProperty("countryCode", out var ccEl) ? ccEl.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(country) && root.TryGetProperty("country", out var cEl))
                        country = cEl.GetString() ?? "";
                    string isp = root.TryGetProperty("isp", out var ispEl) ? ispEl.GetString() ?? "" : "";

                    if (!string.IsNullOrWhiteSpace(ip))
                    {
                        string locationInfo = !string.IsNullOrWhiteSpace(country)
                            ? (!string.IsNullOrWhiteSpace(isp) ? $"{country} • {isp}" : country)
                            : isp;
                        return new ProxyTestResult
                        {
                            Success = true,
                            ExitIp = ip,
                            Country = country,
                            Isp = isp,
                            LatencyMs = sw.ElapsedMilliseconds,
                            Message = string.IsNullOrWhiteSpace(locationInfo)
                                ? $"เชื่อมต่อสำเร็จ • IP: {ip} ({sw.ElapsedMilliseconds} ms)"
                                : $"เชื่อมต่อสำเร็จ • IP: {ip} ({locationInfo}) • {sw.ElapsedMilliseconds} ms"
                        };
                    }
                }
            }
            catch
            {
                // Fallback to HTTPS ipify if HTTP ip-api is blocked by proxy
            }

            sw.Restart();
            var fallbackResp = await client.GetAsync("https://api.ipify.org?format=json", cts.Token);
            sw.Stop();
            if (fallbackResp.IsSuccessStatusCode)
            {
                string json = await fallbackResp.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(json);
                string ip = doc.RootElement.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() ?? "Unknown" : "Unknown";
                return new ProxyTestResult
                {
                    Success = true,
                    ExitIp = ip,
                    LatencyMs = sw.ElapsedMilliseconds,
                    Message = $"เชื่อมต่อสำเร็จ • IP: {ip} ({sw.ElapsedMilliseconds} ms)"
                };
            }

            return new ProxyTestResult
            {
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"เชื่อมต่อไม่สำเร็จ (HTTP {(int)fallbackResp.StatusCode})"
            };
        }
        catch (OperationCanceledException)
        {
            return new ProxyTestResult
            {
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = "หมดเวลาเชื่อมต่อ (Timeout) กรุณาตรวจสอบ IP, Port หรือรหัสผ่าน Proxy"
            };
        }
        catch (Exception ex)
        {
            return new ProxyTestResult
            {
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"เชื่อมต่อ Proxy ไม่สำเร็จ: {ex.GetBaseException().Message}"
            };
        }
        finally
        {
            StopBridgeForInstance(tempId);
        }
    }

    private async Task AcceptLoopAsync(BridgeSession session)
    {
        var token = session.Cts.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await session.Listener.AcceptTcpClientAsync(token);
                client.NoDelay = true;
                _ = Task.Run(() => HandleClientAsync(client, session.Upstream, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (token.IsCancellationRequested) break;
                await Task.Delay(50, token).ConfigureAwait(false);
            }
        }
    }

    private static async Task HandleClientAsync(TcpClient client, ProxyItem upstream, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var clientStream = client.GetStream();
                var headerBytes = await ReadHttpHeaderBlockAsync(clientStream, ct);
                if (headerBytes.Length == 0) return;

                string headerText = Encoding.ASCII.GetString(headerBytes);
                var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0])) return;

                var firstLineParts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (firstLineParts.Length < 2) return;

                string method = firstLineParts[0];
                string requestTarget = firstLineParts[1];
                string httpVersion = firstLineParts.Length >= 3 ? firstLineParts[2] : "HTTP/1.1";

                if (upstream.Protocol == ProxyProtocol.Socks5)
                {
                    await HandleUpstreamSocks5Async(clientStream, method, requestTarget, httpVersion, lines, upstream, ct);
                }
                else
                {
                    await HandleUpstreamHttpOrHttpsAsync(clientStream, lines, upstream, ct);
                }
            }
            catch
            {
                // Connection closed or aborted
            }
        }
    }

    private static async Task HandleUpstreamHttpOrHttpsAsync(
        NetworkStream clientStream,
        string[] lines,
        ProxyItem upstream,
        CancellationToken ct)
    {
        using var upstreamTcp = new TcpClient { NoDelay = true };
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(10));
        await upstreamTcp.ConnectAsync(upstream.Host, upstream.Port, connectCts.Token);

        Stream upstreamStream = upstreamTcp.GetStream();
        if (upstream.Protocol == ProxyProtocol.Https)
        {
            var ssl = new SslStream(upstreamStream, leaveInnerStreamOpen: false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(upstream.Host);
            upstreamStream = ssl;
        }

        // Rebuild HTTP request headers and inject Proxy-Authorization if credentials are provided
        var sb = new StringBuilder();
        sb.Append(lines[0]).Append("\r\n");

        bool hasAuth = !string.IsNullOrEmpty(upstream.Username);
        string? authHeader = null;
        if (hasAuth)
        {
            string rawCreds = $"{upstream.Username}:{upstream.Password ?? string.Empty}";
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawCreds));
            authHeader = $"Proxy-Authorization: Basic {b64}";
        }

        bool authWritten = false;
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase))
            {
                if (authHeader != null)
                {
                    sb.Append(authHeader).Append("\r\n");
                    authWritten = true;
                }
                continue;
            }
            sb.Append(line).Append("\r\n");
        }

        if (authHeader != null && !authWritten)
        {
            sb.Append(authHeader).Append("\r\n");
        }
        sb.Append("\r\n");

        byte[] outHeaderBytes = Encoding.ASCII.GetBytes(sb.ToString());
        await upstreamStream.WriteAsync(outHeaderBytes, ct);
        await upstreamStream.FlushAsync(ct);

        await PipeBidirectionalAsync(clientStream, upstreamStream, ct);
    }

    private static async Task HandleUpstreamSocks5Async(
        NetworkStream clientStream,
        string method,
        string requestTarget,
        string httpVersion,
        string[] lines,
        ProxyItem upstream,
        CancellationToken ct)
    {
        bool isConnect = string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase);
        string targetHost;
        int targetPort;
        string relativePath = "/";

        if (isConnect)
        {
            ParseHostPort(requestTarget, 443, out targetHost, out targetPort);
        }
        else if (Uri.TryCreate(requestTarget, UriKind.Absolute, out var absUri))
        {
            targetHost = absUri.DnsSafeHost;
            targetPort = absUri.Port > 0 ? absUri.Port : 80;
            relativePath = string.IsNullOrEmpty(absUri.PathAndQuery) ? "/" : absUri.PathAndQuery;
        }
        else
        {
            // Extract Host header
            string hostHeader = string.Empty;
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                {
                    hostHeader = lines[i].Substring(5).Trim();
                    break;
                }
            }
            if (string.IsNullOrEmpty(hostHeader)) return;
            ParseHostPort(hostHeader, 80, out targetHost, out targetPort);
            relativePath = requestTarget;
        }

        using var upstreamTcp = new TcpClient { NoDelay = true };
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(10));
        await upstreamTcp.ConnectAsync(upstream.Host, upstream.Port, connectCts.Token);

        using var socksStream = upstreamTcp.GetStream();

        // 1. SOCKS5 Greeting (RFC 1928)
        bool hasAuth = !string.IsNullOrEmpty(upstream.Username);
        byte[] greeting = hasAuth
            ? new byte[] { 0x05, 0x02, 0x00, 0x02 } // Ver 5, 2 methods: NoAuth(0x00), UserPass(0x02)
            : new byte[] { 0x05, 0x01, 0x00 };      // Ver 5, 1 method: NoAuth(0x00)
        await socksStream.WriteAsync(greeting, ct);

        byte[] greetingReply = new byte[2];
        await ReadExactAsync(socksStream, greetingReply, ct);
        if (greetingReply[0] != 0x05 || greetingReply[1] == 0xFF)
        {
            await SendHttpErrorAsync(clientStream, "502 Bad Gateway", "SOCKS5 authentication method rejected", ct);
            return;
        }

        // 2. RFC 1929 Username/Password Authentication if required
        if (greetingReply[1] == 0x02)
        {
            byte[] userBytes = Encoding.UTF8.GetBytes(upstream.Username ?? string.Empty);
            byte[] passBytes = Encoding.UTF8.GetBytes(upstream.Password ?? string.Empty);
            if (userBytes.Length > 255 || passBytes.Length > 255) return;

            byte[] authPacket = new byte[3 + userBytes.Length + passBytes.Length];
            authPacket[0] = 0x01; // Subnegotiation version 1
            authPacket[1] = (byte)userBytes.Length;
            Buffer.BlockCopy(userBytes, 0, authPacket, 2, userBytes.Length);
            authPacket[2 + userBytes.Length] = (byte)passBytes.Length;
            Buffer.BlockCopy(passBytes, 0, authPacket, 3 + userBytes.Length, passBytes.Length);

            await socksStream.WriteAsync(authPacket, ct);

            byte[] authReply = new byte[2];
            await ReadExactAsync(socksStream, authReply, ct);
            if (authReply[1] != 0x00)
            {
                await SendHttpErrorAsync(clientStream, "407 Proxy Authentication Required", "SOCKS5 invalid credentials", ct);
                return;
            }
        }

        // 3. SOCKS5 CONNECT Request
        byte[] hostBytes = Encoding.ASCII.GetBytes(targetHost);
        byte[] connectReq = new byte[4 + 1 + hostBytes.Length + 2];
        connectReq[0] = 0x05; // SOCKS5
        connectReq[1] = 0x01; // CMD: CONNECT
        connectReq[2] = 0x00; // RSV
        connectReq[3] = 0x03; // ATYP: Domain name
        connectReq[4] = (byte)hostBytes.Length;
        Buffer.BlockCopy(hostBytes, 0, connectReq, 5, hostBytes.Length);
        connectReq[5 + hostBytes.Length] = (byte)((targetPort >> 8) & 0xFF);
        connectReq[6 + hostBytes.Length] = (byte)(targetPort & 0xFF);

        await socksStream.WriteAsync(connectReq, ct);

        byte[] respHead = new byte[4];
        await ReadExactAsync(socksStream, respHead, ct);
        if (respHead[1] != 0x00)
        {
            await SendHttpErrorAsync(clientStream, "502 Bad Gateway", $"SOCKS5 connect failed (code {respHead[1]})", ct);
            return;
        }

        // Consume bound address from SOCKS5 reply
        int addrLen = respHead[3] switch
        {
            0x01 => 4,  // IPv4
            0x04 => 16, // IPv6
            0x03 => -1, // Domain
            _ => 0
        };
        if (addrLen == -1)
        {
            byte[] lenByte = new byte[1];
            await ReadExactAsync(socksStream, lenByte, ct);
            addrLen = lenByte[0];
        }
        if (addrLen > 0)
        {
            byte[] discard = new byte[addrLen + 2];
            await ReadExactAsync(socksStream, discard, ct);
        }

        if (isConnect)
        {
            byte[] okBytes = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
            await clientStream.WriteAsync(okBytes, ct);
            await clientStream.FlushAsync(ct);
        }
        else
        {
            // Forward plain HTTP request with relative target path through the established SOCKS5 tunnel
            var sb = new StringBuilder();
            sb.Append($"{method} {relativePath} {httpVersion}\r\n");
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase))
                    continue;
                sb.Append(line).Append("\r\n");
            }
            sb.Append("\r\n");
            byte[] reqBytes = Encoding.ASCII.GetBytes(sb.ToString());
            await socksStream.WriteAsync(reqBytes, ct);
            await socksStream.FlushAsync(ct);
        }

        await PipeBidirectionalAsync(clientStream, socksStream, ct);
    }

    private static void ParseHostPort(string input, int defaultPort, out string host, out int port)
    {
        host = input;
        port = defaultPort;
        int colonIdx = input.LastIndexOf(':');
        if (colonIdx > 0 && colonIdx < input.Length - 1 && int.TryParse(input.Substring(colonIdx + 1), out int parsedPort))
        {
            host = input.Substring(0, colonIdx).Trim('[', ']');
            port = parsedPort;
        }
    }

    private static async Task<byte[]> ReadHttpHeaderBlockAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream(1024);
        byte[] oneByte = new byte[1];
        int matchState = 0; // Looking for \r\n\r\n

        while (ms.Length < 16384)
        {
            int read = await stream.ReadAsync(oneByte.AsMemory(0, 1), ct);
            if (read == 0) break;
            byte b = oneByte[0];
            ms.WriteByte(b);

            if ((matchState == 0 || matchState == 2) && b == (byte)'\r')
                matchState++;
            else if ((matchState == 1 || matchState == 3) && b == (byte)'\n')
                matchState++;
            else if (b == (byte)'\r')
                matchState = 1;
            else
                matchState = 0;

            if (matchState == 4) break;
        }

        return ms.ToArray();
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0) throw new EndOfStreamException("Unexpected EOF from proxy server");
            offset += read;
        }
    }

    private static async Task SendHttpErrorAsync(Stream clientStream, string status, string message, CancellationToken ct)
    {
        string resp = $"HTTP/1.1 {status}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(message)}\r\nConnection: close\r\n\r\n{message}";
        byte[] bytes = Encoding.UTF8.GetBytes(resp);
        await clientStream.WriteAsync(bytes, ct);
    }

    private static async Task PipeBidirectionalAsync(Stream streamA, Stream streamB, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var t1 = CopyStreamSafeAsync(streamA, streamB, linkedCts.Token);
        var t2 = CopyStreamSafeAsync(streamB, streamA, linkedCts.Token);
        await Task.WhenAny(t1, t2);
        try { linkedCts.Cancel(); } catch { }
    }

    private static async Task CopyStreamSafeAsync(Stream source, Stream destination, CancellationToken ct)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16384);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read <= 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                await destination.FlushAsync(ct);
            }
        }
        catch
        {
            // Normal termination on socket close
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var kvp in _sessions)
        {
            kvp.Value.Dispose();
        }
        _sessions.Clear();
    }
}
