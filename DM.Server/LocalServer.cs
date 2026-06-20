using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using DM.Server.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DM.Server;

/// <summary>
/// HTTP API loopback (ASP.NET Minimal API) làm cầu nối extension ↔ app.
/// CHỈ bind <c>127.0.0.1</c>; mọi request phải đến từ loopback và mang đúng token.
/// </summary>
public sealed class LocalServer : IAsyncDisposable
{
    private readonly string _token;
    private readonly Func<DownloadRequest, Guid> _onDownload;
    private readonly Func<int> _activeDownloads;
    private WebApplication? _app;

    /// <param name="onDownload">Callback App đăng ký để enqueue task, trả taskId.</param>
    /// <param name="activeDownloads">Số download đang chạy (cho /api/ping).</param>
    public LocalServer(
        string token,
        Func<DownloadRequest, Guid> onDownload,
        Func<int>? activeDownloads = null)
    {
        _token = token;
        _onDownload = onDownload;
        _activeDownloads = activeDownloads ?? (() => 0);
    }

    public int Port { get; private set; }
    public bool IsRunning => _app is not null;

    public async Task StartAsync(
        int startPort = ServerInfo.DefaultPort,
        int maxAttempts = ServerInfo.MaxPortAttempts,
        CancellationToken ct = default)
    {
        if (_app is not null)
        {
            throw new InvalidOperationException("Server đã chạy.");
        }

        Port = PickFreePort(startPort, maxAttempts);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, Port)); // chỉ 127.0.0.1

        var app = builder.Build();
        ConfigurePipeline(app);

        await app.StartAsync(ct).ConfigureAwait(false);
        _app = app;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_app is null)
        {
            return;
        }
        await _app.StopAsync(ct).ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
        _app = null;
    }

    private void ConfigurePipeline(WebApplication app)
    {
        // Middleware bảo mật — ĐẶT TRƯỚC mọi endpoint.
        app.Use(async (ctx, next) =>
        {
            // (a) chỉ chấp nhận loopback.
            if (!IsLoopback(ctx.Connection.RemoteIpAddress))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("Forbidden: loopback only");
                return;
            }

            // (b) kiểm tra token (so sánh hằng thời gian).
            var provided = ctx.Request.Headers[ServerInfo.TokenHeader].ToString();
            if (!TokensMatch(provided, _token))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("Unauthorized: bad or missing token");
                return;
            }

            await next();
        });

        app.MapGet("/api/ping", () => Results.Ok(new
        {
            ok = true,
            version = ServerInfo.Version,
            activeDownloads = _activeDownloads(),
            port = Port
        }));

        app.MapPost("/api/download", (DownloadRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Url)
                || !Uri.TryCreate(req.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return Results.BadRequest(new { error = "URL không hợp lệ" });
            }

            var taskId = _onDownload(req);
            return Results.Ok(new { taskId });
        });
    }

    /// <summary>Địa chỉ có phải loopback không (127.0.0.0/8 hoặc ::1). Null → từ chối.</summary>
    internal static bool IsLoopback(IPAddress? address)
        => address is not null && IPAddress.IsLoopback(address);

    internal static bool TokensMatch(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Thử từ <paramref name="startPort"/>, +1 nếu bận, tối đa <paramref name="maxAttempts"/>.</summary>
    private static int PickFreePort(int startPort, int maxAttempts)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            int port = startPort + i;
            if (IsPortFree(port))
            {
                return port;
            }
        }
        throw new IOException(
            $"Không tìm được cổng trống trong dải {startPort}..{startPort + maxAttempts - 1}.");
    }

    private static bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
