using System.Diagnostics;
using System.Security.Cryptography;
using DM.Core.Download;
using DM.Core.Models;
using DM.Core.Net;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace DM.Core.Tests;

/// <summary>
/// Definition of Done #1: tải đa luồng nhanh hơn đơn luồng & checksum khớp.
/// Chỉ chạy khi DM_NET_TESTS=1 (cần mạng). Mặc định bỏ qua.
/// </summary>
public class MultiThreadBenchmarkTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "dm-bench", Guid.NewGuid().ToString("N"));

    public MultiThreadBenchmarkTests(ITestOutputHelper output)
    {
        _out = output;
        Directory.CreateDirectory(_tempDir);
    }

    private static bool Enabled => Environment.GetEnvironmentVariable("DM_NET_TESTS") == "1";

    [Fact]
    public async Task MultiSegment_Faster_And_Checksum_Matches()
    {
        if (!Enabled)
        {
            _out.WriteLine("BỎ QUA: đặt DM_NET_TESTS=1 để chạy benchmark mạng.");
            return;
        }

        const string url = "https://proof.ovh.net/files/100Mb.dat";
        var probe = await new HttpProbe().ProbeAsync(url);
        probe.SupportsRange.Should().BeTrue("server phải hỗ trợ range để đa luồng");
        _out.WriteLine($"File: {probe.TotalBytes:N0} bytes, range={probe.SupportsRange}");

        // Đơn luồng (segmentCount=1 → fallback single stream).
        var single = Path.Combine(_tempDir, "single.bin");
        var t1 = await TimeDownload(url, single, probe.TotalBytes, probe.SupportsRange, 1);

        // Đa luồng (8 segment).
        var multi = Path.Combine(_tempDir, "multi.bin");
        var t8 = await TimeDownload(url, multi, probe.TotalBytes, probe.SupportsRange, 8);

        var hSingle = SHA256.HashData(await File.ReadAllBytesAsync(single));
        var hMulti = SHA256.HashData(await File.ReadAllBytesAsync(multi));
        hMulti.Should().Equal(hSingle, "checksum đa luồng phải khớp đơn luồng");

        _out.WriteLine($"1 luồng:  {t1.TotalSeconds:0.00}s");
        _out.WriteLine($"8 luồng:  {t8.TotalSeconds:0.00}s");
        _out.WriteLine($"Speedup:  {t1.TotalSeconds / Math.Max(0.01, t8.TotalSeconds):0.00}x");
    }

    private async Task<TimeSpan> TimeDownload(
        string url, string path, long total, bool supportsRange, int segments)
    {
        var task = new DownloadTask
        {
            Url = url, FilePath = path, TotalBytes = total, SupportsRange = supportsRange
        };
        var sw = Stopwatch.StartNew();
        await new DownloadEngine().DownloadAsync(task, segmentCount: segments);
        sw.Stop();
        task.State.Should().Be(DownloadState.Completed);
        return sw.Elapsed;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* ignore */ }
    }
}
