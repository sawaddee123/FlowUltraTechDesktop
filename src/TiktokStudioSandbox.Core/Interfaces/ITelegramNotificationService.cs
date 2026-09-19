using System.Threading;
using System.Threading.Tasks;

namespace TiktokStudioSandbox.Core.Interfaces;

public interface ITelegramNotificationService
{
    Task<bool> SendMessageAsync(string botToken, string chatId, string message, CancellationToken cancellationToken = default);
    Task<bool> SendPhotoAsync(string botToken, string chatId, byte[] photoBytes, string caption, string fileName = "captcha.png", CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> TestConnectionAsync(string botToken, string chatId, CancellationToken cancellationToken = default);
}
