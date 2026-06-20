namespace DM.Server.Models;

/// <summary>
/// Payload extension gửi lên qua <c>POST /api/download</c>.
/// </summary>
public sealed class DownloadRequest
{
    public required string Url { get; init; }

    public string? FileName { get; init; }

    public string? Referrer { get; init; }

    /// <summary>Chuỗi cookie ("k=v; k2=v2") để gửi kèm khi tải (nhiều CDN yêu cầu).</summary>
    public string? Cookies { get; init; }

    /// <summary>Header bổ sung (Origin, User-Agent, ...).</summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>Loại tải: "file" (mặc định), "HLS", "DASH".</summary>
    public string? Type { get; init; }
}
