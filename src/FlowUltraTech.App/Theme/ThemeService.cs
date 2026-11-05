using System;
using System.Windows;
using Wpf.Ui.Appearance;

namespace FlowUltraTech.App.Theme;

/// <summary>สลับธีม Dark/Light โดยเปลี่ยนชุดโทเคนสีที่ merge ไว้ใน Application.Resources</summary>
public static class ThemeService
{
    private const int TokenDictionaryIndex = 2;

    public static bool IsDark { get; private set; } = true;

    public static event Action<bool>? ThemeChanged;

    public static void Apply(bool dark)
    {
        var app = Application.Current;
        if (app == null) return;

        var tokens = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Theme/Tokens.{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute)
        };

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > TokenDictionaryIndex)
            merged[TokenDictionaryIndex] = tokens;
        else
            merged.Add(tokens);

        try
        {
            ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light, updateAccent: false);
            // สีเน้นของ WPF-UI (CheckBox, Slider, ProgressBar) ให้ตรงกับสีหลักของธีม
            ApplicationAccentColorManager.Apply(
                dark ? System.Windows.Media.Color.FromRgb(0x6B, 0x9A, 0xFF) : System.Windows.Media.Color.FromRgb(0x2D, 0x54, 0xD3),
                dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
            // ApplicationThemeManager สลับพจนานุกรมของ WPF-UI อาจดันตำแหน่ง ให้ยืนยันว่าโทเคนยังอยู่ท้ายสุดก่อน Controls
            EnsureTokenOrder(merged, tokens);
        }
        catch { }

        IsDark = dark;
        ThemeChanged?.Invoke(dark);
    }

    private static void EnsureTokenOrder(System.Collections.ObjectModel.Collection<ResourceDictionary> merged, ResourceDictionary tokens)
    {
        int ti = merged.IndexOf(tokens);
        if (ti < 0) return;
        // โทเคนต้องมาหลัง ThemesDictionary/ControlsDictionary เสมอ เพื่อทับคีย์ของ WPF-UI
        int lastWpfUi = -1;
        for (int i = 0; i < merged.Count; i++)
        {
            var src = merged[i].Source?.OriginalString ?? string.Empty;
            if (src.Contains("Wpf.Ui", StringComparison.OrdinalIgnoreCase) || src.Contains("wpfui", StringComparison.OrdinalIgnoreCase))
                lastWpfUi = i;
        }
        if (ti < lastWpfUi)
        {
            merged.RemoveAt(ti);
            merged.Insert(lastWpfUi, tokens);
        }
    }
}
