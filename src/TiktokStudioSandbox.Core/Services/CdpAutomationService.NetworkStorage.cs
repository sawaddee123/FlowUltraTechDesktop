using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PuppeteerSharp;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Services;

public partial class CdpAutomationService
{
    public async Task StartNetworkHookAsync(int cdpPort, Action<NetworkTrafficItem> onRequest, Action<NetworkTrafficItem> onResponse)
    {
        var logger = _logManager.TryGetLoggerByPort(cdpPort);
        logger?.Info("Network", $"Starting network hook on port {cdpPort}...");

        try
        {
            var browser = await GetOrConnectBrowserAsync(cdpPort);
            if (browser == null)
            {
                logger?.Warn("Network", $"Cannot connect browser on port {cdpPort} for network hook.");
                return;
            }

            if (_hookedPorts.ContainsKey(cdpPort)) return;
            _hookedPorts[cdpPort] = true;
            logger?.Info("Network", $"Network hook is active on port {cdpPort}");

            void AttachPageHooks(IPage page)
            {
                page.Request += (sender, e) =>
                {
                    var item = new NetworkTrafficItem
                    {
                        RequestId = Guid.NewGuid().ToString("N"),
                        Time = DateTime.Now,
                        Method = e.Request.Method.Method,
                        Url = e.Request.Url,
                        ResourceType = e.Request.ResourceType.ToString(),
                        StatusDisplay = "⏳ Pending...",
                        PostData = e.Request.PostData ?? string.Empty
                    };

                    var sbReqHeaders = new StringBuilder();
                    foreach (var (k, v) in e.Request.Headers)
                    {
                        sbReqHeaders.AppendLine($"{k}: {v}");
                        if (k.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                        {
                            item.RequestCookies = v;
                        }
                    }
                    item.RequestHeaders = sbReqHeaders.ToString();

                    var sw = Stopwatch.StartNew();
                    _activeRequests[e.Request.Url + "_" + e.Request.Method] = (item, sw);

                    onRequest(item);
                };

                page.Response += async (sender, e) =>
                {
                    string key = e.Response.Request.Url + "_" + e.Response.Request.Method;
                    if (_activeRequests.TryRemove(key, out var tuple))
                    {
                        var item = tuple.item;
                        tuple.sw.Stop();
                        item.DurationMs = tuple.sw.ElapsedMilliseconds;
                        item.StatusCode = (int)e.Response.Status;
                        item.StatusText = e.Response.StatusText;
                        item.StatusDisplay = $"{(int)e.Response.Status} {e.Response.StatusText}";
                        item.IsCompleted = true;

                        var sbResHeaders = new StringBuilder();
                        var sbResCookies = new StringBuilder();
                        foreach (var (k, v) in e.Response.Headers)
                        {
                            sbResHeaders.AppendLine($"{k}: {v}");
                            if (k.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                            {
                                sbResCookies.AppendLine(v);
                            }
                        }
                        item.ResponseHeaders = sbResHeaders.ToString();
                        item.ResponseCookies = sbResCookies.ToString();

                        try
                        {
                            // Strictly avoid consuming responses for auth/login/redirect endpoints to prevent interference
                            bool isAuthEndpoint = item.Url.Contains("/passport/", StringComparison.OrdinalIgnoreCase) ||
                                                  item.Url.Contains("/ucenter", StringComparison.OrdinalIgnoreCase) ||
                                                  item.Url.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
                                                  item.Url.Contains("/onetap_auth", StringComparison.OrdinalIgnoreCase) ||
                                                  item.Url.Contains("/auth/", StringComparison.OrdinalIgnoreCase);

                            if (!isAuthEndpoint && (item.ResourceType == "xhr" || item.ResourceType == "fetch" || item.Url.Contains("/webcast/")))
                            {
                                var textTask = e.Response.TextAsync();
                                if (await Task.WhenAny(textTask, Task.Delay(300)) == textTask)
                                {
                                    item.ResponseBody = await textTask;
                                }
                            }
                        }
                        catch { }

                        onResponse(item); // Update the existing item in-place
                    }
                };
            }

            var pages = await browser.PagesAsync();
            foreach (var page in pages)
            {
                AttachPageHooks(page);
            }

            // Hook any subsequent pages/tabs opened by TikTok Studio
            browser.TargetCreated += async (sender, e) =>
            {
                if (e.Target.Type == TargetType.Page)
                {
                    try
                    {
                        var newPage = await e.Target.PageAsync();
                        if (newPage != null)
                        {
                            AttachPageHooks(newPage);
                        }
                    }
                    catch { }
                }
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CdpAutomationService] StartNetworkHookAsync error: {ex.Message}");
        }
    }

    public Task StopNetworkHookAsync(int cdpPort)
    {
        var logger = _logManager.TryGetLoggerByPort(cdpPort);
        logger?.Info("Network", $"Stopping network hook on port {cdpPort}...");
        _hookedPorts.TryRemove(cdpPort, out _);
        if (_connectedBrowsers.TryRemove(cdpPort, out var browser))
        {
            try { browser.Disconnect(); } catch { }
        }
        logger?.Info("Network", $"Network hook stopped on port {cdpPort}");
        return Task.CompletedTask;
    }

    public async Task<StorageDumpResult> GetCookiesAndStorageAsync(int cdpPort)
    {
        var result = new StorageDumpResult();
        try
        {
            var browser = await GetOrConnectBrowserAsync(cdpPort);
            if (browser == null) return result;

            var pages = await browser.PagesAsync();
            var page = pages.FirstOrDefault(p => 
                p.Url.Contains("chat", StringComparison.OrdinalIgnoreCase) || 
                p.Url.Contains("webcast", StringComparison.OrdinalIgnoreCase)) ?? pages.FirstOrDefault();

            if (page == null) return result;

            // 1. Primary extraction via CDP Network.getAllCookies (gets ALL profile cookies across all domains)
            try
            {
                var cdpSession = await page.CreateCDPSessionAsync();
                var cdpResponse = await cdpSession.SendAsync("Network.getAllCookies");
                if (cdpResponse.HasValue && cdpResponse.Value.TryGetProperty("cookies", out var cookiesElem) && cookiesElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in cookiesElem.EnumerateArray())
                    {
                        string name = c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        string val = c.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                        string domain = c.TryGetProperty("domain", out var d) ? d.GetString() ?? "" : "";
                        string path = c.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "/";
                        bool httpOnly = c.TryGetProperty("httpOnly", out var ho) && ho.GetBoolean();
                        bool secure = c.TryGetProperty("secure", out var sec) && sec.GetBoolean();
                        string expires = c.TryGetProperty("expires", out var exp) ? exp.ToString() : "";

                        bool isAuth = name.Equals("sessionid", StringComparison.OrdinalIgnoreCase) ||
                                      name.Equals("sessionid_ss", StringComparison.OrdinalIgnoreCase) ||
                                      name.Equals("tt_csrf_token", StringComparison.OrdinalIgnoreCase) ||
                                      name.Equals("passport_csrf_token", StringComparison.OrdinalIgnoreCase) ||
                                      name.Equals("msToken", StringComparison.OrdinalIgnoreCase) ||
                                      name.Equals("access_token", StringComparison.OrdinalIgnoreCase) ||
                                      name.Equals("sid_guard", StringComparison.OrdinalIgnoreCase) ||
                                      name.Equals("odin_tt", StringComparison.OrdinalIgnoreCase);

                        if (name.Equals("sessionid", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(result.SessionIdToken)) result.SessionIdToken = val;
                        if (name.Equals("tt_csrf_token", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(result.CsrfToken)) result.CsrfToken = val;
                        if (name.Equals("msToken", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(result.MsToken)) result.MsToken = val;

                        result.Cookies.Add(new CookieItem
                        {
                            Name = name,
                            Value = val,
                            Domain = domain,
                            Path = path,
                            Expires = expires,
                            HttpOnly = httpOnly,
                            Secure = secure,
                            IsAuthToken = isAuth
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CdpAutomationService] Network.getAllCookies error: {ex.Message}");
            }

            // 2. Fallback if CDP Network.getAllCookies returned 0
            if (result.Cookies.Count == 0)
            {
                foreach (var p in pages)
                {
                    try
                    {
                        var rawCookies = await p.GetCookiesAsync();
                        foreach (var c in rawCookies)
                        {
                            if (result.Cookies.Any(existing => existing.Name == c.Name && existing.Domain == c.Domain)) continue;

                            bool isAuth = c.Name.Equals("sessionid", StringComparison.OrdinalIgnoreCase) ||
                                          c.Name.Equals("sessionid_ss", StringComparison.OrdinalIgnoreCase) ||
                                          c.Name.Equals("tt_csrf_token", StringComparison.OrdinalIgnoreCase) ||
                                          c.Name.Equals("passport_csrf_token", StringComparison.OrdinalIgnoreCase) ||
                                          c.Name.Equals("msToken", StringComparison.OrdinalIgnoreCase) ||
                                          c.Name.Equals("access_token", StringComparison.OrdinalIgnoreCase);

                            if (c.Name.Equals("sessionid", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(result.SessionIdToken)) result.SessionIdToken = c.Value;
                            if (c.Name.Equals("tt_csrf_token", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(result.CsrfToken)) result.CsrfToken = c.Value;
                            if (c.Name.Equals("msToken", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(result.MsToken)) result.MsToken = c.Value;

                            result.Cookies.Add(new CookieItem
                            {
                                Name = c.Name,
                                Value = c.Value,
                                Domain = c.Domain,
                                Path = c.Path,
                                Expires = c.Expires?.ToString() ?? string.Empty,
                                HttpOnly = c.HttpOnly ?? false,
                                Secure = c.Secure ?? false,
                                IsAuthToken = isAuth
                            });
                        }
                    }
                    catch { }
                }
            }

            // 3. Extract LocalStorage and SessionStorage across all pages
            foreach (var p in pages)
            {
                try
                {
                    string storageJson = await p.EvaluateFunctionAsync<string>(@"() => {
                        const ls = {};
                        for (let i = 0; i < localStorage.length; i++) {
                            const k = localStorage.key(i);
                            ls[k] = localStorage.getItem(k);
                        }
                        const ss = {};
                        for (let i = 0; i < sessionStorage.length; i++) {
                            const k = sessionStorage.key(i);
                            ss[k] = sessionStorage.getItem(k);
                        }
                        return JSON.stringify({ ls, ss });
                    }");

                    using var doc = JsonDocument.Parse(storageJson);
                    if (doc.RootElement.TryGetProperty("ls", out var lsElem))
                    {
                        foreach (var prop in lsElem.EnumerateObject())
                        {
                            result.LocalStorage[prop.Name] = prop.Value.GetString() ?? "";
                        }
                    }
                    if (doc.RootElement.TryGetProperty("ss", out var ssElem))
                    {
                        foreach (var prop in ssElem.EnumerateObject())
                        {
                            result.SessionStorage[prop.Name] = prop.Value.GetString() ?? "";
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CdpAutomationService] GetCookiesAndStorageAsync error: {ex.Message}");
        }

        var logger = _logManager.TryGetLoggerByPort(cdpPort);
        logger?.Info("Storage", $"Fetched {result.Cookies.Count} cookies, {result.LocalStorage.Count} LocalStorage, {result.SessionStorage.Count} SessionStorage on port {cdpPort}");
        return result;
    }

    public async Task<PageHtmlResult?> GetFullHtmlAsync(int cdpPort)
    {
        var logger = _logManager.TryGetLoggerByPort(cdpPort);
        logger?.Info("CDP", $"Fetching full HTML on port {cdpPort}...");

        try
        {
            var browser = await GetOrConnectBrowserAsync(cdpPort);
            if (browser == null)
            {
                logger?.Warn("CDP", $"Cannot connect to CDP port {cdpPort} to get HTML. Ensure TikTok Studio is running.");
                return null;
            }

            var pages = await browser.PagesAsync();
            if (pages == null || pages.Length == 0)
            {
                logger?.Warn("CDP", $"No active pages found on port {cdpPort}.");
                return null;
            }

            // Prefer active chat/webcast/studio page over internal devtools
            var page = pages.FirstOrDefault(p => 
                p.Url.Contains("chat", StringComparison.OrdinalIgnoreCase) || 
                p.Url.Contains("webcast", StringComparison.OrdinalIgnoreCase) ||
                p.Url.Contains("studio", StringComparison.OrdinalIgnoreCase))
                ?? pages.FirstOrDefault(p => !string.IsNullOrEmpty(p.Url) && !p.Url.StartsWith("devtools://") && !p.Url.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
                ?? pages.FirstOrDefault();

            if (page == null)
            {
                logger?.Warn("CDP", "No suitable page found to extract HTML.");
                return null;
            }

            string html = string.Empty;
            try
            {
                html = await page.GetContentAsync();
            }
            catch (Exception ex)
            {
                logger?.Debug("CDP", $"page.GetContentAsync failed, falling back to EvaluateExpressionAsync: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                try
                {
                    html = await page.EvaluateExpressionAsync<string>("document.documentElement ? document.documentElement.outerHTML : (document.body ? document.body.outerHTML : '')");
                }
                catch (Exception ex)
                {
                    logger?.Warn("CDP", $"Fallback evaluate HTML failed: {ex.Message}");
                }
            }

            string title = string.Empty;
            try
            {
                title = await page.GetTitleAsync();
            }
            catch { }

            logger?.Info("CDP", $"Successfully extracted HTML ({html?.Length ?? 0} chars) from '{page.Url}' (Title: '{title}')");

            return new PageHtmlResult
            {
                Html = html ?? string.Empty,
                Url = page.Url ?? string.Empty,
                Title = title ?? string.Empty,
                PageCount = pages.Length,
                FetchedAt = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            logger?.Error("CDP", $"Failed to get full HTML: {ex.Message}", null, ex);
            return null;
        }
    }


}
