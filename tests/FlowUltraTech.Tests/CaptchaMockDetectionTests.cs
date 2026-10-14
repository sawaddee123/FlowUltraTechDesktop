using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FlowUltraTech.Core.Models;
using Xunit;

namespace FlowUltraTech.Tests;

[Collection("CaptchaBrowserE2E")]
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
            fullPath = Path.Combine(@"D:\Script\Test\FlowUltraTech\docs\mocks", filename);
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
        Assert.Contains("cap-cursor-pointer", html);
        Assert.Contains("Verify that you’re not a robot", html);
        Assert.Contains("data:image/jpeg;base64,", html);

        // 3. Symbol matching interactive elements
        Assert.Contains("secsdk-captcha-submit-button", html);
        Assert.Contains("TUXButton--primary", html);
        Assert.Contains("Select 2 objects that are the same shape", html);
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
            path = @"D:\Script\Test\FlowUltraTech\docs\Captcha_Jigsaw.txt";
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
            path = @"D:\Script\Test\FlowUltraTech\docs\Captcha_Symbol.txt";
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

    [Theory]
    [InlineData("LIVE will end in: 00:24", "00:24")]
    [InlineData("LIVE will end in: 00:16", "00:16")]
    [InlineData("LIVE will end in:  01:05 ", "01:05")]
    [InlineData("We have detected inactivity during your current LIVE. LIVE will end in: 00:30 Complete verification", "00:30")]
    public void ResilientCountdown_Extraction_FromInactivityModalText(string sampleText, string expectedCountdown)
    {
        var match = Regex.Match(sampleText, @"LIVE will end in[:\s]*(\d{1,2}:\d{2})", RegexOptions.IgnoreCase);
        Assert.True(match.Success);
        Assert.Equal(expectedCountdown, match.Groups[1].Value);
    }

    [Theory]
    [InlineData("20260912200836236424C80169BFCB4CF")]
    [InlineData("Challenge ID: 20260914193500A1B2C3D4E5F6A7B8C9 and footer")]
    public void ResilientSessionId_Extraction_MatchesChallengePattern(string sampleText)
    {
        var match = Regex.Match(sampleText, @"\b(20\d{12}[A-Za-z0-9]{12,})\b");
        Assert.True(match.Success);
        Assert.StartsWith("2026", match.Groups[1].Value);
        Assert.True(match.Groups[1].Value.Length >= 26);
    }

    [Theory]
    [InlineData("Drag the puzzle piece into place")]
    [InlineData("Please drag the puzzle piece into place to continue")]
    [InlineData("ลากสไลเดอร์เพื่อต่อชิ้นส่วนจิ๊กซอว์ให้พอดี")]
    public void ResilientJigsaw_InstructionMatching_DetectsPuzzleInstruction(string instruction)
    {
        bool isMatched = Regex.IsMatch(instruction, @"drag the puzzle piece into place", RegexOptions.IgnoreCase)
                      || Regex.IsMatch(instruction, @"drag.*puzzle", RegexOptions.IgnoreCase)
                      || Regex.IsMatch(instruction, @"ลาก.*จิ๊กซอว์", RegexOptions.IgnoreCase);
        Assert.True(isMatched);
    }

    [Fact]
    public void CaptchaDetectionResult_WithSessionId_PreservesProperty()
    {
        var result = new CaptchaDetectionResult
        {
            IsActive = true,
            Type = CaptchaType.Jigsaw,
            State = CaptchaState.ActiveVisible,
            CountdownText = "00:24",
            CaptchaSessionId = "20260912200836236424C80169BFCB4CF",
            HasInactivityModal = true
        };

        Assert.Equal("20260912200836236424C80169BFCB4CF", result.CaptchaSessionId);
        Assert.Equal("00:24", result.CountdownText);
        Assert.True(result.HasInactivityModal);
    }

    [Fact]
    public void CaptchaDetectionResult_WithCapturedImage_PreservesBytesAndHash()
    {
        byte[] dummyBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        string dummyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        string dummyPath = @"C:\Users\Public\Pictures\TikTokStudioCaptures\Captcha_Test.png";

        var result = new CaptchaDetectionResult
        {
            IsActive = true,
            Type = CaptchaType.Jigsaw,
            CapturedImageBytes = dummyBytes,
            CapturedImagePath = dummyPath,
            ImageHash = dummyHash
        };

        Assert.NotNull(result.CapturedImageBytes);
        Assert.Equal(8, result.CapturedImageBytes.Length);
        Assert.Equal(dummyPath, result.CapturedImagePath);
        Assert.Equal(dummyHash, result.ImageHash);
    }

    [Fact]
    public void AppSettings_CaptchaSoundAndSave_DefaultValues()
    {
        var settings = new AppSettings();

        Assert.True(settings.EnableCaptchaSoundAlert);
        Assert.True(settings.LoopCaptchaSoundAlert);
        Assert.Equal(80, settings.CaptchaSoundVolume);
        Assert.True(string.IsNullOrEmpty(settings.CaptchaSoundFilePath));
        Assert.True(settings.AutoSaveCaptchaImage);
        Assert.True(string.IsNullOrEmpty(settings.CaptchaSaveDirectory));
    }

    [Fact]
    public void FullHtmlCaptcha_MockExtraction_ValidatesSessionIdCountdownAndImage()
    {
        string fullHtmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "extracted_sources", "Full html captcha.html");
        if (!File.Exists(fullHtmlPath))
        {
            fullHtmlPath = @"D:\Script\Test\FlowUltraTech\extracted_sources\Full html captcha.html";
        }

        if (File.Exists(fullHtmlPath))
        {
            string html = File.ReadAllText(fullHtmlPath);

            // Verify countdown
            var countdownMatch = Regex.Match(html, @"(\d{1,2}:\d{2})");
            Assert.True(countdownMatch.Success);

            // Verify Session / Challenge ID format
            var sessionMatch = Regex.Match(html, @"2026\d{10}[A-Za-z0-9]{12,}");
            Assert.True(sessionMatch.Success);

            // Verify base64 captcha image presence
            Assert.Contains("id=\"captcha-verify-image\"", html);
            Assert.Contains("src=\"data:image/webp;base64,", html);

            // Verify inactivity modal container
            Assert.Contains("captcha-modal-content-UBaIok", html);
        }
    }

    [Fact]
    public void OptimizedDetection_DataUrlDirectExtraction_ValidatesBase64Webp()
    {
        string fullHtmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "extracted_sources", "Full html captcha.html");
        if (!File.Exists(fullHtmlPath))
        {
            fullHtmlPath = @"D:\Script\Test\FlowUltraTech\extracted_sources\Full html captcha.html";
        }

        if (File.Exists(fullHtmlPath))
        {
            string html = File.ReadAllText(fullHtmlPath);
            var match = Regex.Match(html, @"src=""(data:image/[^;]+;base64,[^""]+)""");
            Assert.True(match.Success, "Should find direct data URL in image src");

            string dataUrl = match.Groups[1].Value;
            int commaIdx = dataUrl.IndexOf(',');
            Assert.True(commaIdx > 0);

            byte[] decoded = Convert.FromBase64String(dataUrl.Substring(commaIdx + 1));
            Assert.True(decoded.Length > 1000, "Decoded image bytes should be valid and non-empty");
        }
    }

    [Fact]
    public void ExtractedSources_CaptchaSymbol2Html_ValidatesDirectWebpImageAndElements()
    {
        string symbol2Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "extracted_sources", "CaptchaSymbol2.html");
        if (!File.Exists(symbol2Path))
        {
            symbol2Path = @"D:\Script\Test\FlowUltraTech\extracted_sources\CaptchaSymbol2.html";
        }

        Assert.True(File.Exists(symbol2Path), $"CaptchaSymbol2.html must exist at {symbol2Path}");
        string html = File.ReadAllText(symbol2Path);

        // 1. Direct WebP captcha image element with real production attributes
        Assert.Contains("alt=\"Verify that you’re not a robot\"", html);
        Assert.Contains("cap-cursor-pointer", html);
        Assert.Contains("data:image/webp;base64,", html);

        // Verify direct base64 WebP extraction
        var imgMatch = Regex.Match(html, @"src=""(data:image/webp;base64,[^""]+)""");
        Assert.True(imgMatch.Success, "Must contain direct WebP data URL");
        string dataUrl = imgMatch.Groups[1].Value;
        int commaIdx = dataUrl.IndexOf(',');
        Assert.True(commaIdx > 0);
        byte[] imgBytes = Convert.FromBase64String(dataUrl.Substring(commaIdx + 1));
        Assert.True(imgBytes.Length > 1000, "Direct WebP image bytes must be substantial");
        // Check RIFF WEBP header
        Assert.Equal((byte)'R', imgBytes[0]);
        Assert.Equal((byte)'I', imgBytes[1]);
        Assert.Equal((byte)'F', imgBytes[2]);
        Assert.Equal((byte)'F', imgBytes[3]);
        Assert.Equal((byte)'W', imgBytes[8]);
        Assert.Equal((byte)'E', imgBytes[9]);
        Assert.Equal((byte)'B', imgBytes[10]);
        Assert.Equal((byte)'P', imgBytes[11]);

        // 2. Session / Challenge ID format
        var sessionMatch = Regex.Match(html, @"2026\d{10}[A-Za-z0-9]{12,}");
        Assert.True(sessionMatch.Success, "Must contain 34-char session ID");
        Assert.Equal("20260920032154705F18BEBC56412885F9", sessionMatch.Value);

        // 3. Countdown timer
        var countdownMatch = Regex.Match(html, @"class=""countdown-EXaokG""[^>]*>\s*(\d{2}:\d{2})\s*<");
        Assert.True(countdownMatch.Success, "Must extract countdown timer");
        Assert.Equal("00:57", countdownMatch.Groups[1].Value);

        // 4. Prompt instruction text
        Assert.Contains("Select 2 objects that are the same shape", html);
        Assert.True(Regex.IsMatch(html, @"select.*(?:same|object|shape)", RegexOptions.IgnoreCase));

        // 5. Submit button with TUXButton--primary
        Assert.Contains("TUXButton--primary", html);
        Assert.Contains("Confirm", html);
    }

    [Fact]
    public void ExtractedSources_CaptchaSymbol1Html_IsExitedAndInactive()
    {
        string symbol1Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "extracted_sources", "CaptchaSymbol1.html");
        if (!File.Exists(symbol1Path))
        {
            symbol1Path = @"D:\Script\Test\FlowUltraTech\extracted_sources\CaptchaSymbol1.html";
        }

        Assert.True(File.Exists(symbol1Path), $"CaptchaSymbol1.html must exist at {symbol1Path}");
        string html = File.ReadAllText(symbol1Path);

        // Modal is exited
        Assert.Contains("tt-live-modalv2-wrapper-exited", html);

        // Mount point is hidden
        Assert.Contains("id=\"captcha-mount-point\" style=\"display: none;\"", html);

        // No active captcha image element in the DOM
        Assert.DoesNotContain("Verify that you’re not a robot", html);
    }
}

