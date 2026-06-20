namespace DM.Core.Models;

/// <summary>
/// Mô tả một tác vụ tải file: nguồn, đích, tiến độ, trạng thái.
/// Thuần dữ liệu — không chứa logic IO.
/// </summary>
public sealed class DownloadTask
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>URL nguồn cần tải.</summary>
    public required string Url { get; init; }

    /// <summary>Đường dẫn file đích trên đĩa.</summary>
    public required string FilePath { get; set; }

    /// <summary>Tổng kích thước file (byte). -1 nếu server không khai báo (unknown).</summary>
    public long TotalBytes { get; set; } = -1;

    /// <summary>Server có hỗ trợ tải theo byte-range (Accept-Ranges: bytes) hay không.</summary>
    public bool SupportsRange { get; set; }

    public DownloadState State { get; set; } = DownloadState.Queued;

    /// <summary>Danh sách segment. Đơn luồng = 1 segment phủ toàn file.</summary>
    public List<Segment> Segments { get; init; } = new();

    /// <summary>Phân loại (Video, Document, ...). Gán ở phase phân loại.</summary>
    public string? Category { get; set; }

    /// <summary>Thời điểm hẹn tải (scheduler). Null = tải ngay.</summary>
    public DateTimeOffset? ScheduledAt { get; set; }

    /// <summary>Loại stream: null/"file" → byte-range; "HLS"/"DASH" → video engine.</summary>
    public string? StreamType { get; set; }

    /// <summary>Headers cần gửi khi tải (Referer/Origin/Cookie/User-Agent) — cho video stream.</summary>
    public Dictionary<string, string>? RequestHeaders { get; set; }

    /// <summary>Tổng số byte đã tải, cộng dồn từ các segment.</summary>
    public long DownloadedBytes => Segments.Sum(s => s.Downloaded);
}
