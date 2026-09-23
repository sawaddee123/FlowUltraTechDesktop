using System;
using System.Windows;
using System.Windows.Input;

namespace FlowUltraTech.App.Views;

/// <summary>
/// Interaction logic for UpdateDialog.xaml
/// </summary>
public partial class UpdateDialog : Window
{
    public bool IsMandatory { get; }
    public string DownloadUrl { get; }

    public UpdateDialog(
        string currentVersion,
        string newVersion,
        string title,
        string description,
        bool isMandatory,
        string downloadUrl)
    {
        InitializeComponent();

        if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
        {
            Owner = Application.Current.MainWindow;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        IsMandatory = isMandatory;
        DownloadUrl = downloadUrl;

        CurrentVersionText.Text = $"v{currentVersion.Trim().TrimStart('v', 'V')}";
        NewVersionText.Text = $"v{newVersion.Trim().TrimStart('v', 'V')}";

        if (!string.IsNullOrWhiteSpace(title))
        {
            ReleaseTitleText.Text = title;
        }
        else
        {
            ReleaseTitleText.Text = $"เวอร์ชันใหม่ v{newVersion.Trim().TrimStart('v', 'V')}";
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            ReleaseDescriptionText.Text = description;
        }
        else
        {
            ReleaseDescriptionText.Text = "มีการปรับปรุงประสิทธิภาพและเพิ่มความเสถียรของระบบ";
        }

        if (isMandatory)
        {
            MandatoryBanner.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Collapsed;
            FooterHelperText.Text = "จำเป็นต้องอัปเดตเพื่อเข้าใช้งานระบบ Sandbox ต่อไป";
        }
        else
        {
            MandatoryBanner.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Visible;
            FooterHelperText.Text = "ระบบจะเปิดลิงก์ดาวน์โหลดตัวติดตั้งเวอร์ชันใหม่ให้ท่าน";
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsMandatory)
        {
            // Mandatory update cannot be bypassed; exit application
            Application.Current?.Shutdown();
        }
        else
        {
            DialogResult = false;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
