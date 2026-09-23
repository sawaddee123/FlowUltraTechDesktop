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
    // Network Monitor Properties
    // ==========================================
    public ObservableCollection<NetworkTrafficItem> NetworkTraffic { get; } = new();
    public ICollectionView FilteredNetworkTraffic { get; }

    [ObservableProperty]
    private bool _isNetworkHooking;

    public string NetworkHookButtonText => IsNetworkHooking ? "หยุดดักจับ API ชั่วคราว" : "เริ่มดักจับ Network API";
    public string NetworkHookBadgeText => IsNetworkHooking ? "Auto Monitor (เปิดอยู่)" : "พักการดักจับ";

    [ObservableProperty]
    private NetworkTrafficItem? _selectedTraffic;

    [ObservableProperty]
    private string _networkSearchText = string.Empty;

    [ObservableProperty]
    private string _networkUrlFilter = string.Empty;

    [ObservableProperty]
    private string _networkMethodFilter = "ALL";

    [ObservableProperty]
    private string _networkStatusFilter = "ALL";

    public string[] MethodOptions => ["ALL", "GET", "POST", "OPTIONS", "PUT", "DELETE"];
    public string[] StatusOptions => ["ALL", "2xx (สำเร็จ)", "4xx/5xx (Error)", "Pending (รอดำเนินการ)"];

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


    partial void OnIsNetworkHookingChanged(bool value)
    {
        OnPropertyChanged(nameof(NetworkHookButtonText));
        OnPropertyChanged(nameof(NetworkHookBadgeText));
    }


    partial void OnNetworkSearchTextChanged(string value) => FilteredNetworkTraffic?.Refresh();
    partial void OnNetworkUrlFilterChanged(string value) => FilteredNetworkTraffic?.Refresh();
    partial void OnNetworkMethodFilterChanged(string value) => FilteredNetworkTraffic?.Refresh();
    partial void OnNetworkStatusFilterChanged(string value) => FilteredNetworkTraffic?.Refresh();

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

    private void EnsureStorageTimer()
    {
        if (_storageRefreshTimer == null)
        {
            try
            {
                _storageRefreshTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(10)
                };
                _storageRefreshTimer.Tick += async (_, _) =>
                {
                    if (IsRunning)
                    {
                        await FetchCookiesAndStorageAsync();
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

    private bool FilterNetworkTrafficItem(object obj)
    {
        if (obj is not NetworkTrafficItem item) return false;

        if (!string.IsNullOrEmpty(NetworkMethodFilter) && NetworkMethodFilter != "ALL")
        {
            if (!string.Equals(item.Method, NetworkMethodFilter, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrEmpty(NetworkStatusFilter) && NetworkStatusFilter != "ALL")
        {
            if (NetworkStatusFilter.StartsWith("2xx") && (item.StatusCode < 200 || item.StatusCode >= 300)) return false;
            if (NetworkStatusFilter.StartsWith("4xx") && item.StatusCode < 400) return false;
            if (NetworkStatusFilter.StartsWith("Pending") && !item.StatusDisplay.Contains("Pending", StringComparison.OrdinalIgnoreCase)) return false;
        }

        if (!string.IsNullOrWhiteSpace(NetworkUrlFilter))
        {
            if (item.Url == null || !item.Url.Contains(NetworkUrlFilter, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(NetworkSearchText))
        {
            bool match = (item.Url != null && item.Url.Contains(NetworkSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (item.Method != null && item.Method.Contains(NetworkSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (item.StatusDisplay != null && item.StatusDisplay.Contains(NetworkSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (item.ResourceType != null && item.ResourceType.Contains(NetworkSearchText, StringComparison.OrdinalIgnoreCase));
            if (!match) return false;
        }

        return true;
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

        // 2. Ensure Captcha Auto-Detection timer is running (every 5 seconds)
        if (IsCaptchaAutoDetect)
        {
            EnsureCaptchaTimer();
            _captchaDetectTimer?.Start();
        }

        // 3. Start Network Hooking automatically
        if (!IsNetworkHooking)
        {
            await StartNetworkHookInternalAsync();
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

    public async Task StartNetworkHookInternalAsync()
    {
        if (Status != InstanceStatus.Running) return;

        IsNetworkHooking = true;
        await _cdpService.StartNetworkHookAsync(CdpPort, 
            onRequest: (item) =>
            {
                void AddItem()
                {
                    if (NetworkTraffic.Count > 1000) NetworkTraffic.RemoveAt(0);
                    NetworkTraffic.Add(item);
                }

                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        (Action)AddItem);
                }
                else
                {
                    AddItem();
                }
            },
            onResponse: (item) =>
            {
                // Item triggers INotifyPropertyChanged so datagrid updates in-place
            });
        AppendConsole($"[{DateTime.Now:HH:mm:ss}] [Auto Monitor] เริ่มดักจับ Network API อัตโนมัติบนพอร์ต {CdpPort} เรียบร้อย");
    }

    public async Task StopNetworkHookInternalAsync()
    {
        IsNetworkHooking = false;
        await _cdpService.StopNetworkHookAsync(CdpPort);
        AppendConsole($"[{DateTime.Now:HH:mm:ss}] [Network] หยุดดักจับ API บนพอร์ต {CdpPort}");
    }

    [RelayCommand]
    public async Task ToggleNetworkHookAsync()
    {
        if (Status != InstanceStatus.Running)
        {
            StatusMessage = "หน้าต่างยังไม่ได้เปิดทำงาน (กรุณากด Start ก่อนดักจับ Network)";
            return;
        }

        if (!IsNetworkHooking)
        {
            await StartNetworkHookInternalAsync();
        }
        else
        {
            await StopNetworkHookInternalAsync();
        }
    }

    [RelayCommand]
    public void ClearTraffic()
    {
        NetworkTraffic.Clear();
        SelectedTraffic = null;
    }

    [RelayCommand]
    public void ResetNetworkFilters()
    {
        NetworkSearchText = string.Empty;
        NetworkUrlFilter = string.Empty;
        NetworkMethodFilter = "ALL";
        NetworkStatusFilter = "ALL";
        FilteredNetworkTraffic?.Refresh();
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
    public void CopyPayload()
    {
        if (SelectedTraffic != null && !string.IsNullOrEmpty(SelectedTraffic.PostData))
        {
            Clipboard.SetText(SelectedTraffic.PostData);
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
    public void CopyTrafficUrl()
    {
        if (SelectedTraffic != null && !string.IsNullOrEmpty(SelectedTraffic.Url))
        {
            Clipboard.SetText(SelectedTraffic.Url);
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
    public void CopyCookieValue(CookieItem? item)
    {
        if (item != null && !string.IsNullOrEmpty(item.Value))
        {
            Clipboard.SetText(item.Value);
            StorageStatus = $"คัดลอก Cookie '{item.Name}' แล้ว!";
        }
    }

    [RelayCommand]
    public void CopyStorageItem(StorageEntry? item)
    {
        if (item != null && !string.IsNullOrEmpty(item.Value))
        {
            Clipboard.SetText(item.Value);
            StorageStatus = $"คัดลอก '{item.Key}' แล้ว!";
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
