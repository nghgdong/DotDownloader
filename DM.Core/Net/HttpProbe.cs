using System.Net.Http.Headers;

namespace DM.Core.Net;

/// <summary>
/// Thăm dò một URL bằng HEAD request để lấy kích thước, khả năng resume và tên file gợi ý.
/// </summary>
public sealed class HttpProbe
{
    private readonly HttpClient _http;

    public HttpProbe(HttpClient? http = null) => _http = http ?? SharedHttpClient.Instance;

    public async Task<ProbeResult> ProbeAsync(string url, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Content-Length: null nếu server không khai báo → coi là unknown (-1).
        long totalBytes = response.Content.Headers.ContentLength ?? -1;

        // Accept-Ranges: bytes → hỗ trợ resume/đa luồng. "none" hoặc thiếu → không.
        bool supportsRange = response.Headers.AcceptRanges
            .Any(v => v.Equals("bytes", StringComparison.OrdinalIgnoreCase));

        string? fileName = ResolveFileName(response.Content.Headers.ContentDisposition, url);
        string? contentType = response.Content.Headers.ContentType?.MediaType;

        return new ProbeResult
        {
            TotalBytes = totalBytes,
            SupportsRange = supportsRange,
            SuggestedFileName = fileName,
            ContentType = contentType
        };
    }

    /// <summary>Ưu tiên tên từ Content-Disposition, fallback về đuôi đường dẫn URL.</summary>
    internal static string? ResolveFileName(ContentDispositionHeaderValue? disposition, string url)
    {
        var raw = disposition?.FileNameStar ?? disposition?.FileName;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            return raw.Trim('"');
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var last = uri.AbsolutePath.TrimEnd('/');
            var name = last.Length == 0 ? null : Path.GetFileName(last);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return Uri.UnescapeDataString(name);
            }
        }

        return null;
    }
}
