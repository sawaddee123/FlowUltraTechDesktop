using System;
using System.Text.Json;
using System.Threading.Tasks;
using FlowUltraTech.Core.Interfaces;
using Xunit;

namespace FlowUltraTech.Tests;

public class AppUpdateBroadcastTests
{
    [Fact]
    public void AppUpdateEventArgs_Properties_RetainAssignedValues()
    {
        var now = DateTime.UtcNow;
        var args = new AppUpdateEventArgs
        {
            Version = "1.2.0",
            MinRequiredVersion = "1.0.0",
            Title = "FlowUltraTech v1.2.0",
            Description = "- Zero downtime resilience\n- Superadmin broadcast",
            DownloadUrl = "/storage/releases/installer_v1.2.0.exe",
            IsMandatory = true,
            FileSize = 25000000,
            FileHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            PublishedAt = now
        };

        Assert.Equal("1.2.0", args.Version);
        Assert.Equal("1.0.0", args.MinRequiredVersion);
        Assert.Equal("FlowUltraTech v1.2.0", args.Title);
        Assert.Equal("- Zero downtime resilience\n- Superadmin broadcast", args.Description);
        Assert.Equal("/storage/releases/installer_v1.2.0.exe", args.DownloadUrl);
        Assert.True(args.IsMandatory);
        Assert.Equal(25000000, args.FileSize);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", args.FileHash);
        Assert.Equal(now, args.PublishedAt);
    }

    [Fact]
    public void VersionComparison_DetectsNewerVersionProperly()
    {
        var currentVer = new Version("0.1.0");

        bool isNewer1 = Version.TryParse("1.0.0".TrimStart('v', 'V'), out var v1) && v1 > currentVer;
        Assert.True(isNewer1);

        bool isNewer2 = Version.TryParse("v1.2.0".TrimStart('v', 'V'), out var v2) && v2 > currentVer;
        Assert.True(isNewer2);

        bool isOlder = Version.TryParse("0.0.9".TrimStart('v', 'V'), out var v3) && v3 > currentVer;
        Assert.False(isOlder);
    }

    [Theory]
    [InlineData("0.1.1", "0.1.0", "0.1.1", true, false)]   // Same version, mandatory=true -> MUST NOT PROMPT!
    [InlineData("0.1.1", "0.1.0", "0.1.1", false, false)]  // Same version, optional -> MUST NOT PROMPT!
    [InlineData("v0.1.1", "0.1.0", "0.1.1", true, false)]  // Same version with prefix 'v' -> MUST NOT PROMPT!
    [InlineData("0.1.1.0", "0.1.0", "0.1.1", true, false)]// 4-part vs 3-part equal -> MUST NOT PROMPT!
    [InlineData("0.1.0", "0.1.0", "0.1.1", true, false)]   // Remote is older -> MUST NOT PROMPT!
    [InlineData("0.1.2", "0.1.0", "0.1.1", false, true)]   // Remote is newer, optional -> MUST PROMPT
    [InlineData("0.1.2", "0.1.0", "0.1.1", true, true)]    // Remote is newer, mandatory -> MUST PROMPT
    [InlineData("0.1.1", "0.1.2", "0.1.1", false, true)]   // Current is below minRequired -> MUST PROMPT
    public void ShouldPromptUpdate_EnforcesCorrectBehavior(
        string remoteVer, string minVer, string currentVer, bool isMandatory, bool expectedPrompt)
    {
        bool result = FlowUltraTech.Core.VersionHelper.ShouldPromptUpdate(remoteVer, minVer, currentVer, isMandatory);
        Assert.Equal(expectedPrompt, result);
    }
}
