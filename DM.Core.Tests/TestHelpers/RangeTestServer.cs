using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DM.Core.Tests.TestHelpers;

/// <summary>
/// Server cục bộ (Kestrel) phục vụ một mảng byte cố định:
/// <c>/range</c> hỗ trợ byte-range (Accept-Ranges: bytes, 206 Partial Content),
/// <c>/norange</c> KHÔNG hỗ trợ range (để test fallback đơn luồng).
/// Bind cổng 0 → OS tự cấp cổng trống; không cần quyền admin.
/// </summary>
public sealed class RangeTestServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string BaseUrl { get; }

    private RangeTestServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public string RangeUrl => $"{BaseUrl}/range";
    public string NoRangeUrl => $"{BaseUrl}/norange";

    /// <summary>Hỗ trợ range nhưng stream chậm (throttle) để test hủy/resume giữa chừng.</summary>
    public string SlowUrl => $"{BaseUrl}/slow";

    public static async Task<RangeTestServer> StartAsync(byte[] data)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        const string contentType = "application/octet-stream";

        app.MapMethods("/range", new[] { "HEAD" }, (HttpContext ctx) =>
        {
            ctx.Response.Headers.AcceptRanges = "bytes";
            ctx.Response.ContentLength = data.Length;
            ctx.Response.ContentType = contentType;
            return Results.Empty;
        });
        app.MapGet("/range", () => Results.Bytes(data, contentType, enableRangeProcessing: true));

        app.MapMethods("/norange", new[] { "HEAD" }, (HttpContext ctx) =>
        {
            ctx.Response.ContentLength = data.Length;
            ctx.Response.ContentType = contentType;
            return Results.Empty;
        });
        app.MapGet("/norange", () => Results.Bytes(data, contentType, enableRangeProcessing: false));

        app.MapMethods("/slow", new[] { "HEAD" }, (HttpContext ctx) =>
        {
            ctx.Response.Headers.AcceptRanges = "bytes";
            ctx.Response.ContentLength = data.Length;
            ctx.Response.ContentType = contentType;
            return Results.Empty;
        });
        app.MapGet("/slow", async (HttpContext ctx) =>
        {
            var (start, end, isRange) = ParseRange(ctx.Request.Headers["Range"], data.Length);
            ctx.Response.Headers.AcceptRanges = "bytes";
            ctx.Response.ContentType = contentType;
            if (isRange)
            {
                ctx.Response.StatusCode = StatusCodes.Status206PartialContent;
                ctx.Response.Headers.ContentRange = $"bytes {start}-{end}/{data.Length}";
            }
            ctx.Response.ContentLength = end - start + 1;

            const int chunk = 64 * 1024;
            long pos = start;
            try
            {
                while (pos <= end)
                {
                    int n = (int)Math.Min(chunk, end - pos + 1);
                    await ctx.Response.Body.WriteAsync(data.AsMemory((int)pos, n), ctx.RequestAborted);
                    pos += n;
                    await Task.Delay(30, ctx.RequestAborted); // throttle để có thời gian hủy giữa chừng
                }
            }
            catch (OperationCanceledException)
            {
                // client hủy → dừng, bình thường với test resume
            }
        });

        await app.StartAsync();
        var address = app.Urls.First();
        return new RangeTestServer(app, address);
    }

    /// <summary>Parse header "bytes=start-end". Không có header → toàn bộ file (isRange=false).</summary>
    private static (long Start, long End, bool IsRange) ParseRange(string? header, long length)
    {
        if (string.IsNullOrEmpty(header) || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return (0, length - 1, false);
        }

        var spec = header["bytes=".Length..];
        var parts = spec.Split('-', 2);
        long start = string.IsNullOrEmpty(parts[0]) ? 0 : long.Parse(parts[0]);
        long end = parts.Length > 1 && !string.IsNullOrEmpty(parts[1]) ? long.Parse(parts[1]) : length - 1;
        return (start, Math.Min(end, length - 1), true);
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
