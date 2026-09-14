using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TiktokStudioSandbox.App.ViewModels;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;
using TiktokStudioSandbox.Core.Services;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class InstanceDebugLogTests : IDisposable
{
    private readonly string _testDir;

    public InstanceDebugLogTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TiktokStudioSandbox_LogTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void InstanceLogger_LogsMessages_AndWritesToDisk()
    {
        var logger = new InstanceLogger("AccTest01", 9225, "Account Test 01", _testDir);

        logger.Info("Process", "Process started with PID 1234");
        logger.Debug("CDP", "Connecting to WebSocket on port 9225");
        logger.Warn("Network", "Slow response time 2500ms");
        logger.Error("System", "Sample error occurred", new InvalidOperationException("Test exception"));

        var logs = logger.GetRecentLogs();
        Assert.True(logs.Count >= 4);

        Assert.Contains(logs, l => l.Level == InstanceLogLevel.Info && l.Category == "Process" && l.Message.Contains("1234"));
        Assert.Contains(logs, l => l.Level == InstanceLogLevel.Debug && l.Category == "CDP");
        Assert.Contains(logs, l => l.Level == InstanceLogLevel.Warn && l.Category == "Network");
        Assert.Contains(logs, l => l.Level == InstanceLogLevel.Error && l.Details != null && l.Details.Contains("Test exception"));

        // Verify file written to disk
        Assert.True(File.Exists(logger.LogFilePath));
        string fileContent = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("AccTest01", fileContent);
        Assert.Contains("Process started with PID 1234", fileContent);
        Assert.Contains("Test exception", fileContent);
    }

    [Fact]
    public void InstanceLogger_LogAddedEvent_FiresProperly()
    {
        var logger = new InstanceLogger("AccEventTest", 9226, "Event Account", _testDir);
        InstanceLogEntry? receivedEntry = null;

        logger.LogAdded += (entry) =>
        {
            receivedEntry = entry;
        };

        logger.Info("TestCategory", "Hello from test");

        Assert.NotNull(receivedEntry);
        Assert.Equal("TestCategory", receivedEntry!.Category);
        Assert.Equal("Hello from test", receivedEntry.Message);
        Assert.Equal(InstanceLogLevel.Info, receivedEntry.Level);
    }

    [Fact]
    public void InstanceLogger_Clear_ClearsInMemoryLogs()
    {
        var logger = new InstanceLogger("AccClearTest", 9227, "Clear Account", _testDir);
        logger.Info("Test", "Entry 1");
        logger.Info("Test", "Entry 2");

        Assert.True(logger.GetRecentLogs().Count >= 2);

        logger.Clear();
        Assert.Empty(logger.GetRecentLogs());
    }

    [Fact]
    public void InstanceLogManager_CachesLoggers_AndRetrievesByPort()
    {
        var manager = new InstanceLogManager(_testDir);

        var profile = new StudioInstanceProfile
        {
            Name = "My Stream Account",
            ProfileFolderName = "StreamAcc",
            CdpPort = 9330
        };

        var logger1 = manager.GetLogger(profile);
        var logger2 = manager.GetLogger(profile);

        // Same logger instance returned
        Assert.Same(logger1, logger2);
        Assert.Equal("StreamAcc", logger1.ProfileFolderName);
        Assert.Equal(9330, logger1.CdpPort);

        // Can find logger by port
        var loggerByPort = manager.TryGetLoggerByPort(9330);
        Assert.NotNull(loggerByPort);
        Assert.Same(logger1, loggerByPort);

        // Unregistered port returns null
        Assert.Null(manager.TryGetLoggerByPort(59999));
    }

    [Fact]
    public void InstanceCardViewModel_DebugLog_InitializesAndCapturesNewLogs()
    {
        var profile = new StudioInstanceProfile { Name = "CardLogAcc", ProfileFolderName = "CardLog01", CdpPort = 9440 };
        var settings = new AppSettings();
        var logManager = new InstanceLogManager(_testDir);
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService(), logManager);

        var vm = new InstanceCardViewModel(profile, settings, orchestrator, logManager: logManager);

        Assert.NotNull(vm.Logger);
        Assert.NotNull(vm.Logs);
        Assert.NotNull(vm.FilteredLogs);
        Assert.Contains("CardLog01", vm.LogFilePath);

        vm.Logger.Info("TestCategory", "Real-time card test message");

        Assert.Contains(vm.Logs, l => l.Message == "Real-time card test message");
        Assert.Contains("1", vm.LogCountSummary);
    }

    [Fact]
    public void InstanceCardViewModel_DebugLog_FilterByLevelCategoryAndSearchText()
    {
        var profile = new StudioInstanceProfile { Name = "FilterAcc", ProfileFolderName = "Filter01", CdpPort = 9441 };
        var settings = new AppSettings();
        var logManager = new InstanceLogManager(_testDir);
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService(), logManager);
        var vm = new InstanceCardViewModel(profile, settings, orchestrator, logManager: logManager);

        // Clear existing initial logs
        vm.ClearLogsCommand.Execute(null);

        vm.Logs.Add(new InstanceLogEntry { Level = InstanceLogLevel.Info, Category = "Process", Message = "Process launched successfully" });
        vm.Logs.Add(new InstanceLogEntry { Level = InstanceLogLevel.Debug, Category = "CDP", Message = "Evaluating window.location.href" });
        vm.Logs.Add(new InstanceLogEntry { Level = InstanceLogLevel.Error, Category = "Network", Message = "Connection refused to remote server" });

        // Filter by Level: ERROR
        vm.LogLevelFilter = "ERROR";
        var filteredList = vm.FilteredLogs.Cast<InstanceLogEntry>().ToList();
        Assert.Single(filteredList);
        Assert.Equal("Connection refused to remote server", filteredList[0].Message);

        // Filter by Category: CDP
        vm.LogLevelFilter = "ALL";
        vm.LogCategoryFilter = "CDP";
        filteredList = vm.FilteredLogs.Cast<InstanceLogEntry>().ToList();
        Assert.Single(filteredList);
        Assert.Equal("Evaluating window.location.href", filteredList[0].Message);

        // Filter by Search Text
        vm.LogCategoryFilter = "ALL";
        vm.LogSearchText = "launched";
        filteredList = vm.FilteredLogs.Cast<InstanceLogEntry>().ToList();
        Assert.Single(filteredList);
        Assert.Equal("Process launched successfully", filteredList[0].Message);

        // Reset Filters
        vm.ResetLogFiltersCommand.Execute(null);
        Assert.Equal("ALL", vm.LogLevelFilter);
        Assert.Equal("ALL", vm.LogCategoryFilter);
        Assert.Empty(vm.LogSearchText);
        Assert.Equal(3, vm.FilteredLogs.Cast<InstanceLogEntry>().Count());
    }

    [Fact]
    public void InstanceCardViewModel_DebugLog_ClearAndCopyCommands_WorkProperly()
    {
        var profile = new StudioInstanceProfile { Name = "CmdAcc", ProfileFolderName = "Cmd01", CdpPort = 9442 };
        var settings = new AppSettings();
        var logManager = new InstanceLogManager(_testDir);
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService(), logManager);
        var vm = new InstanceCardViewModel(profile, settings, orchestrator, logManager: logManager);

        vm.Logs.Add(new InstanceLogEntry { Level = InstanceLogLevel.Info, Category = "Test", Message = "Message to be cleared" });
        Assert.NotEmpty(vm.Logs);

        // Test Selection Text
        var entry = vm.Logs[0];
        vm.SelectedLog = entry;
        Assert.Contains("Message to be cleared", vm.SelectedLogFullText);
        Assert.Equal(System.Windows.Visibility.Visible, vm.SelectedLogDetailsVisibility);

        // Test Clear
        vm.ClearLogsCommand.Execute(null);
        Assert.Empty(vm.Logs);
        Assert.Null(vm.SelectedLog);
    }

    [Fact]
    public async Task Orchestrator_EmitsLogs_ToTargetInstanceLogger()
    {
        var logManager = new InstanceLogManager(_testDir);
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService(), logManager);

        var profile = new StudioInstanceProfile
        {
            Name = "OrchestratorLogTest",
            ProfileFolderName = "OrchLog01",
            CdpPort = 59990,
            LaunchMode = InstanceLaunchMode.DirectProfile
        };

        var logger = logManager.GetLogger(profile);

        // Toggle window visibility
        orchestrator.ToggleWindowVisibility(profile);

        // Send chat while not running
        await orchestrator.SendChatAsync(profile, "Hello World");

        var recentLogs = logger.GetRecentLogs();
        Assert.Contains(recentLogs, l => l.Category == "Window" && l.Message.Contains("Toggle window visibility"));
        Assert.Contains(recentLogs, l => l.Category == "Chat" && l.Level == InstanceLogLevel.Warn);
    }
}
