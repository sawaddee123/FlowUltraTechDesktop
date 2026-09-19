using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PuppeteerSharp;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Services;

public partial class CdpAutomationService : ICdpAutomationService, IDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly ConcurrentDictionary<int, IBrowser> _connectedBrowsers = new();
    private readonly ConcurrentDictionary<string, (NetworkTrafficItem item, Stopwatch sw)> _activeRequests = new();
    private readonly ConcurrentDictionary<int, bool> _hookedPorts = new();
    private readonly IInstanceLogManager _logManager;
    private IBrowser? _headlessBrowser;

    public CdpAutomationService(IInstanceLogManager? logManager = null)
    {
        _logManager = logManager ?? InstanceLogManager.Shared;
    }

    public async Task<bool> CheckConnectionAsync(int cdpPort)
    {
        try
        {
            var response = await _httpClient.GetAsync($"http://127.0.0.1:{cdpPort}/json/version");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }


    public async Task<string> ExecuteScriptAsync(int cdpPort, string script)
    {
        if (string.IsNullOrWhiteSpace(script)) return "Script cannot be empty.";
        var logger = _logManager.TryGetLoggerByPort(cdpPort);
        logger?.Debug("CDP", $"Executing script on port {cdpPort}: {script}");

        try
        {
            var browser = await GetOrConnectBrowserAsync(cdpPort);
            if (browser == null)
            {
                logger?.Warn("CDP", $"Cannot connect to CDP port {cdpPort}. Ensure TikTok Studio is running.");
                return $"Error: Cannot connect to CDP port {cdpPort}. Ensure TikTok Studio is running.";
            }

            var pages = await browser.PagesAsync();
            var page = pages.FirstOrDefault(p => 
                p.Url.Contains("chat", StringComparison.OrdinalIgnoreCase) || 
                p.Url.Contains("webcast", StringComparison.OrdinalIgnoreCase)) ?? pages.FirstOrDefault();

            if (page == null)
            {
                logger?.Warn("CDP", "No active web page/view found in TikTok LIVE Studio.");
                return "Error: No active web page/view found in TikTok LIVE Studio.";
            }

            var result = await page.EvaluateExpressionAsync<object>(script);
            if (result == null)
            {
                logger?.Debug("CDP", "Script evaluated to undefined.");
                return "undefined";
            }

            string formatted;
            try
            {
                formatted = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                formatted = result.ToString() ?? "null";
            }

            logger?.Debug("CDP", $"Script output: {formatted}");
            return formatted;
        }
        catch (Exception ex)
        {
            logger?.Error("CDP", $"JavaScript Exception: {ex.Message}", null, ex);
            return $"JavaScript Exception: {ex.Message}";
        }
    }


    private async Task<IBrowser?> GetOrConnectBrowserAsync(int cdpPort)
    {
        if (_connectedBrowsers.TryGetValue(cdpPort, out var existingBrowser) && !existingBrowser.IsClosed)
        {
            return existingBrowser;
        }

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var options = new ConnectOptions
                {
                    BrowserURL = $"http://127.0.0.1:{cdpPort}",
                    DefaultViewport = null // Prevents Puppeteer from forcing 800x600 viewport shrink!
                };

                var browser = await Puppeteer.ConnectAsync(options);
                _connectedBrowsers[cdpPort] = browser;

                // Anti-detection & Viewport preservation
                try
                {
                    var pages = await browser.PagesAsync();
                    foreach (var p in pages)
                    {
                        try
                        {
                            await p.SetViewportAsync(null);
                        }
                        catch { }

                        try
                        {
                            await p.EvaluateFunctionOnNewDocumentAsync("() => { Object.defineProperty(navigator, 'webdriver', { get: () => undefined }); }");
                        }
                        catch { }
                    }
                }
                catch { }

                return browser;
            }
            catch (Exception ex)
            {
                if (attempt == 4)
                {
                    Debug.WriteLine($"[CdpAutomationService] ConnectAsync failed for port {cdpPort}: {ex.Message}");
                }
                else
                {
                    await Task.Delay(400);
                }
            }
        }

        return null;
    }


    public void Dispose()
    {
        if (_headlessBrowser != null)
        {
            try { _headlessBrowser.Dispose(); } catch { }
            _headlessBrowser = null;
        }
        foreach (var pair in _connectedBrowsers)
        {
            pair.Value.Dispose();
        }
        _connectedBrowsers.Clear();
        _httpClient.Dispose();
    }
}
