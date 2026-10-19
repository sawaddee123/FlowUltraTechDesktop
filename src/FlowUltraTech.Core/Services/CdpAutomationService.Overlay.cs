using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using PuppeteerSharp;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public partial class CdpAutomationService
{
    private static readonly JsonSerializerOptions OverlayScriptJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed class OverlayDomStepEvalResult
    {
        public bool Ok { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
    }

    public string BuildOverlayInjectScript(string overlayUrl, string step = "full")
    {
        return CreateOverlayInjectScript(overlayUrl, step);
    }

    public static string CreateOverlayInjectScript(string overlayUrl, string step = "full")
    {
        string safeUrlJson = JsonSerializer.Serialize(overlayUrl ?? string.Empty, OverlayScriptJsonOptions);
        string safeStepJson = JsonSerializer.Serialize((step ?? "full").Trim().ToLowerInvariant(), OverlayScriptJsonOptions);

        return $$"""
(() => {
    const targetUrl = {{safeUrlJson}};
    const mode = {{safeStepJson}};

    function isVisible(el) {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return false;
        const style = window.getComputedStyle(el);
        return style.display !== 'none' && style.visibility !== 'hidden' && parseFloat(style.opacity || '1') > 0.02;
    }

    function setReactInputValue(inputEl, value) {
        inputEl.focus();
        const proto = window.HTMLInputElement ? window.HTMLInputElement.prototype : Object.getPrototypeOf(inputEl);
        const descriptor = Object.getOwnPropertyDescriptor(proto, 'value');
        if (descriptor && typeof descriptor.set === 'function') {
            descriptor.set.call(inputEl, value);
        } else {
            inputEl.value = value;
        }
        inputEl.dispatchEvent(new Event('input', { bubbles: true }));
        inputEl.dispatchEvent(new Event('change', { bubbles: true }));
        inputEl.dispatchEvent(new KeyboardEvent('keyup', { bubbles: true, key: 'Enter' }));
    }

    function findUrlInput() {
        const selectors = [
            'input[placeholder*="http" i]',
            'input[placeholder*="tiktok" i]',
            'input[placeholder*="url" i]',
            'input[placeholder*="link" i]',
            'input[placeholder*="ลิงก์" i]',
            '[role="dialog"] input[type="text"]',
            '[class*="modal" i] input[type="text"]',
            '[class*="dialog" i] input[type="text"]',
            'input[type="url"]'
        ];
        for (const sel of selectors) {
            const nodes = Array.from(document.querySelectorAll(sel));
            const found = nodes.find(isVisible);
            if (found) return found;
        }
        return null;
    }

    function findElementByKeywords(keywords, root) {
        const scope = root || document;
        const candidates = Array.from(scope.querySelectorAll('button, [role="button"], a, div, span, li'));
        let best = null;
        let bestLen = 999999;

        for (const el of candidates) {
            if (!isVisible(el)) continue;
            const text = (el.innerText || el.textContent || '').trim();
            const aria = (el.getAttribute('aria-label') || el.getAttribute('title') || '').trim();
            const combined = (text + ' ' + aria).toLowerCase();
            if (!combined) continue;

            for (const kw of keywords) {
                const kwLower = kw.toLowerCase();
                if (text.toLowerCase() === kwLower || aria.toLowerCase() === kwLower || (combined.includes(kwLower) && text.length <= 48)) {
                    if (text.length < bestLen) {
                        best = el;
                        bestLen = Math.max(1, text.length);
                    }
                }
            }
        }
        return best;
    }

    function triggerClick(el) {
        if (!el) return false;
        const clickable = el.closest('button, [role="button"], a, [class*="card" i], [class*="item" i], [class*="btn" i]') || el;
        clickable.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
        clickable.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true }));
        clickable.click();
        return true;
    }

    if (mode === 'open_add_source' || mode === 'step1') {
        if (findUrlInput()) {
            return { ok: true, status: 'already_input_open', detail: 'URL input already visible' };
        }
        const linkCard = findElementByKeywords(['Link', 'ลิงก์', 'Webpage', 'Browser source']);
        if (linkCard && document.querySelector('[role="dialog"], [class*="modal" i]')) {
            return { ok: true, status: 'already_modal_open', detail: 'Add Source modal already open' };
        }
        const addSourceBtn = findElementByKeywords([
            'Add source',
            'เพิ่มซอร์ส',
            'เพิ่มแหล่งที่มา',
            '+ Add source',
            '+ เพิ่มซอร์ส'
        ]);
        if (addSourceBtn && triggerClick(addSourceBtn)) {
            return { ok: true, status: 'clicked_add_source', detail: 'Clicked Add source button' };
        }
        return { ok: false, status: 'add_source_not_found', detail: 'Add source button not found in frame' };
    }

    if (mode === 'select_link' || mode === 'step2') {
        if (findUrlInput()) {
            return { ok: true, status: 'already_input_open', detail: 'URL input already visible' };
        }
        const modalRoot = document.querySelector('[role="dialog"], [class*="modal" i], [class*="drawer" i]') || document;
        const linkOption = findElementByKeywords(['Link', 'ลิงก์', 'Webpage', 'Browser', 'เว็บไซต์'], modalRoot);
        if (linkOption && triggerClick(linkOption)) {
            return { ok: true, status: 'clicked_link_option', detail: 'Clicked Link source option' };
        }
        return { ok: false, status: 'link_option_not_found', detail: 'Link option not found in frame' };
    }

    // Default / mode === 'fill_and_confirm' / 'input' / 'full'
    const urlInput = findUrlInput();
    if (!urlInput) {
        return { ok: false, status: 'url_input_not_found', detail: 'URL input field not found' };
    }

    setReactInputValue(urlInput, targetUrl);

    const dialogScope = urlInput.closest('[role="dialog"], [class*="modal" i], [class*="dialog" i], form') || document;
    const confirmBtn = findElementByKeywords([
        'Add source',
        'เพิ่มซอร์ส',
        'Add',
        'เพิ่ม',
        'Confirm',
        'ตกลง',
        'Save',
        'บันทึก',
        'Apply',
        'Done'
    ], dialogScope) || dialogScope.querySelector('button[type="submit"], button[class*="primary" i]');

    if (confirmBtn && isVisible(confirmBtn)) {
        triggerClick(confirmBtn);
        return { ok: true, status: 'injected_and_confirmed', detail: 'URL filled and confirm clicked' };
    }

    return { ok: true, status: 'injected_input_only', detail: 'URL filled via React native setter' };
})()
""";
    }

    public async Task<OverlayInjectResult> InjectOverlaySourceAsync(
        int cdpPort,
        string overlayUrl,
        Action<string>? logCallback = null)
    {
        string trimmedUrl = (overlayUrl ?? string.Empty).Trim();
        if (cdpPort is <= 0 or > 65535 ||
            string.IsNullOrWhiteSpace(trimmedUrl) ||
            (!trimmedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
             !trimmedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            const string validationMsg = "ลิงก์ Overlay หรือพอร์ต CDP ไม่ถูกต้อง";
            logCallback?.Invoke(validationMsg);
            return new OverlayInjectResult
            {
                Success = false,
                Message = validationMsg,
                InjectedUrl = trimmedUrl,
                StepFailed = "Validation"
            };
        }

        var logger = _logManager.TryGetLoggerByPort(cdpPort);
        logger?.Info("CDP-Overlay", $"Starting One-Click Overlay Auto-Inject on port {cdpPort} -> {trimmedUrl}");
        logCallback?.Invoke($"กำลังเชื่อมต่อ TikTok LIVE Studio (พอร์ต {cdpPort})...");

        try
        {
            bool hasActiveBrowser = _connectedBrowsers.TryGetValue(cdpPort, out var existingBrowser) && !existingBrowser.IsClosed;
            if (!hasActiveBrowser)
            {
                bool portAlive = await CheckConnectionAsync(cdpPort).ConfigureAwait(false);
                if (!portAlive)
                {
                    const string offlineMsg = "เชื่อมต่อ TikTok LIVE Studio ไม่สำเร็จ • เปิดบัญชีแล้วลองใหม่";
                    logCallback?.Invoke(offlineMsg);
                    return new OverlayInjectResult
                    {
                        Success = false,
                        Message = offlineMsg,
                        InjectedUrl = trimmedUrl,
                        StepFailed = "ConnectCdp"
                    };
                }
            }

            var browser = await GetOrConnectBrowserAsync(cdpPort).ConfigureAwait(false);
            if (browser == null)
            {
                const string connectFailMsg = "เชื่อมต่อ TikTok LIVE Studio ไม่สำเร็จ • เปิดบัญชีแล้วลองใหม่";
                logCallback?.Invoke(connectFailMsg);
                return new OverlayInjectResult
                {
                    Success = false,
                    Message = connectFailMsg,
                    InjectedUrl = trimmedUrl,
                    StepFailed = "ConnectCdp"
                };
            }

            var frames = await GetAllCandidateFramesAsync(browser).ConfigureAwait(false);
            if (frames.Count == 0)
            {
                const string noFrameMsg = "ไม่พบหน้าต่าง TikTok LIVE Studio ที่พร้อมใช้งาน • เปิดบัญชีแล้วลองใหม่";
                logCallback?.Invoke(noFrameMsg);
                return new OverlayInjectResult
                {
                    Success = false,
                    Message = noFrameMsg,
                    InjectedUrl = trimmedUrl,
                    StepFailed = "ConnectCdp"
                };
            }

            // Step 1: Open Add Source dialog if not already open
            logCallback?.Invoke("กำลังค้นหาปุ่ม Add source บนฉากไลฟ์...");
            var step1Result = await EvaluateOverlayStepAcrossFramesAsync(frames, trimmedUrl, "open_add_source").ConfigureAwait(false);
            if (step1Result is { Ok: true, Status: "clicked_add_source" })
            {
                await Task.Delay(450).ConfigureAwait(false);
                frames = await GetAllCandidateFramesAsync(browser).ConfigureAwait(false);
            }

            // Step 2: Select Link source option inside Add Source modal
            logCallback?.Invoke("กำลังเลือกซอร์สประเภท Link...");
            var step2Result = await EvaluateOverlayStepAcrossFramesAsync(frames, trimmedUrl, "select_link").ConfigureAwait(false);
            if (step2Result is { Ok: true, Status: "clicked_link_option" })
            {
                await Task.Delay(450).ConfigureAwait(false);
                frames = await GetAllCandidateFramesAsync(browser).ConfigureAwait(false);
            }

            // Step 3 & 4: Fill Overlay URL using React native setter & click Confirm
            logCallback?.Invoke("กำลังกรอกลิงก์นาฬิกาและยืนยันเข้าฉากไลฟ์...");
            var step3Result = await EvaluateOverlayStepAcrossFramesAsync(frames, trimmedUrl, "fill_and_confirm").ConfigureAwait(false);
            if (step3Result is { Ok: true })
            {
                const string okMsg = "ส่งนาฬิกาเข้าฉากไลฟ์สำเร็จ";
                logCallback?.Invoke(okMsg);
                logger?.Info("CDP-Overlay", $"Overlay injected successfully on port {cdpPort}: {step3Result.Status}");
                return new OverlayInjectResult
                {
                    Success = true,
                    Message = okMsg,
                    InjectedUrl = trimmedUrl
                };
            }

            string failedStep = step1Result is { Ok: true }
                ? (step2Result is { Ok: true } ? "FillUrlAndConfirm" : "SelectLinkSource")
                : "FindAddSource";

            const string notFoundMsg = "ไม่พบปุ่ม Add source บนหน้าจอปัจจุบัน • เปิดหน้าเตรียมไลฟ์ใน TikTok Studio แล้วลองใหม่";
            logCallback?.Invoke(notFoundMsg);
            return new OverlayInjectResult
            {
                Success = false,
                Message = notFoundMsg,
                InjectedUrl = trimmedUrl,
                StepFailed = failedStep
            };
        }
        catch (Exception ex)
        {
            logger?.Warn("CDP-Overlay", $"InjectOverlaySourceAsync error on port {cdpPort}: {ex.Message}");
            const string errMsg = "ส่งเข้าฉากอัตโนมัติไม่สำเร็จ • คัดลอกลิงก์ไปวางในเมนู Link ได้ทันที";
            logCallback?.Invoke(errMsg);
            return new OverlayInjectResult
            {
                Success = false,
                Message = errMsg,
                InjectedUrl = trimmedUrl,
                StepFailed = "Exception"
            };
        }
    }

    private static async Task<List<IFrame>> GetAllCandidateFramesAsync(IBrowser browser)
    {
        var result = new List<IFrame>();
        var pages = await browser.PagesAsync().ConfigureAwait(false);
        if (pages == null)
        {
            return result;
        }

        foreach (var page in pages)
        {
            try
            {
                if (page.MainFrame != null)
                {
                    result.Add(page.MainFrame);
                }

                if (page.Frames != null)
                {
                    foreach (var frame in page.Frames)
                    {
                        if (frame != null && !result.Contains(frame))
                        {
                            result.Add(frame);
                        }
                    }
                }
            }
            catch
            {
                // Ignore detached page/frame
            }
        }

        return result;
    }

    private async Task<OverlayDomStepEvalResult?> EvaluateOverlayStepAcrossFramesAsync(
        List<IFrame> frames,
        string overlayUrl,
        string step)
    {
        string script = BuildOverlayInjectScript(overlayUrl, step);
        OverlayDomStepEvalResult? lastNonSuccess = null;

        foreach (var frame in frames)
        {
            try
            {
                var raw = await frame.EvaluateExpressionAsync<JsonElement>(script).ConfigureAwait(false);
                var parsed = JsonSerializer.Deserialize<OverlayDomStepEvalResult>(raw.GetRawText(), OverlayScriptJsonOptions);
                if (parsed == null)
                {
                    continue;
                }

                if (parsed.Ok)
                {
                    return parsed;
                }

                lastNonSuccess = parsed;
            }
            catch
            {
                // Ignore cross-origin or detached frame evaluation errors
            }
        }

        return lastNonSuccess;
    }
}
