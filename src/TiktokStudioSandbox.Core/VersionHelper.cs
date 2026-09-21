using System;

namespace TiktokStudioSandbox.Core;

public static class VersionHelper
{
    public static Version ParseVersionSafe(string? versionStr)
    {
        if (string.IsNullOrWhiteSpace(versionStr)) return new Version(0, 0, 0, 0);
        string clean = versionStr.Trim().TrimStart('v', 'V');
        var parts = clean.Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out int mj) ? mj : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out int mn) ? mn : 0;
        int build = parts.Length > 2 && int.TryParse(parts[2], out int b) ? b : 0;
        int rev = parts.Length > 3 && int.TryParse(parts[3], out int r) ? r : 0;
        return new Version(major, minor, build, rev);
    }

    public static bool IsVersionNewer(string? remoteVersionStr, string? currentVersionStr)
    {
        var remote = ParseVersionSafe(remoteVersionStr);
        var current = ParseVersionSafe(currentVersionStr);
        return remote > current;
    }

    public static bool IsVersionBelowMin(string? minVersionStr, string? currentVersionStr)
    {
        if (string.IsNullOrWhiteSpace(minVersionStr)) return false;
        var min = ParseVersionSafe(minVersionStr);
        var current = ParseVersionSafe(currentVersionStr);
        return current < min;
    }

    /// <summary>
    /// Evaluates whether an update prompt should be shown.
    /// Crucial: If current version is already at or above remote version and not below min required,
    /// NEVER prompt for an update even if IsMandatory is true!
    /// </summary>
    public static bool ShouldPromptUpdate(string? remoteVersion, string? minRequiredVersion, string? currentVersion, bool isMandatory)
    {
        bool isNewer = IsVersionNewer(remoteVersion, currentVersion);
        bool isBelowMin = IsVersionBelowMin(minRequiredVersion, currentVersion);

        if (!isNewer && !isBelowMin)
        {
            return false;
        }

        return true;
    }
}
