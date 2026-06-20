using System.Diagnostics;
using DM.Core.Models;
using DM.Core.Net;
using DM.Core.Util;

namespace DM.Core.Download;

/// <summary>
/// Tải file tuần tự (đơn luồng), stream thẳng ra đĩa theo buffer — không load cả file vào RAM.
/// Dùng cho server không hỗ trợ range, hoặc làm fallback của engine đa luồng.
/// </summary>
public sealed class SingleStreamDownloader
{
    private const int BufferSize = 81920;

    /// <summary>Khoảng tối thiểu giữa hai lần báo tiến độ để không spam UI.</summary>
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(250);

    private readonly HttpClient _http;
    private readonly RateLimiter? _rateLimiter;

    public SingleStreamDownloader(HttpClient? http = null, RateLimiter? rateLimiter = null)
    {
        _http = http ?? SharedHttpClient.Instance;
        _rateLimiter = rateLimiter;
    }

    /// <summary>
    /// Tải <paramref name="url"/> và ghi vào <paramref name="filePath"/>.
    /// </summary>
    /// <param name="totalBytes">Kích thước biết trước (byte); -1 nếu chưa biết.</param>
    public async Task DownloadAsync(
        string url,
        string filePath,
        long totalBytes = -1,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Nếu chưa biết size từ probe, lấy từ response.
        if (totalBytes < 0)
        {
            totalBytes = response.Content.Headers.ContentLength ?? -1;
        }

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dest = new FileStream(
            filePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        var buffer = new byte[BufferSize];
        long downloaded = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastReportAt = TimeSpan.Zero;
        long lastReportBytes = 0;

        Report(progress, downloaded, totalBytes, 0, DownloadState.Downloading);

        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
        {
            if (_rateLimiter is not null)
            {
                await _rateLimiter.ThrottleAsync(read, ct).ConfigureAwait(false);
            }
            await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;

            var elapsed = stopwatch.Elapsed;
            if (elapsed - lastReportAt >= ReportInterval)
            {
                var intervalSeconds = (elapsed - lastReportAt).TotalSeconds;
                var bps = intervalSeconds > 0 ? (downloaded - lastReportBytes) / intervalSeconds : 0;
                Report(progress, downloaded, totalBytes, bps, DownloadState.Downloading);
                lastReportAt = elapsed;
                lastReportBytes = downloaded;
            }
        }

        await dest.FlushAsync(ct).ConfigureAwait(false);

        var totalSeconds = stopwatch.Elapsed.TotalSeconds;
        var avgBps = totalSeconds > 0 ? downloaded / totalSeconds : 0;
        Report(progress, downloaded, totalBytes < 0 ? downloaded : totalBytes, avgBps, DownloadState.Completed);
    }

    private static void Report(
        IProgress<ProgressReport>? progress, long downloaded, long total, double bps, DownloadState state)
    {
        if (progress is null)
        {
            return;
        }

        TimeSpan? eta = null;
        if (total > 0 && bps > 0 && downloaded < total)
        {
            eta = TimeSpan.FromSeconds((total - downloaded) / bps);
        }

        progress.Report(new ProgressReport
        {
            BytesDownloaded = downloaded,
            TotalBytes = total,
            BytesPerSecond = bps,
            Eta = eta,
            State = state
        });
    }
}
