using System.Threading.Tasks;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Interfaces;

public interface IConfigStorageService
{
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    Task SaveSettingsAsync(AppSettings settings);
}
