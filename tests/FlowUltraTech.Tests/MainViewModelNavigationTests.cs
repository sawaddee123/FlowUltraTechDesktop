using System;
using System.IO;
using System.Linq;
using FlowUltraTech.App.ViewModels;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

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
    public void DefaultNavigation_ShouldBeAccountsHome()
    {
        var (vm, tempFile) = CreateTestMainViewModel();
        try
        {
            Assert.Equal(0, vm.SelectedNavIndex);
            Assert.True(vm.IsNavInstances);
            Assert.False(vm.IsNavSettings);
            Assert.True(vm.ShowHome);
            Assert.False(vm.ShowDetail);
            Assert.False(vm.ShowAdvanced);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void NavigateSettings_ThenInstances_ShouldReturnToHome()
    {
        var (vm, tempFile) = CreateTestMainViewModel();
        try
        {
            vm.OpenInstance(vm.Instances[0]);
            vm.NavigateSettings();
            Assert.True(vm.IsNavSettings);
            Assert.False(vm.ShowDetail);

            vm.NavigateInstances();
            Assert.True(vm.ShowHome);
            Assert.Null(vm.SelectedInstance);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void OpenInstance_Advanced_GoBack_ShouldStepBackOnePageAtATime()
    {
        var (vm, tempFile) = CreateTestMainViewModel();
        try
        {
            vm.OpenInstance(vm.Instances[1]);
            Assert.True(vm.ShowDetail);
            Assert.Same(vm.Instances[1], vm.SelectedInstance);

            vm.OpenAdvanced();
            Assert.True(vm.ShowAdvanced);

            vm.GoBack();
            Assert.True(vm.ShowDetail);

            vm.GoBack();
            Assert.True(vm.ShowHome);
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
            vm.NavigateTo("1");
            Assert.True(vm.IsNavSettings);

            vm.NavigateTo("invalid");
            Assert.Equal(1, vm.SelectedNavIndex); // Unchanged
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void AddInstance_AfterRemovingMiddleAccount_ShouldNotReuseFolderOrPort()
    {
        var (vm, tempFile) = CreateTestMainViewModel();
        try
        {
            vm.AddInstance(); // Acc03
            vm.Settings.Instances.RemoveAt(1); // ลบ Acc02 ทิ้ง เหลือ Acc01, Acc03
            vm.AddInstance();

            var folders = vm.Settings.Instances.Select(p => p.ProfileFolderName).ToList();
            var ports = vm.Settings.Instances.Select(p => p.CdpPort).ToList();
            Assert.Equal(folders.Count, folders.Distinct().Count());
            Assert.Equal(ports.Count, ports.Distinct().Count());
            Assert.Equal("Acc04", folders[^1]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void CaptchaWithoutAiSolving_ShouldNeedHelp_AndCountOnSidebar()
    {
        var (vm, tempFile) = CreateTestMainViewModel();
        try
        {
            var card = vm.Instances[0];
            card.Status = InstanceStatus.Running;
            card.IsCaptchaAlertVisible = true;
            card.IsSolvingCaptcha = true;
            Assert.False(card.NeedsHelp);
            Assert.Equal("AI กำลังแก้ Captcha", card.CardStatusText);
            Assert.Equal(0, vm.NeedsHelpCount);

            card.IsSolvingCaptcha = false; // AI แก้ไม่ผ่าน
            Assert.True(card.NeedsHelp);
            Assert.Equal("รอคุณช่วย", card.CardStatusText);
            Assert.Equal("warn", card.CardStatusTone);
            Assert.Equal(1, vm.NeedsHelpCount);

            card.IsCaptchaAlertVisible = false;
            Assert.Equal(0, vm.NeedsHelpCount);
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
