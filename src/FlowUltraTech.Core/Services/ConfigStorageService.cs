using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public class ConfigStorageService : IConfigStorageService
{
    private const string DpapiPrefix = "enc:dpapi:";
    private readonly string _configFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static string ProtectString(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        if (plainText.StartsWith(DpapiPrefix, StringComparison.Ordinal)) return plainText;
        if (!OperatingSystem.IsWindows()) return plainText;

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        IntPtr inPtr = Marshal.AllocHGlobal(plainBytes.Length);
        try
        {
            Marshal.Copy(plainBytes, 0, inPtr, plainBytes.Length);
            var dataIn = new DATA_BLOB { cbData = plainBytes.Length, pbData = inPtr };
            if (CryptProtectData(ref dataIn, "FlowUltraTechSecret", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x1, out var dataOut))
            {
                try
                {
                    byte[] encBytes = new byte[dataOut.cbData];
                    Marshal.Copy(dataOut.pbData, encBytes, 0, dataOut.cbData);
                    return DpapiPrefix + Convert.ToBase64String(encBytes);
                }
                finally
                {
                    if (dataOut.pbData != IntPtr.Zero) LocalFree(dataOut.pbData);
                }
            }
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
        }

        return plainText;
    }

    public static string UnprotectString(string? storedValue, out bool wasPlaintext)
    {
        wasPlaintext = false;
        if (string.IsNullOrEmpty(storedValue)) return string.Empty;
        if (!storedValue.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            wasPlaintext = true;
            return storedValue;
        }
        if (!OperatingSystem.IsWindows()) return storedValue.Substring(DpapiPrefix.Length);

        try
        {
            byte[] encBytes = Convert.FromBase64String(storedValue.Substring(DpapiPrefix.Length));
            IntPtr inPtr = Marshal.AllocHGlobal(encBytes.Length);
            try
            {
                Marshal.Copy(encBytes, 0, inPtr, encBytes.Length);
                var dataIn = new DATA_BLOB { cbData = encBytes.Length, pbData = inPtr };
                if (CryptUnprotectData(ref dataIn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x1, out var dataOut))
                {
                    try
                    {
                        byte[] plainBytes = new byte[dataOut.cbData];
                        Marshal.Copy(dataOut.pbData, plainBytes, 0, dataOut.cbData);
                        return Encoding.UTF8.GetString(plainBytes);
                    }
                    finally
                    {
                        if (dataOut.pbData != IntPtr.Zero) LocalFree(dataOut.pbData);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(inPtr);
            }
        }
        catch { }

        return string.Empty;
    }

    private static bool DecryptSettingsInPlace(AppSettings settings)
    {
        bool needsMigrationSave = false;

        settings.AuthToken = UnprotectString(settings.AuthToken, out bool p1);
        needsMigrationSave |= p1;

        settings.RefreshToken = UnprotectString(settings.RefreshToken, out bool p2);
        needsMigrationSave |= p2;

        settings.CaptchaSolverJwtToken = UnprotectString(settings.CaptchaSolverJwtToken, out bool p3);
        needsMigrationSave |= p3;

        settings.TelegramBotToken = UnprotectString(settings.TelegramBotToken, out bool p4);
        needsMigrationSave |= p4;

        if (settings.Proxies != null)
        {
            foreach (var proxy in settings.Proxies)
            {
                proxy.Password = UnprotectString(proxy.Password, out bool pp);
                needsMigrationSave |= pp;
            }
        }

        return needsMigrationSave;
    }

    private string SerializeWithDpapi(AppSettings settings)
    {
        string origAuth = settings.AuthToken;
        string origRefresh = settings.RefreshToken;
        string origSolverJwt = settings.CaptchaSolverJwtToken;
        string origTelegram = settings.TelegramBotToken;
        var origProxyPasswords = new List<(ProxyItem Proxy, string Password)>();

        try
        {
            settings.AuthToken = ProtectString(origAuth);
            settings.RefreshToken = ProtectString(origRefresh);
            settings.CaptchaSolverJwtToken = ProtectString(origSolverJwt);
            settings.TelegramBotToken = ProtectString(origTelegram);

            if (settings.Proxies != null)
            {
                foreach (var proxy in settings.Proxies)
                {
                    origProxyPasswords.Add((proxy, proxy.Password));
                    proxy.Password = ProtectString(proxy.Password);
                }
            }

            return JsonSerializer.Serialize(settings, _jsonOptions);
        }
        finally
        {
            settings.AuthToken = origAuth;
            settings.RefreshToken = origRefresh;
            settings.CaptchaSolverJwtToken = origSolverJwt;
            settings.TelegramBotToken = origTelegram;
            foreach (var (proxy, pwd) in origProxyPasswords)
            {
                proxy.Password = pwd;
            }
        }
    }

    public ConfigStorageService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _configFilePath = customPath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "FlowUltraTech");
            string legacyFolder = Path.Combine(appData, "TiktokStudioSandbox");
            if (!Directory.Exists(folder) && Directory.Exists(legacyFolder))
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    string legacyConfig = Path.Combine(legacyFolder, "config.json");
                    string newConfig = Path.Combine(folder, "config.json");
                    if (File.Exists(legacyConfig) && !File.Exists(newConfig))
                    {
                        File.Copy(legacyConfig, newConfig, overwrite: false);
                    }
                }
                catch { }
            }
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
                    bool needsMigrationSave = DecryptSettingsInPlace(settings);

                    if (!string.IsNullOrEmpty(settings.DefaultChromiumFlags) && 
                        settings.DefaultChromiumFlags.Contains("--disable-background-timer-throttling=false"))
                    {
                        settings.DefaultChromiumFlags = string.Empty;
                        needsMigrationSave = true;
                    }

                    // Sanitize runtime states: Starting state should NEVER survive app restart
                    foreach (var inst in settings.Instances)
                    {
                        if (inst.Status is InstanceStatus.Starting)
                        {
                            inst.Status = InstanceStatus.Stopped;
                            inst.StatusMessage = "Stopped";
                            inst.ProcessId = null;
                        }
                    }

                    if (needsMigrationSave)
                    {
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

        // Never persist transient 'Starting' state to disk
        foreach (var inst in settings.Instances)
        {
            if (inst.Status is InstanceStatus.Starting)
            {
                inst.Status = InstanceStatus.Stopped;
                inst.ProcessId = null;
                inst.StatusMessage = "Stopped";
            }
        }

        string json = SerializeWithDpapi(settings);
        await File.WriteAllTextAsync(_configFilePath, json);
    }

    public void SaveSettings(AppSettings settings)
    {
        string dir = Path.GetDirectoryName(_configFilePath) ?? string.Empty;
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Never persist transient 'Starting' state to disk
        foreach (var inst in settings.Instances)
        {
            if (inst.Status is InstanceStatus.Starting)
            {
                inst.Status = InstanceStatus.Stopped;
                inst.ProcessId = null;
                inst.StatusMessage = "Stopped";
            }
        }

        string json = SerializeWithDpapi(settings);
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
