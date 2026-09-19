using System;
using TiktokStudioSandbox.Core.Security;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class VmDetectorTests
{
    [Fact]
    public void Detect_RunsWithoutException()
    {
        var result = VmDetector.Detect();

        Assert.NotNull(result);
        Assert.NotNull(result.DetectedIndicators);
        string text = result.ToString();
        Assert.NotNull(text);
    }

    [Fact]
    public void VmDetectionResult_PropertiesFunctionCorrectly()
    {
        var res = new VmDetectionResult
        {
            IsVirtualMachine = true,
            HypervisorType = "VMware",
            Reason = "Detected VMware guest drivers"
        };
        res.DetectedIndicators.Add("vmmouse.sys");

        Assert.True(res.IsVirtualMachine);
        Assert.Equal("VMware", res.HypervisorType);
        Assert.Single(res.DetectedIndicators);
        Assert.Contains("VMware", res.ToString());
    }
}
