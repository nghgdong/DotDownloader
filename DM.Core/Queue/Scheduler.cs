using DM.Core.Models;

namespace DM.Core.Queue;

/// <summary>
/// Hẹn giờ tải: giữ các task có <see cref="DownloadTask.ScheduledAt"/>, định kỳ (mặc định 1 phút)
/// quét task đến giờ → enqueue vào <see cref="DownloadQueue"/>.
/// </summary>
public sealed class Scheduler : IDisposable
{
    private readonly DownloadQueue _queue;
    private readonly Func<DateTimeOffset> _now;
    private readonly Timer _timer;
    private readonly List<(DownloadTask Task, IProgress<ProgressReport>? Progress)> _scheduled = new();
    private readonly object _gate = new();

    public Scheduler(DownloadQueue queue, TimeSpan? interval = null, Func<DateTimeOffset>? now = null)
    {
        _queue = queue;
        _now = now ?? (() => DateTimeOffset.Now);
        var period = interval ?? TimeSpan.FromMinutes(1);
        _timer = new Timer(_ => Tick(), null, period, period);
    }

    /// <summary>Thêm task hẹn giờ. <c>ScheduledAt == null</c> → enqueue ngay ở tick kế.</summary>
    public void Schedule(DownloadTask task, IProgress<ProgressReport>? progress = null)
    {
        lock (_gate)
        {
            _scheduled.Add((task, progress));
        }
        task.State = DownloadState.Queued;
    }

    /// <summary>Quét và enqueue task đến giờ (gọi tự động bởi timer; public để test gọi trực tiếp).</summary>
    public void Tick()
    {
        var now = _now();
        List<(DownloadTask Task, IProgress<ProgressReport>? Progress)> due;
        lock (_gate)
        {
            due = _scheduled.Where(s => s.Task.ScheduledAt is null || s.Task.ScheduledAt <= now).ToList();
            foreach (var d in due)
            {
                _scheduled.Remove(d);
            }
        }
        foreach (var d in due)
        {
            _queue.Add(d.Task, d.Progress);
        }
    }

    public void Dispose() => _timer.Dispose();
}
