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
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;

namespace FlowUltraTech.App.ViewModels;

public partial class InstanceCardViewModel : ObservableObject
{
    // ==========================================
    // Cookies & Storage Properties
    // ==========================================
    public ObservableCollection<CookieItem> Cookies { get; } = new();
    public ICollectionView FilteredCookies { get; }

    public ObservableCollection<StorageEntry> LocalStorageItems { get; } = new();
    public ICollectionView FilteredLocalStorage { get; }

    public ObservableCollection<StorageEntry> SessionStorageItems { get; } = new();
    public ICollectionView FilteredSessionStorage { get; }

    [ObservableProperty]
    private string _cookieNameFilter = string.Empty;

    [ObservableProperty]
    private string _cookieDomainFilter = string.Empty;

    [ObservableProperty]
    private string _cookieValueFilter = string.Empty;

    [ObservableProperty]
    private string _storageKeyFilter = string.Empty;

    [ObservableProperty]
    private string _storageValueFilter = string.Empty;

    [ObservableProperty]
    private bool _isStorageAutoRefresh = true;

    [ObservableProperty]
    private string _storageStatus = "พร้อมอัปเดตเรียลไทม์อัตโนมัติ";
    partial void OnCookieNameFilterChanged(string value) => FilteredCookies?.Refresh();
    partial void OnCookieDomainFilterChanged(string value) => FilteredCookies?.Refresh();
    partial void OnCookieValueFilterChanged(string value) => FilteredCookies?.Refresh();

    partial void OnStorageKeyFilterChanged(string value)
    {
        FilteredLocalStorage?.Refresh();
        FilteredSessionStorage?.Refresh();
    }

    partial void OnStorageValueFilterChanged(string value)
    {
        FilteredLocalStorage?.Refresh();
        FilteredSessionStorage?.Refresh();
    }

    private bool _isFetchingStorage;

