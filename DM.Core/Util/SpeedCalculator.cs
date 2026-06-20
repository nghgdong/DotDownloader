using System.Diagnostics;

namespace DM.Core.Util;

/// <summary>
/// Tính tốc độ tải tổng theo cửa sổ trượt (mặc định ~5s) và ETA, kèm cổng throttle
/// để không raise progress quá ~4 lần/giây. KHÔNG thread-safe — engine phải gọi trong lock.
/// </summary>
public sealed class SpeedCalculator
{
    /// <summary>Khoảng tối thiểu giữa hai mẫu để giới hạn số mẫu lưu (tránh phình ở tốc độ cao).</summary>
    private static readonly TimeSpan SampleSpacing = TimeSpan.FromMilliseconds(50);

    private readonly TimeSpan _window;
    private readonly TimeSpan _minReportInterval;
    private readonly List<(TimeSpan At, long Total)> _samples = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastReportAt;
    private bool _hasReported;

    public SpeedCalculator(TimeSpan? window = null, TimeSpan? minReportInterval = null)
    {
        _window = window ?? TimeSpan.FromSeconds(5);
        _minReportInterval = minReportInterval ?? TimeSpan.FromMilliseconds(250);
    }

    /// <summary>Ghi nhận tổng số byte đã tải tại thời điểm hiện tại.</summary>
    public void Record(long totalBytes)
    {
        var now = _clock.Elapsed;

        // Gộp các mẫu quá gần nhau: chỉ cập nhật mẫu cuối thay vì thêm mới.
        if (_samples.Count > 0 && now - _samples[^1].At < SampleSpacing)
        {
            _samples[^1] = (now, totalBytes);
        }
        else
        {
            _samples.Add((now, totalBytes));
        }

        // Giữ lại đúng một mẫu mốc nằm ngay trước cửa sổ + toàn bộ mẫu trong cửa sổ.
        var cutoff = now - _window;
        int anchor = 0;
        for (int i = 0; i < _samples.Count; i++)
        {
            if (_samples[i].At <= cutoff)
            {
                anchor = i;
            }
            else
            {
                break;
            }
        }
        if (anchor > 0)
        {
            _samples.RemoveRange(0, anchor);
        }
    }

    /// <summary>Tốc độ trung bình trong cửa sổ trượt (byte/giây). 0 nếu chưa đủ dữ liệu.</summary>
    public double BytesPerSecond
    {
        get
        {
            if (_samples.Count < 2)
            {
                return 0;
            }
            var oldest = _samples[0];
            var newest = _samples[^1];
            var seconds = (newest.At - oldest.At).TotalSeconds;
            return seconds <= 0 ? 0 : (newest.Total - oldest.Total) / seconds;
        }
    }

    /// <summary>ETA dựa trên tốc độ cửa sổ. Null nếu không biết size hoặc tốc độ 0.</summary>
    public TimeSpan? Eta(long downloaded, long totalSize)
    {
        if (totalSize <= 0 || downloaded >= totalSize)
        {
            return null;
        }
        var bps = BytesPerSecond;
        return bps <= 0 ? null : TimeSpan.FromSeconds((totalSize - downloaded) / bps);
    }

    /// <summary>True nếu đủ thời gian từ lần report trước (hoặc <paramref name="force"/>). Cập nhật mốc khi true.</summary>
    public bool ShouldReport(bool force = false)
    {
        var now = _clock.Elapsed;
        if (!force && _hasReported && now - _lastReportAt < _minReportInterval)
        {
            return false;
        }
        _hasReported = true;
        _lastReportAt = now;
        return true;
    }
}
