using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Interfaces;

public interface ICdpAutomationService : IDisposable
{
    Task<bool> CheckConnectionAsync(int cdpPort);
    Task<bool> SendChatMessageAsync(int cdpPort, string message);
    Task<string?> GetChatPageUrlAsync(int cdpPort);
    Task<List<LiveChatMessageItem>> FetchLiveChatMessagesAsync(int cdpPort, string? targetChannel = null);

    // Debug Features
    Task<string> ExecuteScriptAsync(int cdpPort, string script);
    Task StartNetworkHookAsync(int cdpPort, Action<NetworkTrafficItem> onRequest, Action<NetworkTrafficItem> onResponse);
    Task StopNetworkHookAsync(int cdpPort);
    Task<StorageDumpResult> GetCookiesAndStorageAsync(int cdpPort);
    Task<PageHtmlResult?> GetFullHtmlAsync(int cdpPort);
    Task<CaptchaDetectionResult> DetectCaptchaElementsAsync(int cdpPort, bool forceFullHtml = false);
    Task<CaptchaSolveResult> SolveCaptchaWithMockAsync(
        int cdpPort,
        CaptchaDetectionResult? detection = null,
        string? customMockJsonPath = null,
        Action<string>? progressLogger = null,
        int delaySeconds = 0,
        string? instanceLeaseId = null,
        string? profileFolderName = null);
    Task<CaptchaSolveResult> SolveCaptchaAsync(
        int cdpPort,
        CaptchaDetectionResult? detection = null,
        CaptchaSolverType solverMode = CaptchaSolverType.LocalApiServer,
        string? solverApiEndpoint = null,
        string? solverJwtToken = null,
        string? customMockJsonPath = null,
        Action<string>? progressLogger = null,
        int delaySeconds = 0,
        string? instanceLeaseId = null,
        string? profileFolderName = null);
    Task<(bool Success, string Message)> TestSolverApiConnectionAsync(string? endpoint = null, string? jwtToken = null);
}
