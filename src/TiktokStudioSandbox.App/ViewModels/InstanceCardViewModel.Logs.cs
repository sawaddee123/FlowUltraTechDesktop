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
    // Debug Logs Properties
    // ==========================================
    public IInstanceLogger Logger { get; }
    public ObservableCollection<InstanceLogEntry> Logs { get; } = new();
    public ICollectionView FilteredLogs { get; }

    [ObservableProperty]
    private string _logLevelFilter = "ALL";

    [ObservableProperty]
    private string _logCategoryFilter = "ALL";

    [ObservableProperty]
    private string _logSearchText = string.Empty;

    [ObservableProperty]
    private InstanceLogEntry? _selectedLog;

    public string[] LogLevelOptions => ["ALL", "DEBUG", "INFO", "WARN", "ERROR"];
    public string[] LogCategoryOptions => ["ALL", "Process", "CDP", "Window", "Profile", "Network", "Storage", "Chat", "System", "General"];

    public string LogFilePath => Logger.LogFilePath;
    public string LogCountSummary => $"ทั้งหมด {Logs.Count} รายการ (Error: {Logs.Count(x => x.Level == InstanceLogLevel.Error)}, Warn: {Logs.Count(x => x.Level == InstanceLogLevel.Warn)})";
    public string SelectedLogFullText => SelectedLog != null ? SelectedLog.FullLine : string.Empty;
    public Visibility SelectedLogDetailsVisibility => SelectedLog != null ? Visibility.Visible : Visibility.Collapsed;


    partial void OnLogLevelFilterChanged(string value) => FilteredLogs?.Refresh();
    partial void OnLogCategoryFilterChanged(string value) => FilteredLogs?.Refresh();
    partial void OnLogSearchTextChanged(string value) => FilteredLogs?.Refresh();
    partial void OnSelectedLogChanged(InstanceLogEntry? value)
    {
        OnPropertyChanged(nameof(SelectedLogFullText));
        OnPropertyChanged(nameof(SelectedLogDetailsVisibility));
    }


    private bool FilterLogEntry(object obj)
    {
        if (obj is not InstanceLogEntry entry) return false;

        if (!string.IsNullOrEmpty(LogLevelFilter) && LogLevelFilter != "ALL")
        {
            if (!string.Equals(entry.LevelDisplay, LogLevelFilter, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrEmpty(LogCategoryFilter) && LogCategoryFilter != "ALL")
        {
            if (!string.Equals(entry.Category, LogCategoryFilter, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(LogSearchText))
        {
            bool match = (entry.Message != null && entry.Message.Contains(LogSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (entry.Category != null && entry.Category.Contains(LogSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (entry.Details != null && entry.Details.Contains(LogSearchText, StringComparison.OrdinalIgnoreCase));
            if (!match) return false;
        }

        return true;
    }


    [RelayCommand]
    public void ClearLogs()
    {
        Logs.Clear();
        Logger.Clear();
        SelectedLog = null;
        OnPropertyChanged(nameof(LogCountSummary));
    }

    [RelayCommand]
    public void CopyAllLogs()
    {
        var sb = new StringBuilder();
        foreach (InstanceLogEntry item in FilteredLogs)
        {
            sb.AppendLine(item.FullLine);
        }
        if (sb.Length > 0)
        {
            Clipboard.SetText(sb.ToString());
        }
    }

    [RelayCommand]
    public void CopySelectedLog()
    {
        if (SelectedLog != null)
        {
            Clipboard.SetText(SelectedLog.FullLine);
        }
    }

    [RelayCommand]
    public void OpenLogFile()
    {
        try
        {
            if (!System.IO.File.Exists(Logger.LogFilePath))
            {
                System.IO.File.WriteAllText(Logger.LogFilePath, $"=== Log file for {Name} ===\n");
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Logger.LogFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error("System", $"Cannot open log file: {ex.Message}", ex);
        }
    }

    [RelayCommand]
    public void OpenLogsFolder()
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(Logger.LogFilePath) ?? string.Empty;
            if (System.IO.Directory.Exists(dir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("System", $"Cannot open logs folder: {ex.Message}", ex);
        }
    }

    [RelayCommand]
    public void ResetLogFilters()
    {
        LogLevelFilter = "ALL";
        LogCategoryFilter = "ALL";
        LogSearchText = string.Empty;
        FilteredLogs?.Refresh();
    }


}
