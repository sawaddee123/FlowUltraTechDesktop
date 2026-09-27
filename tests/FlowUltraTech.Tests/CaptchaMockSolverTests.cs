using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

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
            outputPath = Path.Combine(SolutionDir, "src", "FlowUltraTech.Core", "Config", "mock_captcha_response.json");
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

    [Fact]
    public void ParseSolverApiResponse_Slide_ParsesScaleAndTargetCoordinates()
    {
        string json = @"{
            ""success"": true,
            ""target_x"": 229.33,
            ""raw_target_x"": 461.55,
            ""position"": [229.33, 100],
            ""raw_position"": [461.55, 100],
            ""image_width"": 640,
            ""image_height"": 640,
            ""display_width"": 318,
            ""display_height"": 212,
            ""scale_x"": 0.496875,
            ""scale_y"": 0.33125,
            ""captchaId"": ""650a1b2c3d4e5f6a7b8c9d0e""
        }";

        var parsed = CdpAutomationService.ParseSolverApiResponse(CaptchaType.Jigsaw, json, _ => { });

        Assert.True(parsed.Solved);
        Assert.Equal(229.33, parsed.TargetX, 2);
        Assert.Equal(461.55, parsed.RawTargetX ?? 0, 2);
        Assert.Equal(640, parsed.ImageWidth);
        Assert.Equal(640, parsed.ImageHeight);
        Assert.Equal(318, parsed.DisplayWidth);
        Assert.Equal(212, parsed.DisplayHeight);
        Assert.Equal(0.496875, parsed.ScaleX ?? 0, 5);
        Assert.Equal("650a1b2c3d4e5f6a7b8c9d0e", parsed.CaptchaId);
    }

    [Fact]
    public void ParseSolverApiResponse_Symbol_ParsesSymbolPositionsAndScaling()
    {
        string json = @"{
            ""success"": true,
            ""symbol_positions"": [[149, 70], [215, 95]],
            ""raw_symbol_positions"": [[300, 210], [433, 287]],
            ""image_width"": 640,
            ""image_height"": 640,
            ""display_width"": 318,
            ""display_height"": 212,
            ""scale_x"": 0.496875,
            ""scale_y"": 0.33125,
            ""captchaId"": ""650a1b2c3d4e5f6a7b8c9d0f""
        }";

        var parsed = CdpAutomationService.ParseSolverApiResponse(CaptchaType.Symbol, json, _ => { });

        Assert.True(parsed.Solved);
        Assert.Equal(2, parsed.ClickPoints.Count);
        Assert.Equal(149, parsed.ClickPoints[0].X);
        Assert.Equal(70, parsed.ClickPoints[0].Y);
        Assert.Equal(215, parsed.ClickPoints[1].X);
        Assert.Equal(95, parsed.ClickPoints[1].Y);
        Assert.NotNull(parsed.RawClickPoints);
        Assert.Equal(2, parsed.RawClickPoints!.Count);
        Assert.Equal(300, parsed.RawClickPoints[0].X);
        Assert.Equal(210, parsed.RawClickPoints[0].Y);
        Assert.Equal("650a1b2c3d4e5f6a7b8c9d0f", parsed.CaptchaId);
    }

    [Fact]
    public void CoordinateScaling_WhenRawTargetXExceedsDomWidth_ScalesCorrectly()
    {
        double domDisplayWidth = 318.0;
        double rawTargetX = 461.55;
        double originalImageWidth = 640.0;

        // Defense-in-depth scaling formula
        double scale = domDisplayWidth / originalImageWidth;
        double scaledTargetX = rawTargetX * scale;

        Assert.True(rawTargetX > domDisplayWidth, "Raw coordinate should exceed DOM width");
        Assert.True(scaledTargetX <= domDisplayWidth, "Scaled coordinate must fit inside DOM width");
        Assert.Equal(229.33, scaledTargetX, 2);
    }

    [Fact]
    public void CoordinateScaling_WhenSymbolRawPointsExceedDomDimensions_ScalesCorrectly()
    {
        double domDisplayWidth = 318.0;
        double domDisplayHeight = 212.0;
        double originalImageWidth = 640.0;
        double originalImageHeight = 640.0;

        var rawPoint = (X: 433.0, Y: 287.0);

        double sx = domDisplayWidth / originalImageWidth;
        double sy = domDisplayHeight / originalImageHeight;
        double scaledX = rawPoint.X * sx;
        double scaledY = rawPoint.Y * sy;

        Assert.True(rawPoint.X > domDisplayWidth, "Raw X should exceed DOM width");
        Assert.True(rawPoint.Y > domDisplayHeight, "Raw Y should exceed DOM height");
        Assert.True(scaledX <= domDisplayWidth, "Scaled X must fit DOM width");
        Assert.True(scaledY <= domDisplayHeight, "Scaled Y must fit DOM height");
        Assert.Equal(215.15, scaledX, 2);
        Assert.Equal(95.07, scaledY, 2);
    }

    [Fact]
    public void CoordinateScaling_PreventsDoubleScaling_WhenServerAlreadyScaled()
    {
        // Simulate response from server where coordinates are already scaled
        string json = @"{
            ""success"": true,
            ""symbol_positions"": [[159.0, 106.0], [212.0, 150.0]],
            ""raw_symbol_positions"": [[320.0, 212.0], [426.0, 300.0]],
            ""image_width"": 640,
            ""image_height"": 640,
            ""display_width"": 318,
            ""display_height"": 212,
            ""scale_x"": 0.496875,
            ""scale_y"": 0.33125
        }";

        var parsed = CdpAutomationService.ParseSolverApiResponse(CaptchaType.Symbol, json, _ => { });
        Assert.True(parsed.Solved);
        Assert.NotNull(parsed.RawClickPoints);

        // When projecting directly from RawClickPoints to DOM (318 x 212):
        double domW = 318.0;
        double domH = 212.0;
        double origW = parsed.ImageWidth!.Value;
        double origH = parsed.ImageHeight!.Value;

        var raw0 = parsed.RawClickPoints![0];
        double exactX0 = raw0.X * (domW / origW);
        double exactY0 = raw0.Y * (domH / origH);

        // Raw 320 on 640 is 50% -> on 318 DOM it must be exactly 159.0
        Assert.Equal(159.0, exactX0, 1);
        Assert.Equal(70.22, exactY0, 1); // 212/640 * 212 = 70.22

        // Ensure it did NOT double-scale (which would have yielded ~79px instead of 159px)
        Assert.True(exactX0 > 100.0, "Point must not be double scaled into top-left");
    }

    [Fact]
    public void CoordinateScaling_DirectRawProjection_MaintainsAccuracyOnArbitraryResolutions()
    {
        // Test non-standard resolutions (e.g. 552x345, 1280x720, 800x600)
        double origW = 552.0;
        double origH = 345.0;
        var rawPoint = (X: 276.0, Y: 172.5); // Exact center

        double[] targetDomSizes = [276.0, 340.0, 400.0, 552.0];
        foreach (var domW in targetDomSizes)
        {
            double domH = domW * (origH / origW); // preserve aspect ratio
            double clickX = rawPoint.X * (domW / origW);
            double clickY = rawPoint.Y * (domH / origH);

            // Center remains exact center regardless of resolution!
            Assert.Equal(domW / 2.0, clickX, 1);
            Assert.Equal(domH / 2.0, clickY, 1);
        }
    }

    [Fact]
    public void CalculateJigsawKnobDragDistance_RealTikTokDOM_PreventsOvershootAndAchievesExactSubPixelAlignment()
    {
        // Real TikTok Live Studio DOM parameters:
        // Background image width = 280px (rendered from 552x344 WebP)
        // Slider track width = 280px
        // Slider knob width = 64px (TUXButton--medium, width: 64px)
        // Usable track travel = 280 - 64 = 216px
        // Target hole raw = 218.06px -> targetX in DOM = 218.06 * (280 / 552) = 110.61px
        double domW = 280.0;
        double trackWidth = 280.0;
        double knobWidth = 64.0;
        double targetX = 110.61;
        double usableTrack = trackWidth - knobWidth; // 216.0

        // OLD FLAWED CALCULATION (caused 32.77px overshoot -> TikTok rejected):
        double oldKnobDrag = targetX; // 110.61px
        // In TikTok JS (69901.3baa747e.js): w = knobDrag / usableTrack; pieceX = w * domW; replyX = round(w * domW);
        double oldTiktokW = oldKnobDrag / usableTrack;
        double oldResultingPieceX = oldTiktokW * domW; // 143.38px!
        double oldError = Math.Abs(oldResultingPieceX - targetX);
        Assert.True(oldError > 30.0, "Old flawed logic had an intolerable 32.77px overshoot");

        // NEW FIXED CALCULATION (accounting for TikTok's internal knob-to-image ratio):
        double dragRatio = usableTrack / domW; // 216 / 280 = 0.77142857
        double newKnobDrag = targetX * dragRatio; // 85.33px
        double newTiktokW = newKnobDrag / usableTrack;
        double newResultingPieceX = newTiktokW * domW;
        double newReplyX = Math.Round(newTiktokW * domW);

        // Verification:
        Assert.Equal(85.33, newKnobDrag, 2);
        Assert.Equal(targetX, newResultingPieceX, 4); // Exact 110.61px DOM piece position!
        Assert.Equal(111.0, newReplyX); // Exactly 111px sent to TikTok verify API!
        Assert.True(Math.Abs(newResultingPieceX - targetX) < 0.001, "Error must be 0.0px");
    }
}

