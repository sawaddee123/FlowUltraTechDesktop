using System;
using System.IO;
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
}
