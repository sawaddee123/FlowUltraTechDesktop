using System;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Models;
using TiktokStudioSandbox.Core.Services;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class CaptchaLeaseHeaderTests
{
    [Fact]
    public async Task SolveCaptchaAsync_AcceptsInstanceLeaseId_GracefullyHandlesOfflinePort()
    {
        using var cdpService = new CdpAutomationService();
        var result = await cdpService.SolveCaptchaAsync(
            cdpPort: 58888,
            solverMode: CaptchaSolverType.LocalApiServer,
            solverApiEndpoint: "http://127.0.0.1:8001",
            instanceLeaseId: "lease_test_verification_header");

        Assert.False(result.Success);
        Assert.Contains("ไม่สามารถเชื่อมต่อ CDP Port 58888 ได้", result.Message);
    }
}
