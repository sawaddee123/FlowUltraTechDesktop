using System.Collections.Generic;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Interfaces;

public interface IProxifierService
{
    string GenerateProfileXml(IEnumerable<StudioInstanceProfile> instances, IEnumerable<ProxyItem> proxies);
    void UpdateProfileFile(string ppxPath, IEnumerable<StudioInstanceProfile> instances, IEnumerable<ProxyItem> proxies);
    bool ReloadProxifier(string proxifierExePath, string ppxPath);
}
