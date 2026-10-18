using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowUltraTech.App.Services;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;

namespace FlowUltraTech.App.ViewModels;

public partial class InstanceCardViewModel : ObservableObject
{
    // ==========================================
    // 🧩 Captcha Auto-Detection & Copy Properties
    // ==========================================
    private System.Windows.Threading.DispatcherTimer? _captchaDetectTimer;
    private string? _lastReportedCaptchaHtml;
    private int _autoCaptchaScanCount;

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

    // ==========================================
    // 🧩 Captcha Solver & Image Properties
    // ==========================================
    [ObservableProperty]
    private bool _isAutoSolveCaptcha = true;

    [ObservableProperty]
    private bool _isSolvingCaptcha;

    public bool CanSolveCaptcha => IsRunning && !IsSolvingCaptcha;

    [ObservableProperty]
    private string _detectedCaptchaTypeDescription = "-";

    [ObservableProperty]
    private string _detectedCaptchaCountdown = "00:00";

    [ObservableProperty]
    private string _detectedCaptchaSessionId = "-";

    [ObservableProperty]
    private string _lastCapturedImagePath = "-";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaptchaImageSource))]
    [NotifyPropertyChangedFor(nameof(HasDirectImage))]
    [NotifyPropertyChangedFor(nameof(HasCaptchaImage))]
    private System.Windows.Media.ImageSource? _directCaptchaImageSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaptchaImageSource))]
    [NotifyPropertyChangedFor(nameof(HasCroppedImage))]
    [NotifyPropertyChangedFor(nameof(HasCaptchaImage))]
    private System.Windows.Media.ImageSource? _croppedCaptchaImageSource;

    public System.Windows.Media.ImageSource? CaptchaImageSource => DirectCaptchaImageSource ?? CroppedCaptchaImageSource;

    public bool HasDirectImage => DirectCaptchaImageSource != null;
    public bool HasCroppedImage => CroppedCaptchaImageSource != null;
    public bool HasCaptchaImage => HasDirectImage || HasCroppedImage;

    private CaptchaDetectionResult? _lastDetectionResult;

    partial void OnIsSolvingCaptchaChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSolveCaptcha));
    }

    private static System.Windows.Media.Imaging.BitmapImage? ToBitmapImage(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private bool _isCheckingCaptcha;

    private void EnsureCaptchaTimer()
    {
        if (_captchaDetectTimer == null)
        {
            try
            {
                _captchaDetectTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _captchaDetectTimer.Tick += async (_, _) =>
                {
                    if (IsRunning && IsCaptchaAutoDetect && !_isCheckingCaptcha && !IsSolvingCaptcha)
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
            if (IsRunning && !_isCheckingCaptcha && !IsSolvingCaptcha)
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
        if (isAuto && (_isCheckingCaptcha || IsSolvingCaptcha)) return new CaptchaDetectionResult();

        _isCheckingCaptcha = true;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var detectTask = _cdpService.DetectCaptchaElementsAsync(CdpPort, forceFullHtml: !isAuto);
            CaptchaDetectionResult result;
            if (await Task.WhenAny(detectTask, Task.Delay(isAuto ? 8000 : 15000)) == detectTask)
            {
                result = await detectTask;
            }
            else
            {
                _ = detectTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                Logger.Warn("Captcha", $"[Auto-Detect] Captcha check on '{Name}' (Port {CdpPort}) timed out after {(isAuto ? 8 : 15)}s.");
                return new CaptchaDetectionResult();
            }
            sw.Stop();
            if (isAuto) _autoCaptchaScanCount++;

            // Real Captcha Active on Screen
            if (result.IsActive)
            {
                Logger.Info("Captcha", $"[Auto-Detect] Active captcha detected on '{Name}' in {sw.ElapsedMilliseconds}ms: {result.TypeDescription}");

                // 1. Trigger audio alert IMMEDIATELY (zero delay before any UI/Bitmap/IO work)
                if (_settings.EnableCaptchaSoundAlert)
                {
                    Logger.Info("Captcha", $"Triggering immediate audio alert for captcha on '{Name}' (Volume: {_settings.CaptchaSoundVolume:P0}, Loop: {_settings.LoopCaptchaSoundAlert})");
                    AudioAlertService.PlayAlert(
                        _settings.CaptchaSoundVolume,
                        _settings.CaptchaSoundFilePath,
                        _settings.LoopCaptchaSoundAlert);
                }

                // 2. Immediately bring window to front so Chromium compositor un-throttles and user can see Captcha
                try
                {
                    Logger.Info("Captcha", $"Auto-bringing TikTok Studio window to front for '{Name}' (Port {CdpPort})...");
                    bool showOk = _orchestrator.ShowWindow(Profile);
                    SyncFromProfile();
                    if (!showOk)
                    {
                        Logger.Warn("Captcha", $"Auto-bring window to front returned false for '{Name}' (Window handle not available yet).");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Captcha", $"Failed to auto-show window on captcha detection: {ex.Message}", ex);
                }

                _lastDetectionResult = result;
                DetectedCaptchaHtml = result.CombinedHtml;
                DetectedCaptchaCount = result.Elements.Count;
                LastCaptchaDetectedTime = DateTime.Now.ToString("HH:mm:ss");
                DetectedCaptchaTypeDescription = result.TypeDescription;
                DetectedCaptchaCountdown = !string.IsNullOrEmpty(result.CountdownText) ? result.CountdownText : "N/A";
                DetectedCaptchaSessionId = !string.IsNullOrEmpty(result.CaptchaSessionId) ? result.CaptchaSessionId : "-";
                CaptchaAlertTitle = $"🚨 ตรวจพบ Captcha จริง! ({result.TypeDescription})";
                CaptchaAlertMessage = $"พบ Captcha กำลังแสดงผล: {result.TypeDescription}" + 
                    (!string.IsNullOrEmpty(result.CountdownText) ? $" | เหลือเวลา: {result.CountdownText}" : "");
                IsCaptchaAlertVisible = true;

                RunOnUi(() =>
                {
                    var directBmp = ToBitmapImage(result.DirectImageBytes);
                    var croppedBmp = ToBitmapImage(result.CroppedImageBytes);
                    DirectCaptchaImageSource = directBmp ?? croppedBmp;
                    CroppedCaptchaImageSource = croppedBmp ?? directBmp;
                });

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

                // Auto solve if enabled
                if (IsAutoSolveCaptcha && !IsSolvingCaptcha && IsRunning)
                {
                    _ = Task.Run(async () =>
                    {
                        await SolveCaptchaInternalAsync(result);
                    });
                }

                string reportKey = !string.IsNullOrEmpty(result.CombinedHtml)
                    ? result.CombinedHtml
                    : $"{result.Type}_{result.CaptchaSessionId}_{result.PromptText}";
                if (_lastReportedCaptchaHtml != reportKey)
                {
                    _lastReportedCaptchaHtml = reportKey;
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

                                byte[]? telegramImageBytes = result.CroppedImageBytes ?? result.CapturedImageBytes;
                                if (_settings.TelegramSendPhoto && telegramImageBytes != null && telegramImageBytes.Length > 0)
                                {
                                    await _telegramService.SendPhotoAsync(_settings.TelegramBotToken, _settings.TelegramChatId, telegramImageBytes, caption);
                                }
                                else
                                {
                                    await _telegramService.SendMessageAsync(_settings.TelegramBotToken, _settings.TelegramChatId, caption);
                                }
                            }
                            catch { }
                        });
                    }

                    // 💾 Auto-save Captcha image if enabled
                    if (_settings.AutoSaveCaptchaImage && result.CapturedImageBytes != null && result.CapturedImageBytes.Length > 0)
                    {
                        try
                        {
                            string saveDir = !string.IsNullOrWhiteSpace(_settings.CaptchaSaveDirectory)
                                ? _settings.CaptchaSaveDirectory
                                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures), "TikTokStudioCaptures");
                            Directory.CreateDirectory(saveDir);
                            byte[] saveBytes = result.CroppedImageBytes ?? result.CapturedImageBytes;
                            string ext = (saveBytes.Length > 12 && saveBytes[0] == 0x52 && saveBytes[8] == 0x57) ? "webp" : "png";
                            string fileName = $"Captcha_{ProfileFolderName}_{result.Type}_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
                            string fullPath = Path.Combine(saveDir, fileName);
                            await File.WriteAllBytesAsync(fullPath, saveBytes);
                            LastCapturedImagePath = fullPath;
                        }
                        catch { }
                    }
                }
            }
            else
            {
                // Result is NOT active (either not present at all, or elements are hidden/unmounted in DOM)
                if (isAuto)
                {
                    if (_autoCaptchaScanCount == 1 || _autoCaptchaScanCount % 12 == 0)
                    {
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🛡️ [Auto Monitor] ตรวจสอบ Captcha: สถานะปกติ (Scan #{_autoCaptchaScanCount}, {sw.ElapsedMilliseconds}ms - ไม่พบสิ่งกีดขวาง)");
                    }
                    Logger.Debug("Captcha", $"[Auto-Detect] Scan #{_autoCaptchaScanCount} completed in {sw.ElapsedMilliseconds}ms. Window status: Normal (0 active captcha)");
                }

                if (isAuto && IsCaptchaAlertVisible)
                {
                    IsCaptchaAlertVisible = false;
                    _lastReportedCaptchaHtml = null;
                    DetectedCaptchaElements.Clear();
                    SelectedCaptchaElement = null;
                    RunOnUi(() =>
                    {
                        DirectCaptchaImageSource = null;
                        CroppedCaptchaImageSource = null;
                    });
                    AudioAlertService.StopAlert();
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
        finally
        {
            _isCheckingCaptcha = false;
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
        AudioAlertService.StopAlert();
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
        DetectedCaptchaSessionId = "-";
        LastCapturedImagePath = "-";
        IsCaptchaAlertVisible = false;
        _lastReportedCaptchaHtml = null;
        _lastDetectionResult = null;
        DirectCaptchaImageSource = null;
        CroppedCaptchaImageSource = null;
        AudioAlertService.StopAlert();
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

    [RelayCommand]
    public async Task SolveCaptchaAsync()
    {
        await SolveCaptchaInternalAsync(_lastDetectionResult);
    }

    [RelayCommand]
    public async Task SolveCaptchaMockAsync()
    {
        await SolveCaptchaInternalAsync(_lastDetectionResult);
    }

    public async Task<CaptchaSolveResult?> SolveCaptchaInternalAsync(CaptchaDetectionResult? detection = null)
    {
        if (Status != InstanceStatus.Running || IsSolvingCaptcha) return null;

        IsSolvingCaptcha = true;
        OnPropertyChanged(nameof(CanSolveCaptcha));
        StatusMessage = "กำลังดำเนินการแก้ Captcha...";
        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🤖 [CAPTCHA SOLVER] กำลังเริ่มขั้นตอนการแก้ Captcha...");
        Logger.Info("CaptchaSolver", $"Starting Captcha solve on '{Name}' (Port: {CdpPort}, Mode: {_settings.CaptchaSolverMode}, Endpoint: {_settings.CaptchaSolverEndpoint})...");

        try
        {
            string effectiveSolverToken = !string.IsNullOrWhiteSpace(_settings.AuthToken)
                ? _settings.AuthToken
                : (!string.IsNullOrWhiteSpace(_settings.CaptchaSolverJwtToken) ? _settings.CaptchaSolverJwtToken : string.Empty);

            // 🛡️ Proactive Check: If JWT is expired or expiring within 3 minutes, refresh proactively before calling API
            if (_authService != null && !string.IsNullOrWhiteSpace(_settings.RefreshToken) &&
                IsJwtTokenExpiredOrClose(effectiveSolverToken, bufferSeconds: 180))
            {
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🔄 [CAPTCHA SOLVER] ตรวจพบ JWT Token ใกล้หมดอายุ กำลังต่ออายุล่วงหน้าอัตโนมัติ (Proactive Refresh)...");
                try
                {
                    var preRefresh = await _authService.RefreshTokenAsync(_settings.RefreshToken, _settings.AuthApiEndpoint);
                    if (preRefresh.Success && !string.IsNullOrWhiteSpace(preRefresh.AccessToken))
                    {
                        effectiveSolverToken = preRefresh.AccessToken;
                        _settings.AuthToken = preRefresh.AccessToken;
                        _settings.CaptchaSolverJwtToken = preRefresh.AccessToken;
                        if (!string.IsNullOrWhiteSpace(preRefresh.RefreshToken))
                        {
                            _settings.RefreshToken = preRefresh.RefreshToken;
                        }
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🔑 [CAPTCHA SOLVER] ต่ออายุ Token ล่วงหน้าเรียบร้อย พร้อมใช้งาน 24 ชั่วโมง");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("CaptchaSolver", $"Proactive token refresh attempt failed: {ex.Message}");
                }
            }

            var result = await _cdpService.SolveCaptchaAsync(
                cdpPort: CdpPort,
                detection: detection ?? _lastDetectionResult,
                solverMode: _settings.CaptchaSolverMode,
                solverApiEndpoint: _settings.CaptchaSolverEndpoint,
                solverJwtToken: effectiveSolverToken,
                customMockJsonPath: null,
                progressLogger: msg => AppendConsole(msg),
                delaySeconds: _settings.AutoSolveDelaySeconds,
                instanceLeaseId: ActiveLeaseId,
                profileFolderName: ProfileFolderName
            );

            // If token expired or unauthorized (401), automatically refresh the JWT token and retry solve once
            if (!result.Success &&
                (result.ServerResponseJson?.Contains("Token expired", StringComparison.OrdinalIgnoreCase) == true ||
                 result.ServerResponseJson?.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) == true ||
                 result.ServerResponseJson?.Contains("Invalid token", StringComparison.OrdinalIgnoreCase) == true ||
                 result.Message.Contains("Token expired", StringComparison.OrdinalIgnoreCase) ||
                 result.Message.Contains("401", StringComparison.OrdinalIgnoreCase)) &&
                _authService != null &&
                !string.IsNullOrWhiteSpace(_settings.RefreshToken))
            {
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🔄 [CAPTCHA SOLVER] ตรวจพบ JWT Token หมดอายุ (401) กำลังขอต่ออายุ Token อัตโนมัติ (Auto-refresh token)...");
                Logger.Info("CaptchaSolver", $"Solver token expired on '{Name}'. Attempting auto-refresh token...");

                var refreshRes = await _authService.RefreshTokenAsync(_settings.RefreshToken, _settings.AuthApiEndpoint);
                if (refreshRes.Success && !string.IsNullOrWhiteSpace(refreshRes.AccessToken))
                {
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🔑 [CAPTCHA SOLVER] ต่ออายุ Token สำเร็จ! กำลังส่งคำขอแก้ Captcha ใหม่อีกครั้ง...");
                    Logger.Info("CaptchaSolver", $"Token refreshed successfully. Retrying captcha solve on '{Name}'...");

                    _settings.AuthToken = refreshRes.AccessToken;
                    _settings.CaptchaSolverJwtToken = refreshRes.AccessToken;
                    if (!string.IsNullOrWhiteSpace(refreshRes.RefreshToken))
                    {
                        _settings.RefreshToken = refreshRes.RefreshToken;
                    }

                    // Retry solve immediately
                    result = await _cdpService.SolveCaptchaAsync(
                        cdpPort: CdpPort,
                        detection: detection ?? _lastDetectionResult,
                        solverMode: _settings.CaptchaSolverMode,
                        solverApiEndpoint: _settings.CaptchaSolverEndpoint,
                        solverJwtToken: refreshRes.AccessToken,
                        customMockJsonPath: null,
                        progressLogger: msg => AppendConsole(msg),
                        delaySeconds: 0,
                        instanceLeaseId: ActiveLeaseId,
                        profileFolderName: ProfileFolderName
                    );
                }
                else
                {
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] ⚠️ [CAPTCHA SOLVER] ต่ออายุ Token อัตโนมัติไม่สำเร็จ ({refreshRes.Message})");
                }
            }

            if (result.Success)
            {
                StatusMessage = $"✅ แก้ Captcha สำเร็จ ({result.ElapsedSeconds:F1}s)";
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🎉 [CAPTCHA SOLVED] แก้ไข Captcha สำเร็จเรียบร้อยใน {result.ElapsedSeconds:F1} วินาที!");
                Logger.Info("CaptchaSolver", $"Captcha solved successfully on '{Name}' in {result.ElapsedSeconds:F1}s (Mode: {_settings.CaptchaSolverMode}).");
                AudioAlertService.StopAlert();
                IsCaptchaAlertVisible = false;

                // Telegram Alert on Solved
                if (_settings.EnableTelegramAlert && _settings.TelegramAlertOnSolved &&
                    !string.IsNullOrWhiteSpace(_settings.TelegramBotToken) && !string.IsNullOrWhiteSpace(_settings.TelegramChatId))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            string caption = $"✅ <b>[TikTok LIVE Studio] แก้ Captcha สำเร็จ!</b>\n\n" +
                                             $"• บัญชี: <b>{Name}</b> (Port: {CdpPort})\n" +
                                             $"• ใช้เวลา: {result.ElapsedSeconds:F1} วินาที\n" +
                                             $"• โหมด: {_settings.CaptchaSolverMode}";
                            await _telegramService.SendMessageAsync(_settings.TelegramBotToken, _settings.TelegramChatId, caption);
                        }
                        catch { }
                    });
                }
            }
            else
            {
                StatusMessage = $"❌ การแก้ Captcha ล้มเหลว: {result.Message}";
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] ❌ [CAPTCHA FAILED] {result.Message}");
                Logger.Warn("CaptchaSolver", $"Captcha solve failed on '{Name}' (Port: {CdpPort}): {result.Message}");

                // Telegram Alert on Failed
                if (_settings.EnableTelegramAlert && _settings.TelegramAlertOnFailed &&
                    !string.IsNullOrWhiteSpace(_settings.TelegramBotToken) && !string.IsNullOrWhiteSpace(_settings.TelegramChatId))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            string caption = $"❌ <b>[TikTok LIVE Studio] แก้ Captcha ล้มเหลว!</b>\n\n" +
                                             $"• บัญชี: <b>{Name}</b> (Port: {CdpPort})\n" +
                                             $"• ข้อความ: {result.Message}\n" +
                                             $"• โหมด: {_settings.CaptchaSolverMode}";
                            await _telegramService.SendMessageAsync(_settings.TelegramBotToken, _settings.TelegramChatId, caption);
                        }
                        catch { }
                    });
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("Captcha", $"Error solving captcha: {ex.Message}", ex);
            StatusMessage = $"เกิดข้อผิดพลาดในการแก้: {ex.Message}";
            return null;
        }
        finally
        {
            IsSolvingCaptcha = false;
            OnPropertyChanged(nameof(CanSolveCaptcha));
        }
    }

    [RelayCommand]
    public void SilenceAlert()
    {
        AudioAlertService.StopAlert();
        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🔇 ปิดเสียงแจ้งเตือน Captcha แล้ว");
    }

    [RelayCommand]
    public void OpenCaptchaImageFolder()
    {
        string dir = !string.IsNullOrWhiteSpace(_settings.CaptchaSaveDirectory) && Directory.Exists(_settings.CaptchaSaveDirectory)
            ? _settings.CaptchaSaveDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures), "TikTokStudioCaptures");

        if (!Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); } catch { }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"ไม่สามารถเปิดโฟลเดอร์: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SaveCaptchaImageAsAsync()
    {
        byte[]? bytes = _lastDetectionResult?.CapturedImageBytes;
        if (bytes == null || bytes.Length == 0)
        {
            StatusMessage = "ไม่มีภาพ Captcha ให้บันทึก";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"Save Captcha Image - {Name}",
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|All Files (*.*)|*.*",
            FileName = $"Captcha_{ProfileFolderName}_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            DefaultExt = "png"
        };

        if (dialog.ShowDialog() == true)
        {
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            StatusMessage = $"บันทึกรูปภาพเรียบร้อย: {Path.GetFileName(dialog.FileName)}";
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] 💾 บันทึกรูปภาพ Captcha แล้ว: {dialog.FileName}");
        }
    }

    [RelayCommand]
    public void CopyCaptchaImage()
    {
        if (CaptchaImageSource is System.Windows.Media.Imaging.BitmapSource bmpSource)
        {
            try
            {
                Clipboard.SetImage(bmpSource);
                StatusMessage = "คัดลอกรูปภาพ Captcha ลง Clipboard แล้ว";
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] 📋 คัดลอกรูปภาพ Captcha ลง Clipboard เรียบร้อย");
            }
            catch (Exception ex)
            {
                StatusMessage = $"คัดลอกรูปภาพไม่สำเร็จ: {ex.Message}";
            }
        }
        else
        {
            StatusMessage = "ไม่มีรูปภาพให้คัดลอก";
        }
    }

    private static bool IsJwtTokenExpiredOrClose(string? jwtToken, int bufferSeconds = 180)
    {
        if (string.IsNullOrWhiteSpace(jwtToken)) return true;
        try
        {
            var parts = jwtToken.Split('.');
            if (parts.Length < 2) return true;
            string payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }
            byte[] bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.TryGetProperty("exp", out var expElem) && expElem.TryGetInt64(out long expUnix))
            {
                var expTime = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                return DateTimeOffset.UtcNow.AddSeconds(bufferSeconds) >= expTime;
            }
        }
        catch { }
        return false;
    }
}
