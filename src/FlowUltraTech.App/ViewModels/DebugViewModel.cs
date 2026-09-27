using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.App.ViewModels;

public partial class DebugViewModel : ObservableObject
{
    private readonly ICdpAutomationService _cdpService;

    [ObservableProperty]
    private int _targetPort = 9221;

    [ObservableProperty]
    private string _connectionStatus = "⚪ ไม่ได้เชื่อมต่อ";

    // 1. Script Console
    [ObservableProperty]
    private string _scriptInput = "window.location.href";

    [ObservableProperty]
    private string _consoleOutput = "ยินดีต้อนรับสู่ CDP Script Debug Console!\nพิมพ์ JavaScript แล้วกด [▶ รันสคริปต์] เพื่อทดสอบกับ TikTok LIVE Studio...\n";

    // 2. Network Hook
    [ObservableProperty]
    private bool _isHooking;

    [ObservableProperty]
    private ObservableCollection<NetworkTrafficItem> _networkTraffic = new();

    [ObservableProperty]
    private NetworkTrafficItem? _selectedTraffic;

    [ObservableProperty]
    private string _filterText = string.Empty;

    // 3. Storage & Cookies
    [ObservableProperty]
    private ObservableCollection<CookieItem> _cookies = new();

    [ObservableProperty]
    private ObservableCollection<StorageEntry> _localStorageItems = new();

    [ObservableProperty]
    private ObservableCollection<StorageEntry> _sessionStorageItems = new();

    [ObservableProperty]
    private string _sessionIdToken = "-";

    [ObservableProperty]
    private string _csrfToken = "-";

    [ObservableProperty]
    private string _storageStatus = "ยังไม่ได้ดึงข้อมูล";

    [ObservableProperty]
    private string? _lastExportedHtmlPath;

    // 4. Captcha Inspector
    [ObservableProperty]
    private ObservableCollection<CaptchaElementItem> _captchaElements = new();

    [ObservableProperty]
    private CaptchaElementItem? _selectedCaptchaElement;

    [ObservableProperty]
    private string _captchaRawHtml = string.Empty;

    [ObservableProperty]
    private int _detectedCaptchaCount;

    [ObservableProperty]
    private string _captchaStatusText = "ยังไม่ได้ตรวจจับ";

    public DebugViewModel(ICdpAutomationService cdpService)
    {
        _cdpService = cdpService;
    }

    [RelayCommand]
    public async Task CheckCdpStatusAsync()
    {
        bool ok = await _cdpService.CheckConnectionAsync(TargetPort);
        ConnectionStatus = ok ? $"🟢 เชื่อมต่อพอร์ต {TargetPort} สำเร็จ" : $"🔴 พอร์ต {TargetPort} ไม่ตอบสนอง (โปรด Start Studio ก่อน)";
    }

    [RelayCommand]
    public async Task ExecuteScriptAsync()
    {
        if (string.IsNullOrWhiteSpace(ScriptInput)) return;

        string time = DateTime.Now.ToString("HH:mm:ss");
        AppendConsole($"[{time}] > {ScriptInput}");

        string result = await _cdpService.ExecuteScriptAsync(TargetPort, ScriptInput);
        AppendConsole($"[{time}] < {result}\n");
    }

    [RelayCommand]
    public void ClearConsole()
    {
        ConsoleOutput = string.Empty;
    }

    [RelayCommand]
    public void CopyConsole()
    {
        if (!string.IsNullOrEmpty(ConsoleOutput))
        {
            Clipboard.SetText(ConsoleOutput);
        }
    }

