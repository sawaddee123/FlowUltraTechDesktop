using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

public class ProcessLauncherPortTests
{
    [Fact]
    public async Task GetPidListeningOnPort_FindsChildProcessListener()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Assert.Null(ProcessLauncher.GetPidListeningOnPort(port));

        using var child = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"$l=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,{port});$l.Start();Start-Sleep 30\"",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        try
        {
            int? pid = null;
            for (int i = 0; i < 40 && pid == null; i++)
            {
                await Task.Delay(250);
                pid = ProcessLauncher.GetPidListeningOnPort(port);
            }
            Assert.Equal(child.Id, pid);
        }
        finally
        {
            child.Kill();
        }
    }
}
