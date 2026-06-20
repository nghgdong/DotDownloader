using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DM.Core.Models;

namespace DM.App.ViewModels;

/// <summary>
/// Bọc <see cref="DownloadTask"/> cho binding. Report tiến độ đến từ thread tải được gom lại,
/// chỉ đẩy lên UI mỗi ~250ms qua <see cref="DispatcherTimer"/> (không lag khi tải nhanh).
/// </summary>
public partial class DownloadItemViewModel : ObservableObject, IDisposable
{
    public DownloadTask Task { get; }

    [ObservableProperty] private double progressPercent;
    [ObservableProperty] private string speedText = "";
    [ObservableProperty] private string etaText = "";
    [ObservableProperty] private string peersText = "";
    [ObservableProperty] private DownloadState state;

    public string FileName =>
        string.IsNullOrEmpty(Task.FilePath) ? Task.Url : Path.GetFileName(Task.FilePath);
    public string SizeText => FormatBytes(Task.TotalBytes);
    public string Category => Task.Category ?? "Other";
    public string Url => Task.Url;
    public bool IsTorrent => string.Equals(Task.StreamType, "Torrent", StringComparison.OrdinalIgnoreCase);

    private readonly DispatcherTimer _timer;
    private readonly object _gate = new();
    private ProgressReport? _latest;

    /// <summary>Đích nhận report từ engine (gọi từ thread tải; chỉ ghi đệm, không đụng UI).</summary>
    public IProgress<ProgressReport> Progress { get; }

    public DownloadItemViewModel(DownloadTask task)
    {
        Task = task;
        state = task.State;
        if (task.State == DownloadState.Completed)
        {
            progressPercent = 100;
        }

        Progress = new SyncProgress(r => { lock (_gate) { _latest = r; } });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Flush();
        _timer.Start();
    }

    private void Flush()
    {
        ProgressReport? snapshot;
        lock (_gate) { snapshot = _latest; }

        State = Task.State; // đồng bộ trạng thái đổi do command/queue

        if (snapshot is not { } r)
        {
            return;
        }

        ProgressPercent = r.TotalBytes > 0
            ? Math.Clamp(r.BytesDownloaded * 100.0 / r.TotalBytes, 0, 100)
            : (r.State == DownloadState.Completed ? 100 : ProgressPercent);
        SpeedText = r.State == DownloadState.Downloading ? FormatSpeed(r.BytesPerSecond) : "";
        EtaText = r.State == DownloadState.Downloading && r.Eta is { } e ? FormatEta(e) : "";
        if (r.Seeds is { } s && r.Peers is { } p) // chỉ torrent mới có
        {
            PeersText = $"{s} / {p}";
        }
        if (r.State == DownloadState.Completed)
        {
            ProgressPercent = 100;
        }
    }

    public void Dispose() => _timer.Stop();

    // ---------- format ----------

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "?";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.##} {units[u]}";
    }

    private static string FormatSpeed(double bytesPerSec)
        => bytesPerSec <= 0 ? "" : $"{FormatBytes((long)bytesPerSec)}/s";

    private static string FormatEta(TimeSpan eta)
        => eta.TotalHours >= 1 ? eta.ToString(@"h\:mm\:ss") : eta.ToString(@"m\:ss");

    private sealed class SyncProgress : IProgress<ProgressReport>
    {
        private readonly Action<ProgressReport> _action;
        public SyncProgress(Action<ProgressReport> action) => _action = action;
        public void Report(ProgressReport value) => _action(value);
    }
}
