namespace DM.Core.Net;

/// <summary>
/// Kết quả thăm dò (HEAD) một URL trước khi tải.
/// </summary>
public readonly record struct ProbeResult
{
    /// <summary>Kích thước file (byte). -1 nếu server không khai báo Content-Length (unknown).</summary>
    public required long TotalBytes { get; init; }

    /// <summary>Server có hỗ trợ byte-range (Accept-Ranges: bytes).</summary>
    public required bool SupportsRange { get; init; }

    /// <summary>Tên file gợi ý (từ Content-Disposition hoặc đuôi URL). Null nếu không suy ra được.</summary>
    public string? SuggestedFileName { get; init; }

    public string? ContentType { get; init; }

    public bool HasKnownSize => TotalBytes >= 0;
}
