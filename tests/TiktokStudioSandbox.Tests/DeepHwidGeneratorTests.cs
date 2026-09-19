using System;
using TiktokStudioSandbox.Core.Security;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class DeepHwidGeneratorTests
{
    [Fact]
    public void GenerateDeepHwid_ReturnsValidSha256Hex()
    {
        string hwid = DeepHwidGenerator.GenerateDeepHwid();

        Assert.NotNull(hwid);
        Assert.Equal(64, hwid.Length); // SHA-256 produces 32 bytes = 64 hex characters
        Assert.Matches("^[a-f0-9]{64}$", hwid);
    }

    [Fact]
    public void GenerateDeepHwid_IsConsistentAcrossCalls()
    {
        string hwid1 = DeepHwidGenerator.GenerateDeepHwid();
        string hwid2 = DeepHwidGenerator.GenerateDeepHwid();

        Assert.Equal(hwid1, hwid2);
    }

    [Fact]
    public async System.Threading.Tasks.Task GenerateDeepHwidAsync_ReturnsValidSha256Hex()
    {
        string hwid = await DeepHwidGenerator.GenerateDeepHwidAsync();

        Assert.NotNull(hwid);
        Assert.Equal(64, hwid.Length);
        Assert.Matches("^[a-f0-9]{64}$", hwid);
    }

    [Fact]
    public void GenerateFallbackHwid_ProducesValidSha256()
    {
        string fallbackHwid = DeepHwidGenerator.GenerateFallbackHwid("test-guid-1234");

        Assert.NotNull(fallbackHwid);
        Assert.Equal(64, fallbackHwid.Length);
        Assert.Matches("^[a-f0-9]{64}$", fallbackHwid);
    }

    [Fact]
    public void ReadWmiSafe_WhenInvalidClass_ReturnsEmptyWithoutThrowing()
    {
        string val = DeepHwidGenerator.ReadWmiSafe("NonExistent_WmiClass_1234", "FakeProp");
        Assert.NotNull(val);
    }

    [Fact]
    public void ReadRegistryMachineGuidSafe_DoesNotThrow()
    {
        var exception = Record.Exception(() => DeepHwidGenerator.ReadRegistryMachineGuidSafe());
        Assert.Null(exception);
    }
}
