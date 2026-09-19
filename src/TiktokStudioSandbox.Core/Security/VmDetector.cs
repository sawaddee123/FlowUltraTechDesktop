using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace TiktokStudioSandbox.Core.Security;

public class VmDetectionResult
{
    public bool IsVirtualMachine { get; set; }
    public string? HypervisorType { get; set; }
    public string? Reason { get; set; }
    public List<string> DetectedIndicators { get; set; } = new();

    public override string ToString()
    {
        return IsVirtualMachine
            ? $"[VM Detected: {HypervisorType}] {Reason} (Indicators: {string.Join(", ", DetectedIndicators)})"
            : "[Physical Hardware] No virtual environment detected.";
    }
}

/// <summary>
/// Detects if the current environment is running inside a Virtual Machine or Sandbox (VMware, VirtualBox, Sandboxie).
/// Examines Motherboard/BIOS Manufacturer, Virtual Device Drivers, Process Lists, and Injected Modules.
/// </summary>
public static class VmDetector
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    public static VmDetectionResult Detect()
    {
        var result = new VmDetectionResult();

        // 1. Check Sandboxie
        CheckSandboxie(result);

        // 2. Check Virtual Drivers in System32
        CheckVirtualDrivers(result);

        // 3. Check Running VM Processes
        CheckVirtualProcesses(result);

        // 4. Check Hardware WMI (System Manufacturer & BIOS)
        CheckHardwareWmi(result);

        result.IsVirtualMachine = result.DetectedIndicators.Count > 0;
        if (result.IsVirtualMachine && string.IsNullOrEmpty(result.Reason))
        {
            result.Reason = $"ตรวจพบสภาพแวดล้อมเสมือน ({result.HypervisorType ?? "Virtualization"})";
        }

        return result;
    }

    private static void CheckSandboxie(VmDetectionResult result)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            // Sandboxie injects SbieDll.dll into every sandboxed process
            IntPtr sbieModule = GetModuleHandle("SbieDll.dll");
            if (sbieModule != IntPtr.Zero)
            {
                result.HypervisorType ??= "Sandboxie";
                result.DetectedIndicators.Add("Sandboxie Module Injected (SbieDll.dll)");
            }
        }
        catch { }

        try
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (File.Exists(Path.Combine(winDir, "System32", "drivers", "SbieDrv.sys")))
            {
                result.HypervisorType ??= "Sandboxie";
                result.DetectedIndicators.Add("Sandboxie Driver Found (SbieDrv.sys)");
            }
        }
        catch { }
    }

    private static void CheckVirtualDrivers(VmDetectionResult result)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            string driversDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers");
            if (!Directory.Exists(driversDir)) return;

            // VirtualBox Drivers
            string[] vboxDrivers = ["VBoxMouse.sys", "VBoxGuest.sys", "VBoxSF.sys", "VBoxVideo.sys"];
            foreach (var driver in vboxDrivers)
            {
                if (File.Exists(Path.Combine(driversDir, driver)))
                {
                    result.HypervisorType ??= "VirtualBox";
                    result.DetectedIndicators.Add($"VirtualBox Driver ({driver})");
                }
            }

            // VMware Drivers
            string[] vmwareDrivers = ["vmmouse.sys", "vmhgfs.sys", "vmusbmouse.sys", "vm3dmp.sys", "vmx_svga.sys"];
            foreach (var driver in vmwareDrivers)
            {
                if (File.Exists(Path.Combine(driversDir, driver)))
                {
                    result.HypervisorType ??= "VMware";
                    result.DetectedIndicators.Add($"VMware Driver ({driver})");
                }
            }
        }
        catch { }
    }

    private static void CheckVirtualProcesses(VmDetectionResult result)
    {
        try
        {
            var processes = Process.GetProcesses().Select(p => p.ProcessName.ToLowerInvariant()).ToHashSet();

            // VirtualBox Processes
            string[] vboxProcesses = ["vboxservice", "vboxtray"];
            foreach (var proc in vboxProcesses)
            {
                if (processes.Contains(proc))
                {
                    result.HypervisorType ??= "VirtualBox";
                    result.DetectedIndicators.Add($"VirtualBox Process ({proc}.exe)");
                }
            }

            // VMware Processes
            string[] vmwareProcesses = ["vmtoolsd", "vmwaretray", "vmwareuser"];
            foreach (var proc in vmwareProcesses)
            {
                if (processes.Contains(proc))
                {
                    result.HypervisorType ??= "VMware";
                    result.DetectedIndicators.Add($"VMware Process ({proc}.exe)");
                }
            }

            // Sandboxie Processes
            string[] sbieProcesses = ["sandboxiedcomlaunch", "sandboxiecrypto", "sbiectrl", "sandman"];
            foreach (var proc in sbieProcesses)
            {
                if (processes.Contains(proc))
                {
                    result.HypervisorType ??= "Sandboxie";
                    result.DetectedIndicators.Add($"Sandboxie Process ({proc}.exe)");
                }
            }
        }
        catch { }
    }

    private static void CheckHardwareWmi(VmDetectionResult result)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            // Query Win32_ComputerSystem
            using var csSearcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in csSearcher.Get())
            {
                string manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                string model = obj["Model"]?.ToString() ?? "";

                if (manufacturer.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
                    model.Contains("VMware", StringComparison.OrdinalIgnoreCase))
                {
                    result.HypervisorType ??= "VMware";
                    result.DetectedIndicators.Add($"ComputerSystem: {manufacturer} {model}");
                }
                else if (manufacturer.Contains("innotek", StringComparison.OrdinalIgnoreCase) ||
                         manufacturer.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) ||
                         model.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase))
                {
                    result.HypervisorType ??= "VirtualBox";
                    result.DetectedIndicators.Add($"ComputerSystem: {manufacturer} {model}");
                }
                else if (manufacturer.Contains("QEMU", StringComparison.OrdinalIgnoreCase) ||
                         model.Contains("QEMU", StringComparison.OrdinalIgnoreCase))
                {
                    result.HypervisorType ??= "QEMU";
                    result.DetectedIndicators.Add($"ComputerSystem: {manufacturer} {model}");
                }
            }

            // Query Win32_BIOS
            using var biosSearcher = new ManagementObjectSearcher("SELECT SerialNumber, Version, SMBIOSBIOSVersion FROM Win32_BIOS");
            foreach (ManagementObject obj in biosSearcher.Get())
            {
                string serial = obj["SerialNumber"]?.ToString() ?? "";
                string version = obj["Version"]?.ToString() ?? "";
                string smbios = obj["SMBIOSBIOSVersion"]?.ToString() ?? "";
                string combined = $"{serial} {version} {smbios}";

                if (combined.Contains("VMware", StringComparison.OrdinalIgnoreCase))
                {
                    result.HypervisorType ??= "VMware";
                    result.DetectedIndicators.Add($"BIOS: {combined}");
                }
                else if (combined.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) ||
                         combined.Contains("VBOX", StringComparison.OrdinalIgnoreCase))
                {
                    result.HypervisorType ??= "VirtualBox";
                    result.DetectedIndicators.Add($"BIOS: {combined}");
                }
            }
        }
        catch
        {
            // Ignore WMI errors
        }
    }
}
