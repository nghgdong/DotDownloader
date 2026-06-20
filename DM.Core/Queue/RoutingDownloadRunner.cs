using DM.Core.Models;
using DM.Core.Torrent;

namespace DM.Core.Queue;

/// <summary>
/// Điều hướng task tới runner phù hợp: <c>StreamType="Torrent"</c> → <see cref="TorrentDownloader"/>,
/// còn lại → runner HTTP (engine byte-range/HLS/DASH). Cho phép một <see cref="DownloadQueue"/>
/// quản lý cả tải HTTP lẫn torrent.
/// </summary>
public sealed class RoutingDownloadRunner : IDownloadRunner
{
    public const string TorrentType = "Torrent";

    private readonly IDownloadRunner _httpRunner;
    private readonly TorrentDownloader _torrent;

    public RoutingDownloadRunner(IDownloadRunner httpRunner, TorrentDownloader torrent)
    {
        _httpRunner = httpRunner;
        _torrent = torrent;
    }

    public static bool IsTorrent(DownloadTask task)
        => string.Equals(task.StreamType, TorrentType, StringComparison.OrdinalIgnoreCase);

    public Task RunAsync(DownloadTask task, IProgress<ProgressReport>? progress, CancellationToken ct)
        => IsTorrent(task)
            ? _torrent.DownloadAsync(task, progress, ct)
            : _httpRunner.RunAsync(task, progress, ct);
}
