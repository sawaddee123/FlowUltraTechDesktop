using System;
using System.IO;
using FlowUltraTech.App.ViewModels;
using FlowUltraTech.Core;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

public class MaxTiktokAccountsQuotaTests
{
    private (MainViewModel vm, string tempFile) CreateTestMainViewModel(int maxAccounts = 2)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"quotatest_{Guid.NewGuid():N}.json");
        var storage = new ConfigStorageService(tempFile);
        var settings = storage.LoadSettings();
        settings.MaxTiktokAccounts = maxAccounts;
        settings.Instances.Clear();
        settings.Instances.Add(new StudioInstanceProfile { Name = "Account 01", ProfileFolderName = "Acc01", CdpPort = 9222 });
        settings.Instances.Add(new StudioInstanceProfile { Name = "Account 02", ProfileFolderName = "Acc02", CdpPort = 9223 });
        settings.Instances.Add(new StudioInstanceProfile { Name = "Account 03", ProfileFolderName = "Acc03", CdpPort = 9224 });
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
    public void AppSettings_DefaultsToMaxTiktokAccounts1()
    {
        var settings = new AppSettings();
        Assert.Equal(1, settings.MaxTiktokAccounts);
        Assert.Equal(AppConstants.DefaultServerEndpoint, settings.AuthApiEndpoint);
        Assert.Equal(string.Empty, settings.AuthToken);
    }

    [Fact]
    public void WhenRunningInstancesReachQuota_NonRunningCardsAreQuotaReachedAndCannotStart()
    {
        var (vm, tempFile) = CreateTestMainViewModel(maxAccounts: 1);
        try
        {
            Assert.Equal(3, vm.Instances.Count);
            Assert.Equal(1, vm.MaxTiktokAccounts);

            // Initially none running: CanStart is true
            Assert.True(vm.Instances[0].CanStart);
            Assert.True(vm.Instances[1].CanStart);
            Assert.True(vm.Instances[2].CanStart);

            // Simulate card 0 becoming Running
            vm.Instances[0].Status = InstanceStatus.Running;
            vm.UpdateQuotaState();

            // Card 0 is running: IsRunning = true, CanStart = false (because it's already running)
            Assert.True(vm.Instances[0].IsRunning);
            Assert.False(vm.Instances[0].CanStart);

            // Card 1 and 2 are stopped, but quota of 1 is reached!
            Assert.True(vm.IsQuotaFull);
            Assert.True(vm.Instances[1].IsQuotaReached);
            Assert.False(vm.Instances[1].CanStart);
            Assert.Contains("เปิดครบโควต้าสูงสุดแล้ว", vm.Instances[1].StartButtonToolTip);

            Assert.True(vm.Instances[2].IsQuotaReached);
            Assert.False(vm.Instances[2].CanStart);

            // Now card 0 stops
            vm.Instances[0].Status = InstanceStatus.Stopped;
            vm.UpdateQuotaState();

            // Quota is freed: cards 0, 1, 2 can start again
            Assert.False(vm.IsQuotaFull);
            Assert.False(vm.Instances[1].IsQuotaReached);
            Assert.True(vm.Instances[1].CanStart);
            Assert.True(vm.Instances[2].CanStart);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
