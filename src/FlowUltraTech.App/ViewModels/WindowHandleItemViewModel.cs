using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.App.ViewModels;

public class WindowHandleItemViewModel : ObservableObject
{
    private readonly Action<WindowHandleItemViewModel, bool>? _onVisibilityChanged;
    private readonly Action<WindowHandleItemViewModel>? _onBringToFront;
    private bool _suppressCallback;
    private bool _isVisible;

    public IntPtr Handle { get; }
    public string HandleHex => $"0x{Handle.ToInt64():X8}";
    public string Title { get; }
    public string ClassName { get; }
    public int ProcessId { get; }
    public int Width { get; }
    public int Height { get; }
    public string SizeDisplay => $"{Width} x {Height}";
    public string WindowRole { get; }
    public bool IsPrimary { get; }
    public bool IsIconic { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
            {
                if (!_suppressCallback)
                {
                    _onVisibilityChanged?.Invoke(this, value);
                }
            }
        }
    }

    public IRelayCommand BringToFrontCommand { get; }

    public WindowHandleItemViewModel(
        StudioWindowHandleInfo info,
        Action<WindowHandleItemViewModel, bool>? onVisibilityChanged = null,
        Action<WindowHandleItemViewModel>? onBringToFront = null)
    {
        Handle = info.Handle;
        Title = info.Title;
        ClassName = info.ClassName;
        ProcessId = info.ProcessId;
        Width = info.Width;
        Height = info.Height;
        WindowRole = info.WindowRole;
        IsPrimary = info.IsPrimary;
        IsIconic = info.IsIconic;
        _isVisible = info.IsVisible;

        _onVisibilityChanged = onVisibilityChanged;
        _onBringToFront = onBringToFront;

        BringToFrontCommand = new RelayCommand(() => _onBringToFront?.Invoke(this));
    }

    public void SetVisibleSilently(bool visible)
    {
        _suppressCallback = true;
        try
        {
            SetProperty(ref _isVisible, visible, nameof(IsVisible));
        }
        finally
        {
            _suppressCallback = false;
        }
    }
}