    [RelayCommand]
    public async Task ToggleNetworkHookAsync()
    {
        if (!IsHooking)
        {
            IsHooking = true;
            await _cdpService.StartNetworkHookAsync(TargetPort, 
                onRequest: (item) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (NetworkTraffic.Count > 400) NetworkTraffic.RemoveAt(0);
                        NetworkTraffic.Add(item);
                    });
                },
                onResponse: (item) =>
                {
                    // IN-PLACE UPDATE (update อันเดิม)
                    // The item properties already trigger INotifyPropertyChanged!
                });
            AppendConsole($"[Network] เริ่มดักจับ API บนพอร์ต {TargetPort} แล้ว...");
        }
        else
        {
            IsHooking = false;
            await _cdpService.StopNetworkHookAsync(TargetPort);
            AppendConsole($"[Network] หยุดดักจับ API บนพอร์ต {TargetPort}");
        }
    }

    [RelayCommand]
    public void ClearTraffic()
    {
        NetworkTraffic.Clear();
        SelectedTraffic = null;
    }

    [RelayCommand]
    public async Task FetchCookiesAndStorageAsync()
    {
        StorageStatus = "กำลังดึงข้อมูล Cookies & Storage จาก Chromium...";
        var dump = await _cdpService.GetCookiesAndStorageAsync(TargetPort);

        Cookies.Clear();
        foreach (var c in dump.Cookies) Cookies.Add(c);

        LocalStorageItems.Clear();
        foreach (var pair in dump.LocalStorage) 
        {
            LocalStorageItems.Add(new StorageEntry { Key = pair.Key, Value = pair.Value });
        }

        SessionStorageItems.Clear();
        foreach (var pair in dump.SessionStorage) 
        {
            SessionStorageItems.Add(new StorageEntry { Key = pair.Key, Value = pair.Value });
        }

        SessionIdToken = dump.SessionIdToken ?? "ไม่พบคุกกี้ sessionid";
        CsrfToken = dump.CsrfToken ?? "ไม่พบคุกกี้ tt_csrf_token";
        StorageStatus = $"ดึงข้อมูลสำเร็จ ({dump.Cookies.Count} คุกกี้, {dump.LocalStorage.Count} LocalStorage, {dump.SessionStorage.Count} SessionStorage) เวลา {dump.FetchedAt:HH:mm:ss}";
    }

    [RelayCommand]
    public void CopySessionId()
    {
        if (!string.IsNullOrEmpty(SessionIdToken) && SessionIdToken != "-")
        {
            Clipboard.SetText(SessionIdToken);
            StorageStatus = "คัดลอก sessionid เรียบร้อยแล้ว!";
        }
    }

    [RelayCommand]
    public void CopyCsrfToken()
    {
        if (!string.IsNullOrEmpty(CsrfToken) && CsrfToken != "-")
        {
            Clipboard.SetText(CsrfToken);
            StorageStatus = "คัดลอก tt_csrf_token เรียบร้อยแล้ว!";
        }
    }

    [RelayCommand]
    public void CopyAllCookiesString()
    {
        if (Cookies.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var c in Cookies)
        {
            sb.Append($"{c.Name}={c.Value}; ");
        }
        Clipboard.SetText(sb.ToString().TrimEnd());
        StorageStatus = "คัดลอก Cookie Header String ทั้งหมดแล้ว!";
    }

    [RelayCommand]
    public void CopyCookieValue(CookieItem? item)
    {
        if (item != null && !string.IsNullOrEmpty(item.Value))
        {
            Clipboard.SetText(item.Value);
            StorageStatus = $"คัดลอกค่า Cookie '{item.Name}' เรียบร้อยแล้ว!";
        }
    }

    [RelayCommand]
    public void CopyStorageItem(StorageEntry? item)
    {
        if (item != null && !string.IsNullOrEmpty(item.Value))
        {
            Clipboard.SetText(item.Value);
            StorageStatus = $"คัดลอกค่า Storage '{item.Key}' เรียบร้อยแล้ว!";
        }
    }

    [RelayCommand]
    public void CopyResponseBody()
    {
        if (SelectedTraffic != null && !string.IsNullOrEmpty(SelectedTraffic.ResponseBody))
        {
            Clipboard.SetText(SelectedTraffic.ResponseBody);
        }
    }

    [RelayCommand]
    public void CopyRequestHeaders()
    {
        if (SelectedTraffic != null && !string.IsNullOrEmpty(SelectedTraffic.RequestHeaders))
        {
            Clipboard.SetText(SelectedTraffic.RequestHeaders);
        }
    }

    [RelayCommand]
    public async Task<string?> ExportFullHtmlAsync(string? targetFilePath = null)
    {
        string time = DateTime.Now.ToString("HH:mm:ss");
        AppendConsole($"[{time}] ⏳ กำลังดึง Full HTML จากพอร์ต {TargetPort} ผ่าน CDP...");

        try
        {
            var result = await _cdpService.GetFullHtmlAsync(TargetPort);
            if (result == null || string.IsNullOrWhiteSpace(result.Html))
            {
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] ❌ ดึง Full HTML ไม่สำเร็จ (พอร์ต {TargetPort} ไม่ตอบสนอง หรือไม่พบหน้าเว็บ)\n");
                return null;
            }

            if (string.IsNullOrWhiteSpace(targetFilePath))
            {
                string defaultName = $"TikTokStudio_Port{TargetPort}_{DateTime.Now:yyyyMMdd_HHmmss}.html";
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = $"Export Full HTML (Port: {TargetPort})",
                    Filter = "HTML Files (*.html;*.htm)|*.html;*.htm|All Files (*.*)|*.*",
                    FileName = defaultName,
                    DefaultExt = "html"
                };

                if (dialog.ShowDialog() != true)
                {
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] ℹ️ ยกเลิกการ Export Full HTML\n");
                    return null;
                }

                targetFilePath = dialog.FileName;
            }

            string dir = System.IO.Path.GetDirectoryName(targetFilePath) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            string headerComment = $"<!-- [TikTok LIVE Studio Sandbox] Exported: {result.FetchedAt:yyyy-MM-dd HH:mm:ss} | Port: {TargetPort} | URL: {result.Url} | Title: {result.Title} -->\n";
            string finalHtml = result.Html;
            if (finalHtml.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
            {
                int idx = finalHtml.IndexOf('>');
                if (idx >= 0)
                {
                    finalHtml = finalHtml.Substring(0, idx + 1) + "\n" + headerComment + finalHtml.Substring(idx + 1);
                }
                else
                {
                    finalHtml = headerComment + finalHtml;
                }
            }
            else
            {
                finalHtml = headerComment + finalHtml;
            }

            await System.IO.File.WriteAllTextAsync(targetFilePath, finalHtml, Encoding.UTF8);
            var fileInfo = new System.IO.FileInfo(targetFilePath);
            LastExportedHtmlPath = targetFilePath;
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ✅ Export Full HTML สำเร็จ: {targetFilePath} ({fileInfo.Length:N0} bytes)\n");
            return targetFilePath;
        }
        catch (Exception ex)
        {
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ❌ เกิดข้อผิดพลาด: {ex.Message}\n");
            return null;
        }
    }

    [RelayCommand]
    public void OpenLastExportedHtml()
    {
        if (!string.IsNullOrEmpty(LastExportedHtmlPath) && System.IO.File.Exists(LastExportedHtmlPath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = LastExportedHtmlPath,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    [RelayCommand]
    public async Task<string?> CopyCaptchaHtmlAsync()
    {
        string time = DateTime.Now.ToString("HH:mm:ss");
        AppendConsole($"[{time}] 🔍 กำลังค้นหา Captcha ในพอร์ต {TargetPort}...");

        try
        {
            var result = await _cdpService.DetectCaptchaElementsAsync(TargetPort, forceFullHtml: true);
            if (result.Found && !string.IsNullOrWhiteSpace(result.CombinedHtml))
            {
                try
                {
                    Clipboard.SetText(result.CombinedHtml);
                }
                catch (Exception clipEx)
                {
                    AppendConsole($"[{time}] ⚠️ ไม่สามารถคัดลอกลง Clipboard ได้: {clipEx.Message}");
                }
                string statusLabel = result.IsActive ? $"🚨 แสดงผลจริง ({result.TypeDescription})" : "ℹ️ ซ่อนอยู่ใน DOM";
                AppendConsole($"[{time}] ✅ คัดลอก Captcha Full HTML ({result.Elements.Count} รายการ, {statusLabel}) ลง Clipboard สำเร็จ! ({result.CombinedHtml.Length:N0} ตัวอักษร)\n");
                return result.CombinedHtml;
            }
            else
            {
                AppendConsole($"[{time}] ℹ️ ไม่พบ Element ที่มี Captcha ในหน้าต่างนี้\n");
                return null;
            }
        }
        catch (Exception ex)
        {
            AppendConsole($"[{time}] ❌ Error: {ex.Message}\n");
            return null;
        }
    }

    [RelayCommand]
    public async Task<CaptchaDetectionResult> DetectCaptchaAsync()
    {
        string time = DateTime.Now.ToString("HH:mm:ss");
        CaptchaStatusText = $"[{time}] กำลังสแกนหา Captcha ในพอร์ต {TargetPort}...";
        AppendConsole($"[{time}] 🔍 กำลังค้นหา Captcha ในพอร์ต {TargetPort}...");

        try
        {
            var result = await _cdpService.DetectCaptchaElementsAsync(TargetPort, forceFullHtml: true);
            CaptchaElements.Clear();
            foreach (var el in result.Elements)
            {
                CaptchaElements.Add(el);
            }
            if (CaptchaElements.Count > 0 && SelectedCaptchaElement == null)
            {
                SelectedCaptchaElement = CaptchaElements[0];
            }

            DetectedCaptchaCount = result.Elements.Count;
            CaptchaRawHtml = result.CombinedHtml;

            if (result.IsActive)
            {
                CaptchaStatusText = $"[{time}] 🚨 ตรวจพบ Captcha จริง! ({result.TypeDescription})";
                AppendConsole($"[{time}] 🚨 ตรวจพบ Captcha จริงกำลังแสดงผลบนหน้าจอ! ประเภท: {result.TypeDescription} (พบ {result.Elements.Count} Elements, Countdown: {result.CountdownText ?? "N/A"})");
            }
            else if (result.Found)
            {
                CaptchaStatusText = $"[{time}] ℹ️ พบ {result.Elements.Count} Element ใน DOM (ซ่อน/ปิดอยู่)";
                AppendConsole($"[{time}] ℹ️ พบ {result.Elements.Count} Captcha elements ใน DOM แต่ทั้งหมดอยู่ในสถานะซ่อนหรือปิดอยู่ (ไม่ใช่ Captcha ที่กำลังแสดงผล)");
            }
            else
            {
                CaptchaStatusText = $"[{time}] 🟢 ไม่พบ Captcha ในพอร์ตนี้ (ปกติ)";
                AppendConsole($"[{time}] ℹ️ ไม่พบ Element ที่มี Captcha ในหน้าต่างนี้\n");
            }
            return result;
        }
        catch (Exception ex)
        {
            CaptchaStatusText = $"[{time}] ❌ Error: {ex.Message}";
            AppendConsole($"[{time}] ❌ Error: {ex.Message}\n");
            return new CaptchaDetectionResult();
        }
    }

    [RelayCommand]
    public void CopyElementHtml(CaptchaElementItem? item)
    {
        var target = item ?? SelectedCaptchaElement;
        if (target != null && !string.IsNullOrEmpty(target.Html))
        {
            try
            {
                Clipboard.SetText(target.Html);
                CaptchaStatusText = $"คัดลอก {target.Summary} ลง Clipboard สำเร็จ!";
            }
            catch (Exception ex)
            {
                CaptchaStatusText = $"คัดลอกไม่สำเร็จ: {ex.Message}";
            }
        }
    }



    [RelayCommand]
    public void ClearCaptcha()
    {
        CaptchaElements.Clear();
        SelectedCaptchaElement = null;
        CaptchaRawHtml = string.Empty;
        DetectedCaptchaCount = 0;
        CaptchaStatusText = "ล้างข้อมูลเรียบร้อย";
    }

    private const int MaxConsoleOutputLength = 64 * 1024; // 64 KB limit to prevent LOH allocation churn

    private void AppendConsole(string text)
    {
        string newText = string.IsNullOrEmpty(ConsoleOutput) ? (text + "\n") : (ConsoleOutput + text + "\n");
        if (newText.Length > MaxConsoleOutputLength)
        {
            int cutIndex = newText.IndexOf('\n', newText.Length - (MaxConsoleOutputLength * 3 / 4));
            newText = cutIndex > 0 ? newText.Substring(cutIndex + 1) : newText.Substring(newText.Length - (MaxConsoleOutputLength * 3 / 4));
        }
        ConsoleOutput = newText;
    }
}
