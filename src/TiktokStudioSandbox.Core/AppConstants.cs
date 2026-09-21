using System;
using System.Reflection;

namespace TiktokStudioSandbox.Core;

public static class AppConstants
{
    /// <summary>
    /// Official Central Server Endpoint (Handles Auth, Dynamic Slots, and Captcha Solver)
    /// </summary>
    public const string DefaultServerEndpoint = "http://210.246.215.66:8001";

    private static readonly Lazy<string> _currentVersion = new(() =>
    {
        try
        {
            var entryAsm = Assembly.GetEntryAssembly();
            Assembly targetAsm = (entryAsm != null && entryAsm.GetName().Name?.StartsWith("TiktokStudioSandbox", StringComparison.OrdinalIgnoreCase) == true)
                ? entryAsm
                : typeof(AppConstants).Assembly;

            var infoVer = targetAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVer))
            {
                int plusIdx = infoVer.IndexOf('+');
                if (plusIdx > 0) infoVer = infoVer.Substring(0, plusIdx);
                return infoVer.Trim();
            }
            var ver = targetAsm.GetName().Version;
            if (ver != null)
            {
                return $"{ver.Major}.{ver.Minor}.{ver.Build}";
            }
        }
        catch { }
        return "0.1.7";
    });

    /// <summary>
    /// Current Application Version (Dynamically read from Assembly / csproj)
    /// </summary>
    public static string CurrentVersion => _currentVersion.Value;
}
