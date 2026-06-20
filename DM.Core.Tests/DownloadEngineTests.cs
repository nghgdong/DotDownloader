using System.Security.Cryptography;
using DM.Core.Download;
using DM.Core.Models;
using DM.Core.Net;
using DM.Core.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class DownloadEngineTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "dm-tests", Guid.NewGuid().ToString("N"));

    private static byte[] MakePayload(int size)
    {
        var data = new byte[size];
        new Random(1234).NextBytes(data);
        return data;
    }

    [Fact]
    public async Task MultiSegment_Download_Matches_Original_Checksum()
    {
        var data = MakePayload(20 * 1024 * 1024); // 20 MB → nhiều segment, nhiều vòng buffer
        await using var server = await RangeTestServer.StartAsync(data);

        var probe = await new HttpProbe().ProbeAsync(server.RangeUrl);
        probe.SupportsRange.Should().BeTrue();
        probe.TotalBytes.Should().Be(data.Length);

        var dest = Path.Combine(_tempDir, "multi.bin");
        var task = new DownloadTask
        {
            Url = server.RangeUrl,
            FilePath = dest,
            TotalBytes = probe.TotalBytes,
            SupportsRange = probe.SupportsRange
        };

        await new DownloadEngine().DownloadAsync(task, segmentCount: 8);

        task.State.Should().Be(DownloadState.Completed);
        task.Segments.Should().HaveCount(8);
        task.DownloadedBytes.Should().Be(data.Length);

        var written = await File.ReadAllBytesAsync(dest);
        SHA256.HashData(written).Should().Equal(SHA256.HashData(data));
    }

    [Fact]
    public async Task MultiSegment_Matches_SingleStream_Download()
    {
        var data = MakePayload(8 * 1024 * 1024);
        await using var server = await RangeTestServer.StartAsync(data);

        // Bản đơn luồng (tham chiếu)
        var singleDest = Path.Combine(_tempDir, "single.bin");
        await new SingleStreamDownloader().DownloadAsync(server.RangeUrl, singleDest, data.Length);

        // Bản đa luồng
        var multiDest = Path.Combine(_tempDir, "multi2.bin");
        var task = new DownloadTask
        {
            Url = server.RangeUrl,
            FilePath = multiDest,
            TotalBytes = data.Length,
            SupportsRange = true
        };
        await new DownloadEngine().DownloadAsync(task, segmentCount: 8);

        var single = SHA256.HashData(await File.ReadAllBytesAsync(singleDest));
        var multi = SHA256.HashData(await File.ReadAllBytesAsync(multiDest));
        multi.Should().Equal(single);
    }

    [Fact]
    public async Task NoRange_Server_Falls_Back_To_SingleStream()
    {
        var data = MakePayload(2 * 1024 * 1024);
        await using var server = await RangeTestServer.StartAsync(data);

        var probe = await new HttpProbe().ProbeAsync(server.NoRangeUrl);
        probe.SupportsRange.Should().BeFalse();

        var dest = Path.Combine(_tempDir, "fallback.bin");
        var task = new DownloadTask
        {
            Url = server.NoRangeUrl,
            FilePath = dest,
            TotalBytes = probe.TotalBytes,
            SupportsRange = probe.SupportsRange
        };

        await new DownloadEngine().DownloadAsync(task, segmentCount: 8);

        task.State.Should().Be(DownloadState.Completed);
        task.Segments.Should().BeEmpty(); // fallback đơn luồng → không chia segment
        SHA256.HashData(await File.ReadAllBytesAsync(dest)).Should().Equal(SHA256.HashData(data));
    }

    [Fact]
    public void BuildSegments_Splits_Evenly_With_Remainder_On_Last()
    {
        var task = new DownloadTask { Url = "http://x", FilePath = "x.bin", TotalBytes = 1000 };

        DownloadEngine.BuildSegments(task, 3);

        task.Segments.Should().HaveCount(3);
        task.Segments[0].Start.Should().Be(0);
        task.Segments[0].End.Should().Be(332);   // chunk = 333
        task.Segments[1].Start.Should().Be(333);
        task.Segments[2].End.Should().Be(999);    // segment cuối ôm phần dư
        task.Segments.Sum(s => s.Length).Should().Be(1000);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
