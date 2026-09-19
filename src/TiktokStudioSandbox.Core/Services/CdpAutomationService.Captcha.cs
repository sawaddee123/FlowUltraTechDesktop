using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PuppeteerSharp;
using PuppeteerSharp.Input;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Services;

public partial class CdpAutomationService
{
    public async Task<CaptchaDetectionResult> DetectCaptchaElementsAsync(int cdpPort, bool forceFullHtml = false)
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



    private static string? GetImageDimensions(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 24) return null;
        try
        {
            // PNG (Header: 89 50 4E 47 0D 0A 1A 0A)
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
                if (width > 0 && height > 0) return $"{width}x{height}";
            }
            // WebP (Header: RIFF .... WEBP)
            if (bytes.Length >= 30 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            {
                if (bytes[12] == 0x56 && bytes[13] == 0x50 && bytes[14] == 0x38 && bytes[15] == 0x4C && bytes.Length >= 25)
                {
                    uint b0 = bytes[21], b1 = bytes[22], b2 = bytes[23], b3 = bytes[24];
                    int width = 1 + (int)(b0 | ((b1 & 0x3F) << 8));
                    int height = 1 + (int)(((b1 >> 6) | (b2 << 2) | ((b3 & 0xF) << 10)));
                    if (width > 0 && height > 0) return $"{width}x{height}";
                }
                if (bytes[12] == 0x56 && bytes[13] == 0x50 && bytes[14] == 0x38 && bytes[15] == 0x58 && bytes.Length >= 30)
                {
                    int width = 1 + bytes[24] + (bytes[25] << 8) + (bytes[26] << 16);
                    int height = 1 + bytes[27] + (bytes[28] << 8) + (bytes[29] << 16);
                    if (width > 0 && height > 0) return $"{width}x{height}";
                }
            }
            // JPEG (Header: FF D8 FF)
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                int idx = 2;
                while (idx < bytes.Length - 8)
                {
                    if (bytes[idx] == 0xFF)
                    {
                        byte marker = bytes[idx + 1];
                        if (marker >= 0xC0 && marker <= 0xC3)
                        {
                            int height = (bytes[idx + 5] << 8) | bytes[idx + 6];
                            int width = (bytes[idx + 7] << 8) | bytes[idx + 8];
                            if (width > 0 && height > 0) return $"{width}x{height}";
                        }
                        int length = (bytes[idx + 2] << 8) | bytes[idx + 3];
                        idx += 2 + length;
                    }
                    else
                    {
                        idx++;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public Task<CaptchaSolveResult> SolveCaptchaWithMockAsync(
        int cdpPort,
        CaptchaDetectionResult? detection = null,
        string? customMockJsonPath = null,
        Action<string>? progressLogger = null,
        int delaySeconds = 0,
        string? instanceLeaseId = null)
    {
        return SolveCaptchaAsync(
            cdpPort,
            detection,
            solverMode: CaptchaSolverType.MockResponse,
            solverApiEndpoint: null,
            solverJwtToken: null,
            customMockJsonPath: customMockJsonPath,
            progressLogger: progressLogger,
            delaySeconds: delaySeconds,
            instanceLeaseId: instanceLeaseId);
    }

    public async Task<CaptchaSolveResult> SolveCaptchaAsync(
        int cdpPort,
        CaptchaDetectionResult? detection = null,
        CaptchaSolverType solverMode = CaptchaSolverType.LocalApiServer,
        string? solverApiEndpoint = null,
        string? solverJwtToken = null,
        string? customMockJsonPath = null,
        Action<string>? progressLogger = null,
        int delaySeconds = 0,
        string? instanceLeaseId = null)
    {
        var solveResult = new CaptchaSolveResult();
        var stopwatch = Stopwatch.StartNew();
        var logger = _logManager.TryGetLoggerByPort(cdpPort);

        void Log(string msg)
        {
            solveResult.Logs.Add(msg);
            progressLogger?.Invoke(msg);
            logger?.Info("CaptchaSolve", msg);
        }

        try
        {
            Log($"[{DateTime.Now:HH:mm:ss}] 🚀 [CAPTCHA SOLVE] เริ่มต้นกระบวนการแก้ Captcha บนพอร์ต {cdpPort} (โหมด: {solverMode})...");

            var browser = await GetOrConnectBrowserAsync(cdpPort);
            if (browser == null)
            {
                solveResult.Success = false;
                solveResult.Message = $"ไม่สามารถเชื่อมต่อ CDP Port {cdpPort} ได้";
                Log($"[{DateTime.Now:HH:mm:ss}] ❌ [CAPTCHA SOLVE] {solveResult.Message}");
                return solveResult;
            }

            if (detection == null || !detection.IsActive || detection.Type == CaptchaType.None)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [CAPTCHA SOLVE] กำลังตรวจจับสถานะ Captcha ล่าสุดก่อนดำเนินการ...");
                detection = await DetectCaptchaElementsAsync(cdpPort, forceFullHtml: false);
            }

            solveResult.CaptchaType = detection.TypeDescription;

            if (!detection.IsActive || detection.Type == CaptchaType.None)
            {
                solveResult.Success = false;
                solveResult.Message = "ตรวจไม่พบ Captcha ที่กำลังแสดงผลอยู่จริงในขณะนี้ (Inactive/None)";
                Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVE] {solveResult.Message}");
                return solveResult;
            }

            Log($"[{DateTime.Now:HH:mm:ss}] 📋 [CAPTCHA SOLVE] ตรวจพบ: {detection.TypeDescription} (Session: {detection.CaptchaSessionId ?? "N/A"})");

            // Configurable Delay (0 for manual immediate solve, 5s for auto solve)
            if (delaySeconds > 0)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] ⏳ [CAPTCHA SOLVE] กำลังหน่วงเวลา {delaySeconds} วินาที (Solve Delay: {delaySeconds}s)...");
                await Task.Delay(delaySeconds * 1000);
                Log($"[{DateTime.Now:HH:mm:ss}] ⏱️ [CAPTCHA SOLVE] หน่วงเวลาครบ {delaySeconds} วินาทีเรียบร้อย ดำเนินการส่งข้อมูล...");
            }
            else
            {
                Log($"[{DateTime.Now:HH:mm:ss}] ⚡ [CAPTCHA SOLVE] เริ่มการแก้ทันทีโดยไม่หน่วงเวลา (Immediate Mode)...");
            }

            // Find active page with captcha
            var pages = await browser.PagesAsync();
            IPage? targetPage = null;
            IFrame? targetFrame = null;

            foreach (var page in pages)
            {
                var hasModal = await page.EvaluateExpressionAsync<bool>(
                    "!!document.querySelector('#captcha-verify-container-main-page, #captcha-verify-image, .secsdk-captcha-drag-icon, #jigsaw-slider-knob, #symbol-submit-btn, [class*=\"secsdk-captcha\" i]')");
                if (hasModal)
                {
                    targetPage = page;
                    break;
                }
                foreach (var frame in page.Frames)
                {
                    var frameHasModal = await frame.EvaluateExpressionAsync<bool>(
                        "!!document.querySelector('#captcha-verify-container-main-page, #captcha-verify-image, .secsdk-captcha-drag-icon, #jigsaw-slider-knob, #symbol-submit-btn, [class*=\"secsdk-captcha\" i]')");
                    if (frameHasModal)
                    {
                        targetPage = page;
                        targetFrame = frame;
                        break;
                    }
                }
                if (targetPage != null) break;
            }

            targetPage ??= pages.FirstOrDefault();
            if (targetPage == null)
            {
                solveResult.Success = false;
                solveResult.Message = "ไม่พบหน้าต่างเพจสำหรับส่งคำสั่งแก้ Captcha";
                Log($"[{DateTime.Now:HH:mm:ss}] ❌ [CAPTCHA SOLVE] {solveResult.Message}");
                return solveResult;
            }

            double targetX = 228;
            var clickPoints = new List<(double X, double Y)>();
            bool solvedViaApi = false;
            string? currentCaptchaId = null;
            string? usedEndpoint = null;

            // 1. Solve with Live API Server if selected
            if (solverMode == CaptchaSolverType.LocalApiServer)
            {
                string endpoint = (string.IsNullOrWhiteSpace(solverApiEndpoint) ? AppConstants.DefaultServerEndpoint : solverApiEndpoint).Trim().TrimEnd('/');
                usedEndpoint = endpoint;
                Log($"[{DateTime.Now:HH:mm:ss}] 🤖 [CAPTCHA SOLVE] ส่งรูปภาพไปยัง Local Solver API Server: {endpoint}...");

                // Prepare Base64 Image
                string? base64Image = null;
                if (detection.CroppedImageBytes != null && detection.CroppedImageBytes.Length > 0)
                {
                    base64Image = Convert.ToBase64String(detection.CroppedImageBytes);
                }
                else if (detection.DirectImageBytes != null && detection.DirectImageBytes.Length > 0)
                {
                    base64Image = Convert.ToBase64String(detection.DirectImageBytes);
                }
                else if (!string.IsNullOrWhiteSpace(detection.ImageSrc))
                {
                    base64Image = detection.ImageSrc;
                }

                if (string.IsNullOrEmpty(base64Image))
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 📷 [CAPTCHA SOLVE] ดึงภาพแคปต์ชาสดจาก Element Screenshot บนหน้าจอ...");
                    string imgSel = detection.Type == CaptchaType.Jigsaw
                        ? "#captcha-verify-image, .captcha_verify_img_slide, img[class*=\"slide\" i]"
                        : "#captcha-verify-image, #symbol-img-box, .captcha_verify_img_symbol, img[class*=\"symbol\" i]";
                    var imgHandle = targetFrame != null ? await targetFrame.QuerySelectorAsync(imgSel) : await targetPage.QuerySelectorAsync(imgSel);
                    if (imgHandle != null)
                    {
                        byte[]? freshBytes = await imgHandle.ScreenshotDataAsync(new ElementScreenshotOptions { Type = ScreenshotType.Png });
                        if (freshBytes != null && freshBytes.Length > 0)
                        {
                            base64Image = Convert.ToBase64String(freshBytes);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(base64Image))
                {
                    try
                    {
                        string apiPath = detection.Type == CaptchaType.Jigsaw ? "/api/v1/captcha/slide" : "/api/v1/captcha/symbol";
                        string fullUrl = $"{endpoint}{apiPath}";
                        var payload = new { image = base64Image };
                        string jsonBody = JsonSerializer.Serialize(payload);

                        using var req = new HttpRequestMessage(HttpMethod.Post, fullUrl)
                        {
                            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                        };

                        if (!string.IsNullOrWhiteSpace(solverJwtToken))
                        {
                            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", solverJwtToken.Trim());
                        }
                        if (!string.IsNullOrWhiteSpace(instanceLeaseId))
                        {
                            req.Headers.TryAddWithoutValidation("X-Instance-Lease", instanceLeaseId.Trim());
                        }

                        using var resp = await _httpClient.SendAsync(req);
                        if (!resp.IsSuccessStatusCode && resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            // Fallback to non-prefixed route /captcha/slide or /captcha/symbol
                            string altPath = detection.Type == CaptchaType.Jigsaw ? "/captcha/slide" : "/captcha/symbol";
                            using var altReq = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}{altPath}")
                            {
                                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                            };
                            if (!string.IsNullOrWhiteSpace(solverJwtToken))
                            {
                                altReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", solverJwtToken.Trim());
                            }
                            if (!string.IsNullOrWhiteSpace(instanceLeaseId))
                            {
                                altReq.Headers.TryAddWithoutValidation("X-Instance-Lease", instanceLeaseId.Trim());
                            }
                            using var altResp = await _httpClient.SendAsync(altReq);
                            if (altResp.IsSuccessStatusCode)
                            {
                                string altJson = await altResp.Content.ReadAsStringAsync();
                                solveResult.ServerResponseJson = altJson;
                                (solvedViaApi, targetX, clickPoints, currentCaptchaId) = ParseSolverApiResponse(detection.Type, altJson, Log);
                            }
                        }
                        else if (resp.IsSuccessStatusCode)
                        {
                            string respJson = await resp.Content.ReadAsStringAsync();
                            solveResult.ServerResponseJson = respJson;
                            (solvedViaApi, targetX, clickPoints, currentCaptchaId) = ParseSolverApiResponse(detection.Type, respJson, Log);
                        }
                        else
                        {
                            Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVE] Solver API Server ตอบกลับสถานะ: {resp.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVE] เรียก Solver API Server ไม่สำเร็จ ({ex.Message}) กำลังสลับไปใช้ Fallback Mock...");
                    }
                }
                else
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVE] ไม่พบภาพ Base64 ของ Captcha สำหรับส่งเข้า API");
                }
            }

            // 2. Fallback to Mock JSON if not solved via API
            if (!solvedViaApi)
            {
                string mockFilePath = customMockJsonPath ?? Path.Combine(AppContext.BaseDirectory, "Config", "mock_captcha_response.json");
                if (!File.Exists(mockFilePath))
                {
                    var searchPaths = new[]
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "mock_captcha_response.json"),
                        Path.Combine(Directory.GetCurrentDirectory(), "src", "TiktokStudioSandbox.Core", "Config", "mock_captcha_response.json"),
                        Path.Combine(Directory.GetCurrentDirectory(), "Config", "mock_captcha_response.json")
                    };
                    foreach (var p in searchPaths)
                    {
                        if (File.Exists(p))
                        {
                            mockFilePath = p;
                            break;
                        }
                    }
                }

                string mockJsonText;
                if (File.Exists(mockFilePath))
                {
                    mockJsonText = await File.ReadAllTextAsync(mockFilePath);
                    Log($"[{DateTime.Now:HH:mm:ss}] 📄 [CAPTCHA SOLVE] โหลด Mock Server Response จากไฟล์: {Path.GetFileName(mockFilePath)}");
                }
                else
                {
                    mockJsonText = @"{
                        ""slide"": { ""target_x"": 228, ""position"": [228, 100], ""time"": 0.15 },
                        ""symbol"": { ""symbol_positions"": [[100, 110], [215, 140]], ""time"": 0.23 }
                    }";
                    Log($"[{DateTime.Now:HH:mm:ss}] ℹ️ [CAPTCHA SOLVE] ใช้ Mock Server Response ค่าเริ่มต้น");
                }

                solveResult.ServerResponseJson = mockJsonText;
                using var jsonDoc = JsonDocument.Parse(mockJsonText);
                var root = jsonDoc.RootElement;

                if (detection.Type == CaptchaType.Jigsaw)
                {
                    if (root.TryGetProperty("slide", out var slideObj))
                    {
                        if (slideObj.TryGetProperty("target_x", out var txElem))
                        {
                            targetX = txElem.GetDouble();
                        }
                        else if (slideObj.TryGetProperty("position", out var posElem) && posElem.GetArrayLength() > 0)
                        {
                            targetX = posElem[0].GetDouble();
                        }
                    }
                }
                else if (detection.Type == CaptchaType.Symbol)
                {
                    if (root.TryGetProperty("symbol", out var symbolObj) && symbolObj.TryGetProperty("symbol_positions", out var posArray))
                    {
                        foreach (var item in posArray.EnumerateArray())
                        {
                            if (item.GetArrayLength() >= 2)
                            {
                                clickPoints.Add((item[0].GetDouble(), item[1].GetDouble()));
                            }
                        }
                    }
                    if (clickPoints.Count == 0)
                    {
                        clickPoints.Add((100, 110));
                        clickPoints.Add((215, 140));
                    }
                }
            }

            // Execute action based on Captcha Type
            if (detection.Type == CaptchaType.Jigsaw)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 🧩 [CAPTCHA SOLVE] โหมด Jigsaw: พิกัดเป้าหมายคือ X={targetX:F1}px");

                string knobSelector = "#captcha_slide_button, #jigsaw-slider-knob, .secsdk-captcha-drag-icon, .captcha_verify_slide--drag-icon, [role=\"slider\"]";
                var knobHandle = targetFrame != null 
                    ? await targetFrame.QuerySelectorAsync(knobSelector)
                    : await targetPage.QuerySelectorAsync(knobSelector);

                if (knobHandle == null)
                {
                    solveResult.Success = false;
                    solveResult.Message = "ไม่พบปุ่มเลื่อนสไลเดอร์ (Slider Knob) บนหน้าจอ";
                    Log($"[{DateTime.Now:HH:mm:ss}] ❌ [CAPTCHA SOLVE] {solveResult.Message}");
                    return solveResult;
                }

                var box = await knobHandle.BoundingBoxAsync();
                if (box == null)
                {
                    solveResult.Success = false;
                    solveResult.Message = "ไม่สามารถคำนวณตำแหน่ง Bounding Box ของปุ่มสไลเดอร์ได้";
                    Log($"[{DateTime.Now:HH:mm:ss}] ❌ [CAPTCHA SOLVE] {solveResult.Message}");
                    return solveResult;
                }

                decimal startX = box.X + box.Width / 2;
                decimal startY = box.Y + box.Height / 2;

                Log($"[{DateTime.Now:HH:mm:ss}] 🕹️ [CAPTCHA SOLVE] พบปุ่ม Knob ที่พิกัด ({startX:F0}, {startY:F0}) กำลังจำลองการลากไปยัง X={startX + (decimal)targetX:F0}...");

                // Smooth mouse drag with human-like Bezier curve
                await targetPage.Mouse.MoveAsync(startX, startY);
                await targetPage.Mouse.DownAsync();

                int steps = 18;
                for (int step = 1; step <= steps; step++)
                {
                    double progress = (double)step / steps;
                    double ease = 1 - Math.Pow(1 - progress, 3);
                    decimal currentX = startX + (decimal)(targetX * ease);
                    decimal currentY = startY + (decimal)(Random.Shared.NextDouble() * 2 - 1);
                    await targetPage.Mouse.MoveAsync(currentX, currentY);
                    await Task.Delay(Random.Shared.Next(15, 28));
                }

                await Task.Delay(50);
                await targetPage.Mouse.UpAsync();

                Log($"[{DateTime.Now:HH:mm:ss}] 🕹️ [CAPTCHA SOLVE] ลากสไลเดอร์ถึงตำแหน่งเป้าหมายเรียบร้อยแล้ว");
            }
            else if (detection.Type == CaptchaType.Symbol)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [CAPTCHA SOLVE] โหมด Symbol: พิกัดคลิกเป้าหมายมี {clickPoints.Count} จุด");

                string imgSelector = "#captcha-verify-image, #symbol-img-box, .captcha_verify_img_symbol, img[class*=\"symbol\" i]";
                var imgHandle = targetFrame != null 
                    ? await targetFrame.QuerySelectorAsync(imgSelector)
                    : await targetPage.QuerySelectorAsync(imgSelector);

                if (imgHandle == null)
                {
                    solveResult.Success = false;
                    solveResult.Message = "ไม่พบรูปภาพสัญลักษณ์สำหรับคลิกเลือก";
                    Log($"[{DateTime.Now:HH:mm:ss}] ❌ [CAPTCHA SOLVE] {solveResult.Message}");
                    return solveResult;
                }

                var imgBox = await imgHandle.BoundingBoxAsync();
                if (imgBox == null)
                {
                    solveResult.Success = false;
                    solveResult.Message = "ไม่สามารถคำนวณตำแหน่ง Bounding Box ของรูปภาพสัญลักษณ์ได้";
                    Log($"[{DateTime.Now:HH:mm:ss}] ❌ [CAPTCHA SOLVE] {solveResult.Message}");
                    return solveResult;
                }

                for (int i = 0; i < clickPoints.Count; i++)
                {
                    var pt = clickPoints[i];
                    decimal targetClickX = imgBox.X + (decimal)pt.X;
                    decimal targetClickY = imgBox.Y + (decimal)pt.Y;

                    Log($"[{DateTime.Now:HH:mm:ss}] 🎯 [CAPTCHA SOLVE] คลิกเลือกสัญลักษณ์จุดที่ {i + 1}: ({pt.X:F0}, {pt.Y:F0})");
                    await targetPage.Mouse.ClickAsync(targetClickX, targetClickY);
                    await Task.Delay(Random.Shared.Next(250, 450));
                }

                string submitSelector = "#symbol-submit-btn, .secsdk-captcha-submit-button, button[class*=\"verify-btn\" i]";
                var submitHandle = targetFrame != null
                    ? await targetFrame.QuerySelectorAsync(submitSelector)
                    : await targetPage.QuerySelectorAsync(submitSelector);

                if (submitHandle != null)
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔘 [CAPTCHA SOLVE] กดปุ่มยืนยันผล (Submit / Confirm)...");
                    await submitHandle.ClickAsync();
                }
            }
            else
            {
                Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVE] ประเภท {detection.TypeDescription} ยังไม่มีการจำลอง Action อัตโนมัติ");
            }

            // Verification phase
            await Task.Delay(1500);

            var verifyScript = @"(() => {
                const toast = document.querySelector('#jigsaw-toast, #symbol-toast, .tux-toast, [class*=""toast"" i]');
                const modal = document.querySelector('#captcha-modal-wrapper, .tt-live-modalv2-wrapper, #captcha-mount-point, #captcha-verify-container-main-page');
                const isModalHidden = !modal || window.getComputedStyle(modal).display === 'none' || modal.classList.contains('tt-live-modalv2-wrapper-exited');
                const toastText = toast ? (toast.textContent || '') : '';
                return JSON.stringify({ isModalHidden, toastText });
            })()";

            string verifyJson = await targetPage.EvaluateExpressionAsync<string>(verifyScript);
            bool isResolved = false;
            string feedbackMsg = "";

            if (!string.IsNullOrEmpty(verifyJson))
            {
                using var verifyDoc = JsonDocument.Parse(verifyJson);
                bool isModalHidden = verifyDoc.RootElement.TryGetProperty("isModalHidden", out var h) && h.GetBoolean();
                string toastText = verifyDoc.RootElement.TryGetProperty("toastText", out var t) ? t.GetString() ?? "" : "";
                isResolved = isModalHidden || toastText.Contains("Success", StringComparison.OrdinalIgnoreCase) || toastText.Contains("สำเร็จ", StringComparison.OrdinalIgnoreCase);
                feedbackMsg = toastText;
            }

            stopwatch.Stop();
            solveResult.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;

            if (isResolved)
            {
                solveResult.Success = true;
                solveResult.Message = "แก้ Captcha สำเร็จเรียบร้อย! (ผ่านการตรวจสอบและ Modal ปิดแล้ว)";
                Log($"[{DateTime.Now:HH:mm:ss}] ✅ [CAPTCHA SOLVE] {solveResult.Message} (ใช้เวลารวม {solveResult.ElapsedSeconds:F2}s)");
                _ = ReportCaptchaVerificationAsync(usedEndpoint, solverJwtToken, instanceLeaseId, currentCaptchaId, true, "TikTok Modal closed and verified", solveResult.ElapsedSeconds);
            }
            else
            {
                solveResult.Success = false;
                solveResult.Message = string.IsNullOrEmpty(feedbackMsg)
                    ? "TikTok ปฏิเสธคำตอบหรือไม่ผ่านการตรวจสอบ (Modal ยังคงแสดงผล)"
                    : $"TikTok ตอบกลับ: {feedbackMsg}";
                Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVE] {solveResult.Message} (ใช้เวลารวม {solveResult.ElapsedSeconds:F2}s)");
                _ = ReportCaptchaVerificationAsync(usedEndpoint, solverJwtToken, instanceLeaseId, currentCaptchaId, false, solveResult.Message, solveResult.ElapsedSeconds);
            }

            return solveResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            solveResult.Success = false;
            solveResult.Message = $"เกิดข้อผิดพลาดในการแก้ Captcha: {ex.Message}";
            solveResult.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            Log($"[{DateTime.Now:HH:mm:ss}] ❌ [CAPTCHA SOLVE ERROR] {ex.Message}");
            return solveResult;
        }
    }

    private async Task ReportCaptchaVerificationAsync(
        string? endpoint,
        string? token,
        string? leaseId,
        string? captchaId,
        bool isSuccess,
        string feedback,
        double durationTotalSec)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(captchaId))
            return;

        try
        {
            string url = $"{endpoint.TrimEnd('/')}/api/v1/captcha/verify-result";
            var payload = new
            {
                captchaId = captchaId,
                status = isSuccess ? "VERIFIED_SUCCESS" : "REJECTED_BY_TIKTOK",
                feedback = feedback,
                durationTotalSec = durationTotalSec
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
            if (!string.IsNullOrWhiteSpace(leaseId))
                req.Headers.TryAddWithoutValidation("X-Instance-Lease", leaseId.Trim());

            using var resp = await _httpClient.SendAsync(req);
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📡 [CAPTCHA VERIFY] ส่งผลยืนยันไปยัง Server: {(isSuccess ? "VERIFIED_SUCCESS" : "REJECTED_BY_TIKTOK")} (Status {resp.StatusCode})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA VERIFY] ไม่สามารถส่งผลยืนยัน: {ex.Message}");
        }
    }

    private static (bool Solved, double TargetX, List<(double X, double Y)> ClickPoints, string? CaptchaId) ParseSolverApiResponse(
        CaptchaType type,
        string jsonText,
        Action<string> log)
    {
        double targetX = 228;
        var clickPoints = new List<(double X, double Y)>();
        string? captchaId = null;
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            if (root.TryGetProperty("captchaId", out var cidElem))
            {
                captchaId = cidElem.GetString();
            }

            if (type == CaptchaType.Jigsaw)
            {
                if (root.TryGetProperty("position", out var posElem) && posElem.GetArrayLength() > 0)
                {
                    targetX = posElem[0].GetDouble();
                    log($"[{DateTime.Now:HH:mm:ss}] 🎯 [SOLVER API] คำนวณตำแหน่งสไลเดอร์สำเร็จ: X={targetX:F1}px (ID: {captchaId})");
                    return (true, targetX, clickPoints, captchaId);
                }
                if (root.TryGetProperty("target_x", out var txElem))
                {
                    targetX = txElem.GetDouble();
                    log($"[{DateTime.Now:HH:mm:ss}] [SOLVER API] คำนวณตำแหน่งสไลเดอร์สำเร็จ: X={targetX:F1}px (ID: {captchaId})");
                    return (true, targetX, clickPoints, captchaId);
                }
            }
            else if (type == CaptchaType.Symbol)
            {
                if (root.TryGetProperty("symbol_positions", out var spArray))
                {
                    foreach (var item in spArray.EnumerateArray())
                    {
                        if (item.GetArrayLength() >= 2)
                        {
                            clickPoints.Add((item[0].GetDouble(), item[1].GetDouble()));
                        }
                    }
                    if (clickPoints.Count > 0)
                    {
                        log($"[{DateTime.Now:HH:mm:ss}] [SOLVER API] คำนวณพิกัดสัญลักษณ์สำเร็จ {clickPoints.Count} จุด (ID: {captchaId})");
                        return (true, targetX, clickPoints, captchaId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log($"[{DateTime.Now:HH:mm:ss}] [SOLVER API] ไม่สามารถ Parse JSON จาก Solver Server: {ex.Message}");
        }

        return (false, targetX, clickPoints, captchaId);
    }

    public async Task<(bool Success, string Message)> TestSolverApiConnectionAsync(string? endpoint = null, string? jwtToken = null)
    {
        if (endpoint != null && string.IsNullOrWhiteSpace(endpoint))
        {
            return (false, "กรุณากรอก URL สำหรับ Captcha Solver API");
        }

        string targetEndpoint = (string.IsNullOrWhiteSpace(endpoint) ? AppConstants.DefaultServerEndpoint : endpoint).Trim().TrimEnd('/');
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, targetEndpoint);
            if (!string.IsNullOrWhiteSpace(jwtToken))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken.Trim());
            }

            using var resp = await _httpClient.SendAsync(req);
            return (true, $"เชื่อมต่อ Captcha Solver API ที่ '{targetEndpoint}' สำเร็จ! (HTTP Status: {resp.StatusCode})");
        }
        catch (Exception ex)
        {
            return (false, $"ไม่สามารถเชื่อมต่อ Captcha Solver API ที่ '{targetEndpoint}': {ex.Message}");
        }
    }

    private async Task<IBrowser?> GetOrLaunchHeadlessBrowserAsync()
    {
        if (_headlessBrowser != null && !_headlessBrowser.IsClosed)
        {
            return _headlessBrowser;
        }

        string[] possibleBrowserPaths = [
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
        ];

        string? browserExe = possibleBrowserPaths.FirstOrDefault(File.Exists);
        if (string.IsNullOrEmpty(browserExe)) return null;

        string userDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TikTokStudioSandbox", "WebMonitorProfile");
        try { Directory.CreateDirectory(userDataDir); } catch { }

        try
        {
            _headlessBrowser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                ExecutablePath = browserExe,
                Headless = true,
                UserDataDir = userDataDir,
                Args = [
                    "--no-sandbox",
                    "--disable-gpu",
                    "--mute-audio",
                    "--disable-background-timer-throttling",
                    "--disable-blink-features=AutomationControlled",
                    "--window-size=1280,800",
                    "--remote-debugging-port=0"
                ]
            });
            return _headlessBrowser;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CdpAutomationService] GetOrLaunchHeadlessBrowserAsync failed: {ex.Message}");
            return null;
        }
    }


}
