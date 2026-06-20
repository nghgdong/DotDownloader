using System.Runtime.ExceptionServices;
using DM.Core.Models;
using DM.Core.Persistence;
using DM.Core.Util;
using DM.Core.Video;

namespace DM.Core.Download;

/// <summary>
/// Điều phối tải một <see cref="DownloadTask"/>: chia file thành N segment, tải song song
/// (khi server hỗ trợ range) hoặc fallback đơn luồng, tổng hợp tiến độ/tốc độ,
/// lưu metadata để resume, retry segment lỗi.
/// </summary>
public sealed class DownloadEngine
{
    public const int DefaultSegmentCount = 8;
    private const long SaveByteThreshold = 1 * 1024 * 1024; // 1MB

    private readonly SegmentDownloader _segmentDownloader;
    private readonly SingleStreamDownloader _singleStreamDownloader;
    private readonly MetadataStore _store;
    private readonly RetryPolicy _retry;
    private readonly HttpClient? _http;
    private VideoDownloadEngine? _video;

    public DownloadEngine(
        HttpClient? http = null,
        MetadataStore? store = null,
        RetryPolicy? retry = null,
        VideoDownloadEngine? video = null,
        RateLimiter? rateLimiter = null)
    {
        _segmentDownloader = new SegmentDownloader(http, rateLimiter);
        _singleStreamDownloader = new SingleStreamDownloader(http, rateLimiter);
        _store = store ?? new MetadataStore();
        _retry = retry ?? new RetryPolicy();
        _http = http;
        _video = video;
    }

