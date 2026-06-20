using DM.Core.Models;

namespace DM.Core.Persistence;

/// <summary>
/// DTO bền vững (JSON) cho một download task: đủ thông tin để resume sau khi đóng app.
/// </summary>
public sealed class DownloadMetadata
{
    public required string Url { get; init; }
    public required long TotalBytes { get; init; }
    public required bool SupportsRange { get; init; }
    public required List<SegmentMetadata> Segments { get; init; }

    public static DownloadMetadata FromTask(DownloadTask task) => new()
    {
        Url = task.Url,
        TotalBytes = task.TotalBytes,
        SupportsRange = task.SupportsRange,
        Segments = task.Segments
            .Select(s => new SegmentMetadata { Start = s.Start, End = s.End, Downloaded = s.Downloaded })
            .ToList()
    };

    /// <summary>Khôi phục danh sách segment (kèm tiến độ) vào <paramref name="task"/> để tải tiếp.</summary>
    public void ApplyTo(DownloadTask task)
    {
        task.Segments.Clear();
        foreach (var s in Segments)
        {
            task.Segments.Add(new Segment { Start = s.Start, End = s.End, Downloaded = s.Downloaded });
        }
    }

    /// <summary>Metadata có khớp với task hiện tại không (URL &amp; size) → mới được phép resume.</summary>
    public bool MatchesSource(DownloadTask task)
        => TotalBytes > 0
           && TotalBytes == task.TotalBytes
           && string.Equals(Url, task.Url, StringComparison.Ordinal);
}

public sealed class SegmentMetadata
{
    public required long Start { get; init; }
    public required long End { get; init; }
    public long Downloaded { get; set; }
}
