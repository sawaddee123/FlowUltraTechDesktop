using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace TiktokStudioSandbox.Core.Security;

/// <summary>
/// Generates a tamper-resistant, deep hardware fingerprint by combining:
/// 1. Motherboard Serial Number (Win32_BaseBoard)
/// 2. CPU Processor ID (Win32_Processor)
/// 3. Physical Disk Drive Serial Number (Win32_DiskDrive)
/// 4. Windows MachineGuid (HKLM\SOFTWARE\Microsoft\Cryptography)
/// Includes safe fallback for non-Windows or environments where WMI is inaccessible.
/// </summary>
public static class DeepHwidGenerator
{
    private static string? _cachedHwid;
    private static readonly object _lock = new();

    /// <summary>
    /// Generates or retrieves the cached SHA-256 Deep Hardware ID.
    /// </summary>
    public static string GenerateDeepHwid(bool forceRefresh = false)
    {
        if (!forceRefresh && !string.IsNullOrEmpty(_cachedHwid))
        {
            return _cachedHwid;
        }

        lock (_lock)
        {
            if (!forceRefresh && !string.IsNullOrEmpty(_cachedHwid))
            {
                return _cachedHwid;
            }

            string mbSerial = ReadWmiSafe("Win32_BaseBoard", "SerialNumber");
            string cpuId = ReadWmiSafe("Win32_Processor", "ProcessorId");
            string diskSerial = ReadWmiSafe("Win32_DiskDrive", "SerialNumber");
            string machineGuid = ReadRegistryMachineGuidSafe();

            // If WMI failed to return primary hardware identifiers, use fallback mechanism
            if (string.IsNullOrWhiteSpace(mbSerial) &&
                string.IsNullOrWhiteSpace(cpuId) &&
                string.IsNullOrWhiteSpace(diskSerial))
            {
                _cachedHwid = GenerateFallbackHwid(machineGuid);
                return _cachedHwid;
            }

            string rawPayload = $"{mbSerial}|{cpuId}|{diskSerial}|{machineGuid}";
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawPayload));
            _cachedHwid = Convert.ToHexString(hashBytes).ToLowerInvariant();
            return _cachedHwid;
        }
    }

    /// <summary>
    /// Generates or retrieves the cached SHA-256 Deep Hardware ID asynchronously without blocking the UI thread.
    /// </summary>
    public static Task<string> GenerateDeepHwidAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && !string.IsNullOrEmpty(_cachedHwid))
        {
            return Task.FromResult(_cachedHwid);
        }

        return Task.Run(() => GenerateDeepHwid(forceRefresh));
    }

    /// <summary>
    /// Safely queries WMI on Windows platforms without crashing if WMI service is broken.
    /// </summary>
    public static string ReadWmiSafe(string className, string propertyName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return string.Empty;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var val = obj[propertyName]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }
        }
        catch
        {
            // WMI service might be stopped, corrupt, or access denied.
        }

        return string.Empty;
    }

    /// <summary>
    /// Safely reads MachineGuid from Windows Registry (checking both 64-bit and 32-bit hives).
    /// </summary>
    public static string ReadRegistryMachineGuidSafe()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return string.Empty;
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var subKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var val = subKey?.GetValue("MachineGuid")?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(val))
            {
                return val;
            }
        }
        catch { }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var subKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var val = subKey?.GetValue("MachineGuid")?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(val))
            {
                return val;
            }
        }
        catch { }

        return string.Empty;
    }

    /// <summary>
    /// Robust fallback fingerprint when WMI service is completely unavailable.
    /// Combines MachineGuid, MAC addresses, MachineName, and system specs.
    /// </summary>
    public static string GenerateFallbackHwid(string? knownMachineGuid = null)
    {
        var sb = new StringBuilder();
        string machineGuid = knownMachineGuid ?? ReadRegistryMachineGuidSafe();
        sb.Append(machineGuid).Append('|');

        try
        {
            var macAddresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            n.OperationalStatus == OperationalStatus.Up)
                .Select(n => n.GetPhysicalAddress().ToString())
                .Where(m => !string.IsNullOrEmpty(m))
                .OrderBy(m => m);

            sb.Append(string.Join("-", macAddresses));
        }
        catch { }

        sb.Append('|').Append(Environment.MachineName);
        sb.Append('|').Append(Environment.UserName);
        sb.Append('|').Append(Environment.ProcessorCount);
        sb.Append('|').Append(RuntimeInformation.OSDescription);

        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
