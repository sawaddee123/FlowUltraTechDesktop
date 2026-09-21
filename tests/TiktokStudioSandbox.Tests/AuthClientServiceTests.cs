using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Services;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class AuthClientServiceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handler { get; set; }

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            Handler = req => Task.FromResult(handler(req));
        }

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            Handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Handler(request);
        }
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenServerReturnsSuccess_ReturnsNewTokens()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri?.ToString().Contains("/api/v1/auth/refreshToken") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"accessToken\":\"new-jwt-token\",\"refreshToken\":\"new-refresh-token\"}")
                };
            }
            if (req.RequestUri?.ToString().Contains("/api/v1/auth/me") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"username\":\"prayuth\",\"role\":\"user\",\"maxTiktokAccounts\":5,\"license\":{\"plan\":\"pro\"}}")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var authService = new AuthClientService(httpClient, "http://127.0.0.1:3006");

        var result = await authService.RefreshTokenAsync("old-refresh-token");

        Assert.True(result.Success);
        Assert.Equal("new-jwt-token", result.AccessToken);
        Assert.Equal("new-refresh-token", result.RefreshToken);
        Assert.Equal("prayuth", result.Username);
        Assert.Equal(5, result.MaxTiktokAccounts);
        Assert.Equal("pro", result.Plan);
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenEmptyToken_ReturnsFailureImmediately()
    {
        using var authService = new AuthClientService();
        var result = await authService.RefreshTokenAsync(string.Empty);

        Assert.False(result.Success);
        Assert.Contains("ว่างเปล่า", result.Message);
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenServerReturnsExpired_ReturnsFailure()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":\"Token expired\"}")
            };
        });

        using var httpClient = new HttpClient(mockHandler);
        using var authService = new AuthClientService(httpClient, "http://127.0.0.1:3006");

        var result = await authService.RefreshTokenAsync("expired-token");

        Assert.False(result.Success);
        Assert.Contains("Token expired", result.Message);
    }

    [Fact]
    public async Task LoginAsync_WhenServerReturnsAppVersionAndSettings_ParsesCorrectly()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri?.ToString().Contains("/api/v1/auth/login") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{" +
                        "\"accessToken\":\"jwt-xyz\"," +
                        "\"refreshToken\":\"rt-xyz\"," +
                        "\"username\":\"testuser\"," +
                        "\"role\":\"user\"," +
                        "\"maxTiktokAccounts\":3," +
                        "\"license\":{\"plan\":\"vip\",\"expiredAt\":\"2026-12-31T23:59:59Z\",\"maxTiktokAccounts\":3}," +
                        "\"appVersion\":{\"latestVersion\":\"2.0.0\",\"minRequiredVersion\":\"1.5.0\",\"downloadUrl\":\"https://example.com/dl\"}," +
                        "\"settings\":{\"telegramBotToken\":\"bot123\",\"telegramChatId\":\"chat456\",\"autoSolveDelaySeconds\":3,\"captchaSoundVolume\":75,\"autoSaveCaptchaImage\":true,\"rules\":[{\"name\":\"Hello\",\"pattern\":\"hi\"}]}" +
                        "}"
                    )
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var authService = new AuthClientService(httpClient, "http://127.0.0.1:3006");

        var result = await authService.LoginAsync("testuser", "pass123", "hwid-1");

        Assert.True(result.Success);
        Assert.Equal("jwt-xyz", result.AccessToken);
        Assert.Equal("rt-xyz", result.RefreshToken);
        Assert.NotNull(result.AppVersion);
        Assert.Equal("2.0.0", result.AppVersion.LatestVersion);
        Assert.Equal("1.5.0", result.AppVersion.MinRequiredVersion);
        Assert.Equal("https://example.com/dl", result.AppVersion.DownloadUrl);
        Assert.NotNull(result.Settings);
        Assert.Equal("bot123", result.Settings.TelegramBotToken);
        Assert.Equal("chat456", result.Settings.TelegramChatId);
        Assert.Equal(3, result.Settings.AutoSolveDelaySeconds);
        Assert.Equal(75, result.Settings.CaptchaSoundVolume);
        Assert.True(result.Settings.AutoSaveCaptchaImage);
        Assert.NotNull(result.Settings.Rules);
        Assert.Single(result.Settings.Rules);
        Assert.Equal("Hello", result.Settings.Rules[0].Name);
    }

    [Fact]
    public async Task GetCloudSettingsAsync_WhenSuccessful_ReturnsSettingsDto()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal("my-token", req.Headers.Authorization?.Parameter);
            if (req.RequestUri?.ToString().Contains("/api/v1/auth/settings") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"settings\":{\"telegramChatId\":\"12345\",\"autoSolveDelaySeconds\":8}}")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var authService = new AuthClientService(httpClient, "http://127.0.0.1:3006");

        var result = await authService.GetCloudSettingsAsync("my-token");

        Assert.True(result.Success);
        Assert.NotNull(result.Settings);
        Assert.Equal("12345", result.Settings.TelegramChatId);
        Assert.Equal(8, result.Settings.AutoSolveDelaySeconds);
    }

    [Fact]
    public async Task SaveCloudSettingsAsync_WhenSuccessful_ReturnsTrue()
    {
        string requestBody = string.Empty;
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            requestBody = req.Content != null ? await req.Content.ReadAsStringAsync() : string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}")
            };
        });

        using var httpClient = new HttpClient(mockHandler);
        using var authService = new AuthClientService(httpClient, "http://127.0.0.1:3006");

        var settings = new TiktokStudioSandbox.Core.Interfaces.CloudSettingsDto
        {
            TelegramBotToken = "test-bot",
            TelegramChatId = "test-chat",
            AutoSolveDelaySeconds = 7
        };

        var success = await authService.SaveCloudSettingsAsync("my-token", settings);

        Assert.True(success);
        Assert.Contains("test-bot", requestBody);
        Assert.Contains("test-chat", requestBody);
    }

    [Fact]
    public async Task ReportSecurityEventAsync_WhenSuccessful_SendsPayloadAndReturnsTrue()
    {
        string requestBody = string.Empty;
        var mockHandler = new MockHttpMessageHandler(async req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            requestBody = req.Content != null ? await req.Content.ReadAsStringAsync() : string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}")
            };
        });

        using var httpClient = new HttpClient(mockHandler);
        using var authService = new AuthClientService(httpClient, "http://127.0.0.1:3006");

        var success = await authService.ReportSecurityEventAsync("my-token", "LICENSE_EXPIRED_KICKOUT", "Account expired", "test-hwid");

        Assert.True(success);
        Assert.Contains("LICENSE_EXPIRED_KICKOUT", requestBody);
        Assert.Contains("test-hwid", requestBody);
    }
}
