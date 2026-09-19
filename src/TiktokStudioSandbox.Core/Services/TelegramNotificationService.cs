using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Interfaces;

namespace TiktokStudioSandbox.Core.Services;

public class TelegramNotificationService : ITelegramNotificationService
{
    private readonly HttpClient _httpClient;

    public TelegramNotificationService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<bool> SendMessageAsync(string botToken, string chatId, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        try
        {
            string url = $"https://api.telegram.org/bot{botToken.Trim()}/sendMessage";
            using var content = new FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("chat_id", chatId.Trim()),
                new System.Collections.Generic.KeyValuePair<string, string>("text", message),
                new System.Collections.Generic.KeyValuePair<string, string>("parse_mode", "HTML")
            });

            using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SendPhotoAsync(
        string botToken,
        string chatId,
        byte[] photoBytes,
        string caption,
        string fileName = "captcha.png",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId) || photoBytes == null || photoBytes.Length == 0)
        {
            return false;
        }

        try
        {
            string url = $"https://api.telegram.org/bot{botToken.Trim()}/sendPhoto";
            using var formData = new MultipartFormDataContent();
            formData.Add(new StringContent(chatId.Trim()), "chat_id");
            
            if (!string.IsNullOrWhiteSpace(caption))
            {
                formData.Add(new StringContent(caption), "caption");
                formData.Add(new StringContent("HTML"), "parse_mode");
            }

            var imageContent = new ByteArrayContent(photoBytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            formData.Add(imageContent, "photo", fileName);

            using var response = await _httpClient.PostAsync(url, formData, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(string botToken, string chatId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return (false, "กรุณากรอก Telegram Bot Token");
        }

        if (string.IsNullOrWhiteSpace(chatId))
        {
            return (false, "กรุณากรอก Telegram Chat ID");
        }

        try
        {
            // 1. Verify Bot Token with getMe
            string getMeUrl = $"https://api.telegram.org/bot{botToken.Trim()}/getMe";
            using var getMeResponse = await _httpClient.GetAsync(getMeUrl, cancellationToken).ConfigureAwait(false);
            string getMeJson = await getMeResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!getMeResponse.IsSuccessStatusCode)
            {
                return (false, $"Bot Token ไม่ถูกต้อง หรือไม่สามารถเชื่อมต่อได้: {getMeResponse.StatusCode}");
            }

            string botUsername = "UnknownBot";
            try
            {
                using var doc = JsonDocument.Parse(getMeJson);
                if (doc.RootElement.TryGetProperty("result", out var res) && res.TryGetProperty("username", out var u))
                {
                    botUsername = u.GetString() ?? botUsername;
                }
            }
            catch { }

            // 2. Send Test Message to Chat ID
            string testMsg = $"🤖 <b>[TikTok LIVE Studio Sandbox]</b>\n\n✅ <b>ทดสอบการเชื่อมต่อ Telegram สำเร็จ!</b>\n• บอท: @{botUsername}\n• เวลา: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n• สถานะ: พร้อมรับการแจ้งเตือน CAPTCHA และเหตุการณ์สตรีม";
            bool sendOk = await SendMessageAsync(botToken, chatId, testMsg, cancellationToken).ConfigureAwait(false);

            if (sendOk)
            {
                return (true, $"เชื่อมต่อและส่งข้อความทดสอบไปยัง @{botUsername} (Chat ID: {chatId}) สำเร็จ!");
            }
            else
            {
                return (false, $"Bot Token ถูกต้อง (@{botUsername}) แต่ไม่สามารถส่งข้อความไปยัง Chat ID '{chatId}' ได้ (กรุณากด Start บอทก่อน หรือตรวจสอบ Chat ID)");
            }
        }
        catch (Exception ex)
        {
            return (false, $"เกิดข้อผิดพลาดในการเชื่อมต่อ Telegram: {ex.Message}");
        }
    }
}
