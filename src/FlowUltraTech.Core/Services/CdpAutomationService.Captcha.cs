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
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public partial class CdpAutomationService
{
    public const string JigsawImageSelector =
        "#captcha-verify-image, .captcha_verify_img_slide, canvas.captcha_verify_img_slide, img[class*=\"slide\" i], #captcha-verify-container-main-page img[alt=\"Captcha\" i]";

    public const string SymbolImageSelector =
        "#captcha-verify-image, #symbol-img-box, .captcha_verify_img_symbol, .captcha_verify_img--bg, canvas.captcha_verify_img--bg, img[class*=\"symbol\" i], " +
        "#captcha-verify-container-main-page img.cap-cursor-pointer, .captcha-verify-container img.cap-cursor-pointer, " +
        "#captcha-verify-container-main-page img[alt*=\"Verify\" i], .captcha-verify-container img[alt*=\"Verify\" i], " +
        "#captcha-verify-container-main-page img[src^=\"data:image/\"], .captcha-verify-container img[src^=\"data:image/\"], " +
        "#captcha-mount-point img[src^=\"data:image/\"], img.cap-cursor-pointer[alt*=\"Verify\" i]";

    public const string SymbolSubmitSelector =
        "#symbol-submit-btn, .secsdk-captcha-submit-button, [class*=\"secsdk-captcha-submit\" i], button[class*=\"verify-btn\" i], " +
        "#captcha-verify-container-main-page button.TUXButton--primary, " +
        ".captcha-verify-container button.TUXButton--primary, " +
        "#captcha-mount-point button.TUXButton--primary, " +
        "#captcha-verify-container-main-page button[type=\"submit\"]";

    private static async Task<bool> IsCaptchaFrameVisibleAsync(IFrame frame)
    {
        for (var current = frame; current.ParentFrame != null; current = current.ParentFrame)
        {
            await using var owner = await current.FrameElementAsync();
            if (owner == null) return false;
            if (!await owner.EvaluateFunctionAsync<bool>(@"(el) => {
                if (el.closest('.tt-live-modalv2-wrapper-exited, [class*=""exit-done""]')) return false;
                const rect = el.getBoundingClientRect();
                if (rect.width <= 0 || rect.height <= 0) return false;
                for (let node = el; node; node = node.parentElement) {
                    const style = node.ownerDocument.defaultView.getComputedStyle(node);
                    if (style.display === 'none' || style.visibility === 'hidden' ||
                        style.visibility === 'collapse' || parseFloat(style.opacity || '1') <= 0.01) return false;
                }
                return true;
            }")) return false;
        }
        return true;
    }

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

                    // visibility/opacity on an ancestor also hides fixed-position children.
                    for (let ancestor = el; ancestor; ancestor = ancestor.parentElement) {
                        const style = ancestor.ownerDocument.defaultView.getComputedStyle(ancestor);
                        if (style.display === 'none' || style.visibility === 'hidden' ||
                            style.visibility === 'collapse' || parseFloat(style.opacity || '1') <= 0.01) return false;
                    }

                    // Check inline or computed styles
                    if (el.style && (el.style.display === 'none' || el.style.visibility === 'hidden')) return false;
                    try {
                        const style = window.getComputedStyle(el);
                        if (style && (style.display === 'none' || style.visibility === 'hidden' || parseFloat(style.opacity || '1') <= 0.01)) {
                            return false;
                        }
                        if (style && (style.position === 'fixed' || style.position === 'sticky')) {
                            const rect = el.getBoundingClientRect();
                            return rect.width > 0 && rect.height > 0;
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

                    // 5. Check text content for captcha / verification prompt
                    const text = (el.innerText || el.textContent || '').trim();
                    if (text.length > 0 && text.length < 300) {
                        if (text.includes('detected inactivity during your current LIVE') ||
                            text.includes('Complete the verification to continue your current LIVE') ||
                            text.includes('LIVE will end in:') ||
                            text.includes('Select 2 objects that are the same shape') ||
                            /select.*2.*objects?/i.test(text) ||
                            /same\s*shape/i.test(text) ||
                            /drag the puzzle piece/i.test(text)) {
                            return true;
                        }
                    }

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
                    const captchaScope = doc.querySelector('#captcha-verify-container-main-page, .captcha-verify-container, #captcha-mount-point, .captcha-modal-content-UBaIok, [class*=""captcha-modal-content""], .tt-live-modalv2-wrapper:not(.tt-live-modalv2-wrapper-exited)') || doc;

                    // 1. Jigsaw (Slide puzzle)
                    const hasJigsawInstruction = /drag the puzzle piece into place/i.test(docText) || /drag.*puzzle/i.test(docText) || /ลาก.*จิ๊กซอว์/i.test(docText);
                    let slideImg = captchaScope.querySelector('#captcha-verify-image, img.captcha_verify_img_slide, canvas.captcha_verify_img_slide, img[src*=""captcha""][class*=""slide""], img[alt=""Captcha""], img[aria-label=""Captcha""], [class*=""captcha-verify-image""]')
                        || doc.querySelector('#captcha-verify-image, img.captcha_verify_img_slide, canvas.captcha_verify_img_slide, img[src*=""captcha""][class*=""slide""], img[alt=""Captcha""], img[aria-label=""Captcha""], [class*=""captcha-verify-image""]');
                    let dragKnob = captchaScope.querySelector('.secsdk-captcha-drag-icon, .captcha_verify_slide--drag-icon, div[role=""slider""], button[role=""slider""], .sec-captcha-drag-icon, [class*=""drag-icon""], [class*=""drag_icon""], [class*=""slider-btn""], [aria-label*=""slider"" i]')
                        || doc.querySelector('.secsdk-captcha-drag-icon, .captcha_verify_slide--drag-icon, div[role=""slider""], button[role=""slider""], .sec-captcha-drag-icon, [class*=""drag-icon""], [class*=""drag_icon""], [class*=""slider-btn""], [aria-label*=""slider"" i]');

                    if (!slideImg && hasJigsawInstruction) {
                        const candidateImgs = Array.from(captchaScope.querySelectorAll('img, canvas')).filter(el => {
                            if (!isElementVisible(el)) return false;
                            const r = el.getBoundingClientRect();
                            return r.width >= 150 && r.height >= 100;
                        });
                        slideImg = candidateImgs.find(el => el.tagName.toLowerCase() === 'img' && (el.src || '').startsWith('data:image/'))
                            || candidateImgs.find(el => el.tagName.toLowerCase() === 'img')
                            || candidateImgs[0]
                            || null;
                    }

                    if (!dragKnob && hasJigsawInstruction) {
                        dragKnob = captchaScope.querySelector('[class*=""slidebar""] button, [class*=""drag-track""] button, [class*=""slide""] [role=""button""], [class*=""slidebar""] [tabindex], button:has(svg)');
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

                    // 2. Symbol (Icon / Shape match) — Strict Text & Element Detection
                    const symbolPatterns = [
                        /select\s*(?:2|two)\s*objects\s*(?:that\s*are\s*)?(?:the\s*)?same\s*shape/i,
                        /choose\s*(?:2|two)\s*objects\s*(?:that\s*are\s*)?(?:the\s*)?same\s*shape/i,
                        /select\s*(?:2|two)\s*(?:identical|matching)\s*objects/i,
                        /click\s*(?:2|two)\s*objects\s*(?:that\s*are\s*)?(?:the\s*)?same\s*shape/i,
                        /the\s*same\s*shape/i,
                        /same\s*shape/i,
                        /choose\s*2\s*objects/i,
                        /เลือก\s*.*วัตถุ.*(?:2|สอง).*เหมือนกัน/i,
                        /เลือก\s*(?:2|สอง)\s*วัตถุ.*เหมือนกัน/i,
                        /รูปทรงเดียวกัน/i,
                        /สัญลักษณ์.*เหมือนกัน/i
                    ];

                    let hasSymbolInstruction = false;
                    let matchedSymbolPrompt = '';
                    for (const pat of symbolPatterns) {
                        if (pat.test(docText)) {
                            hasSymbolInstruction = true;
                            const m = docText.match(pat);
                            matchedSymbolPrompt = m ? m[0] : '';
                            break;
                        }
                    }

                    // Also search in all child elements for symbol prompt text
                    if (!hasSymbolInstruction) {
                        const textNodes = Array.from(doc.querySelectorAll('div, p, span, h1, h2, h3, h4, section, [role=""dialog""]'));
                        for (const node of textNodes) {
                            if (!isElementVisible(node)) continue;
                            const nt = (node.innerText || node.textContent || '').trim();
                            if (nt.length > 0 && nt.length < 250) {
                                for (const pat of symbolPatterns) {
                                    if (pat.test(nt)) {
                                        hasSymbolInstruction = true;
                                        matchedSymbolPrompt = nt;
                                        break;
                                    }
                                }
                                if (hasSymbolInstruction) break;
                            }
                        }
                    }

                    const symbolImgSelector = '#captcha-verify-image, #symbol-img-box, img.captcha_verify_img_symbol, .captcha_verify_img--bg, canvas.captcha_verify_img--bg, img[class*=""symbol"" i], img[src*=""captcha"" i], img[alt*=""captcha"" i], img[alt*=""Verify"" i], img.cap-cursor-pointer, #captcha-verify-container-main-page img[src^=""data:image/""], .captcha-verify-container img[src^=""data:image/""], #captcha-mount-point img[src^=""data:image/""]';
                    let symbolImg = captchaScope.querySelector(symbolImgSelector) || doc.querySelector(symbolImgSelector);
                    let submitBtn = captchaScope.querySelector('#symbol-submit-btn, .secsdk-captcha-submit-button, [class*=""secsdk-captcha-submit""], button[class*=""verify-btn""], .captcha-verify-container button.TUXButton--primary, #captcha-verify-container-main-page button.TUXButton--primary, #captcha-mount-point button.TUXButton--primary, button[type=""submit""], button[class*=""submit""], button[class*=""confirm""]');
                    
                    if (!submitBtn) {
                        const allBtns = Array.from(captchaScope.querySelectorAll('button, div[role=""button""], a[role=""button""]'));
                        submitBtn = allBtns.find(b => {
                            const bt = (b.innerText || b.textContent || '').trim().toLowerCase();
                            return bt === 'confirm' || bt === 'ยืนยัน' || bt.includes('confirm');
                        }) || null;
                    }

                    if (!symbolImg && hasSymbolInstruction) {
                        // Candidate images inside captcha modal/container first (avoid picking live stream <canvas> in studio body)
                        const candidateImgs = Array.from(captchaScope.querySelectorAll('img, canvas')).filter(el => {
                            if (!isElementVisible(el)) return false;
                            const r = el.getBoundingClientRect();
                            return r.width >= 120 && r.height >= 80;
                        });
                        symbolImg = candidateImgs.find(el => el.tagName.toLowerCase() === 'img' && (el.src || '').startsWith('data:image/'))
                            || candidateImgs.find(el => el.tagName.toLowerCase() === 'img')
                            || candidateImgs[0]
                            || null;
                    }

                    let prompt = '';
                    const promptEl = captchaScope.querySelector('.secsdk-captcha-label, .captcha_verify_label, [class*=""tip-wrapper""], [class*=""prompt""], [class*=""title""]');
                    if (promptEl && isElementVisible(promptEl)) prompt = (promptEl.textContent || '').trim();
                    if (!prompt && matchedSymbolPrompt) prompt = matchedSymbolPrompt;

                    const captchaContainer = doc.querySelector('[id*=""captcha"" i], [class*=""captcha"" i], [class*=""secsdk"" i], .tt-live-modalv2-wrapper, [role=""dialog""]');
                    const hasVisibleContainer = !!(captchaContainer && isElementVisible(captchaContainer));
                    const hasValidSymbolImg = !!(symbolImg && isElementVisible(symbolImg));
                    const hasValidSubmitBtn = !!(submitBtn && isElementVisible(submitBtn));

                    if ((hasSymbolInstruction && (hasValidSymbolImg || hasValidSubmitBtn || hasVisibleContainer)) ||
                        (hasValidSymbolImg && (hasValidSubmitBtn || /same\s*shape|same\s*object|symbol/i.test(prompt)))) {
                        return {
                            type: 'Symbol',
                            typeDescription: 'เลือกสัญลักษณ์ 2 รูปที่เหมือนกัน (Symbol Captcha)',
                            imageSrc: (symbolImg && (symbolImg.src || symbolImg.getAttribute('src'))) || '',
                            promptText: prompt || 'Select 2 objects that are the same shape',
                            hasWidgetLoaded: !!(hasValidSymbolImg || hasValidSubmitBtn)
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
                    pageUrl: window.location ? window.location.href : '',
                    countdownText: countdownText,
                    hasInactivityModal: !!inactModalContent,
                    isModalVisible: isModalVisible,
                    isModalExited: isModalExited
                });
            })()";

            var fullHtmlSections = new List<string>();

            // CDP evaluates each document independently, including cross-origin frames.
            foreach (var (page, frame) in pages
                .Where(p => p.Url == null || !p.Url.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => p.Frames.Select(f => (Page: p, Frame: f))))
            {
                try
                {
                    if (!await IsCaptchaFrameVisibleAsync(frame)) continue;
                    string json = await frame.EvaluateExpressionAsync<string>(detectScript);
                    if (string.IsNullOrWhiteSpace(json) || json == "[]" || json == "{}")
                        continue;

                    using var doc = JsonDocument.Parse(json);
                    JsonElement itemsArray;
                    string pageFullHtml = string.Empty;
                    string pageUrl = page.Url ?? string.Empty;

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
                            result.HasInactivityModal |= inactElem.GetBoolean();

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

                        if (doc.RootElement.TryGetProperty("typeDescription", out var descElem) && !string.IsNullOrWhiteSpace(descElem.GetString()) &&
                            doc.RootElement.TryGetProperty("isActive", out var frameActive) && (frameActive.GetBoolean() || !result.IsActive))
                            result.TypeDescription = descElem.GetString()!;

                        if (doc.RootElement.TryGetProperty("hasWidgetLoaded", out var hwElem) && hwElem.GetBoolean())
                            result.HasWidgetLoaded = true;

                        if (doc.RootElement.TryGetProperty("imageSrc", out var isrcElem) && !string.IsNullOrWhiteSpace(isrcElem.GetString()))
                            result.ImageSrc = isrcElem.GetString()!;

                        if (doc.RootElement.TryGetProperty("promptText", out var pTextElem) && !string.IsNullOrWhiteSpace(pTextElem.GetString()))
                            result.PromptText = pTextElem.GetString()!;

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
                                pageFullHtml = await frame.GetContentAsync();
                            }
                            catch { }
                        }

                        if (!string.IsNullOrWhiteSpace(pageFullHtml))
                        {
                            if (!pageFullHtml.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
                            {
                                pageFullHtml = "<!DOCTYPE html>\n" + pageFullHtml;
                            }
                            if (frame.ParentFrame != null)
                                pageFullHtml = $"<!-- [CAPTCHA IFRAME FULL HTML] Frame URL: {frame.Url} -->\n" + pageFullHtml;
                            fullHtmlSections.Add(pageFullHtml);
                        }

                    }

                    // Priority 1: Direct extract from ImageSrc (data URI) to preserve original WebP resolution without scaling blur
                    if (result.DirectImageBytes == null && !string.IsNullOrWhiteSpace(result.ImageSrc) && result.ImageSrc.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            int commaIdx = result.ImageSrc.IndexOf(',');
                            if (commaIdx >= 0)
                            {
                                byte[] bytes = Convert.FromBase64String(result.ImageSrc.Substring(commaIdx + 1));
                                result.DirectImageBytes = bytes;
                                result.DirectImageDimensions = GetImageDimensions(bytes);
                            }
                        }
                        catch { }
                    }

                    // Priority 2: Capture PNG screenshot of active Captcha element for CroppedImageBytes (and DirectImageBytes fallback)
                    if (result.IsActive && result.Type != CaptchaType.None && (result.DirectImageBytes == null || result.CroppedImageBytes == null))
                    {
                        try
                        {
                            string imgSel = result.Type == CaptchaType.Jigsaw
                                ? JigsawImageSelector
                                : SymbolImageSelector;

                            IElementHandle? imgHandle = await frame.QuerySelectorAsync(imgSel);
                            if (imgHandle == null)
                            {
                                foreach (var f in page.Frames)
                                {
                                    imgHandle = await f.QuerySelectorAsync(imgSel);
                                    if (imgHandle != null) break;
                                }
                            }

                            if (imgHandle != null)
                            {
                                byte[]? freshBytes = await imgHandle.ScreenshotDataAsync(new ElementScreenshotOptions { Type = ScreenshotType.Png });
                                if (freshBytes != null && freshBytes.Length > 0)
                                {
                                    result.CroppedImageBytes = freshBytes;
                                    result.CroppedImageDimensions = GetImageDimensions(freshBytes);
                                    if (result.DirectImageBytes == null)
                                    {
                                        result.DirectImageBytes = freshBytes;
                                        result.DirectImageDimensions = result.CroppedImageDimensions;
                                    }
                                }
                            }
                        }
                        catch { }
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



    public static string? GetImageDimensions(byte[]? bytes)
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
                // VP8 (Lossy WebP: 'V' 'P' '8' ' ' and start code 9D 01 2A at offset 23..25)
                if (bytes[12] == 0x56 && bytes[13] == 0x50 && bytes[14] == 0x38 && bytes[15] == 0x20 &&
                    bytes[23] == 0x9D && bytes[24] == 0x01 && bytes[25] == 0x2A)
                {
                    int width = (bytes[26] | (bytes[27] << 8)) & 0x3FFF;
                    int height = (bytes[28] | (bytes[29] << 8)) & 0x3FFF;
                    if (width > 0 && height > 0) return $"{width}x{height}";
                }
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

    private static (double Width, double Height)? ParseDimensions(string? dimStr)
    {
        if (string.IsNullOrWhiteSpace(dimStr)) return null;
        var parts = dimStr.Split('x', 'X');
        if (parts.Length == 2 && double.TryParse(parts[0], out double w) && double.TryParse(parts[1], out double h) && w > 0 && h > 0)
        {
            return (w, h);
        }
        return null;
    }

    [Obsolete("ระบบ Mock Response ถูกยกเลิกแล้ว เมธอดนี้จะเรียก SolveCaptchaAsync ด้วย AI Server จริง")]
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
            solverMode: CaptchaSolverType.LocalApiServer,
            solverApiEndpoint: null,
            solverJwtToken: null,
            customMockJsonPath: null,
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

            const string activeModalProbeScript = @"(() => {
                const el = document.querySelector('#captcha-verify-container-main-page, .captcha-verify-container, #captcha-verify-image, .secsdk-captcha-drag-icon, #jigsaw-slider-knob, #symbol-submit-btn, img.cap-cursor-pointer, [class*=""secsdk-captcha"" i]');
                if (!el) return false;
                if (el.closest && el.closest('.tt-live-modalv2-wrapper-exited, [class*=""exit-done""]')) return false;
                const rect = el.getBoundingClientRect();
                return rect.width > 0 && rect.height > 0;
            })()";

            foreach (var page in pages)
            {
                var hasModal = await page.EvaluateExpressionAsync<bool>(activeModalProbeScript);
                if (hasModal)
                {
                    targetPage = page;
                    break;
                }
                foreach (var frame in page.Frames)
                {
                    var frameHasModal = await frame.EvaluateExpressionAsync<bool>(activeModalProbeScript);
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
            SolverParsedResult? parsedApiResult = null;

            // Locate image element and DOM bounding box first
            string imgSel = detection.Type == CaptchaType.Jigsaw
                ? JigsawImageSelector
                : SymbolImageSelector;
            var imgHandle = targetFrame != null ? await targetFrame.QuerySelectorAsync(imgSel) : await targetPage.QuerySelectorAsync(imgSel);
            if (imgHandle == null && targetFrame == null)
            {
                foreach (var frame in targetPage.Frames)
                {
                    imgHandle = await frame.QuerySelectorAsync(imgSel);
                    if (imgHandle != null)
                    {
                        targetFrame = frame;
                        break;
                    }
                }
            }

            BoundingBox? imgBox = null;
            if (imgHandle != null)
            {
                try
                {
                    imgBox = await imgHandle.BoundingBoxAsync();
                }
                catch { }
            }

            double? displayWidth = imgBox != null && imgBox.Width > 0 ? (double)imgBox.Width : null;
            double? displayHeight = imgBox != null && imgBox.Height > 0 ? (double)imgBox.Height : null;
            if (displayWidth.HasValue && displayHeight.HasValue)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 📐 [CAPTCHA SOLVE] ขนาด DOM Display ของรูปภาพ: {displayWidth.Value:F0}x{displayHeight.Value:F0}px");
            }

            // 1. Solve with Live API Server if selected
            if (solverMode == CaptchaSolverType.LocalApiServer)
            {
                string endpoint = (string.IsNullOrWhiteSpace(solverApiEndpoint) ? AppConstants.DefaultServerEndpoint : solverApiEndpoint).Trim().TrimEnd('/');
                usedEndpoint = endpoint;
                Log($"[{DateTime.Now:HH:mm:ss}] 🤖 [CAPTCHA SOLVE] ส่งรูปภาพไปยัง Local Solver API Server: {endpoint}...");

                // Prepare Base64 Image (Priority: DirectImageBytes -> ImageSrc (data URI) -> CroppedImageBytes -> Fresh Element Screenshot)
                string? base64Image = null;
                if (detection.DirectImageBytes != null && detection.DirectImageBytes.Length > 0)
                {
                    base64Image = Convert.ToBase64String(detection.DirectImageBytes);
                }
                else if (!string.IsNullOrWhiteSpace(detection.ImageSrc) && detection.ImageSrc.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    base64Image = detection.ImageSrc;
                }
                else if (detection.CroppedImageBytes != null && detection.CroppedImageBytes.Length > 0)
                {
                    base64Image = Convert.ToBase64String(detection.CroppedImageBytes);
                }
                else if (imgHandle != null)
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 📷 [CAPTCHA SOLVE] ดึงภาพแคปต์ชาสดจาก Element Screenshot บนหน้าจอ...");
                    try
                    {
                        byte[]? freshBytes = await imgHandle.ScreenshotDataAsync(new ElementScreenshotOptions { Type = ScreenshotType.Png });
                        if (freshBytes != null && freshBytes.Length > 0)
                        {
                            detection.DirectImageBytes = freshBytes;
                            detection.DirectImageDimensions = GetImageDimensions(freshBytes);
                            base64Image = Convert.ToBase64String(freshBytes);
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(base64Image) && !string.IsNullOrWhiteSpace(detection.ImageSrc))
                {
                    base64Image = detection.ImageSrc;
                }

                if (!string.IsNullOrEmpty(base64Image))
                {
                    try
                    {
                        string apiPath = detection.Type == CaptchaType.Jigsaw ? "/api/v1/captcha/slide" : "/api/v1/captcha/symbol";
                        string fullUrl = $"{endpoint}{apiPath}";
                        var payload = new
                        {
                            image = base64Image,
                            displayWidth = displayWidth,
                            displayHeight = displayHeight,
                            appVersion = AppConstants.CurrentVersion
                        };
                        string jsonBody = JsonSerializer.Serialize(payload);

                        using var req = new HttpRequestMessage(HttpMethod.Post, fullUrl)
                        {
                            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                        };
                        req.Headers.TryAddWithoutValidation("X-App-Version", AppConstants.CurrentVersion);

                        if (!string.IsNullOrWhiteSpace(solverJwtToken))
                        {
                            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", solverJwtToken.Trim());
                        }
                        else
                        {
                            Log($"[{DateTime.Now:HH:mm:ss}] ℹ️ [CAPTCHA SOLVE] ไม่พบ Token กรุณาล็อกอินผ่านแถบด้านบน (หากเซิร์ฟเวอร์เปิดโหมดความปลอดภัย)");
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
                            altReq.Headers.TryAddWithoutValidation("X-App-Version", AppConstants.CurrentVersion);
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
                                parsedApiResult = ParseSolverApiResponse(detection.Type, altJson, Log);
                                solvedViaApi = parsedApiResult.Solved;
                                targetX = parsedApiResult.TargetX;
                                clickPoints = parsedApiResult.ClickPoints;
                                currentCaptchaId = parsedApiResult.CaptchaId;
                            }
                            else
                            {
                                string altErr = await altResp.Content.ReadAsStringAsync();
                                solveResult.ServerResponseJson = altErr;
                                Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVE] Alt route ตอบกลับสถานะ: {(int)altResp.StatusCode} {altResp.StatusCode} - {altErr}");
                                if (altResp.StatusCode == System.Net.HttpStatusCode.Unauthorized || altResp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                                {
                                    solveResult.Message = $"Token expired หรือไม่มีสิทธิ์เข้าถึง Solver API ({(int)altResp.StatusCode} {altResp.StatusCode})";
                                }
                            }
                        }
                        else if (resp.IsSuccessStatusCode)
                        {
                            string respJson = await resp.Content.ReadAsStringAsync();
                            solveResult.ServerResponseJson = respJson;
                            parsedApiResult = ParseSolverApiResponse(detection.Type, respJson, Log);
                            solvedViaApi = parsedApiResult.Solved;
                            targetX = parsedApiResult.TargetX;
                            clickPoints = parsedApiResult.ClickPoints;
                            currentCaptchaId = parsedApiResult.CaptchaId;
                        }
                        else
                        {
                            string errBody = await resp.Content.ReadAsStringAsync();
                            solveResult.ServerResponseJson = errBody;
                            Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVE] Solver API ตอบกลับสถานะ: {(int)resp.StatusCode} {resp.StatusCode} - {errBody}");
                            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                            {
                                solveResult.Message = $"Token expired หรือไม่มีสิทธิ์เข้าถึง Solver API ({(int)resp.StatusCode} {resp.StatusCode})";
                                Log($"[{DateTime.Now:HH:mm:ss}] 🔒 [CAPTCHA SOLVE] เข้าถึงถูกปฏิเสธ: กรุณาตรวจสอบการเข้าสู่ระบบและสถานะ License/Lease");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVE] เรียก Solver API Server ไม่สำเร็จ ({ex.Message})");
                    }
                }
                else
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVE] ไม่พบภาพ Base64 ของ Captcha สำหรับส่งเข้า API");
                }
            }

            // Mock fallback is completely removed: if not solved via AI API, fail gracefully
            if (!solvedViaApi)
            {
                solveResult.Success = false;
                solveResult.Message = string.IsNullOrWhiteSpace(solveResult.Message)
                    ? "ไม่สามารถส่งคำขอหรือถอดรหัส Captcha จาก AI Server ได้ (การเชื่อมต่อล้มเหลว หรือ AI Server ปฏิเสธคำขอ)"
                    : solveResult.Message;
                Log($"[{DateTime.Now:HH:mm:ss}] ❌ [CAPTCHA SOLVE] {solveResult.Message}");
                return solveResult;
            }

            // Execute action based on Captcha Type
            if (detection.Type == CaptchaType.Jigsaw)
            {
                double domW = imgBox != null && imgBox.Width > 0 ? (double)imgBox.Width : 340.0;
                var directDims = ParseDimensions(detection.DirectImageDimensions);
                double origW = parsedApiResult?.ImageWidth ?? directDims?.Width ?? (targetX > 552 ? 640.0 : 552.0);

                if (parsedApiResult?.RawTargetX.HasValue == true && origW > 0)
                {
                    // Priority 1: Direct exact mathematical projection from RAW target X
                    double rawX = parsedApiResult.RawTargetX.Value;
                    targetX = rawX * (domW / origW);
                    Log($"[{DateTime.Now:HH:mm:ss}] 🧩 [CAPTCHA SOLVE] แมป Target X จาก Raw {rawX:F1}px [ขนาดภาพ: {origW:F0}px] สู่ DOM ({domW:F0}px) -> X={targetX:F1}px");
                }
                else if (parsedApiResult?.ScaleX.HasValue == true && Math.Abs(parsedApiResult.ScaleX.Value - 1.0) > 0.001)
                {
                    // Priority 2: Server already scaled to DisplayWidth. If DOM width changed, adjust proportionally.
                    double srvW = parsedApiResult.DisplayWidth ?? domW;
                    if (srvW > 0 && Math.Abs(srvW - domW) > 1.0)
                    {
                        targetX = targetX * (domW / srvW);
                    }
                }
                else if (imgBox != null && imgBox.Width > 0 && targetX > domW)
                {
                    // Priority 3: Fallback Mock or unscaled API where targetX is in raw image coordinates
                    double s = domW / origW;
                    double scaledX = targetX * s;
                    Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVE] ค่า Target X ({targetX:F1}px) เป็นพิกัดภาพดิบ ทำการ Scale สู่ DOM ({domW:F0}px) -> X={scaledX:F1}px");
                    targetX = scaledX;
                }

                // Query moving puzzle piece element in DOM to handle any initial piece offset
                string pieceSelector = "#jigsaw-moving-piece, [class*=\"cap-absolute\"][draggable=\"true\"] img, img[alt=\"Captcha\" i], .captcha_verify_slide--piece";
                var pieceHandle = targetFrame != null 
                    ? await targetFrame.QuerySelectorAsync(pieceSelector)
                    : await targetPage.QuerySelectorAsync(pieceSelector);
                if (pieceHandle == null && targetFrame == null)
                {
                    foreach (var frame in targetPage.Frames)
                    {
                        pieceHandle = await frame.QuerySelectorAsync(pieceSelector);
                        if (pieceHandle != null) break;
                    }
                }

                double pieceStartOffsetX = 0;
                double pieceWidth = 0;
                if (pieceHandle != null)
                {
                    var pieceBox = await pieceHandle.BoundingBoxAsync();
                    if (pieceBox != null)
                    {
                        pieceWidth = (double)pieceBox.Width;
                        if (imgBox != null)
                        {
                            pieceStartOffsetX = Math.Max(0, (double)(pieceBox.X - imgBox.X));
                            Log($"[{DateTime.Now:HH:mm:ss}] 🧩 [CAPTCHA SOLVE] ข้อมูล Piece ใน DOM: Width={pieceBox.Width:F1}px, OffsetX={pieceStartOffsetX:F1}px");
                        }
                    }
                }

                targetX = Math.Clamp(targetX, 0, Math.Max(0, domW));
                Log($"[{DateTime.Now:HH:mm:ss}] 🧩 [CAPTCHA SOLVE] โหมด Jigsaw: พิกัดเป้าหมายชิ้นส่วนในภาพ X={targetX:F1}px (DOM Image Width: {domW:F0}px)");

                string knobSelector = "#captcha_slide_button, #jigsaw-slider-knob, .secsdk-captcha-drag-icon, .captcha_verify_slide--drag-icon, [role=\"slider\"]";
                var knobHandle = targetFrame != null 
                    ? await targetFrame.QuerySelectorAsync(knobSelector)
                    : await targetPage.QuerySelectorAsync(knobSelector);
                if (knobHandle == null && targetFrame == null)
                {
                    foreach (var frame in targetPage.Frames)
                    {
                        knobHandle = await frame.QuerySelectorAsync(knobSelector);
                        if (knobHandle != null) break;
                    }
                }

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

                // Query slider track element to get exact usable travel distance
                string trackSelector = "[class*=\"cap-rounded-full\"], #jigsaw-slidebar-track, .captcha_verify_slide--slidebar, .secsdk-captcha-drag-track, [class*=\"drag-track\"], [class*=\"slidebar\"]";
                var trackHandle = targetFrame != null 
                    ? await targetFrame.QuerySelectorAsync(trackSelector)
                    : await targetPage.QuerySelectorAsync(trackSelector);
                if (trackHandle == null && targetFrame == null)
                {
                    foreach (var frame in targetPage.Frames)
                    {
                        trackHandle = await frame.QuerySelectorAsync(trackSelector);
                        if (trackHandle != null) break;
                    }
                }

                var trackBox = trackHandle != null ? await trackHandle.BoundingBoxAsync() : null;
                double trackWidth = trackBox != null && trackBox.Width > 0 ? (double)trackBox.Width : domW;
                double knobWidth = (double)box.Width > 0 ? (double)box.Width : 64.0;
                double usableTrack = Math.Max(1.0, trackWidth - knobWidth);

                // In TikTok's slider component (69901.3baa747e.js):
                //   knob drag delta: e.x
                //   track usable range: l = trackWidth - knobWidth
                //   normalized track progress: w = e.x / l
                //   piece translation: position.x = w * domW
                //   backend submitted reply: reply.x = Math.round(w * domW)
                // Therefore, to place piece exactly at targetX, the knob must travel:
                //   knobDragDistance = targetX * (usableTrack / domW)
                double knobDragDistance;
                if (pieceStartOffsetX > 5.0 && pieceWidth > 0 && (domW - pieceWidth - pieceStartOffsetX) > 0)
                {
                    // Mock environment with constrained interior piece range
                    double mockPieceTravelRange = domW - pieceWidth - pieceStartOffsetX;
                    double requiredMove = Math.Max(0, targetX - pieceStartOffsetX);
                    knobDragDistance = requiredMove * (usableTrack / mockPieceTravelRange);
                    Log($"[{DateTime.Now:HH:mm:ss}] 🧩 [CAPTCHA SOLVE] โหมด Offset Track: RequiredMove={requiredMove:F1}px, TravelRange={mockPieceTravelRange:F1}px -> KnobDrag={knobDragDistance:F1}px");
                }
                else
                {
                    // Real TikTok Live Studio standard slider ratio
                    double dragRatio = usableTrack / domW;
                    knobDragDistance = targetX * dragRatio;
                    Log($"[{DateTime.Now:HH:mm:ss}] 🧩 [CAPTCHA SOLVE] อัตราส่วนสไลเดอร์ TikTok: TrackWidth={trackWidth:F1}px, KnobWidth={knobWidth:F1}px, DragRatio={dragRatio:F4} -> KnobDrag={knobDragDistance:F1}px (เป้าหมายชิ้นส่วน={targetX:F1}px)");
                }

                knobDragDistance = Math.Clamp(knobDragDistance, 0, usableTrack);

                decimal startX = box.X + box.Width / 2;
                decimal startY = box.Y + box.Height / 2;

                Log($"[{DateTime.Now:HH:mm:ss}] 🕹️ [CAPTCHA SOLVE] พบปุ่ม Knob ที่พิกัด ({startX:F0}, {startY:F0}) กำลังจำลองการลากระยะทาง {knobDragDistance:F1}px ไปยัง X={startX + (decimal)knobDragDistance:F0}...");

                // Smooth mouse drag with human-like Bezier curve
                await targetPage.Mouse.MoveAsync(startX, startY);
                await targetPage.Mouse.DownAsync();

                int steps = 24;
                for (int step = 1; step <= steps; step++)
                {
                    double progress = (double)step / steps;
                    double ease = 1 - Math.Pow(1 - progress, 3);
                    decimal currentX = startX + (decimal)(knobDragDistance * ease);
                    decimal currentY = startY + (decimal)(Random.Shared.NextDouble() * 2 - 1);
                    await targetPage.Mouse.MoveAsync(currentX, currentY);
                    await Task.Delay(Random.Shared.Next(12, 22));
                }

                await Task.Delay(60);
                await targetPage.Mouse.UpAsync();

                try
                {
                    string pieceTransform = await targetPage.EvaluateFunctionAsync<string>(@"() => {
                        const piece = document.querySelector(""#jigsaw-moving-piece, .captcha-verify-container div[draggable='true'] img, .cap-absolute[draggable='true']"");
                        if (!piece) return '';
                        const parent = piece.closest(""div[draggable='true']"") || piece;
                        return parent.style.transform || parent.getAttribute('style') || '';
                    }");
                    if (!string.IsNullOrWhiteSpace(pieceTransform))
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] 🧩 [CAPTCHA SOLVE] สถานะชิ้นส่วนหลังการลาก: '{pieceTransform}'");
                    }
                }
                catch { }

                Log($"[{DateTime.Now:HH:mm:ss}] 🕹️ [CAPTCHA SOLVE] ลากสไลเดอร์ถึงตำแหน่งเป้าหมายเรียบร้อยแล้ว");
            }
            else if (detection.Type == CaptchaType.Symbol)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [CAPTCHA SOLVE] ========== เริ่มโหมด Symbol ==========");
                Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] พิกัดคลิกเป้าหมายมี {clickPoints.Count} จุด");
                Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] targetFrame: {(targetFrame != null ? $"iframe ({targetFrame.Url})" : "null")}");
                Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] targetPage: {targetPage.Url}");

                if (imgBox == null)
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] กำลังค้นหา image element...");
                    string imgSelector = SymbolImageSelector;
                    imgHandle = targetFrame != null
                        ? await targetFrame.QuerySelectorAsync(imgSelector)
                        : await targetPage.QuerySelectorAsync(imgSelector);

                    if (imgHandle != null)
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] ✅ พบ image element ใน {(targetFrame != null ? "iframe" : "main page")}");
                    }
                    else if (targetFrame == null)
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] ไม่พบใน main page, กำลังค้นหาใน frames ทั้งหมด...");
                        foreach (var frame in targetPage.Frames)
                        {
                            imgHandle = await frame.QuerySelectorAsync(imgSelector);
                            if (imgHandle != null)
                            {
                                targetFrame = frame;
                                Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] ✅ พบ image element ใน frame: {frame.Url}");
                                break;
                            }
                        }
                    }
                    else
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] ❌ ไม่พบ image element ใน iframe");
                    }

                    if (imgHandle != null)
                    {
                        imgBox = await imgHandle.BoundingBoxAsync();
                        if (imgBox != null)
                        {
                            Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] ✅ Image BoundingBox: X={imgBox.X:F1}, Y={imgBox.Y:F1}, W={imgBox.Width:F1}, H={imgBox.Height:F1}");
                        }
                        else
                        {
                            Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] ❌ ไม่สามารถดึง BoundingBox จาก image element");
                        }
                    }
                }
                else
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] ✅ ใช้ imgBox ที่มีอยู่แล้ว: W={imgBox.Width:F1}, H={imgBox.Height:F1}");
                }

                if (imgBox == null)
                {
                    solveResult.Success = false;
                    solveResult.Message = "ไม่สามารถคำนวณตำแหน่ง Bounding Box ของรูปภาพสัญลักษณ์ได้";
                    Log($"[{DateTime.Now:HH:mm:ss}] ❌ [CAPTCHA SOLVE] {solveResult.Message}");
                    return solveResult;
                }

                double domW = (double)imgBox.Width;
                double domH = (double)imgBox.Height;
                var directDims = ParseDimensions(detection.DirectImageDimensions);
                double origW = parsedApiResult?.ImageWidth ?? directDims?.Width ?? 640.0;
                double origH = parsedApiResult?.ImageHeight ?? directDims?.Height ?? 640.0;

                Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] DOM Size: {domW:F0}x{domH:F0}px");
                Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] Original Image Size: {origW:F0}x{origH:F0}px");

                bool hasRaw = parsedApiResult?.RawClickPoints != null && parsedApiResult.RawClickPoints.Count == clickPoints.Count && parsedApiResult.RawClickPoints.Count > 0;
                bool wasServerScaled = parsedApiResult?.ScaleX.HasValue == true && Math.Abs(parsedApiResult.ScaleX.Value - 1.0) > 0.001;

                Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] hasRaw: {hasRaw}, wasServerScaled: {wasServerScaled}");
                if (hasRaw)
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] ✅ ใช้ Priority 1: Raw Coordinates Mapping");
                }
                else if (wasServerScaled)
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] ⚠️ ใช้ Priority 2: Server Scaled Coordinates");
                }
                else
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] ⚠️⚠️ ใช้ Priority 3: Fallback (อาจไม่แม่นยำ!)");
                }

                for (int i = 0; i < clickPoints.Count; i++)
                {
                    double clickX;
                    double clickY;

                    if (hasRaw && origW > 0 && origH > 0)
                    {
                        // Priority 1: Direct exact mathematical projection from RAW model coordinates onto current DOM box
                        var rawPt = parsedApiResult!.RawClickPoints![i];
                        clickX = rawPt.X * (domW / origW);
                        clickY = rawPt.Y * (domH / origH);
                        Log($"[{DateTime.Now:HH:mm:ss}] 📐 [SYMBOL P1] จุดที่ {i + 1}: Raw({rawPt.X:F0},{rawPt.Y:F0}) × ({domW:F0}/{origW:F0}, {domH:F0}/{origH:F0}) = DOM({clickX:F1},{clickY:F1})");
                    }
                    else if (wasServerScaled)
                    {
                        // Priority 2: Server already scaled to DisplayWidth/DisplayHeight. Adjust if DOM size changed.
                        var pt = clickPoints[i];
                        double srvW = parsedApiResult?.DisplayWidth ?? domW;
                        double srvH = parsedApiResult?.DisplayHeight ?? domH;
                        double adjustX = (srvW > 0 && Math.Abs(srvW - domW) > 1.0) ? (domW / srvW) : 1.0;
                        double adjustY = (srvH > 0 && Math.Abs(srvH - domH) > 1.0) ? (domH / srvH) : 1.0;
                        clickX = pt.X * adjustX;
                        clickY = pt.Y * adjustY;
                        Log($"[{DateTime.Now:HH:mm:ss}] 📐 [SYMBOL P2] จุดที่ {i + 1}: Scaled({pt.X:F0},{pt.Y:F0}) × adjust({adjustX:F3},{adjustY:F3}) = DOM({clickX:F1},{clickY:F1})");
                    }
                    else
                    {
                        // Priority 3: Fallback / Mock JSON or unscaled API
                        var pt = clickPoints[i];
                        clickX = pt.X;
                        clickY = pt.Y;

                        Log($"[{DateTime.Now:HH:mm:ss}] 📐 [SYMBOL P3] จุดที่ {i + 1}: Direct({pt.X:F0},{pt.Y:F0})");

                        // Only scale if the coordinates clearly exceed DOM dimensions (i.e. they are raw 640px or 552px coordinates)
                        if (clickX > domW || clickY > domH)
                        {
                            double refW = origW > 0 ? origW : (clickX > 552 ? 640.0 : 552.0);
                            double refH = origH > 0 ? origH : (refW == 640 ? 640.0 : 345.0);
                            double sx = domW / refW;
                            double sy = domH / refH;
                            clickX = clickX * sx;
                            clickY = clickY * sy;
                            Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVE] พิกัดจุดที่ {i + 1} ({pt.X:F0}, {pt.Y:F0}) เป็นพิกัดภาพดิบ Scale สู่ DOM -> ({clickX:F1}, {clickY:F1})");
                        }
                    }

                    // Clamp safely within DOM boundaries (with 5px safety margin from borders)
                    double clampedX = Math.Clamp(clickX, 5.0, Math.Max(5.0, domW - 5.0));
                    double clampedY = Math.Clamp(clickY, 5.0, Math.Max(5.0, domH - 5.0));

                    if (Math.Abs(clampedX - clickX) > 0.1 || Math.Abs(clampedY - clickY) > 0.1)
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [SYMBOL DEBUG] จุดที่ {i + 1} ถูก clamp: ({clickX:F1},{clickY:F1}) → ({clampedX:F1},{clampedY:F1})");
                    }

                    clickX = clampedX;
                    clickY = clampedY;

                    decimal targetClickX = imgBox.X + (decimal)clickX;
                    decimal targetClickY = imgBox.Y + (decimal)clickY;

                    Log($"[{DateTime.Now:HH:mm:ss}] 🎯 [SYMBOL CLICK] จุดที่ {i + 1}: DOM({clickX:F1},{clickY:F1}) + ImgBox({imgBox.X:F1},{imgBox.Y:F1}) = Screen({targetClickX:F1},{targetClickY:F1})");
                    await targetPage.Mouse.MoveAsync(targetClickX, targetClickY);
                    await Task.Delay(30);
                    await targetPage.Mouse.ClickAsync(targetClickX, targetClickY, new ClickOptions { Delay = Random.Shared.Next(35, 75) });
                    Log($"[{DateTime.Now:HH:mm:ss}] ✅ [SYMBOL CLICK] คลิกจุดที่ {i + 1} สำเร็จ");
                    await Task.Delay(Random.Shared.Next(250, 450));
                }

                Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] กำลังค้นหาปุ่ม Submit / Confirm...");
                string submitSelector = SymbolSubmitSelector;
                var submitHandle = targetFrame != null
                    ? await targetFrame.QuerySelectorAsync(submitSelector)
                    : await targetPage.QuerySelectorAsync(submitSelector);

                if (submitHandle != null)
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] ✅ พบปุ่ม Submit ใน {(targetFrame != null ? "iframe" : "main page")}");
                }
                else if (targetFrame == null)
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] ไม่พบใน main page, กำลังค้นหาใน frames...");
                    foreach (var frame in targetPage.Frames)
                    {
                        submitHandle = await frame.QuerySelectorAsync(submitSelector);
                        if (submitHandle != null)
                        {
                            targetFrame = frame;
                            Log($"[{DateTime.Now:HH:mm:ss}] 🔣 [SYMBOL DEBUG] ✅ พบปุ่ม Submit ใน frame: {frame.Url}");
                            break;
                        }
                    }
                }

                if (submitHandle != null)
                {
                    // Wait up to 600ms for React TUXButton to remove disabled state after the 2nd click
                    for (int waitIdx = 0; waitIdx < 6; waitIdx++)
                    {
                        try
                        {
                            bool isDisabled = await submitHandle.EvaluateFunctionAsync<bool>(
                                "btn => btn.disabled || btn.getAttribute('aria-disabled') === 'true' || btn.classList.contains('TUXButton--disabled')");
                            if (!isDisabled) break;
                        }
                        catch { break; }
                        await Task.Delay(100);
                    }

                    Log($"[{DateTime.Now:HH:mm:ss}] 🔘 [SYMBOL SUBMIT] กดปุ่มยืนยันผล (Submit / Confirm)...");
                    await submitHandle.ClickAsync(new ClickOptions { Delay = Random.Shared.Next(35, 75) });
                    Log($"[{DateTime.Now:HH:mm:ss}] ✅ [SYMBOL SUBMIT] กดปุ่มสำเร็จ");
                }
                else
                {
                    // Fallback: search by button text ("Confirm" / "ยืนยัน") inside captcha container
                    const string fallbackClickConfirmScript = @"(() => {
                        const scope = document.querySelector('#captcha-verify-container-main-page, .captcha-verify-container, #captcha-mount-point, .captcha-modal-content-UBaIok, .tt-live-modalv2-wrapper') || document;
                        const btns = Array.from(scope.querySelectorAll('button, [role=""button""]'));
                        const confirmBtn = btns.find(b => {
                            const txt = (b.innerText || b.textContent || '').trim().toLowerCase();
                            return txt === 'confirm' || txt === 'ยืนยัน' || txt.includes('confirm');
                        });
                        if (confirmBtn) {
                            confirmBtn.click();
                            return true;
                        }
                        return false;
                    })()";
                    bool clickedViaJs = targetFrame != null
                        ? await targetFrame.EvaluateExpressionAsync<bool>(fallbackClickConfirmScript)
                        : await targetPage.EvaluateExpressionAsync<bool>(fallbackClickConfirmScript);

                    if (clickedViaJs)
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] ✅ [SYMBOL SUBMIT] กดปุ่ม Confirm ผ่าน JS Fallback สำเร็จ");
                    }
                    else
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [SYMBOL SUBMIT] ไม่พบปุ่ม Submit! อาจต้องตรวจสอบ selector");
                    }
                }
            }
            else
            {
                Log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVE] ประเภท {detection.TypeDescription} ยังไม่มีการจำลอง Action อัตโนมัติ");
            }

            // Verification phase
            Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY] ========== เริ่มตรวจสอบผล ==========");
            Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY] รอ 1.5 วินาทีให้ระบบประมวลผล...");
            await Task.Delay(1500);

            var verifyScript = @"(() => {
                const toast = document.querySelector('#jigsaw-toast, #symbol-toast, .tux-toast, [class*=""toast"" i]');
                const modal = document.querySelector('#captcha-modal-wrapper, .tt-live-modalv2-wrapper, #captcha-mount-point, #captcha-verify-container-main-page');
                const isModalHidden = !modal || window.getComputedStyle(modal).display === 'none' || modal.classList.contains('tt-live-modalv2-wrapper-exited');
                const toastText = toast ? (toast.textContent || '') : '';
                const toastVisible = toast ? (window.getComputedStyle(toast).display !== 'none') : false;
                const modalDisplay = modal ? window.getComputedStyle(modal).display : 'not-found';
                return JSON.stringify({
                    isModalHidden,
                    toastText,
                    toastVisible,
                    modalDisplay,
                    hasToast: !!toast,
                    hasModal: !!modal,
                    toastClasses: toast ? toast.className : '',
                    modalClasses: modal ? modal.className : ''
                });
            })()";

            // ตรวจสอบทั้ง iframe และ main page
            string verifyJsonFrame = "";
            string verifyJsonMain = "";
            bool checkedFrame = false;
            bool checkedMain = false;

            if (targetFrame != null && detection.Type == CaptchaType.Symbol)
            {
                try
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY] กำลังตรวจสอบใน iframe: {targetFrame.Url}");
                    verifyJsonFrame = await targetFrame.EvaluateExpressionAsync<string>(verifyScript);
                    checkedFrame = true;
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY] ✅ ตรวจสอบ iframe สำเร็จ");
                }
                catch (Exception ex)
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY] ⚠️ ตรวจสอบ iframe ล้มเหลว: {ex.Message}");
                }
            }

            try
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY] กำลังตรวจสอบใน main page: {targetPage.Url}");
                verifyJsonMain = await targetPage.EvaluateExpressionAsync<string>(verifyScript);
                checkedMain = true;
                Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY] ✅ ตรวจสอบ main page สำเร็จ");
            }
            catch (Exception ex)
            {
                Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY] ⚠️ ตรวจสอบ main page ล้มเหลว: {ex.Message}");
            }

            bool isResolved = false;
            string feedbackMsg = "";

            // ตรวจสอบผลจาก iframe
            if (!string.IsNullOrEmpty(verifyJsonFrame))
            {
                try
                {
                    using var doc = JsonDocument.Parse(verifyJsonFrame);
                    bool isModalHidden = doc.RootElement.TryGetProperty("isModalHidden", out var h) && h.GetBoolean();
                    string toastText = doc.RootElement.TryGetProperty("toastText", out var t) ? t.GetString() ?? "" : "";
                    bool toastVisible = doc.RootElement.TryGetProperty("toastVisible", out var tv) && tv.GetBoolean();
                    string modalDisplay = doc.RootElement.TryGetProperty("modalDisplay", out var md) ? md.GetString() ?? "" : "";
                    bool hasToast = doc.RootElement.TryGetProperty("hasToast", out var ht) && ht.GetBoolean();
                    bool hasModal = doc.RootElement.TryGetProperty("hasModal", out var hm) && hm.GetBoolean();

                    Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY FRAME] hasModal={hasModal}, modalDisplay={modalDisplay}, isModalHidden={isModalHidden}");
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY FRAME] hasToast={hasToast}, toastVisible={toastVisible}, toastText='{toastText}'");

                    if (isModalHidden || toastText.Contains("Success", StringComparison.OrdinalIgnoreCase) || toastText.Contains("สำเร็จ", StringComparison.OrdinalIgnoreCase))
                    {
                        isResolved = true;
                        feedbackMsg = toastText;
                        Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY FRAME] ✅ ตรวจพบว่าสำเร็จจาก iframe!");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY FRAME] ⚠️ Parse JSON ล้มเหลว: {ex.Message}");
                }
            }

            // ตรวจสอบผลจาก main page
            if (!string.IsNullOrEmpty(verifyJsonMain))
            {
                try
                {
                    using var doc = JsonDocument.Parse(verifyJsonMain);
                    bool isModalHidden = doc.RootElement.TryGetProperty("isModalHidden", out var h) && h.GetBoolean();
                    string toastText = doc.RootElement.TryGetProperty("toastText", out var t) ? t.GetString() ?? "" : "";
                    bool toastVisible = doc.RootElement.TryGetProperty("toastVisible", out var tv) && tv.GetBoolean();
                    string modalDisplay = doc.RootElement.TryGetProperty("modalDisplay", out var md) ? md.GetString() ?? "" : "";
                    bool hasToast = doc.RootElement.TryGetProperty("hasToast", out var ht) && ht.GetBoolean();
                    bool hasModal = doc.RootElement.TryGetProperty("hasModal", out var hm) && hm.GetBoolean();

                    Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY MAIN] hasModal={hasModal}, modalDisplay={modalDisplay}, isModalHidden={isModalHidden}");
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY MAIN] hasToast={hasToast}, toastVisible={toastVisible}, toastText='{toastText}'");

                    if (isModalHidden || toastText.Contains("Success", StringComparison.OrdinalIgnoreCase) || toastText.Contains("สำเร็จ", StringComparison.OrdinalIgnoreCase))
                    {
                        isResolved = true;
                        if (!string.IsNullOrEmpty(toastText)) feedbackMsg = toastText;
                        Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY MAIN] ✅ ตรวจพบว่าสำเร็จจาก main page!");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY MAIN] ⚠️ Parse JSON ล้มเหลว: {ex.Message}");
                }
            }

            stopwatch.Stop();
            solveResult.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;

            Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY] ========== สรุปผลการตรวจสอบ ==========");
            Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY] isResolved: {isResolved}");
            Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY] feedbackMsg: '{feedbackMsg}'");
            Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [VERIFY] ใช้เวลารวม: {solveResult.ElapsedSeconds:F2}s");

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
                status = isSuccess ? "VERIFIED_SUCCESS" : "VERIFIED_FAILED",
                feedbackText = feedback,
                feedback = feedback,
                elapsedSeconds = durationTotalSec,
                durationTotalSec = durationTotalSec,
                appVersion = AppConstants.CurrentVersion
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("X-App-Version", AppConstants.CurrentVersion);
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

    public record SolverParsedResult(
        bool Solved,
        double TargetX,
        List<(double X, double Y)> ClickPoints,
        string? CaptchaId,
        double? ImageWidth = null,
        double? ImageHeight = null,
        double? DisplayWidth = null,
        double? DisplayHeight = null,
        double? ScaleX = null,
        double? ScaleY = null,
        double? RawTargetX = null,
        List<(double X, double Y)>? RawClickPoints = null
    );

    internal static SolverParsedResult ParseSolverApiResponse(
        CaptchaType type,
        string jsonText,
        Action<string> log)
    {
        double targetX = 228;
        var clickPoints = new List<(double X, double Y)>();
        var rawClickPoints = new List<(double X, double Y)>();
        string? captchaId = null;
        double? imageWidth = null;
        double? imageHeight = null;
        double? displayWidth = null;
        double? displayHeight = null;
        double? scaleX = null;
        double? scaleY = null;
        double? rawTargetX = null;

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            if (root.TryGetProperty("captchaId", out var cidElem))
            {
                captchaId = cidElem.GetString();
            }
            else if (root.TryGetProperty("captcha_id", out var cid2Elem))
            {
                captchaId = cid2Elem.GetString();
            }
            else if (root.TryGetProperty("id", out var idElem))
            {
                captchaId = idElem.GetString();
            }

            // Check if there is an inner "result" or "data" object
            JsonElement dataElem = root;
            if (root.TryGetProperty("result", out var rElem) && rElem.ValueKind == JsonValueKind.Object)
            {
                dataElem = rElem;
            }
            else if (root.TryGetProperty("data", out var dElem) && dElem.ValueKind == JsonValueKind.Object)
            {
                dataElem = dElem;
            }

            // Read dimension & scale metadata
            if (dataElem.TryGetProperty("image_width", out var iwElem)) imageWidth = iwElem.GetDouble();
            else if (root.TryGetProperty("image_width", out var iwRoot)) imageWidth = iwRoot.GetDouble();

            if (dataElem.TryGetProperty("image_height", out var ihElem)) imageHeight = ihElem.GetDouble();
            else if (root.TryGetProperty("image_height", out var ihRoot)) imageHeight = ihRoot.GetDouble();

            if (dataElem.TryGetProperty("display_width", out var dwElem)) displayWidth = dwElem.GetDouble();
            else if (root.TryGetProperty("display_width", out var dwRoot)) displayWidth = dwRoot.GetDouble();

            if (dataElem.TryGetProperty("display_height", out var dhElem)) displayHeight = dhElem.GetDouble();
            else if (root.TryGetProperty("display_height", out var dhRoot)) displayHeight = dhRoot.GetDouble();

            if (dataElem.TryGetProperty("scale_x", out var sxElem)) scaleX = sxElem.GetDouble();
            else if (root.TryGetProperty("scale_x", out var sxRoot)) scaleX = sxRoot.GetDouble();

            if (dataElem.TryGetProperty("scale_y", out var syElem)) scaleY = syElem.GetDouble();
            else if (root.TryGetProperty("scale_y", out var syRoot)) scaleY = syRoot.GetDouble();

            if (dataElem.TryGetProperty("raw_target_x", out var rtxElem)) rawTargetX = rtxElem.GetDouble();
            else if (root.TryGetProperty("raw_target_x", out var rtxRoot)) rawTargetX = rtxRoot.GetDouble();

            if (type == CaptchaType.Jigsaw)
            {
                bool found = false;
                if (dataElem.TryGetProperty("target_x", out var txElem))
                {
                    targetX = txElem.GetDouble();
                    found = true;
                }
                else if (root.TryGetProperty("target_x", out var txElem2))
                {
                    targetX = txElem2.GetDouble();
                    found = true;
                }
                else if (dataElem.TryGetProperty("position", out var posElem) && posElem.GetArrayLength() > 0)
                {
                    targetX = posElem[0].GetDouble();
                    found = true;
                }
                else if (root.TryGetProperty("position", out var posElem2) && posElem2.GetArrayLength() > 0)
                {
                    targetX = posElem2[0].GetDouble();
                    found = true;
                }

                if (found)
                {
                    string dimStr = imageWidth.HasValue ? $" [Orig: {imageWidth:F0}x{imageHeight:F0}, ScaleX: {scaleX:F4}]" : "";
                    log($"[{DateTime.Now:HH:mm:ss}] 🎯 [SOLVER API] คำนวณตำแหน่งสไลเดอร์สำเร็จ: X={targetX:F1}px{dimStr} (ID: {captchaId})");
                    return new SolverParsedResult(true, targetX, clickPoints, captchaId, imageWidth, imageHeight, displayWidth, displayHeight, scaleX, scaleY, rawTargetX, rawClickPoints);
                }
            }
            else if (type == CaptchaType.Symbol)
            {
                JsonElement spArray;
                bool hasSp = false;
                if (dataElem.TryGetProperty("symbol_positions", out spArray) && spArray.ValueKind == JsonValueKind.Array)
                {
                    hasSp = true;
                }
                else if (root.TryGetProperty("symbol_positions", out spArray) && spArray.ValueKind == JsonValueKind.Array)
                {
                    hasSp = true;
                }

                if (hasSp)
                {
                    foreach (var item in spArray.EnumerateArray())
                    {
                        if (item.GetArrayLength() >= 2)
                        {
                            clickPoints.Add((item[0].GetDouble(), item[1].GetDouble()));
                        }
                    }

                    if (dataElem.TryGetProperty("raw_symbol_positions", out var rawSpArray) && rawSpArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in rawSpArray.EnumerateArray())
                        {
                            if (item.GetArrayLength() >= 2)
                            {
                                rawClickPoints.Add((item[0].GetDouble(), item[1].GetDouble()));
                            }
                        }
                    }

                    if (clickPoints.Count > 0)
                    {
                        string dimStr = imageWidth.HasValue ? $" [Orig: {imageWidth:F0}x{imageHeight:F0}, Scale: {scaleX:F4}/{scaleY:F4}]" : "";
                        log($"[{DateTime.Now:HH:mm:ss}] 🎯 [SOLVER API] คำนวณพิกัดสัญลักษณ์สำเร็จ {clickPoints.Count} จุด{dimStr} (ID: {captchaId})");
                        return new SolverParsedResult(true, targetX, clickPoints, captchaId, imageWidth, imageHeight, displayWidth, displayHeight, scaleX, scaleY, rawTargetX, rawClickPoints);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log($"[{DateTime.Now:HH:mm:ss}] ⚠️ [SOLVER API] ไม่สามารถ Parse JSON จาก Solver Server: {ex.Message}");
        }

        return new SolverParsedResult(false, targetX, clickPoints, captchaId, imageWidth, imageHeight, displayWidth, displayHeight, scaleX, scaleY, rawTargetX, rawClickPoints);
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
            "FlowUltraTech", "WebMonitorProfile");
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
