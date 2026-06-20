using Microsoft.Win32.SafeHandles;
using DM.Core.Models;
using DM.Core.Net;
using DM.Core.Util;

namespace DM.Core.Download;

/// <summary>
/// Tải đúng một byte-range của file (header <c>Range: bytes=start-end</c>) và ghi vào
/// offset tương ứng bằng <see cref="RandomAccess"/> — positioned I/O nên an toàn khi
/// nhiều segment cùng ghi một handle (các vùng byte không chồng lấn, không dùng con trỏ chung).
/// </summary>
public sealed class SegmentDownloader
{
    private const int BufferSize = 81920;

    private readonly HttpClient _http;
    private readonly RateLimiter? _rateLimiter;

    public SegmentDownloader(HttpClient? http = null, RateLimiter? rateLimiter = null)
    {
        _http = http ?? SharedHttpClient.Instance;
        _rateLimiter = rateLimiter;
    }

    /// <summary>
    /// Tải phần còn thiếu của <paramref name="segment"/> (từ <see cref="Segment.CurrentPos"/> đến End)
    /// và ghi vào <paramref name="handle"/> tại đúng offset.
    /// </summary>
    /// <param name="onBytes">Callback delta byte vừa ghi (để engine tổng hợp tốc độ). Phải thread-safe.</param>
    public async Task DownloadAsync(
        SafeFileHandle handle,
        Segment segment,
        string url,
        Action<long>? onBytes = null,
        CancellationToken ct = default)
    {
        if (segment.IsComplete)
        {
            return;
        }

        long from = segment.CurrentPos;
        long to = segment.End;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var buffer = new byte[BufferSize];
        long writeOffset = from;

        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
        {
            // Không ghi vượt quá biên segment (phòng server trả dư).
            long remaining = to - writeOffset + 1;
            if (remaining <= 0)
            {
                break;
            }
            int toWrite = (int)Math.Min(read, remaining);

            if (_rateLimiter is not null)
            {
                await _rateLimiter.ThrottleAsync(toWrite, ct).ConfigureAwait(false);
            }

            await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, toWrite), writeOffset, ct).ConfigureAwait(false);
            writeOffset += toWrite;
            segment.Downloaded += toWrite;
            onBytes?.Invoke(toWrite);

            if (toWrite < read)
            {
                break;
            }
        }
    }
}
