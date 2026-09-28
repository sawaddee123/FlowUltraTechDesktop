using System;
using System.IO;
using System.Threading.Tasks;
using FlowUltraTech.Core;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

public class LoginWindowAndMandatoryAuthTests
{
    private class DummyConfigStorageService : IConfigStorageService
    {
        public AppSettings CurrentSettings { get; set; } = new();

        public AppSettings LoadSettings() => CurrentSettings;
        public void SaveSettings(AppSettings settings) => CurrentSettings = settings;
        public Task SaveSettingsAsync(AppSettings settings)
        {
            CurrentSettings = settings;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void AppSettings_DefaultAuthState_IsUnauthenticated()
    {
        var settings = new AppSettings();
        Assert.True(string.IsNullOrEmpty(settings.AuthToken));
        Assert.True(string.IsNullOrEmpty(settings.RefreshToken));
        Assert.True(string.IsNullOrEmpty(settings.LoggedInUsername));
        Assert.Equal(1, settings.MaxTiktokAccounts);
        Assert.Equal(AppConstants.DefaultServerEndpoint, settings.AuthApiEndpoint);
    }

    [Fact]
    public async Task SolveCaptchaAsync_WhenServerFails_FailsWithoutMockFallback()
    {
        var cdpService = new CdpAutomationService();
        // Disconnected port 59998: should immediately fail with connection error, NOT mock
        var result = await cdpService.SolveCaptchaAsync(
            cdpPort: 59998,
            detection: new CaptchaDetectionResult { IsActive = true, Type = CaptchaType.Jigsaw },
            solverMode: CaptchaSolverType.LocalApiServer,
            solverApiEndpoint: "http://127.0.0.1:59998"
        );

        Assert.False(result.Success);
        Assert.Contains("ไม่สามารถเชื่อมต่อ CDP", result.Message);
        // Ensure no mock fallback JSON was parsed
        Assert.Null(result.ServerResponseJson);
    }

    [Fact]
    public async Task MainViewModel_LogoutAsync_FiresRequestLogoutRedirectAndClearsAuth()
    {
        var storage = new DummyConfigStorageService();
        storage.CurrentSettings.AuthToken = "test_jwt_token_12345";
        storage.CurrentSettings.RefreshToken = "test_refresh_token_67890";
        storage.CurrentSettings.LoggedInUsername = "streamer01";
        storage.CurrentSettings.LicensePlan = "VIP";
        storage.CurrentSettings.LicenseRole = "vip_streamer";
        storage.CurrentSettings.MaxTiktokAccounts = 5;

        // Dummy implementations for MainViewModel
        var windowCtrl = new WindowController();
        var launcher = new ProcessLauncher(windowCtrl);
        var cdp = new CdpAutomationService();
        var orchestrator = new InstanceOrchestrator(launcher, windowCtrl, cdp);
        var proxifier = new ProxifierService();

        var vm = new FlowUltraTech.App.ViewModels.MainViewModel(
            storage,
            orchestrator,
            proxifier,
            cdp
        );

        Assert.True(vm.IsUserLoggedIn);
        Assert.Equal("streamer01", vm.LoggedInUsername);
        Assert.Equal(5, vm.MaxTiktokAccounts);

        bool logoutRedirectFired = false;
        vm.RequestLogoutRedirect += () => logoutRedirectFired = true;

        await vm.LogoutAsync();

        Assert.True(logoutRedirectFired, "RequestLogoutRedirect event should fire on logout");
        Assert.False(vm.IsUserLoggedIn);
        Assert.True(string.IsNullOrEmpty(vm.AuthToken));
        Assert.True(string.IsNullOrEmpty(storage.CurrentSettings.AuthToken));
        Assert.True(string.IsNullOrEmpty(storage.CurrentSettings.LoggedInUsername));
        Assert.Equal(1, storage.CurrentSettings.MaxTiktokAccounts);
        Assert.Equal("ยังไม่ได้เข้าสู่ระบบ", vm.LicensePlan);
    }
}
