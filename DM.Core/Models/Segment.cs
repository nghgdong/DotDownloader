namespace DM.Core.Models;

/// <summary>
/// Một byte-range của file cần tải. Dùng cho cả đơn luồng (1 segment phủ toàn file)
/// lẫn đa luồng (nhiều segment) ở các phase sau.
/// </summary>
public sealed class Segment
{
    /// <summary>Offset byte đầu tiên của segment trong file (inclusive).</summary>
    public required long Start { get; init; }

    /// <summary>Offset byte cuối cùng của segment trong file (inclusive).</summary>
    public required long End { get; init; }

    /// <summary>Số byte đã tải xong của segment này.</summary>
    public long Downloaded { get; set; }

    /// <summary>Tổng số byte segment cần tải (đã bao gồm cả hai đầu mút).</summary>
    public long Length => End - Start + 1;

    /// <summary>Vị trí byte tiếp theo cần ghi/tải (Start + Downloaded).</summary>
    public long CurrentPos => Start + Downloaded;

    /// <summary>Đã tải đủ toàn bộ segment chưa.</summary>
    public bool IsComplete => Downloaded >= Length;
}
