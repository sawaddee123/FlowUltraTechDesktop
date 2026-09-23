using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public class ProxifierService : IProxifierService
{
    public string GenerateProfileXml(IEnumerable<StudioInstanceProfile> instances, IEnumerable<ProxyItem> proxies)
    {
        var root = new XElement("ProxifierProfile",
            new XAttribute("version", "102"),
            new XAttribute("platform", "Windows"),
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema")
        );

        // Options
        var options = new XElement("Options",
            new XElement("Resolve",
                new XElement("AutoModeDetection", new XAttribute("enabled", "false")),
                new XElement("ViaProxy", new XAttribute("enabled", "true")),
                new XElement("ExclusionList", "%ComputerName%; localhost; 127.0.0.1; %Network%")
            ),
            new XElement("Encryption", new XAttribute("mode", "disabled")),
            new XElement("HttpProxiesSupport", new XAttribute("enabled", "false")),
            new XElement("HandleDirectConnections", new XAttribute("enabled", "false")),
            new XElement("ConnectionLoopDetection", new XAttribute("enabled", "true"))
        );
        root.Add(options);

        // ProxyList
        var proxyList = new XElement("ProxyList");
        foreach (var p in proxies.Where(p => p.IsActive))
        {
            var proxyElem = new XElement("Proxy",
                new XAttribute("id", p.ProxifierId),
                new XAttribute("type", p.Protocol == ProxyProtocol.Socks5 ? "SOCKS5" : "HTTPS"),
                new XElement("Address", p.Host),
                new XElement("Port", p.Port),
                new XElement("Options", 48) // DNS resolution through proxy flag
            );

            if (!string.IsNullOrWhiteSpace(p.Username))
            {
                var auth = new XElement("Authentication",
                    new XAttribute("enabled", "true"),
                    new XElement("Username", p.Username),
                    new XElement("Password", p.Password)
                );
                proxyElem.Add(auth);
            }
            proxyList.Add(proxyElem);
        }
        root.Add(proxyList);

        // RuleList
        var ruleList = new XElement("RuleList");
        
        // Custom rules for instances with an assigned proxy
        foreach (var inst in instances.Where(i => i.ProxyId.HasValue))
        {
            var matchingProxy = proxies.FirstOrDefault(p => p.Id == inst.ProxyId);
            if (matchingProxy != null)
            {
                string folderTag = !string.IsNullOrWhiteSpace(inst.ProfileFolderName)
                    ? inst.ProfileFolderName
                    : inst.Name;

                string ruleName = $"TikTok_{folderTag}";

                // Provide per-instance isolated executable filter (TikTok LIVE Studio_{folderTag}.exe)
                // along with general executable name so Proxifier routes traffic distinctly per profile
                string appTargets = $"TikTok LIVE Studio_{folderTag}.exe; TikTok LIVE Studio.exe; live-studio.exe";

                var rule = new XElement("Rule",
                    new XAttribute("enabled", "true"),
                    new XElement("Name", ruleName),
                    new XElement("Applications", appTargets),
                    new XElement("Action", new XAttribute("type", "Proxy"), matchingProxy.ProxifierId)
                );
                ruleList.Add(rule);
            }
        }

        // Default Direct Rule
        var defaultRule = new XElement("Rule",
            new XAttribute("enabled", "true"),
            new XElement("Name", "Default"),
            new XElement("Action", new XAttribute("type", "Direct"))
        );
        ruleList.Add(defaultRule);

        root.Add(ruleList);

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);
        return doc.ToString();
    }

    public void UpdateProfileFile(string ppxPath, IEnumerable<StudioInstanceProfile> instances, IEnumerable<ProxyItem> proxies)
    {
        if (string.IsNullOrWhiteSpace(ppxPath))
        {
            throw new ArgumentException("Proxifier profile path cannot be empty.", nameof(ppxPath));
        }

        string dir = Path.GetDirectoryName(ppxPath) ?? string.Empty;
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (!File.Exists(ppxPath))
        {
            // Generate full fresh file
            string xml = GenerateProfileXml(instances, proxies);
            File.WriteAllText(ppxPath, xml);
            return;
        }

        // Update existing file
        var doc = XDocument.Load(ppxPath);
        var ruleList = doc.Descendants("RuleList").FirstOrDefault();
        if (ruleList == null)
        {
            ruleList = new XElement("RuleList");
            doc.Root?.Add(ruleList);
        }

        // Remove old TikTok rules (including legacy TikTok_Sandbox_ rules)
        ruleList.Elements("Rule")
            .Where(r => r.Element("Name")?.Value.StartsWith("TikTok_") == true)
            .Remove();

        // Update ProxyList
        var proxyList = doc.Descendants("ProxyList").FirstOrDefault();
        if (proxyList == null)
        {
            proxyList = new XElement("ProxyList");
            doc.Root?.Add(proxyList);
        }

        foreach (var p in proxies.Where(p => p.IsActive))
        {
            var existing = proxyList.Elements("Proxy").FirstOrDefault(x => (string?)x.Attribute("id") == p.ProxifierId.ToString());
            if (existing != null)
            {
                existing.Element("Address")?.SetValue(p.Host);
                existing.Element("Port")?.SetValue(p.Port);
            }
            else
            {
                proxyList.Add(new XElement("Proxy",
                    new XAttribute("id", p.ProxifierId),
                    new XAttribute("type", p.Protocol == ProxyProtocol.Socks5 ? "SOCKS5" : "HTTPS"),
                    new XElement("Address", p.Host),
                    new XElement("Port", p.Port),
                    new XElement("Options", 48)
                ));
            }
        }

        // Insert new rules before Default rule
        var defaultRule = ruleList.Elements("Rule").FirstOrDefault(r => r.Element("Name")?.Value == "Default");
        foreach (var inst in instances.Where(i => i.ProxyId.HasValue))
        {
            var matchingProxy = proxies.FirstOrDefault(p => p.Id == inst.ProxyId);
            if (matchingProxy != null)
            {
                string ruleName = !string.IsNullOrWhiteSpace(inst.ProfileFolderName)
                    ? $"TikTok_{inst.ProfileFolderName}"
                    : $"TikTok_{inst.Name}";

                var rule = new XElement("Rule",
                    new XAttribute("enabled", "true"),
                    new XElement("Name", ruleName),
                    new XElement("Applications", "TikTok LIVE Studio.exe; live-studio.exe"),
                    new XElement("Action", new XAttribute("type", "Proxy"), matchingProxy.ProxifierId)
                );

                if (defaultRule != null)
                {
                    defaultRule.AddBeforeSelf(rule);
                }
                else
                {
                    ruleList.Add(rule);
                }
            }
        }

        doc.Save(ppxPath);
    }

    public bool ReloadProxifier(string proxifierExePath, string ppxPath)
    {
        if (!File.Exists(proxifierExePath) || !File.Exists(ppxPath))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = proxifierExePath,
                Arguments = $"\"{ppxPath}\" /silent-load",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProxifierService] Reload failed: {ex.Message}");
            return false;
        }
    }
}
