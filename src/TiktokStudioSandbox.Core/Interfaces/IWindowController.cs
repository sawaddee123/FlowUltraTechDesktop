using System;
using System.Collections.Generic;

namespace TiktokStudioSandbox.Core.Interfaces;

public interface IWindowController
{
    List<IntPtr> FindWindowHandlesForProcess(int processId);
    List<IntPtr> FindWindowHandlesForPort(int cdpPort, int? processId = null);
    bool SetWindowVisibility(int processId, bool visible);
    bool SetWindowVisibilityForInstance(int? processId, int cdpPort, bool visible);
    bool IsAnyWindowVisible(int processId);
    bool IsAnyWindowVisibleForInstance(int? processId, int cdpPort);
    void BringToFront(int processId);
}
