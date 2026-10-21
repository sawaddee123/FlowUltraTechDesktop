using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PuppeteerSharp;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public partial class CdpAutomationService
{
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
                try
                {
                    var (_, cdpResponse) = await TryWaitWithTimeoutAsync(cdpSession.SendAsync("Network.getAllCookies"), 3000);
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
                finally
                {
                    try { await cdpSession.DetachAsync(); } catch { }
                    try { (cdpSession as IDisposable)?.Dispose(); } catch { }
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
                        var (_, rawCookies) = await TryWaitWithTimeoutAsync(p.GetCookiesAsync(), 2000);
                        if (rawCookies == null) continue;
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

            // 3. Extract LocalStorage and SessionStorage across active pages
            foreach (var p in pages)
            {
                if (string.IsNullOrEmpty(p.Url) || p.Url.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase) || p.Url.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var (_, storageJson) = await TryWaitWithTimeoutAsync(p.EvaluateFunctionAsync<string>(@"() => {
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
                    }"), 2000);
                    if (string.IsNullOrWhiteSpace(storageJson)) continue;

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

}
