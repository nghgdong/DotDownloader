using System.Net;
using DM.Core.Models;
using DM.Core.Net;
using MonoTorrent;
using MonoTorrent.Client;

namespace DM.Core.Torrent;

/// <summary>
/// Tải BitTorrent (magnet hoặc file .torrent) qua MonoTorrent. Dùng MỘT <see cref="ClientEngine"/>
/// chung (một cổng lắng nghe). Báo tiến độ qua <see cref="ProgressReport"/>, dừng seed khi xong.
/// Cấu hình hướng tốc độ: nhiều kết nối, mở cổng (UPnP), DHT/PEX/LPD để tìm nhiều peer.
/// </summary>
public sealed class TorrentDownloader : IAsyncDisposable
{
    private readonly ClientEngine _engine;
    private readonly bool _seedAfterComplete;
    private readonly int _maxConnectionsPerTorrent;

    public TorrentDownloader(
        string? cacheDirectory = null,
        bool seedAfterComplete = false,
        int listenPort = 52830,
        int maxGlobalConnections = 500,
        int maxConnectionsPerTorrent = 250)
    {
        _seedAfterComplete = seedAfterComplete;
        _maxConnectionsPerTorrent = maxConnectionsPerTorrent;

        var builder = new EngineSettingsBuilder
        {
            CacheDirectory = cacheDirectory
                ?? Path.Combine(Path.GetTempPath(), "DotDownloader", "torrent-cache"),
            MaximumConnections = maxGlobalConnections,
            MaximumHalfOpenConnections = 16,
            MaximumDownloadRate = 0, // không giới hạn (0)
            AllowPortForwarding = true, // UPnP/NAT-PMP mở cổng → nhận peer vào
            AllowLocalPeerDiscovery = true,
            // Cổng cố định cho cả TCP nghe lẫn DHT (port forwarding mới ổn định).
            ListenEndPoints = new Dictionary<string, IPEndPoint>
            {
                ["ipv4"] = new IPEndPoint(IPAddress.Any, listenPort),
                ["ipv6"] = new IPEndPoint(IPAddress.IPv6Any, listenPort)
            },
            DhtEndPoint = new IPEndPoint(IPAddress.Any, listenPort) // bật DHT (tìm peer cho magnet)
        };
        _engine = new ClientEngine(builder.ToSettings());
    }

    /// <param name="task">Url = magnet:?... HOẶC đường dẫn file .torrent. FilePath dùng để lấy thư mục lưu.</param>
    public async Task DownloadAsync(
        DownloadTask task, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
    {
        var saveDir = Path.GetDirectoryName(Path.GetFullPath(task.FilePath));
        if (string.IsNullOrEmpty(saveDir))
        {
            saveDir = Directory.GetCurrentDirectory();
        }
        Directory.CreateDirectory(saveDir);

        var manager = await AddAsync(task.Url, saveDir).ConfigureAwait(false);
        await manager.StartAsync().ConfigureAwait(false);

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (manager.State == TorrentState.Error)
                {
                    throw new InvalidOperationException(
                        $"Torrent lỗi: {manager.Error?.Reason}");
                }

                long total = manager.Torrent?.Size ?? -1;
                if (total > 0)
                {
                    task.TotalBytes = total;
                }

                double pct = manager.Progress; // 0..100
                long done = total > 0 ? (long)(total * pct / 100.0) : 0;
                double bps = manager.Monitor.DownloadRate;

                progress?.Report(new ProgressReport
                {
                    BytesDownloaded = done,
                    TotalBytes = total,
                    BytesPerSecond = bps,
                    Eta = (total > 0 && bps > 0 && done < total)
                        ? TimeSpan.FromSeconds((total - done) / bps) : null,
                    State = DownloadState.Downloading,
                    Seeds = manager.Peers.Seeds,
                    Peers = manager.OpenConnections
                });

                if (manager.Complete)
                {
                    break;
                }
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            await manager.StopAsync().ConfigureAwait(false);
            await _engine.RemoveAsync(manager).ConfigureAwait(false);
        }

        progress?.Report(new ProgressReport
        {
            BytesDownloaded = task.TotalBytes,
            TotalBytes = task.TotalBytes,
            BytesPerSecond = 0,
            Eta = TimeSpan.Zero,
            State = DownloadState.Completed
        });
    }

    private TorrentSettings BuildTorrentSettings() => new TorrentSettingsBuilder
    {
        MaximumConnections = _maxConnectionsPerTorrent,
        MaximumDownloadRate = 0,       // không giới hạn
        AllowDht = true,               // tìm peer qua DHT
        AllowPeerExchange = true,      // PEX: học thêm peer từ peer khác
        UploadSlots = 8               // tit-for-tat: upload đủ để peer ưu tiên gửi cho mình
    }.ToSettings();

    private async Task<TorrentManager> AddAsync(string source, string saveDir)
    {
        var settings = BuildTorrentSettings();

        if (source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            var magnet = MagnetLink.Parse(source);
            return await _engine.AddAsync(magnet, saveDir, settings).ConfigureAwait(false);
        }

        MonoTorrent.Torrent torrent;
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // Tải file .torrent về rồi nạp từ bộ nhớ.
            var bytes = await SharedHttpClient.Instance.GetByteArrayAsync(source).ConfigureAwait(false);
            torrent = MonoTorrent.Torrent.Load(bytes);
        }
        else
        {
            torrent = await MonoTorrent.Torrent.LoadAsync(source).ConfigureAwait(false);
        }
        return await _engine.AddAsync(torrent, saveDir, settings).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try { await _engine.StopAllAsync().ConfigureAwait(false); } catch { /* ignore */ }
        _engine.Dispose();
    }
}
