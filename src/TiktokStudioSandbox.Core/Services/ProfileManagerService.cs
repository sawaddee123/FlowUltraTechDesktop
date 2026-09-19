using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Services;

public class ProfileManagerService : IProfileManagerService
{
    private readonly IInstanceLogManager _logManager;

    public ProfileManagerService(IInstanceLogManager? logManager = null)
    {
        _logManager = logManager ?? InstanceLogManager.Shared;
    }

    public static string ResolveProfileDirectoryStatic(StudioInstanceProfile profile)
    {
        if (profile.LaunchMode == InstanceLaunchMode.DefaultProfile)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TikTok LIVE Studio");
        }

        if (!string.IsNullOrWhiteSpace(profile.UserDataDir))
        {
            return profile.UserDataDir;
        }

        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = !string.IsNullOrWhiteSpace(profile.ProfileFolderName) ? profile.ProfileFolderName : profile.SandboxBoxName;
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = "Acc01";
        }
        return Path.Combine(localApp, "TikTokStudioProfiles", folder);
    }

    public string ResolveProfileDirectory(StudioInstanceProfile profile) => ResolveProfileDirectoryStatic(profile);

    public UserProfileInfo? ReadProfileFromDirectory(string profileDirectory)
    {
        if (string.IsNullOrWhiteSpace(profileDirectory))
        {
            return null;
        }

        string resolvedDir = profileDirectory;
        if (!Directory.Exists(resolvedDir))
        {
            if (string.Equals(profileDirectory, "Default", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(profileDirectory, "DefaultProfile", StringComparison.OrdinalIgnoreCase))
            {
                resolvedDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TikTok LIVE Studio");
            }
            else
            {
                resolvedDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TikTokStudioProfiles", profileDirectory);
            }
        }

        if (!Directory.Exists(resolvedDir))
        {
            return null;
        }

        string ttStoreDir = Path.Combine(resolvedDir, "TTStore");
        string servicesPath = Path.Combine(ttStoreDir, "services.json");
        string localAvatar = Path.Combine(ttStoreDir, "avatarGaussian.png");
        if (!File.Exists(localAvatar))
        {
            localAvatar = Path.Combine(ttStoreDir, "avatar.png");
        }
        if (!File.Exists(localAvatar) && Directory.Exists(ttStoreDir))
        {
            var anyPng = Directory.GetFiles(ttStoreDir, "*avatar*.png", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (anyPng != null) localAvatar = anyPng;
        }

        // Fallback: If target folder doesn't have avatar yet, check Default Profile
        if (!File.Exists(localAvatar))
        {
            string defaultAvatar = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TikTok LIVE Studio", "TTStore", "avatarGaussian.png");
            if (File.Exists(defaultAvatar))
            {
                try
                {
                    Directory.CreateDirectory(ttStoreDir);
                    string targetAvatarPath = Path.Combine(ttStoreDir, "avatarGaussian.png");
                    File.Copy(defaultAvatar, targetAvatarPath, true);
                    localAvatar = targetAvatarPath;
                }
                catch { }
            }
        }

        if (!File.Exists(servicesPath))
        {
            return new UserProfileInfo();
        }

        string? jsonContent = ReadFileWithRetry(servicesPath, maxRetries: 3, delayMs: 200);
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            return new UserProfileInfo();
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("AccountService", out var accountService) &&
                accountService.TryGetProperty("state", out var state) &&
                state.TryGetProperty("accountList", out var accountList) &&
                accountList.ValueKind == JsonValueKind.Array &&
                accountList.GetArrayLength() > 0)
            {
                var firstAccount = accountList[0];
                var info = new UserProfileInfo();

                if (firstAccount.TryGetProperty("userId", out var u) && u.ValueKind == JsonValueKind.String)
                {
                    info.UserId = u.GetString();
                }
                if (firstAccount.TryGetProperty("nickname", out var n) && n.ValueKind == JsonValueKind.String)
                {
                    info.Nickname = n.GetString();
                }
                if (firstAccount.TryGetProperty("displayId", out var d) && d.ValueKind == JsonValueKind.String)
                {
                    info.DisplayId = d.GetString();
                }
                if (firstAccount.TryGetProperty("avatar", out var a) && a.ValueKind == JsonValueKind.String)
                {
                    info.AvatarUrl = a.GetString();
                }
                if (firstAccount.TryGetProperty("sessionId", out var s) && s.ValueKind == JsonValueKind.String)
                {
                    info.SessionId = s.GetString();
                }

                if (File.Exists(localAvatar))
                {
                    info.LocalAvatarPath = localAvatar;
                }

                return info;
            }
        }
        catch
        {
            // Json parsing error or format mismatch
        }

        return new UserProfileInfo();
    }

    public UserProfileInfo? ReadProfile(StudioInstanceProfile profile)
    {
        string dir = ResolveProfileDirectory(profile);
        return ReadProfileFromDirectory(dir);
    }

    public bool UpdateProfileInfo(StudioInstanceProfile profile)
    {
        var logger = _logManager.GetLogger(profile);
        var info = ReadProfile(profile);
        if (info != null && info.IsLoggedIn)
        {
            profile.LoggedInUserId = info.UserId;
            profile.LoggedInNickname = info.Nickname;
            profile.LoggedInDisplayId = info.DisplayId;
            profile.LoggedInAvatarUrl = !string.IsNullOrWhiteSpace(info.LocalAvatarPath) && File.Exists(info.LocalAvatarPath)
                ? info.LocalAvatarPath
                : info.AvatarUrl;
            profile.IsLoggedIn = true;
            profile.LastProfileSync = DateTime.Now;
            logger.Info("Profile", $"Found login session: @{profile.LoggedInDisplayId} ({profile.LoggedInNickname}), UID: {profile.LoggedInUserId}");
            return true;
        }

        profile.LoggedInUserId = null;
        profile.LoggedInNickname = null;
        profile.LoggedInDisplayId = null;
        profile.LoggedInAvatarUrl = null;
        profile.IsLoggedIn = false;
        profile.LastProfileSync = DateTime.Now;
        logger.Debug("Profile", "No active login session found in profile directory.");
        return false;
    }

    public IDisposable WatchProfile(StudioInstanceProfile profile, Action<UserProfileInfo> onProfileChanged)
    {
        var logger = _logManager.GetLogger(profile);
        string dir = ResolveProfileDirectory(profile);
        string watchDir = Path.Combine(dir, "TTStore");
        logger.Info("Profile", $"Started watching profile directory for changes: {watchDir}");

        if (!Directory.Exists(watchDir))
        {
            try
            {
                Directory.CreateDirectory(watchDir);
            }
            catch
            {
                return new EmptyDisposable();
            }
        }

        var cts = new CancellationTokenSource();
        FileSystemWatcher? watcher = null;

        try
        {
            watcher = new FileSystemWatcher(watchDir)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
        }
        catch { }

        object lockObj = new object();
        CancellationTokenSource? debouncer = null;
        string? lastNotifiedSignature = null;

        void CheckAndNotify()
        {
            var updated = ReadProfile(profile);
            if (updated != null && updated.IsLoggedIn)
            {
                string sig = $"{updated.UserId}_{updated.DisplayId}_{updated.Nickname}_{updated.LocalAvatarPath ?? updated.AvatarUrl}";
                if (sig != lastNotifiedSignature)
                {
                    lastNotifiedSignature = sig;
                    onProfileChanged(updated);
                }
            }
        }

        void TriggerUpdate()
        {
            lock (lockObj)
            {
                debouncer?.Cancel();
                debouncer?.Dispose();
                debouncer = new CancellationTokenSource();
                var token = debouncer.Token;

                Task.Delay(300, token).ContinueWith(t =>
                {
                    if (!token.IsCancellationRequested && !cts.IsCancellationRequested)
                    {
                        CheckAndNotify();
                    }
                }, TaskScheduler.Default);
            }
        }

        FileSystemEventHandler fileHandler = (s, e) =>
        {
            if (e.Name != null && (e.Name.Contains("services", StringComparison.OrdinalIgnoreCase) || 
                                   e.Name.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                                   e.Name.Contains("avatar", StringComparison.OrdinalIgnoreCase)))
            {
                TriggerUpdate();
            }
        };

        RenamedEventHandler renameHandler = (s, e) =>
        {
            if (e.Name != null && (e.Name.Contains("services", StringComparison.OrdinalIgnoreCase) || 
                                   e.Name.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                                   e.Name.Contains("avatar", StringComparison.OrdinalIgnoreCase)))
            {
                TriggerUpdate();
            }
        };

        if (watcher != null)
        {
            watcher.Changed += fileHandler;
            watcher.Created += fileHandler;
            watcher.Renamed += renameHandler;
        }

        // Safety Net: Polling loop every 1.5 seconds while watching (guarantees detection even if FileSystemWatcher misses atomic replace)
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1500, cts.Token).ConfigureAwait(false);
                    CheckAndNotify();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { }
            }
        }, cts.Token);

        return new ProfileWatcherSubscription(watcher, cts);
    }

    private static string? ReadFileWithRetry(string path, int maxRetries, int delayMs)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                return reader.ReadToEnd();
            }
            catch (IOException)
            {
                if (i == maxRetries - 1) return null;
                Thread.Sleep(delayMs);
            }
        }
        return null;
    }

    private class ProfileWatcherSubscription : IDisposable
    {
        private readonly FileSystemWatcher? _watcher;
        private readonly CancellationTokenSource _cts;

        public ProfileWatcherSubscription(FileSystemWatcher? watcher, CancellationTokenSource cts)
        {
            _watcher = watcher;
            _cts = cts;
        }

        public void Dispose()
        {
            if (_watcher != null)
            {
                try { _watcher.EnableRaisingEvents = false; } catch { }
                try { _watcher.Dispose(); } catch { }
            }
            try { _cts.Cancel(); _cts.Dispose(); } catch { }
        }
    }

    private class EmptyDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
