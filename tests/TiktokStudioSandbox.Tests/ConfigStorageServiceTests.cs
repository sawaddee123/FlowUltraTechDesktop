using System;
using System.IO;
using TiktokStudioSandbox.Core.Models;
using TiktokStudioSandbox.Core.Services;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class ConfigStorageServiceTests
{
    [Fact]
    public void LoadSettings_WhenFileMissing_CreatesAndReturnsDefaultSettings()
    {
        // Arrange
        string tempFile = Path.Combine(Path.GetTempPath(), $"config_test_{Guid.NewGuid():N}.json");

        try
        {
            var service = new ConfigStorageService(tempFile);

            // Act
            var settings = service.LoadSettings();

            // Assert
            Assert.NotNull(settings);
            Assert.True(File.Exists(tempFile));
            Assert.NotEmpty(settings.Instances);
            Assert.NotEmpty(settings.Proxies);
            Assert.NotEmpty(settings.Rules);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SaveSettings_PersistsModifiedSettings()
    {
        // Arrange
        string tempFile = Path.Combine(Path.GetTempPath(), $"config_test_{Guid.NewGuid():N}.json");

        try
        {
            var service = new ConfigStorageService(tempFile);
            var settings = service.LoadSettings();
            settings.Instances.Add(new StudioInstanceProfile
            {
                Name = "Account Test 99",
                SandboxBoxName = "Box_Test99",
                CdpPort = 9999
            });

            // Act
            service.SaveSettings(settings);

            // Assert
            var reloaded = service.LoadSettings();
            Assert.Contains(reloaded.Instances, i => i.SandboxBoxName == "Box_Test99" && i.CdpPort == 9999);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadSettings_WhenInstanceStatusIsStarting_SanitizesToStopped()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"config_sanitize_{Guid.NewGuid():N}.json");

        try
        {
            // Simulate an existing config.json file on disk that has Status: 1 (Starting) and ProcessId
            string rawJsonWithStarting = @"{
                ""Instances"": [
                    {
                        ""Name"": ""Account 03"",
                        ""ProfileFolderName"": ""Acc03"",
                        ""CdpPort"": 9223,
                        ""Status"": 1,
                        ""ProcessId"": 12345,
                        ""StatusMessage"": ""Starting...""
                    }
                ]
            }";
            File.WriteAllText(tempFile, rawJsonWithStarting);

            var service = new ConfigStorageService(tempFile);

            // Act: reload settings
            var reloaded = service.LoadSettings();
            var acc3 = reloaded.Instances.Find(i => i.Name == "Account 03");

            // Assert: Must be sanitized to Stopped and ProcessId cleared
            Assert.NotNull(acc3);
            Assert.Equal(InstanceStatus.Stopped, acc3.Status);
            Assert.Null(acc3.ProcessId);
            Assert.Equal("Stopped", acc3.StatusMessage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SaveSettings_WhenInstanceStatusIsStarting_NeverPersistsStartingToDisk()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"config_save_{Guid.NewGuid():N}.json");

        try
        {
            var service = new ConfigStorageService(tempFile);
            var settings = new AppSettings();
            settings.Instances.Add(new StudioInstanceProfile
            {
                Name = "Account 04",
                Status = InstanceStatus.Starting
            });

            // Act
            service.SaveSettings(settings);

            // Assert
            string json = File.ReadAllText(tempFile);
            Assert.DoesNotContain("\"Status\": 1", json); // 1 = InstanceStatus.Starting

            var reloaded = service.LoadSettings();
            var acc4 = reloaded.Instances.Find(i => i.Name == "Account 04");
            Assert.NotNull(acc4);
            Assert.Equal(InstanceStatus.Stopped, acc4.Status);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
