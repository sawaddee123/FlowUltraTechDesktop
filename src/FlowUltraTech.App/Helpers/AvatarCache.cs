using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace FlowUltraTech.App.Helpers;

/// <summary>
/// TikTok avatars are signed CDN URLs in WebP, which WPF cannot decode.
/// Downloads once, converts to PNG and keeps it on disk so it survives URL expiry.
/// </summary>
public static class AvatarCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly HashSet<string> InFlight = new();
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowUltraTech", "avatars");

    // Key ignores the signed query string, so a refreshed URL for the same image reuses the cache.
    public static string CachePathFor(string url)
    {
        string key = url.Split('?')[0];
        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(Dir, hash + ".png");
    }

    public static async Task<bool> EnsureAsync(string url)
    {
        string path = CachePathFor(url);
        if (File.Exists(path)) return true;
        lock (InFlight) { if (!InFlight.Add(path)) return false; }
        try
        {
            byte[] data = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            using var bmp = SKBitmap.Decode(data);
            if (bmp == null) return false;
            using var img = SKImage.FromBitmap(bmp);
            using var png = img.Encode(SKEncodedImageFormat.Png, 100);
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(path, png.ToArray());
            return true;
        }
        catch { return false; }
        finally { lock (InFlight) InFlight.Remove(path); }
    }
}
