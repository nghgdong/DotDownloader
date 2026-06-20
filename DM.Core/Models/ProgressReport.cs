namespace DM.Core.Models;

/// <summary>
/// Ảnh chụp tiến độ tải tại một thời điểm, phát qua <see cref="IProgress{T}"/>.
/// </summary>
public readonly record struct ProgressReport
{
    public required long BytesDownloaded { get; init; }

    /// <summary>Tổng kích thước; -1 nếu chưa biết (server không trả Content-Length).</summary>
    public required long TotalBytes { get; init; }

    public required double BytesPerSecond { get; init; }

    /// <summary>Thời gian còn lại ước tính; null nếu không tính được (size unknown).</summary>
    public TimeSpan? Eta { get; init; }

    public required DownloadState State { get; init; }

    /// <summary>Cảnh báo cho người dùng (vd: metadata cũ không khớp → tải lại từ đầu). Null nếu không có.</summary>
    public string? Warning { get; init; }

    /// <summary>Số seed đang kết nối (chỉ torrent). Null với loại tải khác.</summary>
    public int? Seeds { get; init; }

    /// <summary>Tổng số peer đang kết nối (chỉ torrent). Null với loại tải khác.</summary>
    public int? Peers { get; init; }
}
