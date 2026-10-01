using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;
using PuppeteerSharp;

// Real Chrome + production CDP service; only the solver HTTP response is a fixture.
var root = Path.GetFullPath(args[0]);
var port = int.Parse(args[1]);
var artifacts = Path.Combine(root, "TestResults", "symbol-e2e");
Directory.CreateDirectory(artifacts);
using var cdp = new CdpAutomationService();
var browser = await Puppeteer.ConnectAsync(new ConnectOptions { BrowserURL = $"http://127.0.0.1:{port}" });
try
{
    await using var server = new SymbolFixtureServer(root);
    var pages = await browser.PagesAsync();
    var page = pages.Single(p => p.Url.Contains("mock_captcha_symbol.html"));
    await page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 900 });
    await page.WaitForFunctionAsync("() => !!document.querySelector('#captcha-verify-image')?.complete && !!document.querySelector('#symbol-submit-btn')");
    await CheckDetection("launcher opens symbol", true);
    await page.ScreenshotAsync(Path.Combine(artifacts, "symbol-active.png"));
    await CheckSolve("wrong coordinates rejected", false, wrong: true);
    await page.ScreenshotAsync(Path.Combine(artifacts, "symbol-wrong-answer.png"));
    await page.EvaluateExpressionAsync("document.querySelector('#symbol-reset-btn').click()");
    await CheckSolve("two matching shapes + Confirm close modal", true);
    await CheckDetection("solved widget is inactive", false);

    // The original capture must remain inactive, even though its modal is in the DOM.
    await page.SetContentAsync(File.ReadAllText(Path.Combine(root, "docs", "Captcha_Symbol.txt")));
    await CheckDetection("original dump is hidden and has no loaded widget", false);

    await page.GoToAsync(server.Endpoint + "/symbol");
    await page.WaitForSelectorAsync("#captcha-verify-image");
    await page.EvaluateExpressionAsync("document.querySelector('#captcha-modal-wrapper').style.visibility = 'hidden'");
    await CheckDetection("hidden ancestor suppresses symbol", false);

    await page.GoToAsync(server.Endpoint + "/host");
    await page.WaitForFunctionAsync("() => document.querySelector('iframe')?.dataset.loaded === 'true'");
    await CheckDetection("cross origin iframe detected", true);
    await CheckSolve("cross origin wrong answer rejected", false, wrong: true);
    var frame = page.Frames.Single(f => f.Url.EndsWith("/symbol"));
    await frame.EvaluateExpressionAsync("document.querySelector('#symbol-reset-btn').click()");
    await CheckSolve("cross origin symbol solved", true);
    await CheckDetection("cross origin solved widget inactive", false);

    await page.GoToAsync(server.Endpoint + "/host");
    await page.WaitForFunctionAsync("() => document.querySelector('iframe')?.dataset.loaded === 'true'");
    await page.EvaluateExpressionAsync("document.querySelector('iframe').style.display = 'none'");
    await CheckDetection("hidden cross origin iframe inactive", false);

    // Protect the working jigsaw detector using its existing browser fixture.
    await page.GoToAsync(new Uri(Path.Combine(root, "docs", "mocks", "mock_captcha_jigsaw.html")).AbsoluteUri);
    var jigsaw = await cdp.DetectCaptchaElementsAsync(port);
    Require(jigsaw.IsActive && jigsaw.Type == CaptchaType.Jigsaw && jigsaw.DirectImageBytes?.Length > 0, "jigsaw detection regression");
    Console.WriteLine("PASS: jigsaw detection regression");
    Console.WriteLine($"PASS: symbol E2E completed; solver requests={server.RequestCount}, image payload verified, production clicks verified");

    async Task CheckDetection(string label, bool active)
    {
        var d = await cdp.DetectCaptchaElementsAsync(port);
        Require(d.IsActive == active, $"{label}: expected active={active}, actual={d.IsActive}, type={d.Type}");
        if (active)
        {
            Require(d.Found && d.Type == CaptchaType.Symbol && d.HasWidgetLoaded, $"{label}: expected loaded Symbol, got {d.Type}");
            Require(d.DirectImageBytes?.Length > 0 && d.DirectImageDimensions == "640x640", $"{label}: original image missing ({d.DirectImageDimensions})");
            Require(!string.IsNullOrWhiteSpace(d.CountdownText), $"{label}: countdown missing");
        }
        Console.WriteLine($"PASS: {label} (type={d.Type}, active={d.IsActive})");
    }

    async Task CheckSolve(string label, bool success, bool wrong = false)
    {
        server.WrongAnswer = wrong;
        var before = server.RequestCount;
        var result = await cdp.SolveCaptchaAsync(port, solverMode: CaptchaSolverType.LocalApiServer,
            solverApiEndpoint: server.Endpoint, progressLogger: Console.WriteLine);
        Require(result.Success == success, $"{label}: {result.Message}");
        Require(server.RequestCount == before + 1, $"{label}: expected exactly one symbol API request");
        Require(server.PayloadValid, $"{label}: API image or display dimensions invalid");
        var context = page.Frames.FirstOrDefault(f => f.Url.EndsWith("/symbol")) ?? page.MainFrame;
        Require(await context.EvaluateExpressionAsync<int>("document.querySelectorAll('.symbol-click-pin').length") == 2, $"{label}: two real mouse clicks missing");
        var verdict = await context.EvaluateExpressionAsync<string>("document.querySelector('#symbol-toast').textContent");
        Require(verdict.Contains(success ? "Successful" : "Failed"), $"{label}: mock did not validate coordinates: {verdict}");
        Console.WriteLine($"PASS: {label}");
    }
}
finally
{
    await browser.CloseAsync(); // The runner always launches a dedicated browser/port.
    browser.Dispose();
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
}

