using System;
using System.Threading.Tasks;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Interfaces;

public interface IInstanceOrchestrator
{
    Task<bool> StartInstanceAsync(StudioInstanceProfile profile, AppSettings settings);
    Task<bool> StartUltraLightLoginAsync(StudioInstanceProfile profile, AppSettings settings);
    Task<bool> StopInstanceAsync(StudioInstanceProfile profile);
    bool ToggleWindowVisibility(StudioInstanceProfile profile);
    bool ShowWindow(StudioInstanceProfile profile);
    bool HideWindow(StudioInstanceProfile profile);
    Task<bool> SendChatAsync(StudioInstanceProfile profile, string message);
    void RefreshStatus(StudioInstanceProfile profile);
    void StopAllInstances();
    event Action<StudioInstanceProfile>? InstanceExited;
}
