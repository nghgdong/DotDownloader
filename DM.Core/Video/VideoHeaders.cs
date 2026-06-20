namespace DM.Core.Video;

/// <summary>
/// Headers cần gửi khi tải playlist/segment/key. Nhiều CDN trả 403 nếu thiếu
/// Referer/Origin/Cookie/User-Agent.
/// </summary>
public sealed class VideoHeaders
{
    public string? Referer { get; init; }
    public string? Origin { get; init; }
    public string? Cookie { get; init; }
    public string? UserAgent { get; init; }

    /// <summary>Header tùy ý khác.</summary>
    public Dictionary<string, string>? Extra { get; init; }

    public static VideoHeaders Empty { get; } = new();

    public void ApplyTo(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(Referer))
        {
            request.Headers.TryAddWithoutValidation("Referer", Referer);
        }
        if (!string.IsNullOrEmpty(Origin))
        {
            request.Headers.TryAddWithoutValidation("Origin", Origin);
        }
        if (!string.IsNullOrEmpty(Cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", Cookie);
        }
        if (!string.IsNullOrEmpty(UserAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        }
        if (Extra is not null)
        {
            foreach (var (k, v) in Extra)
            {
                request.Headers.TryAddWithoutValidation(k, v);
            }
        }
    }

    /// <summary>Tạo từ dictionary (key không phân biệt hoa thường) + cookie/referrer rời.</summary>
    public static VideoHeaders From(
        IReadOnlyDictionary<string, string>? headers, string? referrer = null, string? cookie = null)
    {
        string? Get(string name)
        {
            if (headers is null)
            {
                return null;
            }
            foreach (var (k, v) in headers)
            {
                if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
                {
                    return v;
                }
            }
            return null;
        }

        return new VideoHeaders
        {
            Referer = referrer ?? Get("Referer"),
            Origin = Get("Origin"),
            Cookie = cookie ?? Get("Cookie"),
            UserAgent = Get("User-Agent")
        };
    }
}
