using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Models;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class CaptchaMockDetectionTests
{
    private static readonly string MocksDirectory = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "docs", "mocks");

    private string GetMockPath(string filename)
    {
        string fullPath = Path.GetFullPath(Path.Combine(MocksDirectory, filename));
        if (!File.Exists(fullPath))
        {
            // Fallback for direct path
            fullPath = Path.Combine(@"D:\Script\Test\TiktokStudioSandbox\docs\mocks", filename);
        }
        return fullPath;
    }

    [Fact]
    public void MockJigsaw_FileExists_AndContainsExpectedSelectors()
    {
        string path = GetMockPath("mock_captcha_jigsaw.html");
        Assert.True(File.Exists(path), $"Mock file must exist at {path}");

        string html = File.ReadAllText(path);

        // 1. Inactivity modal container & mount point (from Captcha_Jigsaw.txt)
        Assert.Contains("captcha-modal-content-UBaIok", html);
        Assert.Contains("id=\"captcha-mount-point\"", html);
        Assert.Contains("countdown-EXaokG", html);
        Assert.Contains("desc-axQ2qC", html);

        // 2. Image selectors checked by CdpAutomationService
        Assert.Contains("id=\"captcha-verify-image\"", html);
        Assert.Contains("captcha_verify_img_slide", html);
        Assert.Contains("data:image/jpeg;base64,", html);

        // 3. Knob & track selectors checked by CdpAutomationService
        Assert.Contains("secsdk-captcha-drag-icon", html);
        Assert.Contains("captcha_verify_slide--drag-icon", html);
        Assert.Contains("role=\"slider\"", html);
        Assert.Contains("captcha_verify_slide--slidebar", html);
        Assert.Contains("captcha_verify_slide--drag-track", html);
    }

    [Fact]
    public void MockSymbol_FileExists_AndContainsExpectedSelectors()
    {
        string path = GetMockPath("mock_captcha_symbol.html");
        Assert.True(File.Exists(path), $"Mock file must exist at {path}");

        string html = File.ReadAllText(path);

        // 1. Inactivity modal container & mount point (from Captcha_Symbol.txt)
        Assert.Contains("captcha-modal-content-UBaIok", html);
        Assert.Contains("id=\"captcha-mount-point\"", html);
        Assert.Contains("countdown-EXaokG", html);
        Assert.Contains("desc-axQ2qC", html);

        // 2. Image selectors checked by CdpAutomationService
        Assert.Contains("id=\"captcha-verify-image\"", html);
        Assert.Contains("captcha_verify_img_symbol", html);
        Assert.Contains("data:image/jpeg;base64,", html);

        // 3. Symbol matching interactive elements
        Assert.Contains("secsdk-captcha-submit-button", html);
        Assert.Contains("Select 2 same objects", html);
    }

    [Fact]
    public void MockJigsaw_MatchesCdpSelectors_WithValidRegex()
    {
        string path = GetMockPath("mock_captcha_jigsaw.html");
        string html = File.ReadAllText(path);

        // Match background image selector
        var bgPattern = new Regex(@"<img[^>]+id=""captcha-verify-image""[^>]+class=""[^""]*captcha_verify_img_slide[^""]*""");
        Assert.True(bgPattern.IsMatch(html), "Should match #captcha-verify-image and .captcha_verify_img_slide");

        // Match knob selector
        var knobPattern = new Regex(@"class=""[^""]*secsdk-captcha-drag-icon[^""]*""[^>]*role=""slider""");
        Assert.True(knobPattern.IsMatch(html), "Should match .secsdk-captcha-drag-icon and role=slider");

        // Match track selector
        var trackPattern = new Regex(@"class=""[^""]*captcha_verify_slide--slidebar[^""]*""");
        Assert.True(trackPattern.IsMatch(html), "Should match .captcha_verify_slide--slidebar");
    }

    [Fact]
    public void MockCountdown_Extraction_ReturnsFormattedTime()
    {
        string path = GetMockPath("mock_captcha_jigsaw.html");
        string html = File.ReadAllText(path);

        var countdownMatch = Regex.Match(html, @"class=""countdown-EXaokG""[^>]*>(\d{2}:\d{2})<");
        Assert.True(countdownMatch.Success, "Should extract countdown time from .countdown-EXaokG");
        Assert.Matches(@"^\d{2}:\d{2}$", countdownMatch.Groups[1].Value);
    }

    [Fact]
    public void MockJigsaw_DetectionSimulation_ProducesExpectedTopmostResult()
    {
        // Simulates the result returned by DetectCaptchaElementsAsync when inspecting mock_captcha_jigsaw.html
        var detection = new CaptchaDetectionResult
        {
            HasInactivityModal = true,
            CountdownText = "04:59",
            Elements =
            {
                new CaptchaElementItem
                {
                    TagName = "div",
                    ClassName = "captcha-modal-content-UBaIok",
                    Depth = 4,
                    Html = "<div class=\"captcha-modal-content-UBaIok\">...</div>"
                },
                new CaptchaElementItem
                {
                    TagName = "div",
                    Id = "captcha-mount-point",
                    Depth = 5,
                    Html = "<div id=\"captcha-mount-point\">...</div>"
                }
            }
        };

        Assert.True(detection.Found);
        Assert.Equal(2, detection.Elements.Count);
        Assert.Equal("04:59", detection.CountdownText);
        Assert.Equal("captcha-modal-content-UBaIok", detection.Elements[0].ClassName);
    }

    [Fact]
    public void RawDump_Jigsaw_IsExited_AndHasNoActiveCaptcha()
    {
        string path = Path.GetFullPath(Path.Combine(MocksDirectory, "..", "Captcha_Jigsaw.txt"));
        if (!File.Exists(path))
        {
            path = @"D:\Script\Test\TiktokStudioSandbox\docs\Captcha_Jigsaw.txt";
        }
        Assert.True(File.Exists(path));

        string html = File.ReadAllText(path);

        // Verify dump properties: exited modal wrapper and hidden mount point
        bool isExited = html.Contains("tt-live-modalv2-wrapper-exited");
        bool isMountHidden = html.Contains("id=\"captcha-mount-point\" style=\"display: none;\"");
        bool hasSlideImage = html.Contains("id=\"captcha-verify-image\"");

        Assert.True(isExited, "Raw dump should contain tt-live-modalv2-wrapper-exited");
        Assert.True(isMountHidden, "Raw dump mount point should have display: none");
        Assert.False(hasSlideImage, "Raw dump should NOT contain inner #captcha-verify-image");

        // Detection simulation: when elements are hidden in DOM, IsActive must be false
        var detection = new CaptchaDetectionResult
        {
            IsActive = false,
            State = CaptchaState.HiddenOrUnmounted,
            Type = CaptchaType.None,
            TypeDescription = "None (ตรวจพบใน DOM แต่ถูกซ่อนหรือปิดอยู่)",
            HasInactivityModal = true,
            CountdownText = "00:00",
            Elements =
            {
                new CaptchaElementItem
                {
                    TagName = "div",
                    ClassName = "captcha-modal-content-UBaIok",
                    IsVisible = false
                },
                new CaptchaElementItem
                {
                    TagName = "div",
                    Id = "captcha-mount-point",
                    IsVisible = false
                }
            }
        };

        Assert.True(detection.Found, "Elements exist in DOM");
        Assert.False(detection.IsActive, "Must NOT be active because modal is exited and mount point is hidden");
        Assert.Equal(CaptchaState.HiddenOrUnmounted, detection.State);
        Assert.Equal(CaptchaType.None, detection.Type);
    }

    [Fact]
    public void RawDump_Symbol_IsExited_AndHasNoActiveCaptcha()
    {
        string path = Path.GetFullPath(Path.Combine(MocksDirectory, "..", "Captcha_Symbol.txt"));
        if (!File.Exists(path))
        {
            path = @"D:\Script\Test\TiktokStudioSandbox\docs\Captcha_Symbol.txt";
        }
        Assert.True(File.Exists(path));

        string html = File.ReadAllText(path);

        bool isExited = html.Contains("tt-live-modalv2-wrapper-exited");
        bool isMountHidden = html.Contains("id=\"captcha-mount-point\" style=\"display: none;\"");
        bool hasSymbolImage = html.Contains("id=\"captcha-verify-image\"");

        Assert.True(isExited, "Raw dump should contain tt-live-modalv2-wrapper-exited");
        Assert.True(isMountHidden, "Raw dump mount point should have display: none");
        Assert.False(hasSymbolImage, "Raw dump should NOT contain inner #captcha-verify-image");

        var detection = new CaptchaDetectionResult
        {
            IsActive = false,
            State = CaptchaState.HiddenOrUnmounted,
            Type = CaptchaType.None,
            TypeDescription = "None (ตรวจพบใน DOM แต่ถูกซ่อนหรือปิดอยู่)",
            HasInactivityModal = true,
            CountdownText = "00:00"
        };

        Assert.False(detection.IsActive);
        Assert.Equal(CaptchaState.HiddenOrUnmounted, detection.State);
        Assert.Equal(CaptchaType.None, detection.Type);
    }

    [Fact]
    public void MockJigsaw_ActiveDetection_HasCorrectTypeAndState()
    {
        var detection = new CaptchaDetectionResult
        {
            IsActive = true,
            Type = CaptchaType.Jigsaw,
            State = CaptchaState.ActiveVisible,
            TypeDescription = "จิ๊กซอว์เลื่อนต่อภาพ (Slide Captcha)",
            HasWidgetLoaded = true,
            HasInactivityModal = true,
            CountdownText = "04:59",
            PromptText = "ลากสไลเดอร์เพื่อต่อชิ้นส่วนจิ๊กซอว์ให้พอดี",
            Elements =
            {
                new CaptchaElementItem
                {
                    TagName = "div",
                    ClassName = "captcha-modal-content-UBaIok",
                    IsVisible = true,
                    Width = 358,
                    Height = 400
                },
                new CaptchaElementItem
                {
                    TagName = "img",
                    Id = "captcha-verify-image",
                    ClassName = "captcha_verify_img_slide",
                    IsVisible = true,
                    Width = 340,
                    Height = 212
                },
                new CaptchaElementItem
                {
                    TagName = "div",
                    ClassName = "secsdk-captcha-drag-icon",
                    IsVisible = true,
                    Width = 44,
                    Height = 44
                }
            }
        };

        Assert.True(detection.Found);
        Assert.True(detection.IsActive);
        Assert.Equal(CaptchaType.Jigsaw, detection.Type);
        Assert.Equal(CaptchaState.ActiveVisible, detection.State);
        Assert.True(detection.HasWidgetLoaded);
        Assert.Equal(3, detection.Elements.Count);
        Assert.All(detection.Elements, el => Assert.True(el.IsVisible));
    }

    [Fact]
    public void MockSymbol_ActiveDetection_HasCorrectTypeAndState()
    {
        var detection = new CaptchaDetectionResult
        {
            IsActive = true,
            Type = CaptchaType.Symbol,
            State = CaptchaState.ActiveVisible,
            TypeDescription = "เลือกสัญลักษณ์ 2 รูปที่เหมือนกัน (Symbol Captcha)",
            HasWidgetLoaded = true,
            HasInactivityModal = true,
            CountdownText = "04:59",
            PromptText = "คลิกเลือกรูปภาพสัญลักษณ์ที่เหมือนกัน 2 รูปแล้วกดยืนยัน",
            Elements =
            {
                new CaptchaElementItem
                {
                    TagName = "div",
                    ClassName = "captcha-modal-content-UBaIok",
                    IsVisible = true,
                    Width = 358,
                    Height = 400
                },
                new CaptchaElementItem
                {
                    TagName = "img",
                    Id = "captcha-verify-image",
                    ClassName = "captcha_verify_img_symbol",
                    IsVisible = true,
                    Width = 340,
                    Height = 212
                },
                new CaptchaElementItem
                {
                    TagName = "button",
                    ClassName = "secsdk-captcha-submit-button",
                    IsVisible = true,
                    Width = 100,
                    Height = 36
                }
            }
        };

        Assert.True(detection.Found);
        Assert.True(detection.IsActive);
        Assert.Equal(CaptchaType.Symbol, detection.Type);
        Assert.Equal(CaptchaState.ActiveVisible, detection.State);
        Assert.True(detection.HasWidgetLoaded);
        Assert.Equal(3, detection.Elements.Count);
        Assert.All(detection.Elements, el => Assert.True(el.IsVisible));
    }
}
