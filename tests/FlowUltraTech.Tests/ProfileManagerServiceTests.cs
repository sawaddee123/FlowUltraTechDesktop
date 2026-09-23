using System;
using System.IO;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

public class ProfileManagerServiceTests
{
    private const string SampleServicesJson = @"{
  ""AccountService"": {
    ""state"": {
      ""accountList"": [
        {
          ""userId"": ""7280774255699526662"",
          ""nickname"": ""sawaddee"",
          ""avatar"": ""https://p16-common-sign.tiktokcdn.com/tos-alisg-avt-0068/avatar.webp"",
          ""displayId"": ""sawaddee_.123"",
          ""sessionId"": ""14781b549dc027926e8d87f664473e98"",
          ""timestamp"": 1788787193312,
          ""domain"": ""api16-normal-c-alisg.tiktokv.com""
        }
      ],
      ""secret"": ""b1ee51e5b56c7847d6f1609bee1c5a5fbbb0c484c517673b6a5758b2c93e5ecb""
    },
    ""version"": 0
  }
}";

    private const string SampleEmptyServicesJson = @"{
  ""AccountService"": {
    ""state"": {
      ""accountList"": []
    },
    ""version"": 0
  }
}";

    [Fact]
    public void ReadProfileFromDirectory_WhenValidServicesJson_ReturnsPopulatedUserProfileInfo()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"tt_profile_test_{Guid.NewGuid():N}");
        string ttStoreDir = Path.Combine(tempDir, "TTStore");
        Directory.CreateDirectory(ttStoreDir);
        File.WriteAllText(Path.Combine(ttStoreDir, "services.json"), SampleServicesJson);

        try
        {
            var service = new ProfileManagerService();
            var info = service.ReadProfileFromDirectory(tempDir);

            Assert.NotNull(info);
            Assert.True(info.IsLoggedIn);
            Assert.Equal("7280774255699526662", info.UserId);
            Assert.Equal("sawaddee", info.Nickname);
            Assert.Equal("sawaddee_.123", info.DisplayId);
            Assert.Equal("https://p16-common-sign.tiktokcdn.com/tos-alisg-avt-0068/avatar.webp", info.AvatarUrl);
            Assert.Equal("14781b549dc027926e8d87f664473e98", info.SessionId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void ReadProfileFromDirectory_WhenAccountListEmpty_ReturnsNotLoggedIn()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"tt_profile_test_{Guid.NewGuid():N}");
        string ttStoreDir = Path.Combine(tempDir, "TTStore");
        Directory.CreateDirectory(ttStoreDir);
        File.WriteAllText(Path.Combine(ttStoreDir, "services.json"), SampleEmptyServicesJson);

        try
        {
            var service = new ProfileManagerService();
            var info = service.ReadProfileFromDirectory(tempDir);

            Assert.NotNull(info);
            Assert.False(info.IsLoggedIn);
            Assert.Null(info.UserId);
            Assert.Null(info.DisplayId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void ReadProfileFromDirectory_WhenDirectoryMissing_ReturnsNull()
    {
        var service = new ProfileManagerService();
        var info = service.ReadProfileFromDirectory(@"C:\non_existent_folder_12345");
        Assert.Null(info);
    }

    [Fact]
    public void UpdateProfileInfo_WhenServicesJsonValid_UpdatesProfileModel()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"tt_profile_test_{Guid.NewGuid():N}");
        string ttStoreDir = Path.Combine(tempDir, "TTStore");
        Directory.CreateDirectory(ttStoreDir);
        File.WriteAllText(Path.Combine(ttStoreDir, "services.json"), SampleServicesJson);

        try
        {
            var service = new ProfileManagerService();
            var profile = new StudioInstanceProfile
            {
                UserDataDir = tempDir,
                LaunchMode = InstanceLaunchMode.DirectProfile
            };

            bool success = service.UpdateProfileInfo(profile);

            Assert.True(success);
            Assert.True(profile.IsLoggedIn);
            Assert.Equal("7280774255699526662", profile.LoggedInUserId);
            Assert.Equal("sawaddee", profile.LoggedInNickname);
            Assert.Equal("sawaddee_.123", profile.LoggedInDisplayId);
            Assert.False(string.IsNullOrEmpty(profile.LoggedInAvatarUrl));
            Assert.NotNull(profile.LastProfileSync);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
