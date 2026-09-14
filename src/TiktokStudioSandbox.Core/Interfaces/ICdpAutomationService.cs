using System;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Interfaces;

public interface ICdpAutomationService : IDisposable
{
    Task<bool> CheckConnectionAsync(int cdpPort);
    Task<bool> SendChatMessageAsync(int cdpPort, string message);
    Task<string?> GetChatPageUrlAsync(int cdpPort);

    // Debug Features
    Task<string> ExecuteScriptAsync(int cdpPort, string script);
    Task StartNetworkHookAsync(int cdpPort, Action<NetworkTrafficItem> onRequest, Action<NetworkTrafficItem> onResponse);
    Task StopNetworkHookAsync(int cdpPort);
    Task<StorageDumpResult> GetCookiesAndStorageAsync(int cdpPort);
    Task<PageHtmlResult?> GetFullHtmlAsync(int cdpPort);
    Task<CaptchaDetectionResult> DetectCaptchaElementsAsync(int cdpPort);
}
