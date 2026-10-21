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
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public partial class CdpAutomationService : ICdpAutomationService, IDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly HttpClient _solverHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ConcurrentDictionary<int, IBrowser> _connectedBrowsers = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _connectLocks = new();
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


    private void InvalidateBrowserConnection(int cdpPort)
    {
        if (_connectedBrowsers.TryRemove(cdpPort, out var staleBrowser))
        {
            try { staleBrowser.Disconnect(); } catch { }
            try { staleBrowser.Dispose(); } catch { }
        }
    }

    private async Task<IBrowser?> GetOrConnectBrowserAsync(int cdpPort)
    {
        if (_connectedBrowsers.TryGetValue(cdpPort, out var existingBrowser) && !existingBrowser.IsClosed && existingBrowser.IsConnected)
        {
            return existingBrowser;
        }

        var portLock = _connectLocks.GetOrAdd(cdpPort, _ => new SemaphoreSlim(1, 1));
        if (!await portLock.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            return _connectedBrowsers.TryGetValue(cdpPort, out var current) && !current.IsClosed ? current : null;
        }

        try
        {
            if (_connectedBrowsers.TryGetValue(cdpPort, out existingBrowser) && !existingBrowser.IsClosed && existingBrowser.IsConnected)
            {
                return existingBrowser;
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var options = new ConnectOptions
                    {
                        BrowserURL = $"http://127.0.0.1:{cdpPort}",
                        DefaultViewport = null, // Prevents Puppeteer from forcing 800x600 viewport shrink!
                        ProtocolTimeout = 8000  // Prevent 180s (3m) default CDP protocol freeze on occluded/minimized windows
                    };

                    var connectTask = Puppeteer.ConnectAsync(options);
                    if (await Task.WhenAny(connectTask, Task.Delay(4000)) != connectTask)
                    {
                        _ = connectTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                        throw new TimeoutException($"Timed out connecting to CDP port {cdpPort}");
                    }

                    var browser = await connectTask;
                    _connectedBrowsers[cdpPort] = browser;

                    // Anti-detection & Viewport preservation
                    try
                    {
                        var pages = await browser.PagesAsync();
                        foreach (var p in pages)
                        {
                            try
                            {
                                p.DefaultTimeout = 5000;
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
                    if (attempt == 2)
                    {
                        Debug.WriteLine($"[CdpAutomationService] ConnectAsync failed for port {cdpPort}: {ex.Message}");
                    }
                    else
                    {
                        await Task.Delay(250);
                    }
                }
            }

            return null;
        }
        finally
        {
            portLock.Release();
        }
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
        foreach (var pair in _connectLocks)
        {
            try { pair.Value.Dispose(); } catch { }
        }
        _connectLocks.Clear();
        _httpClient.Dispose();
        _solverHttpClient.Dispose();
    }
}
