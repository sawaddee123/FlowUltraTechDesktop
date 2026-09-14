using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PuppeteerSharp;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Services;

public class CdpAutomationService : ICdpAutomationService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly ConcurrentDictionary<int, IBrowser> _connectedBrowsers = new();
    private readonly ConcurrentDictionary<string, (NetworkTrafficItem item, Stopwatch sw)> _activeRequests = new();
    private readonly ConcurrentDictionary<int, bool> _hookedPorts = new();
    private readonly IInstanceLogManager _logManager;

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

    public async Task<string?> GetChatPageUrlAsync(int cdpPort)
    {
        try
        {
            var browser = await GetOrConnectBrowserAsync(cdpPort);
            if (browser == null) return null;

            var pages = await browser.PagesAsync();
            var chatPage = pages.FirstOrDefault(p => 
                p.Url.Contains("chat", StringComparison.OrdinalIgnoreCase) || 
                p.Url.Contains("webcast", StringComparison.OrdinalIgnoreCase) ||
                p.Url.Contains("room", StringComparison.OrdinalIgnoreCase));

            return chatPage?.Url;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CdpAutomationService] GetChatPageUrl failed: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> SendChatMessageAsync(int cdpPort, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        try
        {
            var browser = await GetOrConnectBrowserAsync(cdpPort);
            if (browser == null) return false;

            var pages = await browser.PagesAsync();
            IPage? targetPage = pages.FirstOrDefault(p => 
                p.Url.Contains("chat", StringComparison.OrdinalIgnoreCase) || 
                p.Url.Contains("webcast", StringComparison.OrdinalIgnoreCase)) ?? pages.FirstOrDefault();

            if (targetPage == null) return false;

            string[] selectors = new[]
            {
                "textarea[placeholder*='chat']",
                "textarea[placeholder*='แชท']",
                "textarea",
                "div[contenteditable='true']",
                "input[placeholder*='chat']"
            };

            IElementHandle? inputElement = null;
            foreach (var sel in selectors)
            {
                inputElement = await targetPage.QuerySelectorAsync(sel);
                if (inputElement != null) break;
            }

            if (inputElement != null)
            {
                await inputElement.FocusAsync();
                await inputElement.TypeAsync(message);
                await Task.Delay(100);
                await targetPage.Keyboard.PressAsync("Enter");
                return true;
            }

            // Fallback to fetch
            return await targetPage.EvaluateFunctionAsync<bool>(@"
                async (msg) => {
                    try {
                        const res = await fetch('/webcast/room/chat/', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ content: msg })
                        });
                        return res.ok;
                    } catch (e) {
                        return false;
                    }
                }
            ", message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CdpAutomationService] SendChatMessage error: {ex.Message}");
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

    public async Task<CaptchaDetectionResult> DetectCaptchaElementsAsync(int cdpPort)
    {
        var result = new CaptchaDetectionResult { DetectedAt = DateTime.Now };
        var logger = _logManager.TryGetLoggerByPort(cdpPort);

        try
        {
            var browser = await GetOrConnectBrowserAsync(cdpPort);
            if (browser == null) return result;

            var pages = await browser.PagesAsync();
            if (pages == null || pages.Length == 0) return result;

            // Script for smart detection, topmost layer calculation, visibility verification, and full HTML extraction
            const string detectScript = @"(() => {
                function getDepth(el) {
                    let d = 0;
                    let cur = el;
                    while (cur && cur.parentElement) {
                        d++;
                        cur = cur.parentElement;
                    }
                    return d;
                }

                function isElementVisible(el) {
                    if (!el) return false;
                    // Check if element or any ancestor is in an exited modal or has exit-done class
                    if (el.closest && el.closest('.tt-live-modalv2-wrapper-exited, [class*=""exit-done""]')) return false;

                    // Check inline or computed styles
                    if (el.style && (el.style.display === 'none' || el.style.visibility === 'hidden')) return false;
                    try {
                        const style = window.getComputedStyle(el);
                        if (style && (style.display === 'none' || style.visibility === 'hidden' || parseFloat(style.opacity || '1') <= 0.01)) {
                            return false;
                        }
                    } catch(e) {}

                    // Check bounding rect dimensions
                    const rect = el.getBoundingClientRect();
                    if (rect.width <= 0 || rect.height <= 0) return false;

                    // Check offsetParent (or root body)
                    return el.offsetParent !== null || el.tagName.toLowerCase() === 'body';
                }

                function isCaptchaElement(el) {
                    if (!el || !el.tagName) return false;
                    const tag = el.tagName.toLowerCase();
                    if (['html', 'head', 'body', 'script', 'style', 'link', 'meta', 'title'].includes(tag)) return false;

                    // 1. Check ID
                    if (el.id && el.id.toLowerCase().includes('captcha')) return true;

                    // 2. Check ClassName
                    const className = typeof el.className === 'string' ? el.className : (el.className && el.className.baseVal ? el.className.baseVal : '');
                    if (className && className.toLowerCase().includes('captcha')) return true;

                    // 3. Check all attributes
                    if (el.attributes) {
                        for (let i = 0; i < el.attributes.length; i++) {
                            const a = el.attributes[i];
                            const name = a.name.toLowerCase();
                            const val = (a.value || '').toLowerCase();
                            if (name.includes('captcha') || val.includes('captcha')) return true;
                        }
                    }

                    // 4. Check known SecSDK / verification tags
                    if (el.id && (el.id.toLowerCase().includes('secsdk') || el.id.toLowerCase().includes('verify-bar') || el.id.toLowerCase().includes('verify_container') || el.id.toLowerCase().includes('verify-image'))) return true;
                    if (className && (className.toLowerCase().includes('secsdk') || className.toLowerCase().includes('verify-bar') || className.toLowerCase().includes('verify_container') || className.toLowerCase().includes('verify-image') || className.toLowerCase().includes('whirl'))) return true;

                    return false;
                }

                function findTopmost(rootDoc) {
                    if (!rootDoc) return [];
                    const all = Array.from(rootDoc.querySelectorAll('*'));
                    const matched = all.filter(isCaptchaElement);
                    if (matched.length === 0) return [];

                    // SMART LAYER: Keep elements that are not contained in any other matched element
                    const topmost = matched.filter(el => {
                        return !matched.some(other => other !== el && other.contains(el));
                    });

                    topmost.sort((a, b) => getDepth(a) - getDepth(b));

                    return topmost.map(el => {
                        const rect = el.getBoundingClientRect();
                        return {
                            tagName: el.tagName.toLowerCase(),
                            id: el.id || '',
                            className: typeof el.className === 'string' ? el.className : (el.className && el.className.baseVal ? el.className.baseVal : ''),
                            html: el.outerHTML,
                            depth: getDepth(el),
                            isVisible: isElementVisible(el),
                            width: rect.width || 0,
                            height: rect.height || 0,
                            x: rect.x || 0,
                            y: rect.y || 0,
                            frameUrl: rootDoc.location ? rootDoc.location.href : ''
                        };
                    });
                }

                const items = [];
                const mainItems = findTopmost(document);
                for (const it of mainItems) items.push(it);

                const iframeHtmls = [];
                const iframes = Array.from(document.querySelectorAll('iframe'));
                for (const iframe of iframes) {
                    try {
                        const fDoc = iframe.contentDocument || (iframe.contentWindow && iframe.contentWindow.document);
                        if (fDoc) {
                            const fItems = findTopmost(fDoc);
                            if (fItems.length > 0) {
                                for (const it of fItems) items.push(it);
                                const fHtml = fDoc.documentElement ? fDoc.documentElement.outerHTML : (fDoc.body ? fDoc.body.outerHTML : '');
                                if (fHtml) {
                                    iframeHtmls.push({
                                        url: fDoc.location ? fDoc.location.href : (iframe.src || ''),
                                        html: fHtml
                                    });
                                }
                            }
                        }
                    } catch(e) {}
                }

                // Inactivity warning modal status
                let inactModalContent = document.querySelector('.captcha-modal-content-UBaIok, [class*=""captcha-modal-content""]');
                if (!inactModalContent) {
                    // Fallback to text detection for inactivity modal
                    const candidateEls = Array.from(document.querySelectorAll('div, section, p, span, h1, h2, h3, h4'));
                    for (const el of candidateEls) {
                        const txt = el.textContent || '';
                        if (txt.includes('detected inactivity during your current LIVE') ||
                            txt.includes('Complete the verification to continue your current LIVE') ||
                            txt.includes('LIVE will end in:')) {
                            inactModalContent = el.closest('[role=""dialog""], .tt-live-modalv2-wrapper, [class*=""modalv2""], [class*=""modal""]') || el;
                            break;
                        }
                    }
                }

                const inactModalWrapper = inactModalContent ? (inactModalContent.closest ? inactModalContent.closest('.tt-live-modalv2-wrapper, [class*=""modalv2-wrapper""]') : null) : null;
                const isModalExited = inactModalWrapper
                    ? (inactModalWrapper.classList.contains('tt-live-modalv2-wrapper-exited') || (inactModalWrapper.className && inactModalWrapper.className.includes('exited')))
                    : false;
                const isModalVisible = inactModalContent ? isElementVisible(inactModalContent) : false;

                // Fallback topmost item if modal is visible but classes were obfuscated
                if (items.length === 0 && inactModalContent && isModalVisible && !isModalExited) {
                    const rect = inactModalContent.getBoundingClientRect();
                    items.push({
                        tagName: inactModalContent.tagName.toLowerCase(),
                        id: inactModalContent.id || '',
                        className: typeof inactModalContent.className === 'string' ? inactModalContent.className : (inactModalContent.className && inactModalContent.className.baseVal ? inactModalContent.className.baseVal : ''),
                        html: inactModalContent.outerHTML,
                        depth: getDepth(inactModalContent),
                        isVisible: true,
                        width: rect.width || 0,
                        height: rect.height || 0,
                        x: rect.x || 0,
                        y: rect.y || 0,
                        frameUrl: document.location ? document.location.href : ''
                    });
                }

                let mainFullHtml = '';
                if (items.length > 0 || (inactModalContent && isModalVisible && !isModalExited)) {
                    mainFullHtml = document.documentElement ? document.documentElement.outerHTML : (document.body ? document.body.outerHTML : '');
                }

                let countdownText = '';
                try {
                    const cdEl = document.querySelector('.countdown-EXaokG, [class*=""countdown""], #captcha-mount-point [class*=""countdown""]');
                    if (cdEl && isElementVisible(cdEl)) {
                        countdownText = (cdEl.textContent || '').trim();
                    }
                } catch(e) {}

                if (!countdownText || !/\d{1,2}:\d{2}/.test(countdownText)) {
                    const scope = inactModalContent || document.body;
                    if (scope) {
                        const text = scope.innerText || scope.textContent || '';
                        const liveEndMatch = text.match(/LIVE will end in[:\s]*(\d{1,2}:\d{2})/i);
                        if (liveEndMatch && liveEndMatch[1]) {
                            countdownText = liveEndMatch[1];
                        } else if (inactModalContent) {
                            const anyCd = (inactModalContent.innerText || inactModalContent.textContent || '').match(/\b(\d{1,2}:\d{2})\b/);
                            if (anyCd && anyCd[1]) {
                                countdownText = anyCd[1];
                            }
                        }
                    }
                }

                // Challenge / Session ID extraction (e.g. 20260912200836236424C80169BFCB4CF...)
                function extractSessionId(doc) {
                    if (!doc) return '';
                    const text = (doc.body ? (doc.body.innerText || doc.body.textContent) : '') || '';
                    const m = text.match(/\b(20\d{12}[A-Za-z0-9]{12,})\b/);
                    return m ? m[1] : '';
                }

                let sessionId = extractSessionId(document);

                // Active Captcha Widget scanning across document and all accessible iframes
                function scanForActiveWidget(doc) {
                    if (!doc) return null;

                    const docText = (doc.body ? (doc.body.innerText || doc.body.textContent) : '') || '';

                    // 1. Jigsaw (Slide puzzle)
                    const hasJigsawInstruction = /drag the puzzle piece into place/i.test(docText) || /drag.*puzzle/i.test(docText) || /ลาก.*จิ๊กซอว์/i.test(docText);
                    let slideImg = doc.querySelector('#captcha-verify-image, img.captcha_verify_img_slide, canvas.captcha_verify_img_slide, img[src*=""captcha""][class*=""slide""], img[alt=""Captcha""], img[aria-label=""Captcha""], [class*=""captcha-verify-image""]');
                    let dragKnob = doc.querySelector('.secsdk-captcha-drag-icon, .captcha_verify_slide--drag-icon, div[role=""slider""], button[role=""slider""], .sec-captcha-drag-icon, [class*=""drag-icon""], [class*=""drag_icon""], [class*=""slider-btn""], [aria-label*=""slider"" i]');

                    if (!slideImg && hasJigsawInstruction) {
                        const candidateImgs = Array.from(doc.querySelectorAll('img, canvas')).filter(el => {
                            if (!isElementVisible(el)) return false;
                            const r = el.getBoundingClientRect();
                            return r.width >= 150 && r.height >= 100;
                        });
                        if (candidateImgs.length > 0) slideImg = candidateImgs[0];
                    }

                    if (!dragKnob && hasJigsawInstruction) {
                        dragKnob = doc.querySelector('[class*=""slidebar""] button, [class*=""drag-track""] button, [class*=""slide""] [role=""button""], [class*=""slidebar""] [tabindex], button:has(svg)');
                    }

                    if ((slideImg && dragKnob && isElementVisible(slideImg) && isElementVisible(dragKnob)) ||
                        (hasJigsawInstruction && (slideImg || dragKnob) && isElementVisible(slideImg || dragKnob))) {
                        return {
                            type: 'Jigsaw',
                            typeDescription: 'จิ๊กซอว์เลื่อนต่อภาพ (Slide Captcha)',
                            imageSrc: (slideImg && (slideImg.src || slideImg.getAttribute('src'))) || '',
                            promptText: hasJigsawInstruction ? 'Drag the puzzle piece into place' : 'ลากสไลเดอร์เพื่อต่อชิ้นส่วนจิ๊กซอว์ให้พอดี',
                            hasWidgetLoaded: !!(slideImg && isElementVisible(slideImg))
                        };
                    }

                    // 2. Symbol (Icon match)
                    const hasSymbolInstruction = /select.*same/i.test(docText) || /click.*same/i.test(docText) || /select.*object/i.test(docText) || /เลือก.*เหมือนกัน/i.test(docText);
                    const symbolImg = doc.querySelector('#captcha-verify-image, img.captcha_verify_img_symbol, .captcha_verify_img--bg, canvas.captcha_verify_img--bg, img[class*=""symbol""]');
                    const submitBtn = doc.querySelector('.secsdk-captcha-submit-button, [class*=""secsdk-captcha-submit""], button[class*=""verify-btn""]');
                    let prompt = '';
                    const promptEl = doc.querySelector('.secsdk-captcha-label, .captcha_verify_label, [class*=""tip-wrapper""], [class*=""prompt""], [class*=""title""]');
                    if (promptEl && isElementVisible(promptEl)) prompt = (promptEl.textContent || '').trim();

                    if (symbolImg && isElementVisible(symbolImg) && (submitBtn || /select|click|same|object|symbol/i.test(prompt) || hasSymbolInstruction)) {
                        return {
                            type: 'Symbol',
                            typeDescription: 'เลือกสัญลักษณ์ 2 รูปที่เหมือนกัน (Symbol Captcha)',
                            imageSrc: symbolImg.src || symbolImg.getAttribute('src') || '',
                            promptText: prompt || (hasSymbolInstruction ? 'Select 2 same objects' : 'คลิกเลือกรูปภาพสัญลักษณ์ที่เหมือนกัน 2 รูปแล้วกดยืนยัน'),
                            hasWidgetLoaded: true
                        };
                    }

                    // 3. Rotate / Whirl
                    const hasRotateInstruction = /rotate.*upright/i.test(docText) || /หมุนภาพ/i.test(docText);
                    const rotateEl = doc.querySelector('[class*=""rotate""], [class*=""whirl""]:not([class*=""title""])');
                    if (!hasJigsawInstruction && (hasRotateInstruction || (rotateEl && isElementVisible(rotateEl))) && dragKnob && isElementVisible(dragKnob)) {
                        return {
                            type: 'Rotate',
                            typeDescription: 'หมุนภาพให้ตรง (Rotate Captcha)',
                            imageSrc: '',
                            promptText: hasRotateInstruction ? 'Rotate the image' : 'หมุนภาพให้อยู่ในแนวตั้งตรง',
                            hasWidgetLoaded: true
                        };
                    }

                    return null;
                }

                let activeWidget = scanForActiveWidget(document);
                if (!activeWidget || !sessionId) {
                    for (const iframe of iframes) {
                        try {
                            const fDoc = iframe.contentDocument || (iframe.contentWindow && iframe.contentWindow.document);
                            if (fDoc) {
                                if (!activeWidget) {
                                    activeWidget = scanForActiveWidget(fDoc);
                                }
                                if (!sessionId) {
                                    sessionId = extractSessionId(fDoc);
                                }
                                if (activeWidget && sessionId) break;
                            }
                        } catch(e) {}
                    }
                }

                let captchaType = 'None';
                let typeDescription = 'None (ไม่พบ Captcha ที่กำลังแสดงผล)';
                let isActive = false;
                let hasWidgetLoaded = false;
                let imageSrc = '';
                let promptText = '';

                if (activeWidget) {
                    captchaType = activeWidget.type;
                    typeDescription = activeWidget.typeDescription;
                    isActive = true;
                    hasWidgetLoaded = activeWidget.hasWidgetLoaded;
                    imageSrc = activeWidget.imageSrc;
                    promptText = activeWidget.promptText;
                } else if (isModalVisible && !isModalExited) {
                    captchaType = 'InactivityModalWaiting';
                    typeDescription = 'Inactivity Warning Modal กำลังเปิด (กำลังรอ SecSDK เรนเดอร์)';
                    isActive = true;
                    hasWidgetLoaded = false;
                    promptText = 'ตรวจพบ Inactivity Modal กำลังนับเวลาถอยหลัง';
                }

                return JSON.stringify({
                    items: items,
                    isActive: isActive,
                    captchaType: captchaType,
                    typeDescription: typeDescription,
                    hasWidgetLoaded: hasWidgetLoaded,
                    imageSrc: imageSrc,
                    promptText: promptText,
                    sessionId: sessionId,
                    mainFullHtml: mainFullHtml,
                    iframeHtmls: iframeHtmls,
                    pageUrl: window.location ? window.location.href : '',
                    countdownText: countdownText,
                    hasInactivityModal: !!inactModalContent,
                    isModalVisible: isModalVisible,
                    isModalExited: isModalExited
                });
            })()";

            var fullHtmlSections = new List<string>();

            foreach (var page in pages)
            {
                if (page.Url != null && page.Url.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    string json = await page.EvaluateExpressionAsync<string>(detectScript);
                    if (string.IsNullOrWhiteSpace(json) || json == "[]" || json == "{}")
                        continue;

                    using var doc = JsonDocument.Parse(json);
                    JsonElement itemsArray;
                    string pageFullHtml = string.Empty;
                    string pageUrl = page.Url ?? string.Empty;
                    var detectedIframeHtmls = new List<(string url, string html)>();

                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (doc.RootElement.TryGetProperty("items", out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
                        {
                            itemsArray = itemsElem;
                        }
                        else
                        {
                            continue;
                        }

                        if (doc.RootElement.TryGetProperty("mainFullHtml", out var mHtml))
                            pageFullHtml = mHtml.GetString() ?? "";

                        if (doc.RootElement.TryGetProperty("pageUrl", out var pUrl) && !string.IsNullOrEmpty(pUrl.GetString()))
                            pageUrl = pUrl.GetString()!;

                        if (doc.RootElement.TryGetProperty("countdownText", out var cdElem) && !string.IsNullOrWhiteSpace(cdElem.GetString()))
                            result.CountdownText = cdElem.GetString()!;

                        if (doc.RootElement.TryGetProperty("sessionId", out var sidElem) && !string.IsNullOrWhiteSpace(sidElem.GetString()))
                            result.CaptchaSessionId = sidElem.GetString()!;

                        if (doc.RootElement.TryGetProperty("hasInactivityModal", out var inactElem))
                            result.HasInactivityModal = inactElem.GetBoolean();

                        if (doc.RootElement.TryGetProperty("isActive", out var actElem) && actElem.GetBoolean())
                            result.IsActive = true;

                        if (doc.RootElement.TryGetProperty("captchaType", out var cTypeElem))
                        {
                            string typeStr = cTypeElem.GetString() ?? "None";
                            if (Enum.TryParse<CaptchaType>(typeStr, true, out var parsedType))
                            {
                                if (result.Type == CaptchaType.None || parsedType != CaptchaType.None)
                                {
                                    result.Type = parsedType;
                                }
                            }
                        }

                        if (doc.RootElement.TryGetProperty("typeDescription", out var descElem) && !string.IsNullOrWhiteSpace(descElem.GetString()))
                            result.TypeDescription = descElem.GetString()!;

                        if (doc.RootElement.TryGetProperty("hasWidgetLoaded", out var hwElem) && hwElem.GetBoolean())
                            result.HasWidgetLoaded = true;

                        if (doc.RootElement.TryGetProperty("imageSrc", out var isrcElem) && !string.IsNullOrWhiteSpace(isrcElem.GetString()))
                            result.ImageSrc = isrcElem.GetString()!;

                        if (doc.RootElement.TryGetProperty("promptText", out var pTextElem) && !string.IsNullOrWhiteSpace(pTextElem.GetString()))
                            result.PromptText = pTextElem.GetString()!;

                        if (doc.RootElement.TryGetProperty("iframeHtmls", out var iframesElem) && iframesElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var iframeElem in iframesElem.EnumerateArray())
                            {
                                string fUrl = iframeElem.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                                string fHtml = iframeElem.TryGetProperty("html", out var h) ? h.GetString() ?? "" : "";
                                if (!string.IsNullOrWhiteSpace(fHtml))
                                {
                                    detectedIframeHtmls.Add((fUrl, fHtml));
                                }
                            }
                        }
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        itemsArray = doc.RootElement;
                    }
                    else
                    {
                        continue;
                    }

                    int newItemsCount = 0;
                    foreach (var elem in itemsArray.EnumerateArray())
                    {
                        string html = elem.TryGetProperty("html", out var h) ? h.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(html)) continue;

                        // Prevent duplicate HTML from different frames/queries
                        if (result.Elements.Any(x => x.Html == html)) continue;

                        result.Elements.Add(new CaptchaElementItem
                        {
                            TagName = elem.TryGetProperty("tagName", out var t) ? t.GetString() ?? "" : "",
                            Id = elem.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                            ClassName = elem.TryGetProperty("className", out var c) ? c.GetString() ?? "" : "",
                            Html = html,
                            Depth = elem.TryGetProperty("depth", out var d) ? d.GetInt32() : 0,
                            FrameUrl = elem.TryGetProperty("frameUrl", out var f) ? f.GetString() ?? "" : "",
                            IsVisible = elem.TryGetProperty("isVisible", out var iv) && iv.GetBoolean(),
                            Width = elem.TryGetProperty("width", out var w) ? w.GetDouble() : 0,
                            Height = elem.TryGetProperty("height", out var ht) ? ht.GetDouble() : 0,
                            X = elem.TryGetProperty("x", out var x) ? x.GetDouble() : 0,
                            Y = elem.TryGetProperty("y", out var y) ? y.GetDouble() : 0
                        });
                        newItemsCount++;
                    }

                    if (newItemsCount > 0)
                    {
                        // Fallback to Puppeteer GetContentAsync if mainFullHtml was empty
                        if (string.IsNullOrWhiteSpace(pageFullHtml))
                        {
                            try
                            {
                                pageFullHtml = await page.GetContentAsync();
                            }
                            catch { }
                        }

                        if (!string.IsNullOrWhiteSpace(pageFullHtml))
                        {
                            if (!pageFullHtml.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
                            {
                                pageFullHtml = "<!DOCTYPE html>\n" + pageFullHtml;
                            }
                            fullHtmlSections.Add(pageFullHtml);
                        }

                        // Also append any iframe full HTMLs containing captcha
                        foreach (var (fUrl, fHtml) in detectedIframeHtmls)
                        {
                            var sbFrame = new StringBuilder();
                            sbFrame.AppendLine("<!-- ====================================================================== -->");
                            sbFrame.AppendLine($"<!-- 🧩 [CAPTCHA IFRAME FULL HTML] Frame URL: {fUrl} -->");
                            sbFrame.AppendLine("<!-- ====================================================================== -->");
                            string formattedFrame = fHtml.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                                ? fHtml
                                : "<!DOCTYPE html>\n" + fHtml;
                            sbFrame.AppendLine(formattedFrame);
                            fullHtmlSections.Add(sbFrame.ToString());
                        }
                    }
                }
                catch { }
            }

            // Determine final State
            if (result.IsActive)
            {
                result.State = CaptchaState.ActiveVisible;
            }
            else if (result.Elements.Count > 0)
            {
                result.State = CaptchaState.HiddenOrUnmounted;
                result.Type = CaptchaType.None;
                result.TypeDescription = "None (ตรวจพบใน DOM แต่ถูกซ่อนหรือปิดอยู่)";
            }
            else
            {
                result.State = CaptchaState.NotPresent;
                result.Type = CaptchaType.None;
                result.TypeDescription = "None";
            }

            if (result.Elements.Count > 0)
            {
                if (fullHtmlSections.Count == 1)
                {
                    result.CombinedHtml = fullHtmlSections[0];
                }
                else if (fullHtmlSections.Count > 1)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < fullHtmlSections.Count; i++)
                    {
                        sb.AppendLine(fullHtmlSections[i]);
                        if (i < fullHtmlSections.Count - 1) sb.AppendLine();
                    }
                    result.CombinedHtml = sb.ToString();
                }
                else
                {
                    // Fallback to elements HTML if full HTML couldn't be captured
                    var sb = new StringBuilder();
                    for (int i = 0; i < result.Elements.Count; i++)
                    {
                        var el = result.Elements[i];
                        sb.AppendLine($"<!-- [Captcha Element #{i + 1}]: {el.Summary} -->");
                        sb.AppendLine(el.Html);
                        if (i < result.Elements.Count - 1) sb.AppendLine();
                    }
                    result.CombinedHtml = sb.ToString();
                }

                if (result.IsActive)
                {
                    logger?.Info("Captcha", $"🚨 [REAL CAPTCHA ACTIVE] ตรวจพบ {result.TypeDescription} กำลังแสดงผลบนพอร์ต {cdpPort} (Countdown='{result.CountdownText ?? "N/A"}', SessionId='{result.CaptchaSessionId ?? "N/A"}', Full HTML: {result.CombinedHtml.Length:N0} ตัวอักษร)");
                    for (int i = 0; i < result.Elements.Count; i++)
                    {
                        var el = result.Elements[i];
                        logger?.Info("Captcha", $"   ├─ [#{i + 1}/{result.Elements.Count}] Tag=<{el.TagName}> ID='{el.Id}' Class='{el.ClassName}' Visible={el.IsVisible} Size={el.Width:F0}x{el.Height:F0}{(string.IsNullOrEmpty(el.FrameUrl) ? "" : $" Frame='{el.FrameUrl}'")}");
                    }
                }
                else
                {
                    logger?.Debug("Captcha", $"[Captcha Inactive DOM] พบ {result.Elements.Count} Captcha elements ใน DOM บนพอร์ต {cdpPort} แต่ถูกซ่อนหรือปิดอยู่ (IsActive=False, State={result.State})");
                }
            }
        }
        catch (Exception ex)
        {
            logger?.Error("Captcha", $"Error detecting captcha: {ex.Message}", ex);
        }

        return result;
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
        foreach (var pair in _connectedBrowsers)
        {
            pair.Value.Dispose();
        }
        _connectedBrowsers.Clear();
        _httpClient.Dispose();
    }
}
