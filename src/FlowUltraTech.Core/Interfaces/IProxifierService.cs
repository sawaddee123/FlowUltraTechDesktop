using System.Collections.Generic;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Interfaces;

public interface IProxifierService
{
    string GenerateProfileXml(IEnumerable<StudioInstanceProfile> instances, IEnumerable<ProxyItem> proxies);
    void UpdateProfileFile(string ppxPath, IEnumerable<StudioInstanceProfile> instances, IEnumerable<ProxyItem> proxies);
    bool ReloadProxifier(string proxifierExePath, string ppxPath);
}
