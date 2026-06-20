using System.Security.Cryptography;
using DM.Core.Video;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DM.Core.Tests.TestHelpers;

/// <summary>
/// Server HLS tổng hợp cục bộ (Kestrel) để test offline: playlist thường + playlist AES-128
/// (key công khai, IV theo media-sequence). KHÔNG cần FFmpeg — chỉ kiểm tải &amp; giải mã.
/// </summary>
public sealed class HlsTestServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    public string BaseUrl { get; }

    /// <summary>Nội dung gốc (plaintext) của các segment, để so sánh sau giải mã.</summary>
    public byte[][] PlainSegments { get; }

    private HlsTestServer(WebApplication app, string baseUrl, byte[][] plain)
    {
        _app = app;
        BaseUrl = baseUrl;
        PlainSegments = plain;
    }

    public string PlainPlaylistUrl => $"{BaseUrl}/plain.m3u8";
    public string EncPlaylistUrl => $"{BaseUrl}/enc.m3u8";
    public string MasterPlaylistUrl => $"{BaseUrl}/master.m3u8";

    public static async Task<HlsTestServer> StartAsync()
    {
        var rng = new Random(2024);
        var sizes = new[] { 5000, 7000, 3000 };
        var plain = sizes.Select(s => { var b = new byte[s]; rng.NextBytes(b); return b; }).ToArray();

        var key = new byte[16];
        rng.NextBytes(key);
        var encrypted = plain.Select((seg, i) => EncryptAes128Cbc(seg, key, HlsDownloader.IvFromSequence(i))).ToArray();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        const string octet = "application/octet-stream";

        app.MapGet("/plain.m3u8", (HttpContext ctx) =>
            Results.Text(BuildPlaylist(plain.Length, "s", null), "application/vnd.apple.mpegurl"));

        app.MapGet("/enc.m3u8", (HttpContext ctx) =>
            Results.Text(BuildPlaylist(plain.Length, "e", $"{Origin(ctx)}/key"), "application/vnd.apple.mpegurl"));

        app.MapGet("/master.m3u8", () => Results.Text(
            "#EXTM3U\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=500000,RESOLUTION=640x360\n" +
            "plain.m3u8\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=1500000,RESOLUTION=1280x720\n" +
            "enc.m3u8\n", "application/vnd.apple.mpegurl"));

        app.MapGet("/key", () => Results.Bytes(key, octet));
        app.MapGet("/s/{i:int}.ts", (int i) => Results.Bytes(plain[i], octet));
        app.MapGet("/e/{i:int}.ts", (int i) => Results.Bytes(encrypted[i], octet));

        await app.StartAsync();
        return new HlsTestServer(app, app.Urls.First(), plain);
    }

    private static string Origin(HttpContext ctx) => $"{ctx.Request.Scheme}://{ctx.Request.Host}";

    private static string BuildPlaylist(int count, string dir, string? keyUri)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:3");
        sb.AppendLine("#EXT-X-MEDIA-SEQUENCE:0");
        if (keyUri is not null)
        {
            sb.AppendLine($"#EXT-X-KEY:METHOD=AES-128,URI=\"{keyUri}\"");
        }
        for (int i = 0; i < count; i++)
        {
            sb.AppendLine("#EXTINF:1.0,");
            sb.AppendLine($"{dir}/{i}.ts");
        }
        sb.AppendLine("#EXT-X-ENDLIST");
        return sb.ToString();
    }

    private static byte[] EncryptAes128Cbc(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        using var enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(data, 0, data.Length);
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
