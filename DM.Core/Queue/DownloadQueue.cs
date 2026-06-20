using DM.Core.Models;

namespace DM.Core.Queue;

/// <summary>
/// Hàng đợi tải: giới hạn số tải đồng thời bằng <see cref="SemaphoreSlim"/> (mặc định 3),
/// task thừa ở trạng thái Queued, task xong/lỗi tự nhường chỗ cho task kế. Hỗ trợ
/// Pause/Resume/Cancel từng task, Pause All / Resume All, và hành động sau khi hết queue.
/// </summary>
public sealed class DownloadQueue : IDisposable
{
    private sealed class QueueItem
    {
        public required DownloadTask Task { get; init; }
        public IProgress<ProgressReport>? Progress { get; init; }
        public CancellationTokenSource Cts { get; set; } = new();
        public bool CancelRequested { get; set; }
        public System.Threading.Tasks.Task Worker { get; set; } = System.Threading.Tasks.Task.CompletedTask;
    }

    private readonly IDownloadRunner _runner;
    private readonly SemaphoreSlim _slots;
    private readonly ISystemPowerController _power;
    private readonly List<QueueItem> _items = new();
    private readonly object _gate = new();

    private int _runningCount;
    private bool _afterActionFired;

    public int MaxConcurrent { get; }
    public AfterQueueAction AfterQueueAction { get; set; } = AfterQueueAction.None;

    public DownloadQueue(IDownloadRunner runner, int maxConcurrent = 3, ISystemPowerController? power = null)
    {
        if (maxConcurrent < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent));
        }
        _runner = runner;
        MaxConcurrent = maxConcurrent;
        _slots = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        _power = power ?? new NoOpPowerController();
    }

    public int RunningCount => Volatile.Read(ref _runningCount);

    public IReadOnlyList<DownloadTask> Tasks
    {
        get { lock (_gate) { return _items.Select(i => i.Task).ToList(); } }
    }

    public bool IsIdle
    {
        get
        {
            lock (_gate)
            {
                return !_items.Any(i =>
                    i.Task.State is DownloadState.Queued or DownloadState.Downloading);
            }
        }
    }

    public DownloadTask Add(DownloadTask task, IProgress<ProgressReport>? progress = null)
    {
        var item = new QueueItem { Task = task, Progress = progress };
        lock (_gate)
        {
            _items.Add(item);
            _afterActionFired = false;
        }
        task.State = DownloadState.Queued;
        item.Worker = RunWorkerAsync(item);
        return task;
    }

    private async Task RunWorkerAsync(QueueItem item)
    {
        try
        {
            await _slots.WaitAsync(item.Cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            item.Task.State = item.CancelRequested ? DownloadState.Canceled : DownloadState.Paused;
            return;
        }

        Interlocked.Increment(ref _runningCount);
        item.Task.State = DownloadState.Downloading;
        try
        {
            await _runner.RunAsync(item.Task, item.Progress, item.Cts.Token).ConfigureAwait(false);
            item.Task.State = DownloadState.Completed;
        }
        catch (OperationCanceledException)
        {
            item.Task.State = item.CancelRequested ? DownloadState.Canceled : DownloadState.Paused;
        }
        catch
        {
            item.Task.State = DownloadState.Failed;
        }
        finally
        {
            Interlocked.Decrement(ref _runningCount);
            _slots.Release();
            MaybeFireAfterAction();
        }
    }

    // ---------- per-item ----------

    public void Pause(DownloadTask task) => CancelItem(task, cancel: false);

    public void Cancel(DownloadTask task) => CancelItem(task, cancel: true);

    private void CancelItem(DownloadTask task, bool cancel)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Task == task);
            if (item is null || item.Task.State is not (DownloadState.Queued or DownloadState.Downloading))
            {
                return;
            }
            item.CancelRequested = cancel;
            item.Cts.Cancel();
        }
    }

    public void Resume(DownloadTask task)
    {
        QueueItem? item;
        lock (_gate)
        {
            item = _items.FirstOrDefault(i => i.Task == task);
            if (item is null || item.Task.State != DownloadState.Paused)
            {
                return;
            }
            _afterActionFired = false;
        }
        RestartItem(item);
    }

    /// <summary>Hủy và gỡ task khỏi hàng đợi (không xóa file đã tải).</summary>
    public void Remove(DownloadTask task)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Task == task);
            if (item is null)
            {
                return;
            }
            item.CancelRequested = true;
            item.Cts.Cancel();
            _items.Remove(item);
        }
    }

    // ---------- bulk ----------

    public void PauseAll()
    {
        lock (_gate)
        {
            foreach (var i in _items)
            {
                if (i.Task.State is DownloadState.Queued or DownloadState.Downloading)
                {
                    i.CancelRequested = false;
                    i.Cts.Cancel();
                }
            }
        }
    }

    public void ResumeAll()
    {
        List<QueueItem> toResume;
        lock (_gate)
        {
            toResume = _items.Where(i => i.Task.State == DownloadState.Paused).ToList();
            _afterActionFired = false;
        }
        foreach (var i in toResume)
        {
            RestartItem(i);
        }
    }

    private void RestartItem(QueueItem item)
    {
        item.Cts.Dispose();
        item.Cts = new CancellationTokenSource();
        item.CancelRequested = false;
        item.Task.State = DownloadState.Queued;
        item.Worker = RunWorkerAsync(item);
    }

    private void MaybeFireAfterAction()
    {
        AfterQueueAction action;
        lock (_gate)
        {
            if (_afterActionFired || AfterQueueAction == AfterQueueAction.None)
            {
                return;
            }
            // "Hết queue" = không còn task đang/chờ chạy VÀ không có task Paused còn treo.
            bool pending = _items.Any(i =>
                i.Task.State is DownloadState.Queued or DownloadState.Downloading or DownloadState.Paused);
            bool anyCompleted = _items.Any(i => i.Task.State == DownloadState.Completed);
            if (pending || !anyCompleted)
            {
                return;
            }
            _afterActionFired = true;
            action = AfterQueueAction;
        }

        try
        {
            if (action == AfterQueueAction.Shutdown)
            {
                _power.Shutdown();
            }
            else if (action == AfterQueueAction.Sleep)
            {
                _power.Sleep();
            }
        }
        catch
        {
            // không để lỗi điều khiển nguồn làm sập queue
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var i in _items)
            {
                i.Cts.Cancel();
                i.Cts.Dispose();
            }
        }
        _slots.Dispose();
    }
}
