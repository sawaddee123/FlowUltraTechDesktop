using System.Diagnostics;
using System.Threading.Tasks;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Interfaces;

public interface IProcessLauncher
{
    Task<Process?> LaunchInstanceAsync(StudioInstanceProfile instance, AppSettings settings);
    Task<Process?> LaunchUltraLightBackgroundAsync(StudioInstanceProfile instance, AppSettings settings);
    bool KillInstance(StudioInstanceProfile instance);
    bool IsInstanceRunning(StudioInstanceProfile instance);
    int? FindProcessId(StudioInstanceProfile instance);
    bool CloseUpdateRelaunch();
}
