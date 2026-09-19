using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;
using TiktokStudioSandbox.Core.Services;

namespace TiktokStudioSandbox.App.ViewModels;

public partial class InstanceCardViewModel : ObservableObject
{
    // ==========================================
    // 🧩 Captcha Auto-Detection & Copy Properties
    // ==========================================
    private System.Windows.Threading.DispatcherTimer? _captchaDetectTimer;
    private string? _lastReportedCaptchaHtml;

    [ObservableProperty]
    private bool _isCaptchaAutoDetect = true;

    [ObservableProperty]
    private bool _autoCopyToClipboardOnDetect = false;


    [ObservableProperty]
    private bool _isCaptchaAlertVisible;

    public Visibility CaptchaAlertVisibility => IsCaptchaAlertVisible ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsCaptchaAlertVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(CaptchaAlertVisibility));
    }

    [ObservableProperty]
    private string _captchaAlertTitle = string.Empty;

    [ObservableProperty]
    private string _captchaAlertMessage = string.Empty;

    [ObservableProperty]
    private string _detectedCaptchaHtml = string.Empty;

    [ObservableProperty]
    private int _detectedCaptchaCount;

    [ObservableProperty]
    private string _lastCaptchaDetectedTime = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CaptchaElementItem> _detectedCaptchaElements = new();

    [ObservableProperty]
    private CaptchaElementItem? _selectedCaptchaElement;

    [ObservableProperty]
    private int _selectedDetailTabIndex;

    // Session & Tokens
    [ObservableProperty]
    private string _sessionIdToken = "-";

    [ObservableProperty]
    private string _csrfToken = "-";

    [ObservableProperty]
    private string _sessionFetchStatus = string.Empty;


    private void EnsureCaptchaTimer()
    {
        if (_captchaDetectTimer == null)
        {
            try
            {
                _captchaDetectTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _captchaDetectTimer.Tick += async (_, _) =>
                {
                    if (IsRunning && IsCaptchaAutoDetect)
                    {
                        await CheckCaptchaInternalAsync(isAuto: true);
                    }
                };
            }
            catch { }
        }
    }

    partial void OnIsCaptchaAutoDetectChanged(bool value)
    {
        EnsureCaptchaTimer();
        if (value)
        {
            _captchaDetectTimer?.Start();
            if (IsRunning)
            {
                _ = CheckCaptchaInternalAsync(isAuto: true);
            }
        }
        else
        {
            _captchaDetectTimer?.Stop();
        }
    }


    // ==========================================
    // 🧩 Captcha Detection & Copy Commands
    // ==========================================
    public async Task<CaptchaDetectionResult> CheckCaptchaInternalAsync(bool isAuto)
    {
        if (Status != InstanceStatus.Running) return new CaptchaDetectionResult();

        try
        {
            var result = await _cdpService.DetectCaptchaElementsAsync(CdpPort);

            // Real Captcha Active on Screen
            if (result.IsActive)
            {
                DetectedCaptchaHtml = result.CombinedHtml;
                DetectedCaptchaCount = result.Elements.Count;
                LastCaptchaDetectedTime = DateTime.Now.ToString("HH:mm:ss");
                CaptchaAlertTitle = $"🚨 ตรวจพบ Captcha จริง! ({result.TypeDescription})";
                CaptchaAlertMessage = $"พบ Captcha กำลังแสดงผล: {result.TypeDescription}" + 
                    (!string.IsNullOrEmpty(result.CountdownText) ? $" | เหลือเวลา: {result.CountdownText}" : "");
                IsCaptchaAlertVisible = true;

                DetectedCaptchaElements.Clear();
                foreach (var el in result.Elements)
                {
                    DetectedCaptchaElements.Add(el);
                }
                if (DetectedCaptchaElements.Count > 0 && SelectedCaptchaElement == null)
                {
                    SelectedCaptchaElement = DetectedCaptchaElements[0];
                }

                if (AutoCopyToClipboardOnDetect)
                {
                    try
                    {
                        Clipboard.SetText(result.CombinedHtml);
                        StatusMessage = $"🚨 ตรวจพบ Captcha จริง ({result.TypeDescription}) และคัดลอก Full HTML ลง Clipboard แล้ว!";
                    }
                    catch { }
                }
                else
                {
                    StatusMessage = $"🚨 ตรวจพบ Captcha จริง ({result.TypeDescription})! สามารถกดคัดลอก Full HTML หรือสั่งแก้ได้ทันที";
                }

                if (_lastReportedCaptchaHtml != result.CombinedHtml)
                {
                    _lastReportedCaptchaHtml = result.CombinedHtml;
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🚨 ====================================================================");
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🚨 [CAPTCHA DETECTED] ตรวจพบ Captcha กำลังแสดงผลอยู่จริงบนหน้าจอ!");
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🚨 บัญชี: '{Name}' | CDP Port: {CdpPort} | ประเภท: {result.TypeDescription}");
                    if (!string.IsNullOrEmpty(result.CountdownText))
                    {
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] ⏳ ตัวนับเวลาถอยหลัง (Countdown): {result.CountdownText}");
                    }
                    if (!string.IsNullOrEmpty(result.PromptText))
                    {
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 💡 คำแนะนำโจทย์: {result.PromptText}");
                    }
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🚨 รายละเอียดองค์ประกอบ ({result.Elements.Count} รายการ):");
                    for (int i = 0; i < result.Elements.Count; i++)
                    {
                        var el = result.Elements[i];
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}]    ├─ [#{i + 1}] {el.Summary} (Depth: {el.Depth}, Visible={el.IsVisible})");
                    }
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] 📋 Full HTML ถูกดึงมาเรียบร้อยแล้ว ({result.CombinedHtml.Length:N0} ตัวอักษร)");
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🚨 ====================================================================\n");

                    Logger.Info("Captcha", $"Auto-detected ACTIVE captcha on '{Name}' (Port: {CdpPort}): {result.TypeDescription}. Countdown='{result.CountdownText ?? "N/A"}'");

                    // 📱 Telegram Alert Notification on Detection
                    if (_settings.EnableTelegramAlert && _settings.TelegramAlertOnDetected &&
                        !string.IsNullOrWhiteSpace(_settings.TelegramBotToken) && !string.IsNullOrWhiteSpace(_settings.TelegramChatId))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                string profileName = !string.IsNullOrEmpty(LoggedInDisplayId) ? $"@{LoggedInDisplayId}" : (!string.IsNullOrEmpty(Profile.StreamerUsername) ? $"@{Profile.StreamerUsername}" : Name);
                                string caption = $"🚨 <b>[TikTok LIVE Studio] ตรวจพบ CAPTCHA!</b>\n\n" +
                                                 $"• บัญชี: <b>{Name}</b> (Port: {CdpPort})\n" +
                                                 $"• โปรไฟล์: <b>{profileName}</b>\n" +
                                                 $"• ประเภท: <b>{result.TypeDescription}</b>\n" +
                                                 $"• Session ID: <code>{result.CaptchaSessionId ?? "-"}</code>\n" +
                                                 $"• เวลา: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n" +
                                                 $"• การแก้: {(_settings.AutoSolveCaptcha ? $"Auto Solve (หน่วง {_settings.AutoSolveDelaySeconds}s)" : "สั่งการด้วยมือ")}";

                                if (_settings.TelegramSendPhoto && result.CapturedImageBytes != null && result.CapturedImageBytes.Length > 0)
                                {
                                    await _telegramService.SendPhotoAsync(_settings.TelegramBotToken, _settings.TelegramChatId, result.CapturedImageBytes, caption);
                                }
                                else
                                {
                                    await _telegramService.SendMessageAsync(_settings.TelegramBotToken, _settings.TelegramChatId, caption);
                                }
                            }
                            catch { }
                        });
                    }
                }
            }
            else
            {
                // Result is NOT active (either not present at all, or elements are hidden/unmounted in DOM)
                if (isAuto && IsCaptchaAlertVisible)
                {
                    IsCaptchaAlertVisible = false;
                    _lastReportedCaptchaHtml = null;
                    DetectedCaptchaElements.Clear();
                    SelectedCaptchaElement = null;
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] ✅ [Auto Detect] Captcha ในหน้าต่างหายไปแล้ว (ผ่านการตรวจสอบเรียบร้อย)\n");
                    StatusMessage = "ตรวจไม่พบ Captcha (หน้าต่างปกติ)";
                }

                // If user manually scanned, let them know if hidden elements were found or none at all
                if (!isAuto)
                {
                    DetectedCaptchaElements.Clear();
                    foreach (var el in result.Elements)
                    {
                        DetectedCaptchaElements.Add(el);
                    }
                    DetectedCaptchaCount = result.Elements.Count;
                    DetectedCaptchaHtml = result.CombinedHtml;
                    if (result.Elements.Count > 0)
                    {
                        SelectedCaptchaElement = result.Elements[0];
                    }

                    if (result.Elements.Count > 0)
                    {
                        StatusMessage = $"พบ {result.Elements.Count} Element ใน DOM แต่ถูกซ่อน/ปิดอยู่ (ไม่ใช่ Captcha ที่กำลังแสดงผล)";
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] ℹ️ พบ {result.Elements.Count} Captcha elements ใน DOM แต่ทั้งหมดอยู่ในสถานะซ่อนหรือปิดอยู่ (display: none หรือ Modal ปิดแล้ว) - หน้าจอปกติ ไม่มี Captcha กวน\n");
                    }
                    else
                    {
                        StatusMessage = "ตรวจไม่พบ Captcha ในหน้าต่างนี้ (ปกติ)";
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🟢 ไม่พบโครงสร้าง Captcha ใดๆ ในหน้าต่างนี้\n");
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("Captcha", $"Error checking captcha: {ex.Message}", ex);
            return new CaptchaDetectionResult();
        }
    }

    [RelayCommand]
    public async Task<string?> CopyCaptchaHtmlAsync()
    {
        if (Status != InstanceStatus.Running)
        {
            Logger.Warn("Captcha", $"Cannot copy captcha: '{Name}' is not running.");
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ⚠️ ไม่สามารถตรวจ Captcha ได้: หน้าต่างนี้ยังไม่ได้เปิดทำงาน กรุณากด Start ก่อน\n");
            StatusMessage = "กรุณาเปิดสตูดิโอก่อนตรวจจับ Captcha";
            return null;
        }

        string time = DateTime.Now.ToString("HH:mm:ss");
        AppendConsole($"[{time}] 🔍 กำลังค้นหา Captcha ในหน้าต่างพอร์ต {CdpPort}...");
        StatusMessage = "กำลังค้นหา Captcha และดึง Full HTML...";

        var result = await CheckCaptchaInternalAsync(isAuto: false);
        if (result.Found && !string.IsNullOrWhiteSpace(result.CombinedHtml))
        {
            try
            {
                Clipboard.SetText(result.CombinedHtml);
                string activeStatus = result.IsActive ? "🚨 กำลังแสดงผลอยู่จริง" : "ℹ️ ซ่อนอยู่ใน DOM";
                StatusMessage = $"✅ คัดลอก Full HTML ({result.Elements.Count} Elements, {activeStatus}) สำเร็จ!";
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] ✅ คัดลอก Full HTML สำเร็จเรียบร้อย! ({activeStatus})");
                AppendConsole($"   📊 พบทั้งหมด: {result.Elements.Count} Element | ประเภท: {result.TypeDescription}");
                for (int i = 0; i < result.Elements.Count; i++)
                {
                    var el = result.Elements[i];
                    AppendConsole($"      [{i + 1}] {el.Summary} (Depth {el.Depth}, Visible={el.IsVisible})");
                }
                AppendConsole($"   📋 โค้ด Full HTML ถูกคัดลอกลง Clipboard แล้ว ({result.CombinedHtml.Length:N0} ตัวอักษร)\n");
            }
            catch (Exception ex)
            {
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] ❌ ไม่สามารถคัดลอกลง Clipboard ได้: {ex.Message}\n");
            }
            return result.CombinedHtml;
        }
        else
        {
            StatusMessage = "ไม่พบ Element ที่มี Captcha ในหน้าต่างนี้";
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ℹ️ ไม่พบ Element ใดๆ ที่มี ID, Class หรือ Attribute ระบุถึง Captcha ในหน้าต่างนี้\n");
            return null;
        }
    }

    [RelayCommand]
    public void CopyDetectedCaptcha()
    {
        if (!string.IsNullOrEmpty(DetectedCaptchaHtml))
        {
            try
            {
                Clipboard.SetText(DetectedCaptchaHtml);
                StatusMessage = $"📋 คัดลอก Captcha Full HTML ({DetectedCaptchaCount} รายการ) ลง Clipboard เรียบร้อย!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"คัดลอกไม่สำเร็จ: {ex.Message}";
            }
        }
    }


    [RelayCommand]
    public void DismissCaptchaAlert()
    {
        IsCaptchaAlertVisible = false;
    }

    [RelayCommand]
    public void OpenCaptchaTab()
    {
        IsDetailOpen = true;
        SelectedDetailTabIndex = 4; // Subtab 5: Captcha Inspector
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
                StatusMessage = $"📋 คัดลอก {target.Summary} ลง Clipboard สำเร็จ!";
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] 📋 คัดลอกเฉพาะ Element {target.Summary} ลง Clipboard แล้ว\n");
            }
            catch (Exception ex)
            {
                StatusMessage = $"คัดลอกไม่สำเร็จ: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    public void ClearCaptchaResults()
    {
        DetectedCaptchaElements.Clear();
        SelectedCaptchaElement = null;
        DetectedCaptchaHtml = string.Empty;
        DetectedCaptchaCount = 0;
        IsCaptchaAlertVisible = false;
        _lastReportedCaptchaHtml = null;
        StatusMessage = "ล้างข้อมูล Captcha เรียบร้อย";
        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🗑️ ล้างข้อมูลผลลัพธ์ Captcha เรียบร้อยแล้ว\n");
    }

    [RelayCommand]
    public async Task ScanCaptchaNowAsync()
    {
        await CopyCaptchaHtmlAsync();
    }

    [RelayCommand]
    public async Task<string?> ExportCaptchaHtmlAsync(string? targetFilePath = null)
    {
        if (string.IsNullOrEmpty(DetectedCaptchaHtml))
        {
            var res = await CheckCaptchaInternalAsync(isAuto: false);
            if (!res.Found)
            {
                StatusMessage = "ไม่พบ Captcha ให้บันทึก";
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(targetFilePath))
        {
            string defaultName = $"Captcha_{ProfileFolderName}_{DateTime.Now:yyyyMMdd_HHmmss}.html";
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = $"Export Captcha HTML - {Name}",
                Filter = "HTML Files (*.html;*.htm)|*.html;*.htm|All Files (*.*)|*.*",
                FileName = defaultName,
                DefaultExt = "html"
            };

            if (dialog.ShowDialog() != true) return null;
            targetFilePath = dialog.FileName;
        }

        string header = $"<!-- [TikTok LIVE Studio Sandbox] Captcha Full HTML Dump | Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Profile: {Name} ({ProfileFolderName}) | Port: {CdpPort} -->\n";
        string content = header + DetectedCaptchaHtml;
        await System.IO.File.WriteAllTextAsync(targetFilePath, content, Encoding.UTF8);

        StatusMessage = $"✅ บันทึก Captcha Full HTML สำเร็จ: {System.IO.Path.GetFileName(targetFilePath)}";
        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 💾 บันทึก Captcha Full HTML เป็นไฟล์เรียบร้อย: {targetFilePath}\n");
        return targetFilePath;
    }


}
