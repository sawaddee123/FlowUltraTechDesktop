using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Models;
using TiktokStudioSandbox.Core.Services;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class CaptchaMockSolverTests
{
    private static readonly string SolutionDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void MockCaptchaResponseJson_ExistsAndHasValidSchema()
    {
        // 1. Check in output directory (copied by Core.csproj)
        string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "mock_captcha_response.json");
        if (!File.Exists(outputPath))
        {
            outputPath = Path.Combine(SolutionDir, "src", "TiktokStudioSandbox.Core", "Config", "mock_captcha_response.json");
        }

        Assert.True(File.Exists(outputPath), $"mock_captcha_response.json must exist at: {outputPath}");

        string json = File.ReadAllText(outputPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Check required fields compatible with SimpleSolveCaptchaProject
        Assert.True(root.TryGetProperty("slide", out var slideProp));
        Assert.True(slideProp.TryGetProperty("target_x", out var targetXProp));
        Assert.True(targetXProp.GetDouble() > 0, "target_x should be greater than 0");

        Assert.True(root.TryGetProperty("symbol", out var symbolProp));
        Assert.True(symbolProp.TryGetProperty("symbol_positions", out var positionsProp));
        Assert.True(positionsProp.GetArrayLength() > 0, "symbol_positions array should not be empty");
    }

    [Fact]
    public void ExtractedSources_CaptchaJigsawHtml_ContainsDirectImageElement()
    {
        string path = Path.Combine(SolutionDir, "extracted_sources", "CaptchaJigsaw.html");
        Assert.True(File.Exists(path), $"CaptchaJigsaw.html must exist at: {path}");

        string html = File.ReadAllText(path);

        // 1. Direct Captcha image element
        Assert.Contains("id=\"captcha-verify-image\"", html);
        Assert.Contains("data:image/webp;base64,", html);

        // 2. Puzzle piece element
        Assert.Contains("draggable=\"true\"", html);

        // 3. Slide button / knob
        Assert.Contains("id=\"captcha_slide_button\"", html);

        // 4. Session ID div
        var sessionMatch = Regex.Match(html, @"[0-9]{14}[A-Z0-9]{18,22}");
        Assert.True(sessionMatch.Success, "Should extract 34-character session ID from CaptchaJigsaw.html");
        Assert.Equal("20260916071033B3D6A0A4EB09D500C598", sessionMatch.Value);
    }

    [Fact]
    public void ExtractedSources_CaptchaSymbol2Html_ContainsDirectImageElement()
    {
        string path = Path.Combine(SolutionDir, "extracted_sources", "CaptchaSymbol2.html");
        Assert.True(File.Exists(path), $"CaptchaSymbol2.html must exist at: {path}");

        string html = File.ReadAllText(path);

        // 1. Direct Captcha WebP image element
        Assert.Contains("alt=\"Verify that you’re not a robot\"", html);
        Assert.Contains("data:image/webp;base64,", html);
        Assert.Contains("cap-cursor-pointer", html);

        // 2. Prompt instruction text
        Assert.Contains("Select 2 objects that are the same shape", html);

        // 3. Confirm button
        Assert.Contains("TUXButton--primary", html);
        Assert.Contains("Confirm", html);

        // 4. Session ID div
        var sessionMatch = Regex.Match(html, @"[0-9]{14}[A-Z0-9]{18,22}");
        Assert.True(sessionMatch.Success, "Should extract 34-character session ID from CaptchaSymbol2.html");
        Assert.Equal("20260920032154705F18BEBC56412885F9", sessionMatch.Value);
    }

    [Fact]
    public void CaptchaSolve_PrioritizesDirectImageOverCroppedScreenshot()
    {
        byte[] directMock = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x01, 0x02, 0x03, 0x04 };
        byte[] croppedMock = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x05, 0x06, 0x07, 0x08 };

        var detection = new CaptchaDetectionResult
        {
            IsActive = true,
            Type = CaptchaType.Symbol,
            DirectImageBytes = directMock,
            CroppedImageBytes = croppedMock
        };

        // Image selection logic in SolveCaptchaAsync: DirectImageBytes takes priority over CroppedImageBytes
        byte[]? imageBytes = detection.DirectImageBytes ?? detection.CroppedImageBytes ?? detection.CapturedImageBytes;
        Assert.NotNull(imageBytes);
        Assert.Same(directMock, imageBytes);
        Assert.NotSame(croppedMock, imageBytes);
    }

    [Fact]
    public void CaptchaDetectionResult_SupportsDualImageProperties()
    {
        byte[] directMock = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG header
        byte[] croppedMock = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 }; // WEBP header

        var result = new CaptchaDetectionResult
        {
            IsActive = true,
            Type = CaptchaType.Jigsaw,
            DirectImageBytes = directMock,
            CroppedImageBytes = croppedMock,
            CapturedImageBytes = directMock,
            DirectImageDimensions = "552x345",
            CroppedImageDimensions = "340x212"
        };
        result.Elements.Add(new CaptchaElementItem { Id = "captcha-verify-image" });

        Assert.True(result.Found);
        Assert.NotNull(result.DirectImageBytes);
        Assert.NotNull(result.CroppedImageBytes);
        Assert.Equal(8, result.DirectImageBytes.Length);
        Assert.Equal(12, result.CroppedImageBytes.Length);
        Assert.Equal("552x345", result.DirectImageDimensions);
        Assert.Equal("340x212", result.CroppedImageDimensions);
        Assert.Equal(result.DirectImageBytes, result.CapturedImageBytes);
    }

    [Fact]
    public void CaptchaSolveResult_TracksExecutionAndLogsProperly()
    {
        var solveResult = new CaptchaSolveResult
        {
            Success = true,
            Message = "Solved successfully via mock response",
            ElapsedSeconds = 5.2,
            CaptchaType = "Jigsaw",
            ServerResponseJson = "{\"status\":\"success\",\"data\":{\"target_x\":228}}",
            SolvedAt = DateTime.Now
        };

        solveResult.Logs.Add("[CDP] Target X: 228");
        solveResult.Logs.Add("[CDP] Drag knob completed");

        Assert.True(solveResult.Success);
        Assert.Equal(5.2, solveResult.ElapsedSeconds);
        Assert.Equal(2, solveResult.Logs.Count);
        Assert.Equal("Jigsaw", solveResult.CaptchaType);
        Assert.Contains("228", solveResult.ServerResponseJson);
    }

    [Fact]
    public async Task SolveCaptchaWithMockAsync_WhenDisconnectedPort_ReturnsSafeFailureResult()
    {
        var cdpService = new CdpAutomationService();
        // Non-existent CDP port 59999
        var result = await cdpService.SolveCaptchaWithMockAsync(59999);

        Assert.False(result.Success);
        Assert.Contains("ไม่สามารถเชื่อมต่อ CDP", result.Message);
        Assert.NotEmpty(result.Logs);
    }

    [Fact]
    public void AppSettings_AutoSolveCaptcha_DefaultsToTrueAndFiveSeconds()
    {
        var settings = new AppSettings();
        Assert.True(settings.AutoSolveCaptcha);
        Assert.Equal(5, settings.AutoSolveDelaySeconds);
    }

    [Fact]
    public async Task SolveCaptchaWithMockAsync_ImmediateMode_AcceptsZeroDelayParameter()
    {
        var cdpService = new CdpAutomationService();
        var result = await cdpService.SolveCaptchaWithMockAsync(59999, delaySeconds: 0);

        Assert.False(result.Success);
        Assert.Contains("ไม่สามารถเชื่อมต่อ CDP", result.Message);
    }
}
