using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DM.App.Services;
using DM.Core.Models;
using DM.Core.Net;
using DM.Core.Queue;
using DM.Core.Util;
using DM.Server.Models;

namespace DM.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DownloadQueue _queue;
    private readonly AppSettings _settings;
    private readonly TaskStore _store;
    private readonly HttpProbe _probe;
    private readonly Dispatcher _dispatcher;

    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();
    public ICollectionView ItemsView { get; }

    public ObservableCollection<string> Categories { get; } =
        new() { "All", "Video", "Document", "Music", "Compressed", "Program", "Other" };
    public string[] StatusFilters { get; } = { "All", "Downloading", "Completed", "Paused" };

    [ObservableProperty] private DownloadItemViewModel? selectedItem;
    [ObservableProperty] private string statusFilter = "All";
    [ObservableProperty] private string categoryFilter = "All";

    public MainViewModel(DownloadQueue queue, AppSettings settings, TaskStore store,
        HttpProbe probe, Dispatcher dispatcher)
    {
        _queue = queue;
        _settings = settings;
        _store = store;
        _probe = probe;
        _dispatcher = dispatcher;

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterPredicate;

        LoadPersisted();
    }

    private void LoadPersisted()
    {
        foreach (var task in _store.Load())
        {
            AddItem(task, enqueue: false);
        }
    }

    partial void OnStatusFilterChanged(string value) => ItemsView.Refresh();
    partial void OnCategoryFilterChanged(string value) => ItemsView.Refresh();

    private bool FilterPredicate(object obj)
    {
        if (obj is not DownloadItemViewModel vm)
        {
            return false;
        }
        bool statusOk = StatusFilter switch
        {
            "Downloading" => vm.State is DownloadState.Queued or DownloadState.Connecting or DownloadState.Downloading,
            "Completed" => vm.State == DownloadState.Completed,
            "Paused" => vm.State == DownloadState.Paused,
            _ => true
        };
        bool categoryOk = CategoryFilter == "All" || vm.Category == CategoryFilter;
        return statusOk && categoryOk;
    }

    // ---------- thêm task ----------

    /// <summary>Thêm tải từ UI (đã probe ở dialog). Chạy trên UI thread.</summary>
    public void AddDownload(string url, string? fileName, long totalBytes, bool supportsRange)
    {
        var streamType = DetectStreamType(url);
        var task = BuildTask(url, fileName, totalBytes, supportsRange, streamType, headers: null);
        AddItem(task, enqueue: true);
        Persist();
    }

    /// <summary>Callback cho LocalServer (chạy ngoài UI thread) — trả taskId ngay, enqueue async.</summary>
    public Guid EnqueueFromRequest(DownloadRequest req)
    {
        var streamType = req.Type is "HLS" or "DASH" ? req.Type : DetectStreamType(req.Url);
        var headers = BuildHeaders(req);
        var task = BuildTask(req.Url, req.FileName, -1, false, streamType, headers);

        _dispatcher.InvokeAsync(async () =>
        {
            if (streamType is null)
            {
                try
                {
                    var p = await _probe.ProbeAsync(req.Url);
                    task.TotalBytes = p.TotalBytes;
                    task.SupportsRange = p.SupportsRange;
                }
                catch
                {
                    // probe lỗi → vẫn thêm task, engine sẽ xử lý đơn luồng
                }
            }
            AddItem(task, enqueue: true);
            Persist();
        });

        return task.Id;
    }

    private void AddItem(DownloadTask task, bool enqueue)
    {
        var vm = new DownloadItemViewModel(task);
        Items.Add(vm);
        if (enqueue)
        {
            _queue.Add(task, vm.Progress);
        }
    }

    private DownloadTask BuildTask(string url, string? fileName, long totalBytes, bool supportsRange,
        string? streamType, Dictionary<string, string>? headers)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? SuggestName(url, streamType) : fileName!;
        var path = CategoryClassifier.ResolveTargetPath(_settings.DownloadDirectory, name, FolderOverrides());
        return new DownloadTask
        {
            Url = url,
            FilePath = path,
            TotalBytes = totalBytes,
            SupportsRange = supportsRange,
            Category = CategoryClassifier.Classify(name).ToString(),
            StreamType = streamType,
            RequestHeaders = headers
        };
    }

    // ---------- lệnh trên item ----------

    [RelayCommand]
    private void Pause(DownloadItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null) return;
        _queue.Pause(item.Task);
        Persist();
    }

    [RelayCommand]
    private void Resume(DownloadItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null) return;
        _queue.Resume(item.Task);
        if (item.Task.State == DownloadState.Paused) // task nạp từ tasks.json, chưa ở trong queue
        {
            _queue.Add(item.Task, item.Progress);
        }
        Persist();
    }

    [RelayCommand]
    private void Cancel(DownloadItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null) return;
        _queue.Cancel(item.Task);
        Persist();
    }

    [RelayCommand]
    private void Delete(DownloadItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null) return;
        _queue.Remove(item.Task);
        Items.Remove(item);
        item.Dispose();
        Persist();
    }

    [RelayCommand]
    private void PauseAll() => _queue.PauseAll();

    [RelayCommand]
    private void ResumeAll()
    {
        foreach (var item in Items)
        {
            if (item.Task.State == DownloadState.Paused)
            {
                _queue.Resume(item.Task);
                if (item.Task.State == DownloadState.Paused)
                {
                    _queue.Add(item.Task, item.Progress);
                }
            }
        }
        Persist();
    }

    public void Persist() => _store.Save(Items.Select(i => i.Task));

    // ---------- helpers ----------

    private static string? DetectStreamType(string url)
    {
        if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) return "Torrent";
        var u = url.Split('?', '#')[0].ToLowerInvariant();
        if (u.EndsWith(".torrent")) return "Torrent";
        if (u.EndsWith(".m3u8")) return "HLS";
        if (u.EndsWith(".mpd")) return "DASH";
        return null;
    }

    private static string SuggestName(string url, string? streamType)
    {
        if (streamType == "Torrent")
        {
            if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                return ParseMagnetName(url) ?? "torrent";
            }
            try
            {
                var n = Path.GetFileName(new Uri(url).AbsolutePath);
                return string.IsNullOrWhiteSpace(n) ? "torrent" : n;
            }
            catch { return "torrent"; }
        }
        try
        {
            var name = Path.GetFileName(new Uri(url).AbsolutePath.TrimEnd('/'));
            if (!string.IsNullOrWhiteSpace(name))
            {
                return streamType is null ? name : Path.ChangeExtension(name, ".mp4");
            }
        }
        catch { /* ignore */ }
        return streamType is null ? "download.bin" : "video.mp4";
    }

    /// <summary>Lấy tên hiển thị từ tham số dn= của magnet link.</summary>
    private static string? ParseMagnetName(string magnet)
    {
        var idx = magnet.IndexOf("dn=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var rest = magnet[(idx + 3)..];
        var end = rest.IndexOf('&');
        var value = end >= 0 ? rest[..end] : rest;
        try { return Uri.UnescapeDataString(value.Replace('+', ' ')); }
        catch { return value; }
    }

    private IReadOnlyDictionary<FileCategory, string>? FolderOverrides()
    {
        if (_settings.CategoryFolders.Count == 0)
        {
            return null;
        }
        var map = new Dictionary<FileCategory, string>();
        foreach (var (k, v) in _settings.CategoryFolders)
        {
            if (Enum.TryParse<FileCategory>(k, out var cat) && !string.IsNullOrWhiteSpace(v))
            {
                map[cat] = v;
            }
        }
        return map.Count > 0 ? map : null;
    }

    private static Dictionary<string, string>? BuildHeaders(DownloadRequest req)
    {
        var h = req.Headers is not null
            ? new Dictionary<string, string>(req.Headers, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(req.Referrer))
        {
            h["Referer"] = req.Referrer!;
        }
        if (!string.IsNullOrWhiteSpace(req.Cookies))
        {
            h["Cookie"] = req.Cookies!;
        }
        return h.Count > 0 ? h : null;
    }
}
