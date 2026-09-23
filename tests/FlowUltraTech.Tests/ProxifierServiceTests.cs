using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

public class ProxifierServiceTests
{
    [Fact]
    public void GenerateProfileXml_ProducesValidXml_WithProxyRules()
    {
        // Arrange
        var service = new ProxifierService();
        var proxy = new ProxyItem
        {
            Id = Guid.NewGuid(),
            Name = "SG Proxy",
            Host = "1.2.3.4",
            Port = 1080,
            Protocol = ProxyProtocol.Socks5,
            ProxifierId = 105
        };

        var instance = new StudioInstanceProfile
        {
            Name = "Acc 1",
            ProfileFolderName = "Acc01",
            LaunchMode = InstanceLaunchMode.DirectProfile,
            ProxyId = proxy.Id
        };

        // Act
        string xml = service.GenerateProfileXml(new[] { instance }, new[] { proxy });

        // Assert
        Assert.NotNull(xml);
        var doc = XDocument.Parse(xml);
        
        var ruleList = doc.Descendants("RuleList").FirstOrDefault();
        Assert.NotNull(ruleList);

        var proxyRule = ruleList.Elements("Rule")
            .FirstOrDefault(r => (string?)r.Element("Name") == "TikTok_Acc01");
        
        Assert.NotNull(proxyRule);
        Assert.Contains("TikTok LIVE Studio.exe", (string?)proxyRule.Element("Applications"));
        Assert.Contains("TikTok LIVE Studio_Acc01.exe", (string?)proxyRule.Element("Applications"));
        Assert.Null(proxyRule.Element("SandboxName"));
        Assert.Equal("105", (string?)proxyRule.Element("Action"));

        var proxyList = doc.Descendants("ProxyList").FirstOrDefault();
        Assert.NotNull(proxyList);
        var proxyElem = proxyList.Elements("Proxy").FirstOrDefault(p => (string?)p.Attribute("id") == "105");
        Assert.NotNull(proxyElem);
        Assert.Equal("1.2.3.4", (string?)proxyElem.Element("Address"));
        Assert.Equal("1080", (string?)proxyElem.Element("Port"));
    }

    [Fact]
    public void UpdateProfileFile_CreatesFileIfMissing()
    {
        // Arrange
        var service = new ProxifierService();
        string tempFile = Path.Combine(Path.GetTempPath(), $"proxifier_test_{Guid.NewGuid():N}.ppx");

        try
        {
            var proxy = new ProxyItem { ProxifierId = 200, Host = "10.0.0.1", Port = 8080 };
            var instance = new StudioInstanceProfile { SandboxBoxName = "Box_Test", ProxyId = proxy.Id };

            // Act
            service.UpdateProfileFile(tempFile, new[] { instance }, new[] { proxy });

            // Assert
            Assert.True(File.Exists(tempFile));
            var doc = XDocument.Load(tempFile);
            Assert.NotNull(doc.Root);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
