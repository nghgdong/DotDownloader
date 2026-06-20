using DM.Core.Video;
using DM.Core.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class HlsDownloaderTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "dm-tests", Guid.NewGuid().ToString("N"));

    private static async Task<byte[]> ConcatAsync(IReadOnlyList<string> files)
    {
        using var ms = new MemoryStream();
        foreach (var f in files)
        {
            ms.Write(await File.ReadAllBytesAsync(f));
        }
        return ms.ToArray();
    }

    private static byte[] ConcatPlain(byte[][] segs)
    {
        using var ms = new MemoryStream();
        foreach (var s in segs)
        {
            ms.Write(s);
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task Downloads_Plain_Hls_Segments_In_Order()
    {
        await using var server = await HlsTestServer.StartAsync();
        var hls = new HlsDownloader();

        var files = await hls.DownloadAsync(server.PlainPlaylistUrl, VideoHeaders.Empty,
            Path.Combine(_tempDir, "plain"));

        files.Should().HaveCount(3);
        (await ConcatAsync(files)).Should().Equal(ConcatPlain(server.PlainSegments));
    }

    [Fact]
    public async Task Decrypts_Aes128_Segments_With_Public_Key()
    {
        await using var server = await HlsTestServer.StartAsync();
        var hls = new HlsDownloader();

        var files = await hls.DownloadAsync(server.EncPlaylistUrl, VideoHeaders.Empty,
            Path.Combine(_tempDir, "enc"));

        files.Should().HaveCount(3);
        // Sau giải mã, nội dung phải khớp plaintext gốc.
        (await ConcatAsync(files)).Should().Equal(ConcatPlain(server.PlainSegments));
    }

    [Fact]
    public async Task Resume_Skips_Already_Downloaded_Segments()
    {
        await using var server = await HlsTestServer.StartAsync();
        var hls = new HlsDownloader();
        var dir = Path.Combine(_tempDir, "resume");

        var first = await hls.DownloadAsync(server.PlainPlaylistUrl, VideoHeaders.Empty, dir);
        var sizesBefore = first.Select(f => new FileInfo(f).LastWriteTimeUtc).ToArray();

        // Chạy lại: file đã có → bỏ qua tải, kết quả vẫn đúng.
        var second = await hls.DownloadAsync(server.PlainPlaylistUrl, VideoHeaders.Empty, dir);
        (await ConcatAsync(second)).Should().Equal(ConcatPlain(server.PlainSegments));
        second.Select(f => new FileInfo(f).LastWriteTimeUtc).Should().Equal(sizesBefore);
    }

    [Fact]
    public async Task GetVariants_Returns_Sorted_By_Bandwidth()
    {
        await using var server = await HlsTestServer.StartAsync();
        var hls = new HlsDownloader();

        var variants = await hls.GetVariantsAsync(server.MasterPlaylistUrl, VideoHeaders.Empty);

        variants.Should().HaveCount(2);
        variants[0].Bandwidth.Should().Be(1500000); // cao nhất trước
        variants[1].Bandwidth.Should().Be(500000);
    }

    [Fact]
    public async Task Variant_Selector_Picks_Chosen_Quality()
    {
        await using var server = await HlsTestServer.StartAsync();
        var hls = new HlsDownloader();

        // Chọn variant bitrate thấp (plain.m3u8, không mã hóa).
        var files = await hls.DownloadAsync(server.MasterPlaylistUrl, VideoHeaders.Empty,
            Path.Combine(_tempDir, "variant"),
            selectVariant: vs => vs.OrderBy(v => v.Bandwidth).First());

        files.Should().HaveCount(3);
        (await ConcatAsync(files)).Should().Equal(ConcatPlain(server.PlainSegments));
    }

    [Fact]
    public async Task Aes128_Decryption_RoundTrips()
    {
        // Kiểm tra trực tiếp hàm giải mã với key/iv biết trước.
        var key = new byte[16];
        var iv = HlsDownloader.IvFromSequence(7);
        new Random(1).NextBytes(key);

        var plain = new byte[1000];
        new Random(2).NextBytes(plain);

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Mode = System.Security.Cryptography.CipherMode.CBC;
        aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        var cipher = aes.CreateEncryptor().TransformFinalBlock(plain, 0, plain.Length);

        var decrypted = HlsDownloader.DecryptAes128Cbc(cipher, key, iv);
        decrypted.Should().Equal(plain);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
