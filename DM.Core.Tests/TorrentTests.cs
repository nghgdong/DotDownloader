using DM.Core.Models;
using DM.Core.Queue;
using DM.Core.Torrent;
using FluentAssertions;
using MonoTorrent;
using Xunit;
using Xunit.Abstractions;

namespace DM.Core.Tests;

public class TorrentTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "dm-torrent-test", Guid.NewGuid().ToString("N"));

    public TorrentTests(ITestOutputHelper output)
    {
        _out = output;
        Directory.CreateDirectory(_tempDir);
    }

    private static bool NetEnabled => Environment.GetEnvironmentVariable("DM_NET_TESTS") == "1";

    private sealed class RecordingRunner : IDownloadRunner
    {
        public DownloadTask? Last;
        public Task RunAsync(DownloadTask task, IProgress<ProgressReport>? progress, CancellationToken ct)
        {
            Last = task;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void IsTorrent_Detects_StreamType()
    {
        RoutingDownloadRunner.IsTorrent(new DownloadTask { Url = "magnet:?x", FilePath = "f", StreamType = "Torrent" })
            .Should().BeTrue();
        RoutingDownloadRunner.IsTorrent(new DownloadTask { Url = "http://x", FilePath = "f", StreamType = "HLS" })
            .Should().BeFalse();
        RoutingDownloadRunner.IsTorrent(new DownloadTask { Url = "http://x", FilePath = "f" })
            .Should().BeFalse();
    }

    [Fact]
    public async Task Routing_Sends_NonTorrent_To_Http_Runner()
    {
        var http = new RecordingRunner();
        await using var torrent = new TorrentDownloader(Path.Combine(_tempDir, "cache"), listenPort: 0);
        var router = new RoutingDownloadRunner(http, torrent);

        var task = new DownloadTask { Url = "https://example.com/a.zip", FilePath = "a.zip" };
        await router.RunAsync(task, null, CancellationToken.None);

        http.Last.Should().BeSameAs(task);
    }

    [Fact]
    public void MagnetLink_Parses_Public_Magnet()
    {
        var magnet = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=Ubuntu";
        var link = MagnetLink.Parse(magnet);
        link.Name.Should().Be("Ubuntu");
        link.InfoHashes.Should().NotBeNull();
    }

    [Fact]
    public async Task Downloads_Public_Torrent_Starts_And_Receives_Data()
    {
        if (!NetEnabled)
        {
            _out.WriteLine("BỎ QUA: đặt DM_NET_TESTS=1 để chạy e2e torrent.");
            return;
        }

        // Tải file .torrent công khai (Ubuntu, được seed tốt) rồi bắt đầu tải nội dung.
        const string torrentUrl =
            "https://releases.ubuntu.com/22.04/ubuntu-22.04.5-live-server-amd64.iso.torrent";
        var torrentFile = Path.Combine(_tempDir, "ubuntu.torrent");
        using (var http = new HttpClient())
        {
            await File.WriteAllBytesAsync(torrentFile, await http.GetByteArrayAsync(torrentUrl));
        }

        await using var downloader = new TorrentDownloader(Path.Combine(_tempDir, "cache"), listenPort: 0);
        var task = new DownloadTask { Url = torrentFile, FilePath = Path.Combine(_tempDir, "out", "x") };

        long maxBytes = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var progress = new Progress<ProgressReport>(r =>
        {
            if (r.BytesDownloaded > maxBytes) maxBytes = r.BytesDownloaded;
            if (r.BytesDownloaded > 5_000_000) cts.Cancel(); // đủ chứng minh peers + pieces chảy
        });

        try { await downloader.DownloadAsync(task, progress, cts.Token); }
        catch (OperationCanceledException) { /* dừng sớm theo chủ đích */ }

        _out.WriteLine($"Metadata size = {task.TotalBytes:N0} bytes, đã tải = {maxBytes:N0} bytes");
        task.TotalBytes.Should().BeGreaterThan(0, "đọc được metadata torrent");
        maxBytes.Should().BeGreaterThan(0, "phải nhận được dữ liệu từ peers");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* ignore */ }
    }
}
