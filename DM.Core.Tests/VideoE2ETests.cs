using DM.Core.Video;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace DM.Core.Tests;

/// <summary>
/// E2E tải HLS/DASH công khai → mp4 bằng FFmpeg thật. Chỉ chạy khi env DM_NET_TESTS=1
/// và FFmpeg gọi được (cần mạng). Mặc định bỏ qua để suite offline luôn xanh.
/// </summary>
public class VideoE2ETests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "dm-e2e", Guid.NewGuid().ToString("N"));

    public VideoE2ETests(ITestOutputHelper output)
    {
        _out = output;
        Directory.CreateDirectory(_tempDir);
    }

    private static bool Enabled => Environment.GetEnvironmentVariable("DM_NET_TESTS") == "1";

    private async Task<bool> PreflightAsync()
    {
        if (!Enabled)
        {
            _out.WriteLine("BỎ QUA: đặt DM_NET_TESTS=1 để chạy e2e mạng.");
            return false;
        }
        if (!await new Ffmpeg().IsAvailableAsync())
        {
            _out.WriteLine("BỎ QUA: không tìm thấy FFmpeg.");
            return false;
        }
        return true;
    }

    private async Task RunHls(string url)
    {
        var output = Path.Combine(_tempDir, "out.mp4");
        var engine = new VideoDownloadEngine();
        await engine.DownloadHlsAsync(url, VideoHeaders.Empty, output);

        var info = new FileInfo(output);
        info.Exists.Should().BeTrue();
        info.Length.Should().BeGreaterThan(100_000);
        _out.WriteLine($"PASS HLS {url} → {info.Length:N0} bytes");
    }

    [Fact]
    public async Task Hls_Public_Mux_Stream()
    {
        if (!await PreflightAsync()) return;
        // Mux test stream (master playlist, TS segment, KHÔNG mã hóa).
        await RunHls("https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8");
    }

    [Fact]
    public async Task Hls_Public_Aes128_Stream()
    {
        if (!await PreflightAsync()) return;
        // JW Player AES-128 sample (key công khai trong playlist).
        await RunHls("https://playertest.longtailvideo.com/adaptive/oceans_aes/oceans_aes.m3u8");
    }

    [Fact]
    public async Task Dash_Public_Stream()
    {
        if (!await PreflightAsync()) return;
        var url = "https://dash.akamaized.net/akamai/bbb_30fps/bbb_30fps.mpd";
        var output = Path.Combine(_tempDir, "dash.mp4");
        var engine = new VideoDownloadEngine();
        await engine.DownloadDashAsync(url, VideoHeaders.Empty, output);

        var info = new FileInfo(output);
        info.Exists.Should().BeTrue();
        info.Length.Should().BeGreaterThan(100_000);
        _out.WriteLine($"PASS DASH {url} → {info.Length:N0} bytes");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* ignore */ }
    }
}
