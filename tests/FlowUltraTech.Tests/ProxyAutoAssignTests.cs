using System;
using System.Linq;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

public class ProxyAutoAssignTests
{
    private static AppSettings Make(int accounts, int proxies)
    {
        var s = new AppSettings { ProxyMode = "auto" };
        for (int i = 0; i < accounts; i++) s.Instances.Add(new StudioInstanceProfile { Name = $"A{i}", ProfileFolderName = $"Acc{i}", CdpPort = 9300 + i });
        for (int i = 0; i < proxies; i++) s.Proxies.Add(new ProxyItem { Name = $"P{i}", Host = $"10.0.0.{i + 1}", Port = 1080 });
        return s;
    }

    [Fact]
    public void AutoAssign_GivesEachAccountADifferentProxy()
    {
        var s = Make(3, 3);
        LocalProxyBridgeService.AutoAssign(s);
        var ids = s.Instances.Select(i => i.ProxyId).ToList();
        Assert.All(ids, id => Assert.NotNull(id));
        Assert.Equal(3, ids.Distinct().Count());
    }

    [Fact]
    public void AutoAssign_KeepsWorkingAssignment_AndMovesAwayFromFailedProxy()
    {
        var s = Make(2, 3);
        LocalProxyBridgeService.AutoAssign(s);
        var keep = s.Instances[0].ProxyId;
        var failed = s.Proxies.First(p => p.Id == s.Instances[1].ProxyId);
        failed.LastTestOk = false;

        Assert.True(LocalProxyBridgeService.AutoAssign(s));
        Assert.Equal(keep, s.Instances[0].ProxyId);
        Assert.NotEqual(failed.Id, s.Instances[1].ProxyId);
        Assert.NotEqual(keep, s.Instances[1].ProxyId);
    }

    [Fact]
    public void AutoAssign_SkipsAccountsWithTheirOwnProxy_AndSharesWhenPoolIsSmall()
    {
        var s = Make(3, 1);
        s.Instances[0].UseCustomProxy = true;
        LocalProxyBridgeService.AutoAssign(s);
        Assert.Null(s.Instances[0].ProxyId);
        Assert.Equal(s.Proxies[0].Id, s.Instances[1].ProxyId);
        Assert.Equal(s.Proxies[0].Id, s.Instances[2].ProxyId);
    }

    [Fact]
    public void ProxyModeOff_ResolvesNoProxyEvenWhenAssigned()
    {
        var s = Make(1, 1);
        LocalProxyBridgeService.AutoAssign(s);
        s.ProxyMode = "off";
        Assert.Null(LocalProxyBridgeService.ResolveEffectiveProxy(s.Instances[0], s));
        s.ProxyMode = "auto";
        Assert.NotNull(LocalProxyBridgeService.ResolveEffectiveProxy(s.Instances[0], s));
    }
}
