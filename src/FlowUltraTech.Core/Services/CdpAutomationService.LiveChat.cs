using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PuppeteerSharp;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public partial class CdpAutomationService
{
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

    public async Task<List<LiveChatMessageItem>> FetchLiveChatMessagesAsync(int cdpPort, string? targetChannel = null)
    {
        var messages = new List<LiveChatMessageItem>();
        var logger = _logManager.TryGetLoggerByPort(cdpPort);
        try
        {
            IBrowser? browser = null;
            if (cdpPort > 0)
            {
                browser = await GetOrConnectBrowserAsync(cdpPort);
            }

            if (browser == null)
            {
                // Fallback 1: Any other active browser connected via CDP
                browser = _connectedBrowsers.Values.FirstOrDefault(b => !b.IsClosed);
            }

            if (browser == null && !string.IsNullOrWhiteSpace(targetChannel))
            {
                // Fallback 2: Launch lightweight headless browser for target channel
                browser = await GetOrLaunchHeadlessBrowserAsync();
            }

            if (browser == null)
            {
                if (!string.IsNullOrWhiteSpace(targetChannel) && !targetChannel.StartsWith("Account", StringComparison.OrdinalIgnoreCase))
                {
                    logger?.Debug("Chat", $"ไม่สามารถเชื่อมต่อเบราว์เซอร์ผ่าน CDP Port {cdpPort} ได้ (เป้าหมาย: @{targetChannel})");
                }
                return messages;
            }

            var pages = await browser.PagesAsync();
            var targetPages = pages;
            if (!string.IsNullOrWhiteSpace(targetChannel))
            {
                string cleanTarget = targetChannel.TrimStart('@').ToLowerInvariant();
                var channelMatches = pages.Where(p => p.Url != null && p.Url.ToLowerInvariant().Contains(cleanTarget)).ToArray();
                if (channelMatches.Length > 0)
                {
                    targetPages = channelMatches;
                }
                else
                {
                    // Find internal chat/webcast views if present without navigating away the main Studio UI
                    var chatPage = pages.FirstOrDefault(p => 
                        p.Url != null && (p.Url.Contains("chat", StringComparison.OrdinalIgnoreCase) || 
                                          p.Url.Contains("webcast", StringComparison.OrdinalIgnoreCase)));
                    if (chatPage != null)
                    {
                        targetPages = new[] { chatPage };
                    }
                    else
                    {
                        targetPages = Array.Empty<IPage>();
                    }
                }
            }

            foreach (var page in targetPages)
            {
                try
                {
                    string js = @"
                    () => {
                        // Dismiss introductory guide if present
                        const guide = document.querySelector('[data-e2e=""scroll-switch-guide""]');
                        if (guide) {
                            const b = guide.querySelector('div, button');
                            if (b) b.click();
                        }

                        const items = [];
                        const seenTexts = new Set();
                        
                        const chatSelectors = [
                            '[data-e2e=""chat-item""]',
                            'div[class*=""chatItem""]',
                            'div[class*=""chat-item""]',
                            'div[class*=""message-item""]',
                            'div[class*=""comment-item""]',
                            'div[class*=""ChatMessage""]',
                            'li[class*=""chat""]',
                            'div[class*=""live-chat-item""]',
                            'div[data-index]'
                        ];

                        for (const selector of chatSelectors) {
                            const elements = document.querySelectorAll(selector);
                            elements.forEach((el, idx) => {
                                if (el.tagName && el.tagName.startsWith('XG-')) return;
                                const text = (el.innerText || el.textContent || '').trim();
                                if (!text || seenTexts.has(text)) return;
                                seenTexts.add(text);

                                let username = '';
                                let nickname = '';
                                const nameEl = el.querySelector('[class*=""nickname""], [class*=""name""], [class*=""user""], [data-e2e=""chat-username""]');
                                if (nameEl) {
                                    nickname = (nameEl.innerText || nameEl.textContent || '').trim();
                                }

                                let msg = '';
                                const msgEl = el.querySelector('[class*=""content""], [class*=""text""], [class*=""message""], [data-e2e=""chat-message""]');
                                if (msgEl) {
                                    msg = (msgEl.innerText || msgEl.textContent || '').trim();
                                } else {
                                    msg = text;
                                }

                                let type = 'Chat';
                                let giftName = '';
                                let diamonds = 0;
                                let repeatCount = 1;

                                const giftEl = el.querySelector('[class*=""gift""], [class*=""diamond""]');
                                if (giftEl || text.includes('ส่งของขวัญ') || text.includes('sent') || text.includes('Gift')) {
                                    type = 'Gift';
                                    giftName = giftEl ? (giftEl.innerText || giftEl.textContent || 'Gift').trim() : 'Gift';
                                } else if (text.includes('กดถูกใจ') || text.includes('liked') || text.includes('Like')) {
                                    type = 'Like';
                                } else if (text.includes('ติดตาม') || text.includes('followed') || text.includes('Follow')) {
                                    type = 'Follow';
                                } else if (text.includes('แชร์') || text.includes('shared') || text.includes('Share')) {
                                    type = 'Share';
                                } else if (text.includes('joined') || text.includes('เข้าร่วม') || text.includes('ยินดีต้อนรับ') || text.includes('Welcome') || text.includes('Viewers') || !nameEl) {
                                    type = 'Other';
                                }

                                const rawObj = {
                                    tag: el.tagName || '',
                                    className: (el.className || '').toString(),
                                    text: text,
                                    msg: msg,
                                    username: username,
                                    nickname: nickname,
                                    type: type,
                                    html: (el.outerHTML || '').substring(0, 400)
                                };

                                items.push({
                                    id: 'cdp_' + Date.now() + '_' + idx,
                                    time: new Date().toISOString(),
                                    type: type,
                                    username: username || nickname,
                                    nickname: nickname || (username ? '@' + username : 'ระบบ/ผู้ชม'),
                                    message: msg,
                                    giftName: giftName,
                                    diamonds: diamonds,
                                    repeatCount: repeatCount,
                                    rawJson: JSON.stringify(rawObj)
                                });
                            });
                        }

                        // Fallback: If no standard chat elements matched, scan chat container text nodes
                        if (items.length === 0) {
                            const chatContainer = document.querySelector('[data-e2e=""live-chat-container""]') || 
                                                  document.querySelector('[data-e2e=""public-screen-live-chat-slot""]') ||
                                                  document.querySelector('.overflow-y-scroll');
                            if (chatContainer) {
                                const nodes = chatContainer.querySelectorAll('div, span, p');
                                nodes.forEach((n, idx) => {
                                    if (n.children.length === 0) {
                                        const txt = (n.innerText || n.textContent || '').trim();
                                        if (txt && txt.length > 0 && txt.length < 300 && !seenTexts.has(txt) && !txt.includes('Viewers') && !txt.includes('Click to send')) {
                                            seenTexts.add(txt);
                                            items.push({
                                                id: 'raw_' + Date.now() + '_' + idx,
                                                time: new Date().toISOString(),
                                                type: 'Other',
                                                username: '',
                                                nickname: 'Other/Raw',
                                                message: txt,
                                                giftName: '',
                                                diamonds: 0,
                                                repeatCount: 1,
                                                rawJson: JSON.stringify({ tag: n.tagName, text: txt, parentTag: n.parentElement ? n.parentElement.tagName : '' })
                                            });
                                        }
                                    }
                                });
                            }
                        }

                        return JSON.stringify(items);
                    }";

                    string json = await page.EvaluateFunctionAsync<string>(js);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        using var doc = JsonDocument.Parse(json);
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            var item = new LiveChatMessageItem
                            {
                                Id = elem.GetProperty("id").GetString() ?? Guid.NewGuid().ToString(),
                                Type = Enum.TryParse<LiveChatMessageType>(elem.GetProperty("type").GetString(), out var t) ? t : LiveChatMessageType.Other,
                                Username = elem.GetProperty("username").GetString() ?? string.Empty,
                                Nickname = elem.GetProperty("nickname").GetString() ?? string.Empty,
                                Message = elem.GetProperty("message").GetString() ?? string.Empty,
                                GiftName = elem.TryGetProperty("giftName", out var g) ? g.GetString() : null,
                                DiamondCount = elem.TryGetProperty("diamonds", out var d) ? d.GetInt32() : 0,
                                RepeatCount = elem.TryGetProperty("repeatCount", out var r) ? r.GetInt32() : 1,
                                RawJson = elem.TryGetProperty("rawJson", out var rj) ? rj.GetString() : elem.GetRawText(),
                                Timestamp = DateTime.Now
                            };
                            messages.Add(item);
                        }
                    }
                }
                catch (Exception pageEx)
                {
                    logger?.Debug("Chat", $"เกิดข้อผิดพลาดในการดึง DOM แชทหน้า {page.Url}: {pageEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            logger?.Error("Chat", $"FetchLiveChatMessagesAsync ขัดข้อง (Port: {cdpPort}): {ex.Message}", ex);
        }
        return messages;
    }


}
