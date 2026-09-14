using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Services;

public class ConfigStorageService : IConfigStorageService
{
    private readonly string _configFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ConfigStorageService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _configFilePath = customPath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "TiktokStudioSandbox");
            Directory.CreateDirectory(folder);
            _configFilePath = Path.Combine(folder, "config.json");
        }
    }

    public AppSettings LoadSettings()
    {
        if (File.Exists(_configFilePath))
        {
            try
            {
                string json = File.ReadAllText(_configFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (settings != null)
                {
                    if (!string.IsNullOrEmpty(settings.DefaultChromiumFlags) && 
                        settings.DefaultChromiumFlags.Contains("--disable-background-timer-throttling=false"))
                    {
                        settings.DefaultChromiumFlags = string.Empty;
                        SaveSettings(settings);
                    }
                    return settings;
                }
            }
            catch
            {
                // Fallback to default
            }
        }

        var defaultSettings = CreateDefaultSettings();
        SaveSettings(defaultSettings);
        return defaultSettings;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        string dir = Path.GetDirectoryName(_configFilePath) ?? string.Empty;
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(_configFilePath, json);
    }

    public void SaveSettings(AppSettings settings)
    {
        string dir = Path.GetDirectoryName(_configFilePath) ?? string.Empty;
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(_configFilePath, json);
    }

    private static AppSettings CreateDefaultSettings()
    {
        var settings = new AppSettings();

        // Check if official Studio exists on system
        string[] possiblePaths = new[]
        {
            @"C:\Program Files\TikTok LIVE Studio\1.31.0\TikTok LIVE Studio.exe",
            @"C:\Program Files\TikTok LIVE Studio\TikTok LIVE Studio.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\TikTok LIVE Studio\TikTok LIVE Studio.exe")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                settings.TikTokStudioExePath = path;
                break;
            }
        }

        // Default Proxies
        var proxy1 = new ProxyItem
        {
            Name = "SOCKS5 Proxy #1",
            Host = "127.0.0.1",
            Port = 10808,
            Protocol = ProxyProtocol.Socks5,
            ProxifierId = 101
        };
        var proxy2 = new ProxyItem
        {
            Name = "SOCKS5 Proxy #2",
            Host = "127.0.0.1",
            Port = 10809,
            Protocol = ProxyProtocol.Socks5,
            ProxifierId = 102
        };
        settings.Proxies.Add(proxy1);
        settings.Proxies.Add(proxy2);

        // Default Instances
        settings.Instances.Add(new StudioInstanceProfile
        {
            Name = "Account 01 (Gaming)",
            ProfileFolderName = "Acc01",
            CdpPort = 9221,
            ProxyId = proxy1.Id,
            LaunchMode = InstanceLaunchMode.DirectProfile
        });

        settings.Instances.Add(new StudioInstanceProfile
        {
            Name = "Account 02 (Talk/Music)",
            ProfileFolderName = "Acc02",
            CdpPort = 9222,
            ProxyId = proxy2.Id,
            LaunchMode = InstanceLaunchMode.DirectProfile
        });

        // Default Auto Reply Rules
        settings.Rules.Add(new AutomationRule
        {
            Name = "Command !help",
            TriggerType = RuleTriggerType.ChatKeyword,
            Pattern = "!help",
            ResponseTemplate = "สวัสดีครับ @{User}! พิมพ์ !rules หรือ !social เพื่อดูข้อมูลเพิ่มเติมครับ",
            CooldownSeconds = 5
        });

        settings.Rules.Add(new AutomationRule
        {
            Name = "Gift Thanks (>= 10 Diamonds)",
            TriggerType = RuleTriggerType.GiftMinDiamonds,
            MinDiamonds = 10,
            ResponseTemplate = "ขอขอบคุณ @{User} มากครับสำหรับ {GiftName} 🙏🎉",
            CooldownSeconds = 2
        });

        return settings;
    }
}
