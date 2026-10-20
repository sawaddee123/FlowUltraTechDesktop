#if DEBUG
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlowUltraTech.Core.Services;

namespace FlowUltraTech.App.Helpers;

/// <summary>
/// ตัวช่วยสำหรับนักพัฒนา (เฉพาะ Debug): เปิดหน้าหลักโดยไม่ล็อกอิน ใช้ config ชั่วคราว
/// แล้วแคปภาพหน้าจอตัวเองเป็น PNG ก่อนปิด ใช้ตรวจ UI แต่ละหน้า
/// args: --preview-main [--theme=light|dark] [--nav=0..4] [--demo=N] [--detail=TAB] [--shot=path] [--delay=sec]
/// </summary>
internal static class PreviewRunner
{
    private static string? Arg(string[] args, string name) =>
        args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?.Substring(name.Length + 1);

    public static void Run(App app, string[] args)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "flow_preview_" + Environment.ProcessId);
        Directory.CreateDirectory(tmp);
        var storage = new ConfigStorageService(Path.Combine(tmp, "config.json"));

        if (string.Equals(Arg(args, "--theme"), "light", StringComparison.OrdinalIgnoreCase))
            Theme.ThemeService.Apply(false);

        string? other = Arg(args, "--window");
        if (other != null)
        {
            Window w = other == "update"
                ? new Views.UpdateDialog("1.7.5", "1.8.0", "ปรับ UI ใหม่ทั้งแอป", "- หน้าตาใหม่ ธีมมืด/สว่าง - แก้ไขการซ่อนและแสดงหน้าต่าง", string.Equals(Arg(args, "--mandatory"), "1"), "http://localhost/x")
                : new Views.LoginWindow();
            w.Loaded += async (_, _) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                var sh = Arg(args, "--shot");
                if (!string.IsNullOrWhiteSpace(sh)) Capture(w, sh!);
                Application.Current.Shutdown();
            };
            app.Run(w);
            return;
        }

        var win = new MainWindow(storage)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 20,
            Top = 20,
        };

        string? shot = Arg(args, "--shot");
        int.TryParse(Arg(args, "--nav"), out int nav);
        int.TryParse(Arg(args, "--demo"), out int demo);
        int.TryParse(Arg(args, "--detail"), out int detailTab);
        bool hasDetail = Arg(args, "--detail") != null;
        double.TryParse(Arg(args, "--delay"), out double delay);
        if (delay <= 0) delay = 3;

        win.Loaded += async (_, _) =>
        {
            try
            {
                var vm = win.ViewModel;
                for (int i = 0; i < demo; i++) vm.AddInstanceCommand.Execute(null);
                await Task.Delay(400);
                if (hasDetail && vm.Instances.Count > 0)
                {
                    var card = vm.Instances[0];
                    card.ToggleDetailCommand.Execute(null);
                    card.SelectedDetailTabIndex = detailTab;
                }
                switch (nav)
                {
                    case 1: vm.NavigateCaptchaCommand.Execute(null); break;
                    case 2: vm.NavigateProxiesCommand.Execute(null); break;
                    case 3: vm.NavigateRulesCommand.Execute(null); break;
                    case 4: vm.NavigateSettingsCommand.Execute(null); break;
                }
                await Task.Delay(TimeSpan.FromSeconds(delay));
                if (!string.IsNullOrWhiteSpace(shot)) Capture(win, shot!);
            }
            catch (Exception ex)
            {
                App.Log($"[Preview] error: {ex}");
            }
            finally
            {
                Application.Current.Shutdown();
            }
        };

        app.Run(win);
    }

    private static void Capture(Window win, string path)
    {
        var visual = (Visual)win.Content;
        int w = (int)win.ActualWidth, h = (int)win.ActualHeight;
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
#endif
