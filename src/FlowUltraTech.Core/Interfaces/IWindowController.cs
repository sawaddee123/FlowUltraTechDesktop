using System;
using System.Collections.Generic;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Interfaces;

public interface IWindowController
{
    List<IntPtr> FindWindowHandlesForProcess(int processId);
    List<IntPtr> FindWindowHandlesForPort(int cdpPort, int? processId = null);
    List<StudioWindowHandleInfo> GetWindowHandlesInfoForInstance(int? processId, int cdpPort);
    bool SetSingleWindowVisibility(IntPtr hWnd, bool visible);
    bool SetAllWindowsVisibilityForInstance(int? processId, int cdpPort, bool visible);
    bool SetWindowVisibility(int processId, bool visible);
    bool SetWindowVisibilityForInstance(int? processId, int cdpPort, bool visible);
    bool IsAnyWindowVisible(int processId);
    bool IsAnyWindowVisibleForInstance(int? processId, int cdpPort);
    void BringToFront(int processId);
    void ClearCacheForPort(int cdpPort);
}

