using System;
using System.IO;
using TiktokStudioSandbox.App.ViewModels;
using TiktokStudioSandbox.Core.Models;
using TiktokStudioSandbox.Core.Services;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class MainViewModelNavigationTests
{
    private (MainViewModel vm, string tempFile) CreateTestMainViewModel()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"mainvm_test_{Guid.NewGuid():N}.json");
        var storage = new ConfigStorageService(tempFile);
        var settings = storage.LoadSettings();
        settings.Instances.Clear();
        settings.Instances.Add(new StudioInstanceProfile { Name = "Account 01", ProfileFolderName = "Acc01", CdpPort = 9222 });
        settings.Instances.Add(new StudioInstanceProfile { Name = "Account 02", ProfileFolderName = "Acc02", CdpPort = 9223 });
        storage.SaveSettings(settings);

        var cdp = new CdpAutomationService();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), cdp);
        var proxifier = new ProxifierService();
        var profileMgr = new ProfileManagerService();
        var telegram = new TelegramNotificationService();

        var vm = new MainViewModel(storage, orchestrator, proxifier, cdp, profileMgr, telegram);
        return (vm, tempFile);
    }

    [Fact]
    public void DefaultNavigation_ShouldBeInstancesTab()
    {
        var (vm, tempFile) = CreateTestMainViewModel();
        try
        {
            Assert.Equal(0, vm.SelectedNavIndex);
            Assert.True(vm.IsNavInstances);
            Assert.False(vm.IsNavCaptcha);
            Assert.False(vm.IsNavProxies);
            Assert.False(vm.IsNavRules);
            Assert.False(vm.IsNavSettings);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData(0, true, false, false, false, false)]
    [InlineData(1, false, true, false, false, false)]
    [InlineData(2, false, false, true, false, false)]
    [InlineData(3, false, false, false, true, false)]
    [InlineData(4, false, false, false, false, true)]
    public void NavigateCommands_ShouldUpdateSelectedNavIndexAndBooleans(int targetIndex, bool isInst, bool isCap, bool isProxy, bool isRule, bool isSetting)
    {
        var (vm, tempFile) = CreateTestMainViewModel();
        try
        {
            switch (targetIndex)
            {
                case 0: vm.NavigateInstances(); break;
                case 1: vm.NavigateCaptcha(); break;
                case 2: vm.NavigateProxies(); break;
                case 3: vm.NavigateRules(); break;
                case 4: vm.NavigateSettings(); break;
            }

            Assert.Equal(targetIndex, vm.SelectedNavIndex);
            Assert.Equal(isInst, vm.IsNavInstances);
            Assert.Equal(isCap, vm.IsNavCaptcha);
            Assert.Equal(isProxy, vm.IsNavProxies);
            Assert.Equal(isRule, vm.IsNavRules);
            Assert.Equal(isSetting, vm.IsNavSettings);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void NavigateTo_WithValidString_ShouldSetIndex()
    {
        var (vm, tempFile) = CreateTestMainViewModel();
        try
        {
            vm.NavigateTo("2");
            Assert.Equal(2, vm.SelectedNavIndex);
            Assert.True(vm.IsNavProxies);

            vm.NavigateTo("invalid");
            Assert.Equal(2, vm.SelectedNavIndex); // Unchanged
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void InstanceCountsAndStatusSummaries_ShouldReflectLoadedInstances()
    {
        var (vm, tempFile) = CreateTestMainViewModel();
        try
        {
            Assert.Equal(2, vm.Instances.Count);
            Assert.Equal(0, vm.RunningInstancesCount);
            Assert.Equal(0, vm.TotalCaptchaAlertCount);
            Assert.False(vm.HasAnyCaptchaAlert);
            Assert.Contains("0/2 บัญชีทำงานอยู่", vm.RunningStatusSummary);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void WhenInstanceStatusChanges_CountsShouldUpdateReactively()
    {
        var (vm, tempFile) = CreateTestMainViewModel();
        try
        {
            var firstInstance = vm.Instances[0];
            firstInstance.Status = InstanceStatus.Running;

            Assert.Equal(1, vm.RunningInstancesCount);
            Assert.Contains("1/2 บัญชีทำงานอยู่", vm.RunningStatusSummary);

            firstInstance.IsCaptchaAlertVisible = true;
            Assert.Equal(1, vm.TotalCaptchaAlertCount);
            Assert.True(vm.HasAnyCaptchaAlert);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