    public async Task DownloadAsync(
        DownloadTask task,
        int segmentCount = DefaultSegmentCount,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        task.State = DownloadState.Connecting;

        // Route stream video (HLS/DASH) sang video engine thay vì byte-range downloader.
        if (IsVideoStream(task.StreamType))
        {
            await RunVideoAsync(task, progress, ct).ConfigureAwait(false);
            return;
        }

        // Không hỗ trợ range hoặc chưa biết size → không chia segment được → fallback đơn luồng.
        if (!task.SupportsRange || task.TotalBytes <= 0 || segmentCount <= 1)
        {
            await RunSingleStreamAsync(task, progress, ct).ConfigureAwait(false);
            return;
        }

        await ResumeOrInitAsync(task, segmentCount, progress, ct).ConfigureAwait(false);

        EnsureDirectory(task.FilePath);

        // OpenOrCreate (KHÔNG Create) để không truncate file khi resume; SetLength đảm bảo đúng size.
        using var handle = File.OpenHandle(
            task.FilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite,
            FileOptions.Asynchronous);
        RandomAccess.SetLength(handle, task.TotalBytes);

        task.State = DownloadState.Downloading;

        long total = task.TotalBytes;
        long accumulated = task.DownloadedBytes; // tính cả phần đã resume
        long sinceSave = 0;
        var speed = new SpeedCalculator();
        var reportGate = new object();
        var saveSignal = new SemaphoreSlim(0, 1);
        using var saverCts = new CancellationTokenSource();

        void OnBytes(long delta)
        {
            long current = Interlocked.Add(ref accumulated, delta);

            // Trigger lưu metadata theo ngưỡng 1MB (cạnh tranh với nhịp 1s ở SaverLoop).
            if (Interlocked.Add(ref sinceSave, delta) >= SaveByteThreshold
                && Interlocked.Exchange(ref sinceSave, 0) >= SaveByteThreshold)
            {
                try { saveSignal.Release(); } catch (SemaphoreFullException) { /* đã có tín hiệu chờ */ }
            }

            lock (reportGate)
            {
                speed.Record(current);
                if (progress is not null && speed.ShouldReport())
                {
                    progress.Report(new ProgressReport
                    {
                        BytesDownloaded = current,
                        TotalBytes = total,
                        BytesPerSecond = speed.BytesPerSecond,
                        Eta = speed.Eta(current, total),
                        State = DownloadState.Downloading
                    });
                }
            }
        }

        // SaverLoop: lưu metadata mỗi 1s HOẶC ngay khi có tín hiệu 1MB (cái nào tới trước).
        async Task SaverLoop()
        {
            while (!saverCts.IsCancellationRequested)
            {
                try
                {
                    await saveSignal.WaitAsync(TimeSpan.FromSeconds(1), saverCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                try { await SaveAsync(task).ConfigureAwait(false); }
                catch { /* lỗi ghi metadata không được làm hỏng phiên tải */ }
            }
        }

        var saver = SaverLoop();
        ExceptionDispatchInfo? failure = null;

        try
        {
            var jobs = task.Segments.Select(seg => _retry.ExecuteAsync(
                c => _segmentDownloader.DownloadAsync(handle, seg, task.Url, OnBytes, c), ct));
            await Task.WhenAll(jobs).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            task.State = DownloadState.Paused;
            failure = ExceptionDispatchInfo.Capture(ex);
        }
        catch (Exception ex)
        {
            task.State = DownloadState.Failed;
            failure = ExceptionDispatchInfo.Capture(ex);
        }

        // Dừng saver trước khi flush lần cuối để không có hai lần ghi chồng nhau.
        saverCts.Cancel();
        try { await saver.ConfigureAwait(false); } catch { /* ignore */ }

        if (failure is not null)
        {
            // Pause/Fail: giữ nguyên metadata, flush trạng thái mới nhất để lần sau resume.
            try { await SaveAsync(task).ConfigureAwait(false); } catch { /* ignore */ }
            failure.Throw();
        }

        // Hoàn tất: xóa .dmmeta, set Completed.
        _store.Delete(task.FilePath);
        task.State = DownloadState.Completed;
        progress?.Report(new ProgressReport
        {
            BytesDownloaded = task.DownloadedBytes,
            TotalBytes = total,
            BytesPerSecond = 0,
            Eta = TimeSpan.Zero,
            State = DownloadState.Completed
        });
    }

    /// <summary>
    /// Nạp <c>.dmmeta</c> nếu khớp nguồn (URL + size) &amp; file còn tồn tại → resume.
    /// Không khớp → bỏ metadata cũ, chia segment mới (tải lại từ đầu) &amp; cảnh báo qua progress.
    /// </summary>
    private async Task ResumeOrInitAsync(
        DownloadTask task, int segmentCount, IProgress<ProgressReport>? progress, CancellationToken ct)
    {
        var meta = await _store.LoadAsync(task.FilePath, ct).ConfigureAwait(false);

        if (meta is not null && meta.MatchesSource(task) && File.Exists(task.FilePath))
        {
            meta.ApplyTo(task);
            return;
        }

        if (meta is not null)
        {
            // Metadata tồn tại nhưng không dùng được → dọn & cảnh báo.
            _store.Delete(task.FilePath);
            progress?.Report(new ProgressReport
            {
                BytesDownloaded = 0,
                TotalBytes = task.TotalBytes,
                BytesPerSecond = 0,
                State = DownloadState.Connecting,
                Warning = "Metadata cũ không khớp nguồn (URL/size đã đổi) — tải lại từ đầu."
            });
        }

        task.Segments.Clear();
        BuildSegments(task, segmentCount);
    }

    private Task SaveAsync(DownloadTask task)
        => _store.SaveAsync(task.FilePath, DownloadMetadata.FromTask(task));

    private static bool IsVideoStream(string? type)
        => string.Equals(type, "HLS", StringComparison.OrdinalIgnoreCase)
           || string.Equals(type, "DASH", StringComparison.OrdinalIgnoreCase);

    private async Task RunVideoAsync(DownloadTask task, IProgress<ProgressReport>? progress, CancellationToken ct)
    {
        _video ??= new VideoDownloadEngine(_http);
        var headers = VideoHeaders.From(task.RequestHeaders);
        task.State = DownloadState.Downloading;
        try
        {
            if (string.Equals(task.StreamType, "HLS", StringComparison.OrdinalIgnoreCase))
            {
                await _video.DownloadHlsAsync(task.Url, headers, task.FilePath, progress, ct: ct).ConfigureAwait(false);
            }
            else
            {
                await _video.DownloadDashAsync(task.Url, headers, task.FilePath, progress, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            task.State = DownloadState.Paused;
            throw;
        }
        catch
        {
            task.State = DownloadState.Failed;
            throw;
        }
        task.State = DownloadState.Completed;
        progress?.Report(new ProgressReport
        {
            BytesDownloaded = 1,
            TotalBytes = 1,
            BytesPerSecond = 0,
            Eta = TimeSpan.Zero,
            State = DownloadState.Completed
        });
    }

    private async Task RunSingleStreamAsync(
        DownloadTask task, IProgress<ProgressReport>? progress, CancellationToken ct)
    {
        task.State = DownloadState.Downloading;
        try
        {
            await _singleStreamDownloader
                .DownloadAsync(task.Url, task.FilePath, task.TotalBytes, progress, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            task.State = DownloadState.Paused;
            throw;
        }
        catch
        {
            task.State = DownloadState.Failed;
            throw;
        }
        task.State = DownloadState.Completed;
    }

    /// <summary>Chia file thành các segment đều nhau; segment cuối ôm phần dư.</summary>
    internal static void BuildSegments(DownloadTask task, int segmentCount)
    {
        long total = task.TotalBytes;
        int n = (int)Math.Min(segmentCount, Math.Max(1, total));
        long chunk = total / n;

        task.Segments.Clear();
        for (int i = 0; i < n; i++)
        {
            long start = i * chunk;
            long end = (i == n - 1) ? total - 1 : start + chunk - 1;
            task.Segments.Add(new Segment { Start = start, End = end });
        }
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
