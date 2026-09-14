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
}
