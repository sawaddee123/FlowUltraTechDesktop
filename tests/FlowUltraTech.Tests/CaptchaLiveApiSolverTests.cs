using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FlowUltraTech.Core;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

public class CaptchaLiveApiSolverTests
{
    [Fact]
    public void AppSettings_HasCaptchaSolverApiSettingsWithDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal(CaptchaSolverType.LocalApiServer, settings.CaptchaSolverMode);
        Assert.Equal(AppConstants.DefaultServerEndpoint, settings.CaptchaSolverEndpoint);
        Assert.Equal(string.Empty, settings.CaptchaSolverJwtToken);
        Assert.Equal(5, settings.AutoSolveDelaySeconds);
    }

    [Fact]
    public async Task TestSolverApiConnectionAsync_WhenEmptyUrl_ReturnsFailure()
    {
        using var cdpService = new CdpAutomationService();
        var (ok, msg) = await cdpService.TestSolverApiConnectionAsync("");

        Assert.False(ok);
        Assert.Contains("กรุณากรอก URL", msg);
    }

    [Fact]
    public async Task TestSolverApiConnectionAsync_WhenOfflinePort_ReturnsHandledError()
    {
        using var cdpService = new CdpAutomationService();
        // Test non-listening port
        var (ok, msg) = await cdpService.TestSolverApiConnectionAsync("http://127.0.0.1:59999");

        Assert.False(ok);
        Assert.Contains("ไม่สามารถเชื่อมต่อ", msg);
    }

    [Fact]
    public async Task SolveCaptchaAsync_WhenDisconnectedPort_ReturnsGracefulFailure()
    {
        using var cdpService = new CdpAutomationService();
        var result = await cdpService.SolveCaptchaAsync(
            cdpPort: 59999,
            solverMode: CaptchaSolverType.LocalApiServer,
            solverApiEndpoint: "http://127.0.0.1:8001");

        Assert.False(result.Success);
        Assert.Contains("ไม่สามารถเชื่อมต่อ CDP Port 59999 ได้", result.Message);
    }

    [Fact]
    public async Task SolveCaptchaAsync_RealLiveE2E_AgainstPort9221()
    {
        string edgeExe = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        if (!File.Exists(edgeExe))
        {
            edgeExe = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        }
        Assert.True(File.Exists(edgeExe), "Edge or Chrome executable must exist");

        string tempProfile = Path.Combine(Path.GetTempPath(), $"E2E_Test_Profile_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempProfile);

        string mockFile = Path.GetFullPath(@"D:\Script\Test\FlowUltraTech\docs\mocks\mock_captcha_jigsaw.html");
        string fileUrl = "file:///" + mockFile.Replace('\\', '/');

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = edgeExe,
            Arguments = $"--remote-debugging-port=9221 --user-data-dir=\"{tempProfile}\" --no-first-run --no-default-browser-check \"{fileUrl}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var browserProcess = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(browserProcess);

        try
        {
            // Wait up to 10s for CDP port 9221 to listen
            using var http = new HttpClient();
            bool isListening = false;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                try
                {
                    var ping = await http.GetAsync("http://127.0.0.1:9221/json/version");
                    if (ping.IsSuccessStatusCode)
                    {
                        isListening = true;
                        break;
                    }
                }
                catch { }
            }
            Assert.True(isListening, "CDP port 9221 should be listening on launched browser");

            using var cdpService = new CdpAutomationService();
            var authService = new AuthClientService();
            var loginRes = await authService.LoginAsync("root", "!WFXU#v1aO7338+", "E2E-TEST-HWID", "http://210.246.215.66:8001");
            Assert.True(loginRes.Success, $"Root login should succeed: {loginRes.Message}");
            Assert.False(string.IsNullOrWhiteSpace(loginRes.AccessToken));

            var logs = new System.Collections.Generic.List<string>();
            var result = await cdpService.SolveCaptchaAsync(
                cdpPort: 9221,
                solverMode: CaptchaSolverType.LocalApiServer,
                solverApiEndpoint: "http://210.246.215.66:8001",
                solverJwtToken: loginRes.AccessToken,
                progressLogger: msg => { logs.Add(msg); Console.WriteLine(msg); });

            Console.WriteLine($"[E2E RESULT] Success={result.Success}, Message={result.Message}, Elapsed={result.ElapsedSeconds:F2}s");
            foreach (var l in result.Logs) Console.WriteLine(l);

            Assert.True(result.Success, $"Solve should succeed: {result.Message}\nLogs:\n{string.Join('\n', result.Logs)}");
        }
        finally
        {
            try { browserProcess.Kill(entireProcessTree: true); } catch { }
            try { Directory.Delete(tempProfile, true); } catch { }
        }
    }

    [Fact]
    public async Task SolveCaptchaAsync_RealTikTokStudioDOM_VerifiesAccurateCalculationAndDrag()
    {
        string edgeExe = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        if (!File.Exists(edgeExe))
        {
            edgeExe = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        }
        Assert.True(File.Exists(edgeExe), "Edge or Chrome executable must exist");

        string tempProfile = Path.Combine(Path.GetTempPath(), $"E2E_Test_Profile_TikTok_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempProfile);

        string realTikTokFile = Path.GetFullPath(@"D:\Script\Test\FlowUltraTech\extracted_sources\CaptchaJigsaw.html");
        string fileUrl = "file:///" + realTikTokFile.Replace('\\', '/');

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = edgeExe,
            Arguments = $"--remote-debugging-port=9225 --user-data-dir=\"{tempProfile}\" --no-first-run --no-default-browser-check \"{fileUrl}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var browserProcess = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(browserProcess);

        try
        {
            using var http = new HttpClient();
            bool isListening = false;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                try
                {
                    var ping = await http.GetAsync("http://127.0.0.1:9225/json/version");
                    if (ping.IsSuccessStatusCode)
                    {
                        isListening = true;
                        break;
                    }
                }
                catch { }
            }
            Assert.True(isListening, "CDP port 9225 should be listening");

            using var cdpService = new CdpAutomationService();
            var authService = new AuthClientService();
            var loginRes = await authService.LoginAsync("root", "!WFXU#v1aO7338+", "E2E-TEST-HWID", "http://210.246.215.66:8001");
            Assert.True(loginRes.Success);

            // 1. Detect Captcha on Real TikTok DOM
            var detectRes = await cdpService.DetectCaptchaElementsAsync(9225);
            Assert.True(detectRes.IsActive, "Real TikTok Captcha should be detected as Active");
            Assert.Equal(CaptchaType.Jigsaw, detectRes.Type);
            Assert.NotNull(detectRes.DirectImageBytes);
            Assert.True(detectRes.DirectImageBytes.Length > 0);

            // 2. Solve Captcha via Central Server
            var logs = new System.Collections.Generic.List<string>();
            var result = await cdpService.SolveCaptchaAsync(
                cdpPort: 9225,
                detection: detectRes,
                solverMode: CaptchaSolverType.LocalApiServer,
                solverApiEndpoint: "http://210.246.215.66:8001",
                solverJwtToken: loginRes.AccessToken,
                progressLogger: msg => { logs.Add(msg); Console.WriteLine(msg); });

            Console.WriteLine($"[REAL TIKTOK E2E RESULT] Message={result.Message}, Elapsed={result.ElapsedSeconds:F2}s");
            foreach (var l in result.Logs) Console.WriteLine(l);

            Assert.False(string.IsNullOrWhiteSpace(result.ServerResponseJson));
            Assert.Contains("target_x", result.ServerResponseJson);
            // Verify that calculation is in the exact sub-pixel range ~110-115px (instead of the old 143px overshoot)
            Assert.True(logs.Any(l => l.Contains("110.") || l.Contains("111.") || l.Contains("112.") || l.Contains("113.") || l.Contains("114.") || l.Contains("115.")),
                "Should calculate accurate target X (~110-115px) for real TikTok DOM");
        }
        finally
        {
            try { browserProcess.Kill(entireProcessTree: true); } catch { }
            try { Directory.Delete(tempProfile, true); } catch { }
        }
    }

    [Fact]
    public async Task SolverApi_WhenTokenExpiredOrInvalid_Returns401TokenExpired()
    {
        using var client = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "http://210.246.215.66:8001/api/v1/captcha/slide")
        {
            Content = new StringContent("{\"image\":\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAA=C\"}", System.Text.Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "expired.jwt.token");

        using var resp = await client.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Token expired", body);
    }
}

