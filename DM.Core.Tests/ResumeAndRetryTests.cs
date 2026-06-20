using System.Net;
using System.Security.Cryptography;
using DM.Core.Download;
using DM.Core.Models;
using DM.Core.Persistence;
using DM.Core.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class ResumeAndRetryTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "dm-tests", Guid.NewGuid().ToString("N"));

    public ResumeAndRetryTests() => Directory.CreateDirectory(_tempDir);

    private static byte[] MakePayload(int size)
    {
        var data = new byte[size];
        new Random(777).NextBytes(data);
        return data;
    }

    [Fact]
    public async Task Cancel_Midway_Then_Resume_With_New_Engine_Matches_Checksum()
    {
        var data = MakePayload(8 * 1024 * 1024);
        await using var server = await RangeTestServer.StartAsync(data);
        var dest = Path.Combine(_tempDir, "resume.bin");

        // --- Phiên 1: tải nửa chừng rồi hủy token cứng ---
        var task1 = new DownloadTask
        {
            Url = server.SlowUrl,
            FilePath = dest,
            TotalBytes = data.Length,
            SupportsRange = true
        };
        using var cts = new CancellationTokenSource();
        var progress = new Progress<ProgressReport>(r =>
        {
            // Hủy sớm (1/4) để chắc chắn dừng giữa chừng trước khi tải xong.
            if (r.BytesDownloaded >= data.Length / 4)
            {
                cts.Cancel();
            }
        });

        var engine1 = new DownloadEngine(retry: RetryPolicy.NoDelay());
        var run1 = async () => await engine1.DownloadAsync(task1, segmentCount: 8, progress, cts.Token);

        await run1.Should().ThrowAsync<OperationCanceledException>();
        task1.State.Should().Be(DownloadState.Paused);
        task1.DownloadedBytes.Should().BeGreaterThan(0).And.BeLessThan(data.Length);
        File.Exists(MetadataStore.MetaPath(dest)).Should().BeTrue("metadata phải còn để resume");

        // --- Phiên 2: engine + task mới, cùng URL/size → resume từ .dmmeta ---
        var task2 = new DownloadTask
        {
            Url = server.SlowUrl,
            FilePath = dest,
            TotalBytes = data.Length,
            SupportsRange = true
        };
        var engine2 = new DownloadEngine(retry: RetryPolicy.NoDelay());
        await engine2.DownloadAsync(task2, segmentCount: 8);

        task2.State.Should().Be(DownloadState.Completed);
        task2.DownloadedBytes.Should().Be(data.Length);
        File.Exists(MetadataStore.MetaPath(dest)).Should().BeFalse("hoàn tất thì xóa .dmmeta");

        var written = await File.ReadAllBytesAsync(dest);
        SHA256.HashData(written).Should().Equal(SHA256.HashData(data));
    }

    [Fact]
    public async Task Resume_Restores_Segment_Progress_From_Metadata()
    {
        var data = MakePayload(2 * 1024 * 1024);
        await using var server = await RangeTestServer.StartAsync(data);
        var dest = Path.Combine(_tempDir, "resume2.bin");

        // Giả lập một phiên trước: viết .dmmeta cho biết segment 0 đã tải xong một phần.
        var store = new MetadataStore();
        var task = new DownloadTask
        {
            Url = server.RangeUrl,
            FilePath = dest,
            TotalBytes = data.Length,
            SupportsRange = true
        };
        DownloadEngine.BuildSegments(task, 4);
        task.Segments[0].Downloaded = 1000; // đã có 1000 byte ở segment đầu

        // Ghi sẵn 1000 byte đầu lên file (đúng nội dung) để resume nối tiếp.
        Directory.CreateDirectory(_tempDir);
        using (var h = File.OpenHandle(dest, FileMode.Create, FileAccess.Write))
        {
            RandomAccess.SetLength(h, data.Length);
            RandomAccess.Write(h, data.AsSpan(0, 1000), 0);
        }
        await store.SaveAsync(dest, DownloadMetadata.FromTask(task));

        // Resume: engine mới, task mới → phải nạp lại segment progress và tải nốt.
        var task2 = new DownloadTask
        {
            Url = server.RangeUrl,
            FilePath = dest,
            TotalBytes = data.Length,
            SupportsRange = true
        };
        await new DownloadEngine().DownloadAsync(task2, segmentCount: 4);

        task2.State.Should().Be(DownloadState.Completed);
        SHA256.HashData(await File.ReadAllBytesAsync(dest)).Should().Equal(SHA256.HashData(data));
    }

    [Fact]
    public async Task Mismatched_Metadata_Restarts_And_Warns()
    {
        var data = MakePayload(512 * 1024);
        await using var server = await RangeTestServer.StartAsync(data);
        var dest = Path.Combine(_tempDir, "mismatch.bin");

        // .dmmeta cũ khai TotalBytes khác (server đã đổi file).
        var store = new MetadataStore();
        await store.SaveAsync(dest, new DownloadMetadata
        {
            Url = server.RangeUrl,
            TotalBytes = 999_999, // khác data.Length
            SupportsRange = true,
            Segments = new() { new SegmentMetadata { Start = 0, End = 999_998, Downloaded = 500 } }
        });

        var warnings = new List<string>();
        var progress = new Progress<ProgressReport>(r =>
        {
            if (r.Warning is not null)
            {
                warnings.Add(r.Warning);
            }
        });

        var task = new DownloadTask
        {
            Url = server.RangeUrl,
            FilePath = dest,
            TotalBytes = data.Length,
            SupportsRange = true
        };
        await new DownloadEngine().DownloadAsync(task, segmentCount: 4, progress);

        await Task.Delay(50); // cho callback progress chạy hết
        task.State.Should().Be(DownloadState.Completed);
        warnings.Should().NotBeEmpty("metadata không khớp phải cảnh báo");
        SHA256.HashData(await File.ReadAllBytesAsync(dest)).Should().Equal(SHA256.HashData(data));
    }

    [Fact]
    public async Task Segment_Recovers_From_Transient_Failures_Via_Retry()
    {
        var data = MakePayload(100_000);
        var dest = Path.Combine(_tempDir, "retry.bin");

        int calls = 0;
        var handler = new MockHttpMessageHandler(_ =>
        {
            calls++;
            if (calls <= 2)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("transient error")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(data)
            };
        });

        var downloader = new SegmentDownloader(new HttpClient(handler));
        var segment = new Segment { Start = 0, End = data.Length - 1 };

        using (var handle = File.OpenHandle(dest, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            RandomAccess.SetLength(handle, data.Length);
            await RetryPolicy.NoDelay().ExecuteAsync(
                c => downloader.DownloadAsync(handle, segment, "http://x/file", null, c));
        }

        calls.Should().Be(3); // fail 2 lần đầu, lần 3 thành công
        segment.Downloaded.Should().Be(data.Length);
        SHA256.HashData(await File.ReadAllBytesAsync(dest)).Should().Equal(SHA256.HashData(data));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
