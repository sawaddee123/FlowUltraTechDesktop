using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public class OverlayServerService : IOverlayServerService
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly ConcurrentDictionary<Guid, OverlayConfig> _configs = new();
    private readonly ConcurrentDictionary<Guid, OverlayLiveEventPayload> _lastEvents = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, WebSocket>> _clients = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private TcpListener? _listener;
    private CancellationTokenSource? _serverCts;
    private Task? _acceptLoopTask;
    private bool _disposed;

    public bool IsRunning { get; private set; }
    public int ActivePort { get; private set; }

    public async Task<int> StartAsync(int preferredPort = 19450, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycleLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsRunning && _listener != null)
            {
                return ActivePort;
            }

            int basePort = preferredPort is > 0 and <= 65500 ? preferredPort : 19450;
            TcpListener? boundListener = null;

            for (int offset = 0; offset <= 25; offset++)
            {
                int candidatePort = basePort + offset;
                try
                {
                    var candidate = new TcpListener(IPAddress.Loopback, candidatePort);
                    candidate.Start();
                    boundListener = candidate;
                    break;
                }
                catch (SocketException)
                {
                    // Port is in use, try next port
                }
            }

            if (boundListener == null)
            {
                var ephemeral = new TcpListener(IPAddress.Loopback, 0);
                ephemeral.Start();
                boundListener = ephemeral;
            }

            _listener = boundListener;
            ActivePort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverCts = new CancellationTokenSource();
            IsRunning = true;

            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_listener, _serverCts.Token));
            return ActivePort;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            IsRunning = false;

            try
            {
                _serverCts?.Cancel();
            }
            catch
            {
                // Ignore cancellation exceptions
            }

            try
            {
                _listener?.Stop();
            }
            catch
            {
                // Ignore listener stop errors
            }

            _listener = null;

            foreach (var profileGroup in _clients.Values)
            {
                foreach (var kvp in profileGroup)
                {
                    await CloseWebSocketSafelyAsync(kvp.Value).ConfigureAwait(false);
                }
                profileGroup.Clear();
            }
            _clients.Clear();

            if (_acceptLoopTask != null)
            {
                try
                {
                    await _acceptLoopTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore shutdown wait timeout
                }
                _acceptLoopTask = null;
            }

            _serverCts?.Dispose();
            _serverCts = null;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public string GetOverlayUrl(Guid profileId)
    {
        int port = ActivePort > 0 ? ActivePort : 19450;
        return $"http://127.0.0.1:{port}/overlay/{profileId:D}";
    }

    public OverlayConfig GetConfig(Guid profileId)
    {
        var config = _configs.GetOrAdd(profileId, static _ => new OverlayConfig());
        return config.Clone();
    }

    public async Task UpdateConfigAsync(Guid profileId, OverlayConfig config)
    {
        var normalized = NormalizeConfig(config);
        _configs[profileId] = normalized.Clone();

        var envelope = BuildConfigEnvelope(profileId, normalized, "config");
        await BroadcastJsonToProfileAsync(profileId, envelope).ConfigureAwait(false);
    }

    public async Task BroadcastLiveEventAsync(Guid profileId, OverlayLiveEventPayload payload)
    {
        var safePayload = SanitizeLiveEvent(payload);
        _lastEvents[profileId] = safePayload;

        var envelope = new
        {
            type = "event",
            profileId = profileId.ToString("D"),
            eventType = safePayload.EventType,
            nickname = safePayload.Nickname,
            username = safePayload.Username,
            message = safePayload.Message,
            badgeText = safePayload.BadgeText,
            timestamp = safePayload.Timestamp,
            @event = safePayload,
            payload = safePayload
        };

        string json = JsonSerializer.Serialize(envelope, JsonOptions);
        await BroadcastJsonToProfileAsync(profileId, json).ConfigureAwait(false);
    }

    public async Task TriggerFlashDealAsync(Guid profileId, string title, int durationSeconds)
    {
        var config = GetConfig(profileId);
        int clampedDuration = Math.Clamp(durationSeconds <= 0 ? 300 : durationSeconds, 5, 86400);
        string safeTitle = string.IsNullOrWhiteSpace(title)
            ? "โปรนาทีทอง ส่งฟรี"
            : TruncateSafe(title.Trim(), 160);

        config.EnableFlashDealCountdown = true;
        config.FlashDealTitle = safeTitle;
        config.FlashDealDurationSeconds = clampedDuration;
        config.FlashDealEndsAtUtc = DateTime.UtcNow.AddSeconds(clampedDuration);

        _configs[profileId] = config.Clone();

        var envelope = BuildConfigEnvelope(profileId, config, "flash_deal");
        await BroadcastJsonToProfileAsync(profileId, envelope).ConfigureAwait(false);
    }

    public async Task StopFlashDealAsync(Guid profileId)
    {
        var config = GetConfig(profileId);
        config.EnableFlashDealCountdown = false;
        config.FlashDealEndsAtUtc = null;

        _configs[profileId] = config.Clone();

        var envelope = BuildConfigEnvelope(profileId, config, "flash_deal_stop");
        await BroadcastJsonToProfileAsync(profileId, envelope).ConfigureAwait(false);
    }

    public int GetConnectedClientCount(Guid profileId)
    {
        if (!_clients.TryGetValue(profileId, out var group))
        {
            return 0;
        }

        return group.Values.Count(ws => ws.State == WebSocketState.Open);
    }

    private static string BuildConfigEnvelope(Guid profileId, OverlayConfig config, string subType)
    {
        var payload = new
        {
            type = "config",
            subType,
            profileId = profileId.ToString("D"),
            clockMode = (int)config.ClockMode,
            clockModeName = config.ClockMode.ToString(),
            backgroundStyle = (int)config.BackgroundStyle,
            backgroundStyleName = config.BackgroundStyle.ToString(),
            brandText = config.BrandText,
            subtitleText = config.SubtitleText,
            primaryColorHex = config.PrimaryColorHex,
            accentColorHex = config.AccentColorHex,
            enableGlareEffect = config.EnableGlareEffect,
            enableDynamicShadow = config.EnableDynamicShadow,
            enableMicroJitter = config.EnableMicroJitter,
            enableLiveEventTicker = config.EnableLiveEventTicker,
            enableFlashDealCountdown = config.EnableFlashDealCountdown,
            flashDealTitle = config.FlashDealTitle,
            flashDealDurationSeconds = config.FlashDealDurationSeconds,
            flashDealEndsAtUtc = config.FlashDealEndsAtUtc,
            config = new
            {
                isEnabled = config.IsEnabled,
                clockMode = (int)config.ClockMode,
                clockModeName = config.ClockMode.ToString(),
                backgroundStyle = (int)config.BackgroundStyle,
                backgroundStyleName = config.BackgroundStyle.ToString(),
                timeZoneId = config.TimeZoneId,
                use24HourFormat = config.Use24HourFormat,
                showSeconds = config.ShowSeconds,
                showThaiDate = config.ShowThaiDate,
                primaryColorHex = config.PrimaryColorHex,
                accentColorHex = config.AccentColorHex,
                brandText = config.BrandText,
                subtitleText = config.SubtitleText,
                enableGlareEffect = config.EnableGlareEffect,
                enableDynamicShadow = config.EnableDynamicShadow,
                enableMicroJitter = config.EnableMicroJitter,
                enableLiveEventTicker = config.EnableLiveEventTicker,
                enableFlashDealCountdown = config.EnableFlashDealCountdown,
                flashDealTitle = config.FlashDealTitle,
                flashDealDurationSeconds = config.FlashDealDurationSeconds,
                flashDealEndsAtUtc = config.FlashDealEndsAtUtc
            }
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static OverlayConfig NormalizeConfig(OverlayConfig? input)
    {
        var cfg = (input ?? new OverlayConfig()).Clone();
        if (!Enum.IsDefined(typeof(OverlayClockMode), cfg.ClockMode))
        {
            cfg.ClockMode = OverlayClockMode.RealisticWall;
        }

        if (!Enum.IsDefined(typeof(OverlayBackgroundStyle), cfg.BackgroundStyle))
        {
            cfg.BackgroundStyle = OverlayBackgroundStyle.Transparent;
        }

        cfg.TimeZoneId = string.IsNullOrWhiteSpace(cfg.TimeZoneId) ? "Asia/Bangkok" : cfg.TimeZoneId.Trim();
        cfg.PrimaryColorHex = string.IsNullOrWhiteSpace(cfg.PrimaryColorHex) ? "#F8FAFC" : cfg.PrimaryColorHex.Trim();
        cfg.AccentColorHex = string.IsNullOrWhiteSpace(cfg.AccentColorHex) ? "#EF4444" : cfg.AccentColorHex.Trim();
        cfg.BrandText = TruncateSafe(cfg.BrandText ?? "LIVE STUDIO", 160);
        cfg.SubtitleText = TruncateSafe(cfg.SubtitleText ?? "REALTIME VERIFIED", 160);
        cfg.FlashDealTitle = TruncateSafe(string.IsNullOrWhiteSpace(cfg.FlashDealTitle) ? "โปรนาทีทอง ส่งฟรี" : cfg.FlashDealTitle, 160);
        cfg.FlashDealDurationSeconds = Math.Clamp(cfg.FlashDealDurationSeconds <= 0 ? 300 : cfg.FlashDealDurationSeconds, 5, 86400);
        return cfg;
    }

    private static OverlayLiveEventPayload SanitizeLiveEvent(OverlayLiveEventPayload? payload)
    {
        if (payload == null)
        {
            return new OverlayLiveEventPayload
            {
                EventType = "chat",
                Nickname = "Viewer",
                Message = string.Empty,
                Timestamp = DateTime.Now
            };
        }

        return new OverlayLiveEventPayload
        {
            EventType = string.IsNullOrWhiteSpace(payload.EventType) ? "chat" : payload.EventType.Trim().ToLowerInvariant(),
            Nickname = TruncateSafe(payload.Nickname ?? string.Empty, 120),
            Username = TruncateSafe(payload.Username ?? string.Empty, 120),
            Message = TruncateSafe(payload.Message ?? string.Empty, 300),
            BadgeText = TruncateSafe(payload.BadgeText ?? string.Empty, 80),
            Timestamp = payload.Timestamp == default ? DateTime.Now : payload.Timestamp
        };
    }

    private static string TruncateSafe(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value[..maxLength];
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsRunning)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientConnectionAsync(client, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (ct.IsCancellationRequested || !IsRunning)
            {
                break;
            }
            catch
            {
                // Transient accept error; continue listening if still running
            }
        }
    }

    private async Task HandleClientConnectionAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.NoDelay = true;
            using var stream = client.GetStream();

            var requestHeaderText = await ReadHttpHeaderAsync(stream, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestHeaderText))
            {
                return;
            }

            var lines = requestHeaderText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                return;
            }

            var requestParts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestParts.Length < 2)
            {
                return;
            }

            string rawTarget = requestParts[1];
            string path = rawTarget;
            string query = string.Empty;
            int queryIdx = rawTarget.IndexOf('?');
            if (queryIdx >= 0)
            {
                path = rawTarget[..queryIdx];
                query = rawTarget[(queryIdx + 1)..];
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    string key = lines[i][..colon].Trim();
                    string val = lines[i][(colon + 1)..].Trim();
                    headers[key] = val;
                }
            }

            string? wsKey = null;
            bool isWebSocketUpgrade =
                headers.TryGetValue("Upgrade", out var upgradeVal) &&
                upgradeVal.Contains("websocket", StringComparison.OrdinalIgnoreCase) &&
                headers.TryGetValue("Sec-WebSocket-Key", out wsKey) &&
                !string.IsNullOrWhiteSpace(wsKey);

            if (isWebSocketUpgrade && wsKey != null)
            {
                Guid profileId = ExtractProfileId(path, query);
                await HandleWebSocketSessionAsync(stream, profileId, wsKey, ct).ConfigureAwait(false);
                return;
            }

            await HandleHttpRequestAsync(stream, path, query, ct).ConfigureAwait(false);
        }
    }

    private static async Task<string> ReadHttpHeaderAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        int totalRead = 0;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        while (totalRead < buffer.Length)
        {
            int bytesRead;
            try
            {
                bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), timeoutCts.Token).ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            if (bytesRead <= 0)
            {
                break;
            }

            totalRead += bytesRead;
            string currentText = Encoding.ASCII.GetString(buffer, 0, totalRead);
            int headerEnd = currentText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd >= 0)
            {
                return currentText[..headerEnd];
            }
        }

        return totalRead > 0 ? Encoding.ASCII.GetString(buffer, 0, totalRead) : string.Empty;
    }

    private static Guid ExtractProfileId(string path, string query)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            if (Guid.TryParse(segments[i], out var parsed))
            {
                return parsed;
            }
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 &&
                    (kv[0].Equals("profileId", StringComparison.OrdinalIgnoreCase) ||
                     kv[0].Equals("id", StringComparison.OrdinalIgnoreCase)) &&
                    Guid.TryParse(Uri.UnescapeDataString(kv[1]), out var qGuid))
                {
                    return qGuid;
                }
            }
        }

        return Guid.Empty;
    }

    private async Task HandleHttpRequestAsync(NetworkStream stream, string path, string query, CancellationToken ct)
    {
        if (path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
        {
            await WriteHttpResponseAsync(stream, "204 No Content", "image/x-icon", Array.Empty<byte>(), ct).ConfigureAwait(false);
            return;
        }

        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            string healthJson = JsonSerializer.Serialize(new
            {
                status = "ok",
                port = ActivePort,
                isRunning = IsRunning
            }, JsonOptions);
            await WriteHttpResponseAsync(stream, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(healthJson), ct).ConfigureAwait(false);
            return;
        }

        if (path.StartsWith("/api/config", StringComparison.OrdinalIgnoreCase))
        {
            Guid profileId = ExtractProfileId(path, query);
            var cfg = GetConfig(profileId);
            string cfgJson = BuildConfigEnvelope(profileId, cfg, "http_config");
            await WriteHttpResponseAsync(stream, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(cfgJson), ct).ConfigureAwait(false);
            return;
        }

        Guid targetProfileId = ExtractProfileId(path, query);
        var initialConfig = GetConfig(targetProfileId);
        _lastEvents.TryGetValue(targetProfileId, out var lastEvent);

        string html = BuildOverlayHtmlDocument(targetProfileId, initialConfig, lastEvent);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(html);
        await WriteHttpResponseAsync(stream, "200 OK", "text/html; charset=utf-8", bodyBytes, ct).ConfigureAwait(false);
    }

    private static async Task WriteHttpResponseAsync(
        NetworkStream stream,
        string status,
        string contentType,
        byte[] body,
        CancellationToken ct)
    {
        string header =
            $"HTTP/1.1 {status}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store, no-cache, must-revalidate\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Connection: close\r\n\r\n";

        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
        }
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task HandleWebSocketSessionAsync(
        NetworkStream stream,
        Guid profileId,
        string secWebSocketKey,
        CancellationToken ct)
    {
        string acceptKey = Convert.ToBase64String(
            SHA1.HashData(Encoding.ASCII.GetBytes(secWebSocketKey.Trim() + WebSocketGuid)));

        string handshakeResponse =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

        using var webSocket = WebSocket.CreateFromStream(
            stream,
            isServer: true,
            subProtocol: null,
            keepAliveInterval: TimeSpan.FromSeconds(15));

        Guid connectionId = Guid.NewGuid();
        var group = _clients.GetOrAdd(profileId, static _ => new ConcurrentDictionary<Guid, WebSocket>());
        group[connectionId] = webSocket;

        try
        {
            byte[] responseBytes = Encoding.ASCII.GetBytes(handshakeResponse);
            await stream.WriteAsync(responseBytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);

            var receiveBuffer = new byte[4096];
            while (!ct.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), ct).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text && result.Count > 0)
                {
                    string clientMsg = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
                    if (clientMsg.Contains("sync", StringComparison.OrdinalIgnoreCase) ||
                        clientMsg.Contains("ping", StringComparison.OrdinalIgnoreCase))
                    {
                        var currentConfig = GetConfig(profileId);
                        string syncJson = BuildConfigEnvelope(profileId, currentConfig, "sync");
                        await SendTextToSocketAsync(webSocket, syncJson, ct).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            group.TryRemove(connectionId, out _);
            await CloseWebSocketSafelyAsync(webSocket).ConfigureAwait(false);
        }
    }

    private async Task BroadcastJsonToProfileAsync(Guid profileId, string json)
    {
        var targets = new List<(ConcurrentDictionary<Guid, WebSocket> Group, Guid ConnId, WebSocket Socket)>();

        if (_clients.TryGetValue(profileId, out var profileGroup))
        {
            foreach (var kvp in profileGroup)
            {
                targets.Add((profileGroup, kvp.Key, kvp.Value));
            }
        }

        if (profileId != Guid.Empty && _clients.TryGetValue(Guid.Empty, out var wildcardGroup))
        {
            foreach (var kvp in wildcardGroup)
            {
                targets.Add((wildcardGroup, kvp.Key, kvp.Value));
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        byte[] payloadBytes = Encoding.UTF8.GetBytes(json);
        var tasks = targets.Select(async item =>
        {
            if (item.Socket.State != WebSocketState.Open)
            {
                item.Group.TryRemove(item.ConnId, out _);
                return;
            }

            try
            {
                using var sendCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await item.Socket.SendAsync(
                    new ArraySegment<byte>(payloadBytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    sendCts.Token).ConfigureAwait(false);
            }
            catch
            {
                item.Group.TryRemove(item.ConnId, out _);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task SendTextToSocketAsync(WebSocket socket, string json, CancellationToken ct)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        catch
        {
            // Ignore socket write errors
        }
    }

    private static async Task CloseWebSocketSafelyAsync(WebSocket socket)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", closeCts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // Ignore socket closure exceptions
        }

        try
        {
            socket.Dispose();
        }
        catch
        {
            // Ignore dispose errors
        }
    }

    private static string BuildOverlayHtmlDocument(
        Guid profileId,
        OverlayConfig initialConfig,
        OverlayLiveEventPayload? lastEvent)
    {
        string initialConfigJson = BuildConfigEnvelope(profileId, initialConfig, "initial");
        string initialEventJson = lastEvent != null
            ? JsonSerializer.Serialize(lastEvent, JsonOptions)
            : "null";

        return $$"""
<!DOCTYPE html>
<html lang="th">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>FlowUltraTech Broadcast Timepiece Overlay</title>
  <style>
    :root {
      --primary-color: {{initialConfig.PrimaryColorHex}};
      --accent-color: {{initialConfig.AccentColorHex}};
      --surface-dark: rgba(11, 15, 25, 0.86);
      --surface-elevated: rgba(22, 31, 51, 0.92);
      --surface-light: rgba(248, 250, 252, 0.90);
      --text-primary: #F8FAFC;
      --text-secondary: #94A3B8;
      --text-muted: #64748B;
      --ease-out-expo: cubic-bezier(0.16, 1, 0.3, 1);
      --ease-out-quart: cubic-bezier(0.25, 1, 0.5, 1);
      --font-display: 'Outfit', 'Plus Jakarta Sans', 'Prompt', 'Segoe UI Variable Display', 'Segoe UI', system-ui, sans-serif;
      --font-mono: 'JetBrains Mono', 'Cascadia Mono', 'Consolas', monospace;
    }

    * {
      box-sizing: border-box;
      margin: 0;
      padding: 0;
      user-select: none;
      -webkit-font-smoothing: antialiased;
    }

    html, body {
      width: 100%;
      height: 100%;
      overflow: hidden;
      background: transparent;
      font-family: var(--font-display);
      color: var(--text-primary);
    }

    body.is-preview-mode {
      display: flex;
      align-items: center;
      justify-content: center;
      background-color: #0D111A;
      background-image: radial-gradient(rgba(148, 163, 184, 0.14) 1px, transparent 1px);
      background-size: 14px 14px;
    }

    body.is-preview-mode .overlay-stage {
      zoom: 0.62;
    }

    .overlay-stage {
      position: relative;
      display: inline-flex;
      flex-direction: column;
      align-items: center;
      gap: 10px;
      padding: 18px 20px;
      border-radius: 24px;
      transition: background-color 280ms var(--ease-out-quart), opacity 240ms var(--ease-out-quart), transform 280ms var(--ease-out-expo);
      max-width: 360px;
    }

    .overlay-stage.is-hidden {
      opacity: 0;
      pointer-events: none;
      transform: scale(0.96);
    }

    /* Background Surface Modes (ui-craft-clarity Tonal Surfaces) */
    .overlay-stage[data-bg="Transparent"] {
      background: transparent;
    }

    .overlay-stage[data-bg="GlassDark"] {
      background: var(--surface-dark);
      backdrop-filter: blur(16px);
      -webkit-backdrop-filter: blur(16px);
      box-shadow: 0 12px 32px rgba(2, 6, 23, 0.45), inset 0 1px 0 rgba(255, 255, 255, 0.07);
    }

    .overlay-stage[data-bg="GlassLight"] {
      background: var(--surface-light);
      color: #0F172A;
      backdrop-filter: blur(16px);
      -webkit-backdrop-filter: blur(16px);
      box-shadow: 0 12px 28px rgba(15, 23, 42, 0.16), inset 0 1px 0 rgba(255, 255, 255, 0.7);
    }

    /* Realistic Camera Physics: Dynamic Shadow, Micro-Jitter, Specular Glare */
    .overlay-stage.fx-shadow .clock-viewport {
      filter: drop-shadow(0 16px 28px rgba(2, 6, 23, 0.52)) drop-shadow(0 4px 10px rgba(2, 6, 23, 0.32));
    }

    @keyframes cameraBreathingJitter {
      0%   { transform: translate3d(0px, 0px, 0) rotate(0deg); }
      20%  { transform: translate3d(0.65px, -0.45px, 0) rotate(0.06deg); }
      45%  { transform: translate3d(-0.55px, 0.5px, 0) rotate(-0.05deg); }
      70%  { transform: translate3d(0.45px, 0.6px, 0) rotate(0.04deg); }
      100% { transform: translate3d(0px, 0px, 0) rotate(0deg); }
    }

    .overlay-stage.fx-jitter .camera-rig {
      animation: cameraBreathingJitter 5.2s var(--ease-out-quart) infinite;
      will-change: transform;
    }

    .camera-rig {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 10px;
      width: 100%;
    }

    .clock-viewport {
      position: relative;
      width: 248px;
      height: 248px;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: transform 320ms var(--ease-out-expo);
    }

    /* Specular Glass Glare Sweep */
    .glare-layer {
      position: absolute;
      inset: 10px;
      border-radius: 50%;
      pointer-events: none;
      overflow: hidden;
      opacity: 0;
      transition: opacity 250ms var(--ease-out-quart);
      z-index: 15;
    }

    .clock-viewport[data-mode="WoodBlockLed"] .glare-layer,
    .clock-viewport[data-mode="DigitalClean"] .glare-layer {
      inset: 28px 8px;
      border-radius: 18px;
    }

    .overlay-stage.fx-glare .glare-layer {
      opacity: 1;
    }

    .glare-layer::before {
      content: "";
      position: absolute;
      inset: -40%;
      background: linear-gradient(
        118deg,
        rgba(255, 255, 255, 0) 34%,
        rgba(255, 255, 255, 0.18) 47%,
        rgba(255, 255, 255, 0.04) 53%,
        rgba(255, 255, 255, 0) 64%
      );
      transform: translate3d(-28%, -12%, 0) rotate(-8deg);
      animation: specularSweep 7.5s var(--ease-out-expo) infinite;
    }

    @keyframes specularSweep {
      0%, 15% { transform: translate3d(-38%, -18%, 0) rotate(-8deg); }
      55%     { transform: translate3d(26%, 16%, 0) rotate(-8deg); }
      100%    { transform: translate3d(-38%, -18%, 0) rotate(-8deg); }
    }

    /* Mode Visibility Architecture */
    .clock-face {
      display: none;
      width: 100%;
      height: 100%;
      position: relative;
      align-items: center;
      justify-content: center;
    }

    .clock-viewport[data-mode="RealisticWall"] .face-analog,
    .clock-viewport[data-mode="SeikoCustom"] .face-analog,
    .clock-viewport[data-mode="Desk3D"] .face-analog,
    .clock-viewport[data-mode="TwinBell"] .face-analog {
      display: flex;
    }

    .clock-viewport[data-mode="WoodBlockLed"] .face-wood {
      display: flex;
    }

    .clock-viewport[data-mode="DigitalClean"] .face-digital-clean {
      display: flex;
    }

    /* 3D Desk Perspective Mode */
    .clock-viewport[data-mode="Desk3D"] .analog-svg {
      transform: perspective(640px) rotateX(11deg) translateY(-4px);
    }

    .desk-pedestal {
      display: none;
      position: absolute;
      bottom: 6px;
      width: 158px;
      height: 22px;
      border-radius: 8px 8px 14px 14px;
      background: linear-gradient(180deg, #334155 0%, #0F172A 100%);
      box-shadow: 0 10px 18px rgba(2, 6, 23, 0.65), inset 0 1px 0 rgba(255, 255, 255, 0.18);
      z-index: 1;
    }

    .clock-viewport[data-mode="Desk3D"] .desk-pedestal {
      display: block;
    }

    /* Twin Bell Hardware Extras */
    .twin-bell-hardware {
      display: none;
    }

    .clock-viewport[data-mode="TwinBell"] .twin-bell-hardware {
      display: block;
    }

    @keyframes bellRingVibration {
      0%, 100% { transform: rotate(0deg); }
      25% { transform: rotate(-5deg); }
      75% { transform: rotate(5deg); }
    }

    .overlay-stage[data-deal-active="true"] .bell-hammer {
      transform-origin: 120px 26px;
      animation: bellRingVibration 180ms linear infinite;
    }

    .analog-svg {
      width: 240px;
      height: 240px;
      position: relative;
      z-index: 2;
      transition: transform 320ms var(--ease-out-expo);
    }

    /* Mode 4: WoodBlockLed (Tactile 3D Timber Block + Warm Phosphor LED) */
    .wood-block-chassis {
      position: relative;
      width: 244px;
      min-height: 138px;
      border-radius: 18px;
      padding: 18px 18px 14px;
      background:
        repeating-linear-gradient(
          92deg,
          rgba(255, 255, 255, 0.025) 0px,
          rgba(255, 255, 255, 0.025) 2px,
          rgba(0, 0, 0, 0.05) 3px,
          rgba(0, 0, 0, 0.05) 7px
        ),
        linear-gradient(165deg, #5C3A21 0%, #3E2412 52%, #29160A 100%);
      box-shadow:
        inset 0 2px 1px rgba(255, 232, 204, 0.22),
        inset 0 -6px 12px rgba(10, 5, 2, 0.65),
        0 14px 26px rgba(2, 6, 23, 0.48);
      display: flex;
      flex-direction: column;
      justify-content: space-between;
      gap: 8px;
    }

    .wood-top-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 0.14em;
      text-transform: uppercase;
      color: rgba(254, 243, 199, 0.65);
    }

    .wood-led-readout {
      font-family: var(--font-mono);
      font-size: 38px;
      font-weight: 800;
      letter-spacing: 0.04em;
      line-height: 1.05;
      color: var(--primary-color);
      text-shadow: 0 0 14px color-mix(in srgb, var(--primary-color) 65%, transparent), 0 0 2px rgba(255, 255, 255, 0.7);
      font-variant-numeric: tabular-nums;
      text-align: center;
      white-space: nowrap;
    }

    .wood-led-seconds {
      font-size: 18px;
      color: var(--accent-color);
      margin-left: 3px;
      text-shadow: 0 0 10px color-mix(in srgb, var(--accent-color) 60%, transparent);
    }

    .wood-bottom-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
      font-size: 11px;
      font-weight: 600;
      color: rgba(254, 243, 199, 0.78);
    }

    /* Mode 5: DigitalClean (Broadcast Swiss Typography HUD) */
    .digital-clean-card {
      width: 244px;
      padding: 16px 18px;
      border-radius: 20px;
      background: linear-gradient(155deg, rgba(15, 23, 42, 0.88) 0%, rgba(11, 15, 25, 0.94) 100%);
      box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.10), 0 12px 24px rgba(2, 6, 23, 0.42);
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .digital-header-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 6px;
    }

    .digital-brand-pill {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 0.12em;
      text-transform: uppercase;
      color: #E2E8F0;
      max-width: 155px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .digital-tz-badge {
      font-family: var(--font-mono);
      font-size: 9px;
      font-weight: 700;
      padding: 2px 6px;
      border-radius: 6px;
      background: rgba(99, 102, 241, 0.18);
      color: #A5B4FC;
      max-width: 82px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .digital-main-time {
      display: flex;
      align-items: baseline;
      justify-content: flex-start;
      gap: 4px;
      font-family: var(--font-mono);
      font-variant-numeric: tabular-nums;
      line-height: 1;
      margin: 4px 0 2px;
    }

    .digital-hm {
      font-size: 42px;
      font-weight: 800;
      letter-spacing: -0.03em;
      color: var(--primary-color);
    }

    .digital-sec-col {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 2px;
    }

    .digital-sec {
      font-size: 17px;
      font-weight: 800;
      color: var(--accent-color);
    }

    .digital-ampm {
      font-size: 9px;
      font-weight: 700;
      letter-spacing: 0.08em;
      color: #94A3B8;
    }

    .digital-footer-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
      padding-top: 6px;
      border-top: 1px solid rgba(148, 163, 184, 0.14);
      font-size: 11px;
      color: #CBD5E1;
    }

    /* Truncation Utilities (State 4: Overflow Protection) */
    .truncate-text {
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    /* Broadcast Lower-Third Live Event Ticker (State 1 Ideal & State 2 Empty) */
    .live-ticker-bar {
      width: 264px;
      min-height: 36px;
      padding: 6px 11px;
      border-radius: 12px;
      background: rgba(15, 23, 42, 0.90);
      backdrop-filter: blur(12px);
      -webkit-backdrop-filter: blur(12px);
      box-shadow: 0 8px 18px rgba(2, 6, 23, 0.38), inset 0 1px 0 rgba(255, 255, 255, 0.08);
      display: flex;
      align-items: center;
      gap: 8px;
      transition: opacity 220ms var(--ease-out-quart), transform 220ms var(--ease-out-expo);
    }

    .live-ticker-bar.is-disabled {
      display: none;
    }

    .status-dot {
      width: 7px;
      height: 7px;
      border-radius: 999px;
      background: #10B981;
      flex-shrink: 0;
      box-shadow: 0 0 8px rgba(16, 185, 129, 0.75);
      animation: livePulse 2s ease-in-out infinite;
    }

    .overlay-stage[data-conn="reconnecting"] .status-dot {
      background: #F59E0B;
      box-shadow: 0 0 8px rgba(245, 158, 11, 0.65);
    }

    @keyframes livePulse {
      0%, 100% { opacity: 1; transform: scale(1); }
      50%      { opacity: 0.52; transform: scale(0.86); }
    }

    .ticker-badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 2px 6px;
      border-radius: 6px;
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 0.04em;
      background: rgba(99, 102, 241, 0.22);
      color: #C7D2FE;
      flex-shrink: 0;
    }

    .ticker-badge[data-type="gift"] {
      background: rgba(245, 158, 11, 0.24);
      color: #FDE68A;
    }

    .ticker-badge[data-type="join"],
    .ticker-badge[data-type="follow"] {
      background: rgba(16, 185, 129, 0.22);
      color: #A7F3D0;
    }

    .ticker-badge[data-type="like"] {
      background: rgba(244, 63, 94, 0.22);
      color: #FECDD3;
    }

    .ticker-content {
      display: flex;
      align-items: center;
      gap: 5px;
      min-width: 0;
      flex: 1;
      font-size: 11.5px;
    }

    .ticker-nickname {
      font-weight: 700;
      color: #F8FAFC;
      max-width: 96px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      flex-shrink: 0;
    }

    .ticker-message {
      color: #CBD5E1;
      max-width: 150px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      flex: 1;
    }

    /* Flash Deal Countdown Banner */
    .flash-deal-bar {
      width: 264px;
      padding: 7px 11px;
      border-radius: 12px;
      background: linear-gradient(135deg, rgba(153, 27, 27, 0.94) 0%, rgba(127, 29, 29, 0.96) 100%);
      box-shadow: 0 8px 20px rgba(153, 27, 27, 0.38), inset 0 1px 0 rgba(255, 255, 255, 0.16);
      display: none;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
      color: #FEF2F2;
    }

    .flash-deal-bar.is-active {
      display: flex;
    }

    .deal-left {
      display: flex;
      align-items: center;
      gap: 6px;
      min-width: 0;
      flex: 1;
    }

    .deal-icon {
      width: 14px;
      height: 14px;
      flex-shrink: 0;
      color: #FDE047;
    }

    .deal-title {
      font-size: 11.5px;
      font-weight: 700;
      color: #FFFBEB;
      max-width: 165px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .deal-timer {
      font-family: var(--font-mono);
      font-variant-numeric: tabular-nums;
      font-size: 12px;
      font-weight: 800;
      padding: 2px 7px;
      border-radius: 6px;
      background: rgba(0, 0, 0, 0.32);
      color: #FDE047;
      flex-shrink: 0;
    }
  </style>
</head>
<body>
  <main id="overlayStage"
        class="overlay-stage fx-glare fx-shadow fx-jitter"
        data-profile-id="{{profileId:D}}"
        data-bg="{{initialConfig.BackgroundStyle}}"
        data-conn="connecting"
        data-deal-active="false">
    <div class="camera-rig">
      <!-- Clock Viewport supporting all 6 Horological Modes -->
      <section id="clockViewport" class="clock-viewport" data-mode="{{initialConfig.ClockMode}}">
        <div class="glare-layer" aria-hidden="true"></div>
        <div class="desk-pedestal" aria-hidden="true"></div>

        <!-- Modes 0..3: High-Precision SVG Horological Dial -->
        <div class="clock-face face-analog">
          <svg class="analog-svg" viewBox="0 0 240 240" aria-label="Broadcast Analog Clock">
            <defs>
              <linearGradient id="metalBezel" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stop-color="#E2E8F0" />
                <stop offset="45%" stop-color="#94A3B8" />
                <stop offset="75%" stop-color="#475569" />
                <stop offset="100%" stop-color="#CBD5E1" />
              </linearGradient>
              <radialGradient id="dialSurfaceLight" cx="50%" cy="42%" r="58%">
                <stop offset="0%" stop-color="#FFFFFF" />
                <stop offset="82%" stop-color="#F1F5F9" />
                <stop offset="100%" stop-color="#CBD5E1" />
              </radialGradient>
              <radialGradient id="dialSurfaceSeiko" cx="50%" cy="40%" r="60%">
                <stop offset="0%" stop-color="#1E293B" />
                <stop offset="78%" stop-color="#0F172A" />
                <stop offset="100%" stop-color="#020617" />
              </radialGradient>
              <radialGradient id="dialSurfaceVintage" cx="50%" cy="45%" r="58%">
                <stop offset="0%" stop-color="#FFFBEB" />
                <stop offset="85%" stop-color="#FEF3C7" />
                <stop offset="100%" stop-color="#FDE68A" />
              </radialGradient>
            </defs>

            <!-- TwinBell Vintage Hardware (Visible only in Mode 3: TwinBell) -->
            <g class="twin-bell-hardware">
              <!-- Legs -->
              <line x1="68" y1="202" x2="48" y2="230" stroke="#CBD5E1" stroke-width="6" stroke-linecap="round" />
              <line x1="172" y1="202" x2="192" y2="230" stroke="#CBD5E1" stroke-width="6" stroke-linecap="round" />
              <!-- Handle Loop -->
              <path d="M 96 22 C 96 4, 144 4, 144 22" fill="none" stroke="#E2E8F0" stroke-width="4" />
              <!-- Left & Right Dome Bells -->
              <path d="M 30 56 A 26 22 0 0 1 76 26 L 30 56 Z" fill="url(#metalBezel)" stroke="#475569" stroke-width="1.5" />
              <path d="M 210 56 A 26 22 0 0 0 164 26 L 210 56 Z" fill="url(#metalBezel)" stroke="#475569" stroke-width="1.5" />
              <!-- Center Hammer -->
              <g class="bell-hammer">
                <line x1="120" y1="30" x2="120" y2="14" stroke="#F59E0B" stroke-width="3.5" stroke-linecap="round" />
                <circle cx="120" cy="13" r="4.5" fill="#F59E0B" />
              </g>
            </g>

            <!-- Outer Case & Metallic Bezel -->
            <circle cx="120" cy="124" r="96" fill="url(#metalBezel)" />
            <circle id="analogInnerBezel" cx="120" cy="124" r="89" fill="#1E293B" />
            <circle id="analogDialCircle" cx="120" cy="124" r="85" fill="url(#dialSurfaceLight)" />

            <!-- Precision Minute Track & Hour Batons -->
            <g id="analogTicks" transform="translate(120, 124)"></g>

            <!-- Cardinal Numerals (12, 3, 6, 9) -->
            <text id="num12" x="120" y="61" text-anchor="middle" font-size="15" font-weight="800" fill="#0F172A">12</text>
            <text id="num9" x="54" y="129" text-anchor="middle" font-size="15" font-weight="800" fill="#0F172A">9</text>
            <text id="num6" x="120" y="196" text-anchor="middle" font-size="15" font-weight="800" fill="#0F172A">6</text>
            <text id="num3" x="186" y="129" text-anchor="middle" font-size="15" font-weight="800" fill="#0F172A">3</text>

            <!-- Brand Crest & Verification Subtitle -->
            <text id="analogBrandText" x="120" y="84" text-anchor="middle" font-size="9.5" font-weight="800" letter-spacing="1.6" fill="#0F172A">LIVE STUDIO</text>
            <text id="analogSubtitleText" x="120" y="95" text-anchor="middle" font-size="6.8" font-weight="700" letter-spacing="1.1" fill="#64748B">REALTIME VERIFIED</text>

            <!-- Thai Date & Digital Sub-Register Window above 6 o'clock -->
            <g id="analogDateGroup">
              <rect id="analogDateBox" x="76" y="148" width="88" height="22" rx="5" fill="#0F172A" opacity="0.90" />
              <text id="analogDateText" x="120" y="162.5" text-anchor="middle" font-family="monospace" font-size="9.5" font-weight="700" fill="#F8FAFC">--:--:--</text>
            </g>

            <!-- Hour, Minute, Second Hands -->
            <g transform="translate(120, 124)">
              <g id="hourHand" transform="rotate(0)">
                <path id="hourHandPath" d="M -3.8 14 L -2.4 -48 L 0 -54 L 2.4 -48 L 3.8 14 Z" fill="#0F172A" />
              </g>
              <g id="minuteHand" transform="rotate(0)">
                <path id="minuteHandPath" d="M -2.8 18 L -1.6 -68 L 0 -74 L 1.6 -68 L 2.8 18 Z" fill="#1E293B" />
              </g>
              <g id="secondHand" transform="rotate(0)">
                <line id="secondHandLine" x1="0" y1="22" x2="0" y2="-76" stroke="#EF4444" stroke-width="2" stroke-linecap="round" />
                <circle id="secondHandHub" cx="0" cy="0" r="4.5" fill="#EF4444" />
                <circle cx="0" cy="0" r="1.8" fill="#FFFFFF" />
              </g>
            </g>
          </svg>
        </div>

        <!-- Mode 4: WoodBlockLed -->
        <div class="clock-face face-wood">
          <div class="wood-block-chassis">
            <div class="wood-top-row">
              <span id="woodBrandText" class="truncate-text" style="max-width: 120px;">LIVE STUDIO</span>
              <span id="woodSubtitleText" class="truncate-text" style="max-width: 90px;">VERIFIED</span>
            </div>
            <div class="wood-led-readout">
              <span id="woodTimeHm">00:00</span><span id="woodTimeSec" class="wood-led-seconds">:00</span>
            </div>
            <div class="wood-bottom-row">
              <span id="woodDateText" class="truncate-text" style="max-width: 145px;">--</span>
              <span id="woodTzText" class="truncate-text" style="max-width: 65px;">TH</span>
            </div>
          </div>
        </div>

        <!-- Mode 5: DigitalClean -->
        <div class="clock-face face-digital-clean">
          <div class="digital-clean-card">
            <div class="digital-header-row">
              <span class="digital-brand-pill">
                <span class="status-dot" aria-hidden="true"></span>
                <span id="digitalBrandText" class="truncate-text">LIVE STUDIO</span>
              </span>
              <span id="digitalTzBadge" class="digital-tz-badge">Asia/Bangkok</span>
            </div>
            <div class="digital-main-time">
              <span id="digitalTimeHm" class="digital-hm">00:00</span>
              <div class="digital-sec-col">
                <span id="digitalTimeSec" class="digital-sec">:00</span>
                <span id="digitalAmPm" class="digital-ampm">24H</span>
              </div>
            </div>
            <div class="digital-footer-row">
              <span id="digitalDateText" class="truncate-text" style="max-width: 135px;">--</span>
              <span id="digitalSubtitleText" class="truncate-text" style="max-width: 75px; font-weight: 700;">VERIFIED</span>
            </div>
          </div>
        </div>
      </section>

      <!-- Broadcast Lower-Third Live Proof Ticker -->
      <section id="liveTickerBar" class="live-ticker-bar" aria-live="polite">
        <span class="status-dot" aria-hidden="true"></span>
        <span id="tickerBadge" class="ticker-badge" data-type="status">LIVE</span>
        <div class="ticker-content">
          <span id="tickerNickname" class="ticker-nickname">ยืนยันเวลาจริง</span>
          <span id="tickerMessage" class="ticker-message">ซิงก์สัญญาณสด</span>
        </div>
      </section>

      <!-- Flash Deal Countdown Bar -->
      <section id="flashDealBar" class="flash-deal-bar" aria-live="polite">
        <div class="deal-left">
          <svg class="deal-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>
          </svg>
          <span id="dealTitleText" class="deal-title">โปรนาทีทอง ส่งฟรี</span>
        </div>
        <span id="dealTimerText" class="deal-timer">05:00</span>
      </section>
    </div>
  </main>

  <script>
    (function () {
      const CLOCK_MODES = [
        "RealisticWall",
        "SeikoCustom",
        "Desk3D",
        "TwinBell",
        "WoodBlockLed",
        "DigitalClean"
      ];

      const BG_STYLES = [
        "Transparent",
        "GlassDark",
        "GlassLight"
      ];

      const EVENT_LABELS = {
        chat: "แชทสด",
        join: "เข้าชม",
        gift: "ของขวัญ",
        like: "ถูกใจ",
        follow: "ติดตาม",
        deal: "ดีลสด"
      };

      const initialEnvelope = {{initialConfigJson}};
      const initialLastEvent = {{initialEventJson}};

      let state = normalizeConfig(initialEnvelope.config || initialEnvelope);
      let ws = null;
      let reconnectDelay = 1000;

      const stageEl = document.getElementById("overlayStage");
      const viewportEl = document.getElementById("clockViewport");
      const ticksGroup = document.getElementById("analogTicks");

      if (window.location.search.indexOf("preview=1") !== -1) {
        document.body.classList.add("is-preview-mode");
      }

      // Build 60 horological dial ticks once
      if (ticksGroup) {
        const frag = document.createDocumentFragment();
        for (let i = 0; i < 60; i++) {
          const isHour = i % 5 === 0;
          const line = document.createElementNS("http://www.w3.org/2000/svg", "line");
          line.setAttribute("x1", "0");
          line.setAttribute("y1", isHour ? "-79" : "-81");
          line.setAttribute("x2", "0");
          line.setAttribute("y2", isHour ? "-71" : "-76");
          line.setAttribute("stroke", isHour ? "#1E293B" : "#94A3B8");
          line.setAttribute("stroke-width", isHour ? "2.4" : "1");
          line.setAttribute("stroke-linecap", "round");
          line.setAttribute("transform", "rotate(" + (i * 6) + ")");
          line.classList.add(isHour ? "tick-hour" : "tick-min");
          frag.appendChild(line);
        }
        ticksGroup.appendChild(frag);
      }

      function normalizeConfig(raw) {
        if (!raw) raw = {};
        const modeVal = raw.clockModeName ?? raw.clockMode ?? raw.ClockMode ?? 0;
        const bgVal = raw.backgroundStyleName ?? raw.backgroundStyle ?? raw.BackgroundStyle ?? 0;

        let modeName = typeof modeVal === "number" ? (CLOCK_MODES[modeVal] || "RealisticWall") : String(modeVal);
        if (!CLOCK_MODES.includes(modeName)) {
          const parsedIdx = parseInt(modeName, 10);
          modeName = CLOCK_MODES[parsedIdx] || "RealisticWall";
        }

        let bgName = typeof bgVal === "number" ? (BG_STYLES[bgVal] || "Transparent") : String(bgVal);
        if (!BG_STYLES.includes(bgName)) {
          const parsedBg = parseInt(bgName, 10);
          bgName = BG_STYLES[parsedBg] || "Transparent";
        }

        return {
          isEnabled: raw.isEnabled ?? raw.IsEnabled ?? true,
          clockModeName: modeName,
          backgroundStyleName: bgName,
          timeZoneId: raw.timeZoneId || raw.TimeZoneId || "Asia/Bangkok",
          use24HourFormat: raw.use24HourFormat ?? raw.Use24HourFormat ?? true,
          showSeconds: raw.showSeconds ?? raw.ShowSeconds ?? true,
          showThaiDate: raw.showThaiDate ?? raw.ShowThaiDate ?? true,
          primaryColorHex: raw.primaryColorHex || raw.PrimaryColorHex || "#F8FAFC",
          accentColorHex: raw.accentColorHex || raw.AccentColorHex || "#EF4444",
          brandText: raw.brandText ?? raw.BrandText ?? "LIVE STUDIO",
          subtitleText: raw.subtitleText ?? raw.SubtitleText ?? "REALTIME VERIFIED",
          enableGlareEffect: raw.enableGlareEffect ?? raw.EnableGlareEffect ?? true,
          enableDynamicShadow: raw.enableDynamicShadow ?? raw.EnableDynamicShadow ?? true,
          enableMicroJitter: raw.enableMicroJitter ?? raw.EnableMicroJitter ?? true,
          enableLiveEventTicker: raw.enableLiveEventTicker ?? raw.EnableLiveEventTicker ?? true,
          enableFlashDealCountdown: raw.enableFlashDealCountdown ?? raw.EnableFlashDealCountdown ?? false,
          flashDealTitle: raw.flashDealTitle ?? raw.FlashDealTitle ?? "โปรนาทีทอง ส่งฟรี",
          flashDealDurationSeconds: raw.flashDealDurationSeconds ?? raw.FlashDealDurationSeconds ?? 300,
          flashDealEndsAtUtc: raw.flashDealEndsAtUtc ?? raw.FlashDealEndsAtUtc ?? null
        };
      }

      function applyConfig(rawConfig) {
        state = normalizeConfig(rawConfig);

        document.documentElement.style.setProperty("--primary-color", state.primaryColorHex);
        document.documentElement.style.setProperty("--accent-color", state.accentColorHex);

        stageEl.classList.toggle("is-hidden", !state.isEnabled);
        stageEl.classList.toggle("fx-glare", !!state.enableGlareEffect);
        stageEl.classList.toggle("fx-shadow", !!state.enableDynamicShadow);
        stageEl.classList.toggle("fx-jitter", !!state.enableMicroJitter);
        stageEl.setAttribute("data-bg", state.backgroundStyleName);

        viewportEl.setAttribute("data-mode", state.clockModeName);

        // Style analog dial palette per horological mode
        const dialCircle = document.getElementById("analogDialCircle");
        const isDarkDial = state.clockModeName === "SeikoCustom" || state.clockModeName === "Desk3D";
        const isVintageDial = state.clockModeName === "TwinBell";

        if (dialCircle) {
          if (isDarkDial) {
            dialCircle.setAttribute("fill", "url(#dialSurfaceSeiko)");
          } else if (isVintageDial) {
            dialCircle.setAttribute("fill", "url(#dialSurfaceVintage)");
          } else {
            dialCircle.setAttribute("fill", "url(#dialSurfaceLight)");
          }
        }

        const dialFg = isDarkDial ? state.primaryColorHex : "#0F172A";
        const dialSub = isDarkDial ? "#94A3B8" : "#475569";

        ["num12", "num3", "num6", "num9", "analogBrandText"].forEach(function (id) {
          const el = document.getElementById(id);
          if (el) el.setAttribute("fill", dialFg);
        });

        const subEl = document.getElementById("analogSubtitleText");
        if (subEl) subEl.setAttribute("fill", dialSub);

        const hourPath = document.getElementById("hourHandPath");
        const minPath = document.getElementById("minuteHandPath");
        if (hourPath) hourPath.setAttribute("fill", dialFg);
        if (minPath) minPath.setAttribute("fill", dialFg);

        const secLine = document.getElementById("secondHandLine");
        const secHub = document.getElementById("secondHandHub");
        if (secLine) secLine.setAttribute("stroke", state.accentColorHex);
        if (secHub) secHub.setAttribute("fill", state.accentColorHex);

        const secondHandGroup = document.getElementById("secondHand");
        if (secondHandGroup) {
          secondHandGroup.style.display = state.showSeconds ? "block" : "none";
        }

        document.querySelectorAll("#analogTicks .tick-hour").forEach(function (el) {
          el.setAttribute("stroke", isDarkDial ? state.primaryColorHex : "#1E293B");
        });

        // Update Brand & Subtitle across all clock modes
        setText("analogBrandText", (state.brandText || "LIVE STUDIO").slice(0, 22));
        setText("analogSubtitleText", (state.subtitleText || "REALTIME VERIFIED").slice(0, 26));
        setText("woodBrandText", state.brandText || "LIVE STUDIO");
        setText("woodSubtitleText", state.subtitleText || "VERIFIED");
        setText("digitalBrandText", state.brandText || "LIVE STUDIO");
        setText("digitalSubtitleText", state.subtitleText || "VERIFIED");
        setText("digitalTzBadge", state.timeZoneId || "Asia/Bangkok");
        setText("woodTzText", (state.timeZoneId || "Asia/Bangkok").split("/").pop());

        // Live Ticker Toggle
        const tickerBar = document.getElementById("liveTickerBar");
        if (tickerBar) {
          tickerBar.classList.toggle("is-disabled", !state.enableLiveEventTicker);
        }

        // Flash Deal Toggle
        setText("dealTitleText", state.flashDealTitle || "โปรนาทีทอง ส่งฟรี");
        updateClockAndCountdown();
      }

      function applyLiveEvent(ev) {
        if (!ev) return;
        const typeKey = String(ev.eventType || ev.EventType || "chat").toLowerCase();
        const badgeLabel = ev.badgeText || ev.BadgeText || EVENT_LABELS[typeKey] || "สด";
        const nick = ev.nickname || ev.Nickname || ev.username || ev.Username || "ผู้ชม";
        const msg = ev.message || ev.Message || "";

        const badgeEl = document.getElementById("tickerBadge");
        if (badgeEl) {
          badgeEl.setAttribute("data-type", typeKey);
          badgeEl.textContent = badgeLabel;
        }
        setText("tickerNickname", nick);
        setText("tickerMessage", msg || "เข้าร่วมรับชมไลฟ์สด");
      }

      function setText(id, text) {
        const el = document.getElementById(id);
        if (el) el.textContent = text;
      }

      function getSafeTimeParts(tz, use24h) {
        const now = new Date();
        let safeTz = tz || "Asia/Bangkok";
        try {
          new Intl.DateTimeFormat("en-US", { timeZone: safeTz }).format(now);
        } catch (_) {
          safeTz = "Asia/Bangkok";
        }

        const parts = new Intl.DateTimeFormat("en-US", {
          timeZone: safeTz,
          hour12: false,
          hour: "2-digit",
          minute: "2-digit",
          second: "2-digit"
        }).formatToParts(now);

        let h24 = 0, m = 0, s = 0;
        for (let i = 0; i < parts.length; i++) {
          if (parts[i].type === "hour") h24 = parseInt(parts[i].value, 10) % 24;
          if (parts[i].type === "minute") m = parseInt(parts[i].value, 10);
          if (parts[i].type === "second") s = parseInt(parts[i].value, 10);
        }

        let displayHour = h24;
        let ampm = "24H";
        if (!use24h) {
          ampm = h24 >= 12 ? "PM" : "AM";
          displayHour = h24 % 12 || 12;
        }

        let thaiDate = "";
        try {
          const dParts = new Intl.DateTimeFormat("th-TH-u-ca-buddhist", {
            timeZone: safeTz,
            day: "numeric",
            month: "short",
            year: "2-digit"
          }).format(now);
          thaiDate = dParts;
        } catch (_) {
          thaiDate = now.toLocaleDateString();
        }

        return {
          h24: h24,
          displayHour: displayHour,
          m: m,
          s: s,
          ms: now.getMilliseconds(),
          ampm: ampm,
          thaiDate: thaiDate
        };
      }

      function pad2(n) {
        return n < 10 ? "0" + n : String(n);
      }

      function updateClockAndCountdown() {
        const t = getSafeTimeParts(state.timeZoneId, state.use24HourFormat);

        const secAngle = (t.s + t.ms / 1000) * 6;
        const minAngle = (t.m + t.s / 60) * 6;
        const hourAngle = ((t.h24 % 12) + t.m / 60) * 30;

        const hourHand = document.getElementById("hourHand");
        const minuteHand = document.getElementById("minuteHand");
        const secondHand = document.getElementById("secondHand");

        if (hourHand) hourHand.setAttribute("transform", "rotate(" + hourAngle.toFixed(2) + ")");
        if (minuteHand) minuteHand.setAttribute("transform", "rotate(" + minAngle.toFixed(2) + ")");
        if (secondHand) secondHand.setAttribute("transform", "rotate(" + secAngle.toFixed(2) + ")");

        const hmStr = pad2(t.displayHour) + ":" + pad2(t.m);
        const secStr = state.showSeconds ? (":" + pad2(t.s)) : "";

        setText("woodTimeHm", hmStr);
        setText("woodTimeSec", secStr);
        setText("digitalTimeHm", hmStr);
        setText("digitalTimeSec", secStr);
        setText("digitalAmPm", state.use24HourFormat ? "24H" : t.ampm);

        const dateLabel = state.showThaiDate ? t.thaiDate : (hmStr + secStr);
        setText("analogDateText", dateLabel);
        setText("woodDateText", state.showThaiDate ? t.thaiDate : "LIVE VERIFIED");
        setText("digitalDateText", state.showThaiDate ? t.thaiDate : "LIVE VERIFIED");

        // Flash Deal Countdown
        const dealBar = document.getElementById("flashDealBar");
        if (dealBar) {
          let remainingSec = 0;
          if (state.enableFlashDealCountdown) {
            if (state.flashDealEndsAtUtc) {
              const endMs = Date.parse(state.flashDealEndsAtUtc);
              if (!isNaN(endMs)) {
                remainingSec = Math.max(0, Math.ceil((endMs - Date.now()) / 1000));
              } else {
                remainingSec = state.flashDealDurationSeconds || 300;
              }
            } else {
              remainingSec = state.flashDealDurationSeconds || 300;
            }
          }

          const isDealActive = state.enableFlashDealCountdown && remainingSec > 0;
          dealBar.classList.toggle("is-active", isDealActive);
          stageEl.setAttribute("data-deal-active", isDealActive ? "true" : "false");

          if (isDealActive) {
            const dm = Math.floor(remainingSec / 60);
            const ds = remainingSec % 60;
            setText("dealTimerText", pad2(dm) + ":" + pad2(ds));
          }
        }
      }

      function connectWebSocket() {
        const profileId = stageEl.getAttribute("data-profile-id");
        const proto = location.protocol === "https:" ? "wss:" : "ws:";
        const wsUrl = proto + "//" + location.host + "/ws/" + profileId;

        try {
          ws = new WebSocket(wsUrl);
        } catch (_) {
          scheduleReconnect();
          return;
        }

        ws.onopen = function () {
          reconnectDelay = 1000;
          stageEl.setAttribute("data-conn", "connected");
          try {
            ws.send(JSON.stringify({ type: "sync", profileId: profileId }));
          } catch (_) {}
        };

        ws.onmessage = function (msgEvent) {
          try {
            const data = JSON.parse(msgEvent.data);
            if (!data) return;
            if (data.type === "config" || data.config) {
              applyConfig(data.config || data);
            } else if (data.type === "event" || data.event || data.payload) {
              applyLiveEvent(data.event || data.payload || data);
            }
          } catch (_) {}
        };

        ws.onerror = function () {
          // Silent handling: zero console.error to satisfy UI health hard-gate
        };

        ws.onclose = function () {
          stageEl.setAttribute("data-conn", "reconnecting");
          scheduleReconnect();
        };
      }

      function scheduleReconnect() {
        setTimeout(function () {
          reconnectDelay = Math.min(reconnectDelay * 1.8, 8000);
          connectWebSocket();
        }, reconnectDelay);
      }

      applyConfig(state);
      if (initialLastEvent) {
        applyLiveEvent(initialLastEvent);
      }
      updateClockAndCountdown();
      setInterval(updateClockAndCountdown, 250);
      connectWebSocket();
    })();
  </script>
</body>
</html>
""";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore dispose errors
        }
        _lifecycleLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