    private void EnsureStorageTimer()
    {
        if (_storageRefreshTimer == null)
        {
            try
            {
                _storageRefreshTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(15)
                };
                _storageRefreshTimer.Tick += async (_, _) =>
                {
                    if (IsRunning && !_isFetchingStorage && !_isCheckingCaptcha && !IsSolvingCaptcha)
                    {
                        _isFetchingStorage = true;
                        try
                        {
                            await FetchCookiesAndStorageAsync();
                        }
                        finally
                        {
                            _isFetchingStorage = false;
                        }
                    }
                };
            }
            catch { }
        }
    }

    partial void OnIsStorageAutoRefreshChanged(bool value)
    {
        EnsureStorageTimer();
        if (value)
        {
            _storageRefreshTimer?.Start();
            if (IsRunning)
            {
                _ = FetchCookiesAndStorageAsync();
            }
        }
        else
        {
            _storageRefreshTimer?.Stop();
        }
    }

    private void NotifyLiveChatCounts()
    {
        OnPropertyChanged(nameof(FilterChatLabel));
        OnPropertyChanged(nameof(FilterGiftLabel));
        OnPropertyChanged(nameof(FilterLikeFollowLabel));
        OnPropertyChanged(nameof(FilterAutoReplyLabel));
        OnPropertyChanged(nameof(FilterOtherLabel));
        OnPropertyChanged(nameof(LiveChatCountSummary));
    }

    private bool FilterCookieItem(object obj)
    {
        if (obj is not CookieItem cookie) return false;

        if (!string.IsNullOrWhiteSpace(CookieNameFilter) && (cookie.Name == null || !cookie.Name.Contains(CookieNameFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!string.IsNullOrWhiteSpace(CookieDomainFilter) && (cookie.Domain == null || !cookie.Domain.Contains(CookieDomainFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!string.IsNullOrWhiteSpace(CookieValueFilter) && (cookie.Value == null || !cookie.Value.Contains(CookieValueFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private bool FilterLocalStorageItem(object obj)
    {
        if (obj is not StorageEntry entry) return false;

        if (!string.IsNullOrWhiteSpace(StorageKeyFilter) && (entry.Key == null || !entry.Key.Contains(StorageKeyFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!string.IsNullOrWhiteSpace(StorageValueFilter) && (entry.Value == null || !entry.Value.Contains(StorageValueFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private bool FilterSessionStorageItem(object obj)
    {
        if (obj is not StorageEntry entry) return false;

        if (!string.IsNullOrWhiteSpace(StorageKeyFilter) && (entry.Key == null || !entry.Key.Contains(StorageKeyFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!string.IsNullOrWhiteSpace(StorageValueFilter) && (entry.Value == null || !entry.Value.Contains(StorageValueFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }


    // ==========================================
    // Network Hook Commands & Auto Monitor
    // ==========================================
    public async Task StartAutoMonitoringAsync()
    {
        if (Status != InstanceStatus.Running) return;

        // 1. Ensure Storage Auto-Refresh timer is running
        IsStorageAutoRefresh = true;
        EnsureStorageTimer();
        _storageRefreshTimer?.Start();

        // 2. Ensure Captcha Auto-Detection timer is running (every 2 seconds)
        if (IsCaptchaAutoDetect)
        {
            EnsureCaptchaTimer();
            _captchaDetectTimer?.Start();
            _ = CheckCaptchaInternalAsync(isAuto: true);
        }

        // 4. Trigger initial snapshot fetch of Cookies & Storage
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            if (IsRunning)
            {
                if (Application.Current != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        await FetchCookiesAndStorageAsync();
                    });
                }
                else
                {
                    await FetchCookiesAndStorageAsync();
                }
            }
        });

        // 5. Ensure Live Chat Monitoring timer is running (every 2 seconds)
        // Only start chat monitoring if account is logged in AND a valid streamer username is configured
        if (IsLiveChatMonitoring && IsLoggedIn && !string.IsNullOrWhiteSpace(StreamerUsername) && !StreamerUsername.StartsWith("Account", StringComparison.OrdinalIgnoreCase))
        {
            EnsureChatTimer();
            _chatMonitorTimer?.Start();
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                if (IsLiveChatMonitoring)
                {
                    await PollLiveChatInternalAsync();
                }
            });
        }
    }

    // ==========================================
    // Cookies & Storage Commands
    // ==========================================
    [RelayCommand]
    public async Task FetchCookiesAndStorageAsync()
    {
        if (Status != InstanceStatus.Running)
        {
            StorageStatus = "หน้าต่างยังไม่ได้เปิดทำงาน (กรุณากด Start ก่อน)";
            return;
        }

        StorageStatus = "กำลังดึงข้อมูล Cookies & Storage จาก Chromium...";
        try
        {
            var dump = await _cdpService.GetCookiesAndStorageAsync(CdpPort);

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

            SessionIdToken = dump.SessionIdToken ?? "ไม่พบ sessionid";
            CsrfToken = dump.CsrfToken ?? "ไม่พบ tt_csrf_token";
            StorageStatus = $"อัปเดตเรียลไทม์สำเร็จ ({dump.Cookies.Count} Cookies, {dump.LocalStorage.Count} LocalStorage) เวลา {dump.FetchedAt:HH:mm:ss}";
            SessionFetchStatus = StorageStatus;
        }
        catch (Exception ex)
        {
            StorageStatus = $"ดึงข้อมูลไม่สำเร็จ: {ex.Message}";
        }
    }

    [RelayCommand]
    public Task FetchSessionInfoAsync() => FetchCookiesAndStorageAsync();

    [RelayCommand]
    public void ResetCookieFilters()
    {
        CookieNameFilter = string.Empty;
        CookieDomainFilter = string.Empty;
        CookieValueFilter = string.Empty;
        FilteredCookies?.Refresh();
    }

    [RelayCommand]
    public void ResetStorageFilters()
    {
        StorageKeyFilter = string.Empty;
        StorageValueFilter = string.Empty;
        FilteredLocalStorage?.Refresh();
        FilteredSessionStorage?.Refresh();
    }

    [RelayCommand]
    public void CopySessionId()
    {
        if (!string.IsNullOrEmpty(SessionIdToken) && SessionIdToken != "-")
        {
            Clipboard.SetText(SessionIdToken);
            SessionFetchStatus = "คัดลอก sessionid แล้ว!";
        }
    }

    [RelayCommand]
    public void CopyCsrfToken()
    {
        if (!string.IsNullOrEmpty(CsrfToken) && CsrfToken != "-")
        {
            Clipboard.SetText(CsrfToken);
            SessionFetchStatus = "คัดลอก tt_csrf_token แล้ว!";
        }
    }


}
