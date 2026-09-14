using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Interfaces;

public interface IConfigStorageService
{
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    Task SaveSettingsAsync(AppSettings settings);
}
