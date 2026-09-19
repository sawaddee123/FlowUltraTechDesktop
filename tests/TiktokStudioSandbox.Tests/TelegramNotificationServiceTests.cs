using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Models;
using TiktokStudioSandbox.Core.Services;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class TelegramNotificationServiceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public HttpStatusCode StatusCodeToReturn { get; set; } = HttpStatusCode.OK;
        public string ContentToReturn { get; set; } = "{\"ok\": true, \"result\": {\"username\": \"TestBot\"}}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var response = new HttpResponseMessage(StatusCodeToReturn);
            response.Content = new StringContent(ContentToReturn);
            return response;
        }
    }

    [Fact]
    public async Task SendMessageAsync_WithValidInputs_PostsCorrectPayload()
    {
        var mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(mockHandler);
        var telegramService = new TelegramNotificationService(httpClient);

        string botToken = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";
        string chatId = "-100123456789";
        string message = "<b>Test Alert</b>";

        bool result = await telegramService.SendMessageAsync(botToken, chatId, message);

        Assert.True(result);
        Assert.NotNull(mockHandler.LastRequest);
        Assert.Equal(HttpMethod.Post, mockHandler.LastRequest.Method);
        Assert.Contains(botToken, mockHandler.LastRequest.RequestUri?.ToString());
        Assert.Contains("sendMessage", mockHandler.LastRequest.RequestUri?.ToString());
        Assert.Contains(chatId, mockHandler.LastRequestBody);
        Assert.Contains("Test+Alert", mockHandler.LastRequestBody);
    }

    [Fact]
    public async Task SendMessageAsync_WithEmptyInputs_ReturnsFalseWithoutCallingNetwork()
    {
        var mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(mockHandler);
        var telegramService = new TelegramNotificationService(httpClient);

        bool result1 = await telegramService.SendMessageAsync("", "-100123456789", "Hello");
        bool result2 = await telegramService.SendMessageAsync("123:token", "", "Hello");
        bool result3 = await telegramService.SendMessageAsync("123:token", "-100123456789", "");

        Assert.False(result1);
        Assert.False(result2);
        Assert.False(result3);
        Assert.Null(mockHandler.LastRequest);
    }

    [Fact]
    public async Task SendPhotoAsync_WithValidBytes_SendsMultipartFormData()
    {
        var mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(mockHandler);
        var telegramService = new TelegramNotificationService(httpClient);

        byte[] fakePngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        string botToken = "123456:ABC";
        string chatId = "987654";
        string caption = "Captcha Found!";

        bool result = await telegramService.SendPhotoAsync(botToken, chatId, fakePngBytes, caption);

        Assert.True(result);
        Assert.NotNull(mockHandler.LastRequest);
        Assert.Equal(HttpMethod.Post, mockHandler.LastRequest.Method);
        Assert.Contains("sendPhoto", mockHandler.LastRequest.RequestUri?.ToString());
        Assert.NotNull(mockHandler.LastRequestBody);
        Assert.Contains("chat_id", mockHandler.LastRequestBody);
        Assert.Contains("caption", mockHandler.LastRequestBody);
        Assert.Contains("Captcha Found!", mockHandler.LastRequestBody);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenGetMeFails_ReturnsFailureMessage()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.Unauthorized,
            ContentToReturn = "{\"ok\": false, \"error_code\": 401, \"description\": \"Unauthorized\"}"
        };
        var httpClient = new HttpClient(mockHandler);
        var telegramService = new TelegramNotificationService(httpClient);

        var (success, message) = await telegramService.TestConnectionAsync("invalid_token", "12345");

        Assert.False(success);
        Assert.Contains("ไม่ถูกต้อง", message);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenGetMeSucceedsAndSendMessageSucceeds_ReturnsSuccessMessage()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ContentToReturn = "{\"ok\": true, \"result\": {\"username\": \"MyLiveBot\"}}"
        };
        var httpClient = new HttpClient(mockHandler);
        var telegramService = new TelegramNotificationService(httpClient);

        var (success, message) = await telegramService.TestConnectionAsync("valid_token", "12345");

        Assert.True(success);
        Assert.Contains("MyLiveBot", message);
        Assert.Contains("สำเร็จ", message);
    }

    [Fact]
    public void AppSettings_HasTelegramSettingsWithSafeDefaults()
    {
        var settings = new AppSettings();

        Assert.False(settings.EnableTelegramAlert);
        Assert.Equal(string.Empty, settings.TelegramBotToken);
        Assert.Equal(string.Empty, settings.TelegramChatId);
        Assert.True(settings.TelegramAlertOnDetected);
        Assert.True(settings.TelegramAlertOnSolved);
        Assert.True(settings.TelegramAlertOnFailed);
        Assert.True(settings.TelegramSendPhoto);
    }
}
