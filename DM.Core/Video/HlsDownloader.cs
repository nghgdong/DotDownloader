using System.Collections.Concurrent;
using System.Security.Cryptography;
using DM.Core.Download;
using DM.Core.Models;
using DM.Core.Net;

namespace DM.Core.Video;

/// <summary>
/// Tải toàn bộ segment của một HLS stream vào thư mục tạm (đa luồng có giới hạn), giải mã
/// AES-128 nếu playlist khai báo key CÔNG KHAI, resume theo segment đã tải. Trả về danh sách
/// file segment (đã giải mã, theo đúng thứ tự) để muxer ghép.
/// </summary>
public sealed class HlsDownloader
{
    private readonly HttpClient _http;
    private readonly RetryPolicy _retry;

    public HlsDownloader(HttpClient? http = null, RetryPolicy? retry = null)
    {
        _http = http ?? SharedHttpClient.Instance;
        _retry = retry ?? new RetryPolicy();
    }

    /// <summary>Lấy danh sách variant của master playlist (rỗng nếu là media playlist).</summary>
    public async Task<IReadOnlyList<HlsVariant>> GetVariantsAsync(
        string masterUrl, VideoHeaders headers, CancellationToken ct = default)
    {
        var (text, finalUri) = await GetTextAsync(masterUrl, headers, ct).ConfigureAwait(false);
        var playlist = M3u8Parser.Parse(text, finalUri);
        return playlist.IsMaster
            ? playlist.Variants.OrderByDescending(v => v.Bandwidth).ToList()
            : Array.Empty<HlsVariant>();
    }

    /// <param name="maxConcurrency">Số segment tải song song.</param>
    /// <param name="selectVariant">Chọn variant từ master playlist; null → chất lượng cao nhất.</param>
    /// <returns>Danh sách file segment theo thứ tự (gồm init segment fMP4 nếu có ở đầu).</returns>
    public async Task<IReadOnlyList<string>> DownloadAsync(
        string playlistUrl,
        VideoHeaders headers,
        string tempDir,
        IProgress<ProgressReport>? progress = null,
        int maxConcurrency = 6,
        Func<IReadOnlyList<HlsVariant>, HlsVariant>? selectVariant = null,
        CancellationToken ct = default)
    {
        var (text, finalUri) = await GetTextAsync(playlistUrl, headers, ct).ConfigureAwait(false);
        var playlist = M3u8Parser.Parse(text, finalUri);

        if (playlist.IsMaster)
        {
            if (playlist.Variants.Count == 0)
            {
                throw new InvalidOperationException("Master playlist không có variant nào.");
            }
            var sorted = playlist.Variants.OrderByDescending(v => v.Bandwidth).ToList();
            var chosen = selectVariant is not null ? selectVariant(sorted) : sorted[0];
            (text, finalUri) = await GetTextAsync(chosen.Uri, headers, ct).ConfigureAwait(false);
            playlist = M3u8Parser.Parse(text, finalUri);
        }

        if (playlist.Segments.Count == 0)
        {
            throw new InvalidOperationException("Media playlist không có segment nào.");
        }

        Directory.CreateDirectory(tempDir);

        var orderedFiles = new List<string>();
        int offset = 0;

        // fMP4: init segment phải đứng đầu.
        if (playlist.InitSegmentUri is not null)
        {
            var initPath = Path.Combine(tempDir, "init.mp4");
            await DownloadRawIfMissingAsync(playlist.InitSegmentUri, headers, initPath, ct).ConfigureAwait(false);
            orderedFiles.Add(initPath);
            offset = 1;
        }

        int total = playlist.Segments.Count;
        var segFiles = new string[total];
        int done = 0;
        var keyCache = new ConcurrentDictionary<string, byte[]>();
        using var sem = new SemaphoreSlim(maxConcurrency);

        var jobs = playlist.Segments.Select(async (seg, index) =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var outPath = Path.Combine(tempDir, $"seg{index + offset:D5}.ts");
                segFiles[index] = outPath;

                if (!(File.Exists(outPath) && new FileInfo(outPath).Length > 0)) // resume: bỏ qua nếu đã tải
                {
                    await _retry.ExecuteAsync(
                        c => DownloadSegmentAsync(seg, headers, outPath, keyCache, c), ct).ConfigureAwait(false);
                }

                int d = Interlocked.Increment(ref done);
                progress?.Report(new ProgressReport
                {
                    BytesDownloaded = d, // semantics video: số segment đã xong
                    TotalBytes = total,
                    BytesPerSecond = 0,
                    State = DownloadState.Downloading
                });
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(jobs).ConfigureAwait(false);

        orderedFiles.AddRange(segFiles);
        return orderedFiles;
    }

    private async Task DownloadSegmentAsync(
        HlsSegment seg, VideoHeaders headers, string outPath,
        ConcurrentDictionary<string, byte[]> keyCache, CancellationToken ct)
    {
        var data = await GetBytesAsync(seg.Uri, headers, ct).ConfigureAwait(false);

        if (seg.Key is { IsEncrypted: true } key)
        {
            if (!key.IsAes128 || key.Uri is null)
            {
                throw new NotSupportedException(
                    $"Phương thức mã hóa '{key.Method}' không hỗ trợ (có thể là DRM — ngoài scope).");
            }
            var keyBytes = await GetKeyAsync(key.Uri, headers, keyCache, ct).ConfigureAwait(false);
            var iv = key.Iv ?? IvFromSequence(seg.MediaSequence);
            data = DecryptAes128Cbc(data, keyBytes, iv);
        }

        var partPath = outPath + ".part";
        await File.WriteAllBytesAsync(partPath, data, ct).ConfigureAwait(false);
        File.Move(partPath, outPath, overwrite: true);
    }

    private async Task DownloadRawIfMissingAsync(
        string url, VideoHeaders headers, string outPath, CancellationToken ct)
    {
        if (File.Exists(outPath) && new FileInfo(outPath).Length > 0)
        {
            return;
        }
        await _retry.ExecuteAsync(async c =>
        {
            var data = await GetBytesAsync(url, headers, c).ConfigureAwait(false);
            var partPath = outPath + ".part";
            await File.WriteAllBytesAsync(partPath, data, c).ConfigureAwait(false);
            File.Move(partPath, outPath, overwrite: true);
        }, ct).ConfigureAwait(false);
    }

    private async Task<byte[]> GetKeyAsync(
        string keyUri, VideoHeaders headers, ConcurrentDictionary<string, byte[]> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(keyUri, out var cached))
        {
            return cached;
        }
        var bytes = await GetBytesAsync(keyUri, headers, ct).ConfigureAwait(false);
        cache[keyUri] = bytes;
        return bytes;
    }

    private async Task<(string Text, Uri FinalUri)> GetTextAsync(
        string url, VideoHeaders headers, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        headers.ApplyTo(request);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var finalUri = response.RequestMessage?.RequestUri ?? new Uri(url);
        return (text, finalUri);
    }

    private async Task<byte[]> GetBytesAsync(string url, VideoHeaders headers, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        headers.ApplyTo(request);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Giải mã AES-128-CBC (PKCS7) — chỉ dùng cho key công khai trong playlist.</summary>
    internal static byte[] DecryptAes128Cbc(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }

    /// <summary>IV mặc định khi #EXT-X-KEY không khai IV = số thứ tự segment (big-endian 16 byte).</summary>
    internal static byte[] IvFromSequence(long sequence)
    {
        var iv = new byte[16];
        var b = BitConverter.GetBytes((ulong)sequence);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(b);
        }
        Array.Copy(b, 0, iv, 8, 8);
        return iv;
    }
}
