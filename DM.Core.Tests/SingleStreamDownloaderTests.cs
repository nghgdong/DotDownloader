using System.Net;
using System.Security.Cryptography;
using DM.Core.Download;
using DM.Core.Models;
using DM.Core.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class SingleStreamDownloaderTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "dm-tests", Guid.NewGuid().ToString("N"));

    private static byte[] MakePayload(int size)
    {
        var data = new byte[size];
        new Random(42).NextBytes(data);
        return data;
    }

    private static SingleStreamDownloader DownloaderReturning(byte[] payload)
    {
        var handler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        return new SingleStreamDownloader(new HttpClient(handler));
    }

    [Fact]
    public async Task Download_Writes_Exact_Bytes_To_Disk()
    {
        var payload = MakePayload(250_000); // > buffer (81920) → nhiều vòng đọc
        var downloader = DownloaderReturning(payload);
        var dest = Path.Combine(_tempDir, "out.bin");

        await downloader.DownloadAsync("https://example.com/file.bin", dest, payload.Length);

        File.Exists(dest).Should().BeTrue();
        var written = await File.ReadAllBytesAsync(dest);
        written.Length.Should().Be(payload.Length);
        SHA256.HashData(written).Should().Equal(SHA256.HashData(payload));
    }

    [Fact]
    public async Task Download_Reports_Completion_With_Full_Bytes()
    {
        var payload = MakePayload(120_000);
        var downloader = DownloaderReturning(payload);
        var dest = Path.Combine(_tempDir, "out2.bin");

        var reports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(r => reports.Add(r));

        await downloader.DownloadAsync("https://example.com/file.bin", dest, payload.Length, progress);

        // Progress là async (SynchronizationContext) → chờ một nhịp để callback chạy hết.
        await Task.Delay(50);

        reports.Should().NotBeEmpty();
        reports[^1].State.Should().Be(DownloadState.Completed);
        reports[^1].BytesDownloaded.Should().Be(payload.Length);
    }

    [Fact]
    public async Task Download_Works_When_Size_Unknown()
    {
        var payload = MakePayload(5_000);
        var downloader = DownloaderReturning(payload);
        var dest = Path.Combine(_tempDir, "out3.bin");

        await downloader.DownloadAsync("https://example.com/stream", dest, totalBytes: -1);

        (await File.ReadAllBytesAsync(dest)).Length.Should().Be(payload.Length);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
