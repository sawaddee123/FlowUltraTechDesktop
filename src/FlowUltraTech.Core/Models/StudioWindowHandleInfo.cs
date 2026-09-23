using System;

namespace FlowUltraTech.Core.Models;

public class StudioWindowHandleInfo
{
    public IntPtr Handle { get; set; }
    public string HandleHex => $"0x{Handle.ToInt64():X8}";
    public string Title { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string SizeDisplay => $"{Width} x {Height}";
    public bool IsVisible { get; set; }
    public bool IsIconic { get; set; }
    public bool IsPrimary { get; set; }
    public string WindowRole { get; set; } = "Auxiliary / Background";
}
