using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;

namespace FlowUltraTech.Tests;

public class LongTermPerformanceSoakTests : IDisposable
{
    private readonly string _testLogsDir;

    public LongTermPerformanceSoakTests()
    {
        _testLogsDir = Path.Combine(Path.GetTempPath(), "FlowUltraTech_SoakTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testLogsDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testLogsDir))
            {
                Directory.Delete(_testLogsDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void HighFrequencyLogging_10000Entries_CompletesFastAndMaintainsBoundedMemory()
    {
        // Arrange
        var logger = new InstanceLogger("SoakTestProfile", 9222, "SoakAccount", _testLogsDir);

        // Warm up GC
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memBefore = GC.GetTotalMemory(true);

        var sw = Stopwatch.StartNew();

        // Act - Simulate 10,000 rapid log calls (simulating days of intense chat & event traffic)
        for (int i = 0; i < 10000; i++)
        {
            logger.Info("Chat", $"[LiveChat #{i}] Viewer @test_user_{i}: Hello world message payload with some metadata details!");
        }

        sw.Stop();

        // Assert
        var recent = logger.GetRecentLogs();
        Assert.True(recent.Count <= 1000, $"In-memory logs should be capped at 1,000, but was {recent.Count}");
        Assert.True(sw.ElapsedMilliseconds < 3000, $"10,000 logs took {sw.ElapsedMilliseconds}ms, should be < 3000ms after optimization");

        // Verify file was created
        Assert.True(File.Exists(logger.LogFilePath));

        // Post-GC memory should not blow up
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memAfter = GC.GetTotalMemory(true);
        long deltaMb = (memAfter - memBefore) / (1024 * 1024);

        Assert.True(deltaMb < 50, $"Memory delta should be minimal (<50MB), was {deltaMb}MB");
    }

    [Fact]
    public void LogRotation_DoesNotCrashWhenExceeding10MB()
    {
        // Arrange
        var logger = new InstanceLogger("RotationProfile", 9223, "RotationAccount", _testLogsDir);

        // Generate a large payload line (~1 KB)
        string largeLine = new string('A', 1024);

        // Write ~11 MB of logs (11,000 lines * 1 KB)
        for (int i = 0; i < 11000; i++)
        {
            logger.Info("Stress", largeLine);
        }

        // Assert - Main log and .bak should exist and neither should exceed max size
        Assert.True(File.Exists(logger.LogFilePath));
        string backupFile = logger.LogFilePath + ".bak";
        Assert.True(File.Exists(backupFile), "Backup log file .bak should have been created upon rotation");

        var fi = new FileInfo(logger.LogFilePath);
        Assert.True(fi.Length < 11 * 1024 * 1024, "Log file size should be within bounds after rotation");
    }

    [Fact]
    public void ConsoleOutput_StringBoundCap_PreventsLOHBloat()
    {
        // Simulate AppendConsole bounding logic
        string consoleOutput = string.Empty;
        const int maxLen = 64 * 1024;

        void Append(string text)
        {
            string newText = string.IsNullOrEmpty(consoleOutput) ? (text + "\n") : (consoleOutput + text + "\n");
            if (newText.Length > maxLen)
            {
                int cutIndex = newText.IndexOf('\n', newText.Length - (maxLen * 3 / 4));
                newText = cutIndex > 0 ? newText.Substring(cutIndex + 1) : newText.Substring(newText.Length - (maxLen * 3 / 4));
            }
            consoleOutput = newText;
        }

        // Run 5,000 append operations
        for (int i = 0; i < 5000; i++)
        {
            Append($"[{DateTime.Now:HH:mm:ss}] Long running streaming activity line number {i} with metadata payload text.");
        }

        // Must strictly not exceed maxLen
        Assert.True(consoleOutput.Length <= maxLen, $"ConsoleOutput length ({consoleOutput.Length}) exceeded limit ({maxLen})");
    }

    [Fact]
    public void InstanceLogManager_LifecycleCleanup_RemovesEvictedLoggers()
    {
        var logManager = new InstanceLogManager(_testLogsDir);

        // Create 20 instances
        for (int i = 0; i < 20; i++)
        {
            var l = logManager.GetLogger($"TempProfile_{i}", 9300 + i);
            l.Info("Test", $"Message {i}");
        }

        // Evict 10 instances
        for (int i = 0; i < 10; i++)
        {
            logManager.RemoveLogger($"TempProfile_{i}", 9300 + i);
        }

        // Verify remaining
        Assert.Null(logManager.TryGetLoggerByPort(9300));
        Assert.NotNull(logManager.TryGetLoggerByPort(9315));

        // Clear all
        logManager.Clear();
        Assert.Null(logManager.TryGetLoggerByPort(9315));
    }
}