sealed class SymbolFixtureServer : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly Task loop;
    private readonly string html;
    private volatile bool stopping;
    public string Endpoint { get; }
    public bool WrongAnswer { get; set; }
    public int RequestCount { get; private set; }
    public bool PayloadValid { get; private set; }

    public SymbolFixtureServer(string root)
    {
        html = File.ReadAllText(Path.Combine(root, "docs", "mocks", "mock_captcha_symbol.html"));
        // Remove captured external application bundles: the fixture provides its own JS.
        html = System.Text.RegularExpressions.Regex.Replace(html, "<script[^>]*src=\"[^\"]*\"[^>]*></script>", "");
        html = System.Text.RegularExpressions.Regex.Replace(html, "<link[^>]*>|<img[^>]*src=\"https?://[^\"]*\"[^>]*>", "");
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Endpoint = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add(Endpoint + "/");
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        loop = ServeAsync();
    }

    private async Task ServeAsync()
    {
        try
        {
            while (listener.IsListening)
            {
                var ctx = await listener.GetContextAsync();
                var path = ctx.Request.Url!.AbsolutePath;
                string body;
                if (path == "/api/v1/captcha/symbol")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream);
                    using var payload = JsonDocument.Parse(await reader.ReadToEndAsync());
                    var p = payload.RootElement;
                    var encoded = p.GetProperty("image").GetString()!;
                    if (encoded.StartsWith("data:")) encoded = encoded[(encoded.IndexOf(',') + 1)..];
                    var bytes = Convert.FromBase64String(encoded);
                    PayloadValid = bytes.Length > 1000 && p.GetProperty("displayWidth").GetDouble() > 0 && p.GetProperty("displayHeight").GetDouble() > 0;
                    RequestCount++;
                    var points = WrongAnswer ? new[] { new[] { 80, 80 }, new[] { 150, 150 } } : new[] { new[] { 450, 230 }, new[] { 480, 390 } };
                    body = JsonSerializer.Serialize(new { success = true, symbol_positions = points, raw_symbol_positions = points, image_width = 640, image_height = 640 });
                    ctx.Response.ContentType = "application/json";
                }
                else if (path == "/symbol")
                {
                    body = html;
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                }
                else if (path == "/host")
                {
                    var otherOrigin = Endpoint.Replace("127.0.0.1", "localhost");
                    body = $"<!doctype html><html><body style='margin:0'><iframe src='{otherOrigin}/symbol' style='width:100%;height:850px;border:0' onload=\"this.dataset.loaded='true'\"></iframe></body></html>";
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    body = "Not found";
                }
                var data = Encoding.UTF8.GetBytes(body);
                ctx.Response.ContentLength64 = data.Length;
                await ctx.Response.OutputStream.WriteAsync(data);
                ctx.Response.Close();
            }
        }
        catch (HttpListenerException) when (stopping) { }
        catch (ObjectDisposedException) when (stopping) { }
    }

    public async ValueTask DisposeAsync()
    {
        stopping = true;
        listener.Stop();
        listener.Close();
        await loop;
    }
}
