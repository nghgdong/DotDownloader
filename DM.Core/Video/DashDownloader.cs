using DM.Core.Download;
using DM.Core.Models;
using DM.Core.Net;

namespace DM.Core.Video;

/// <summary>
/// Tải DASH: chọn track video + audio (qua <see cref="MpdParser"/>), tải init + segment
/// từng track vào thư mục tạm rồi nối byte thành file fMP4 mỗi track. Việc mux video+audio
/// do <see cref="FfmpegMuxer"/> đảm nhiệm.
/// </summary>
public sealed class DashDownloader
{
    private readonly HttpClient _http;
    private readonly RetryPolicy _retry;

    public DashDownloader(HttpClient? http = null, RetryPolicy? retry = null)
    {
        _http = http ?? SharedHttpClient.Instance;
        _retry = retry ?? new RetryPolicy();
    }

    /// <returns>(đường dẫn track video, đường dẫn track audio) — có thể null nếu thiếu track.</returns>
    public async Task<(string? VideoFile, string? AudioFile)> DownloadAsync(
        string mpdUrl,
        VideoHeaders headers,
        string tempDir,
        IProgress<ProgressReport>? progress = null,
        int maxConcurrency = 6,
        CancellationToken ct = default)
    {
        var (text, finalUri) = await GetTextAsync(mpdUrl, headers, ct).ConfigureAwait(false);
        var manifest = MpdParser.Parse(text, finalUri);

        if (manifest.Video is null && manifest.Audio is null)
        {
            throw new InvalidOperationException("MPD không tìm được track video/audio hỗ trợ.");
        }

        Directory.CreateDirectory(tempDir);

        int total = (manifest.Video?.SegmentUrls.Count ?? 0) + (manifest.Audio?.SegmentUrls.Count ?? 0);
        int done = 0;
        void Tick()
        {
            int d = Interlocked.Increment(ref done);
            progress?.Report(new ProgressReport
            {
                BytesDownloaded = d,
                TotalBytes = total,
                BytesPerSecond = 0,
                State = DownloadState.Downloading
            });
        }

        string? videoFile = manifest.Video is null
            ? null
            : await DownloadTrackAsync(manifest.Video, headers, tempDir, "video.mp4", maxConcurrency, Tick, ct)
                .ConfigureAwait(false);

        string? audioFile = manifest.Audio is null
            ? null
            : await DownloadTrackAsync(manifest.Audio, headers, tempDir, "audio.mp4", maxConcurrency, Tick, ct)
                .ConfigureAwait(false);

        return (videoFile, audioFile);
    }

    private async Task<string> DownloadTrackAsync(
        DashRepresentation rep, VideoHeaders headers, string tempDir, string fileName,
        int maxConcurrency, Action onSegment, CancellationToken ct)
    {
        var partDir = Path.Combine(tempDir, rep.Id + "_" + fileName);
        Directory.CreateDirectory(partDir);

        var segFiles = new string[rep.SegmentUrls.Count];
        using var sem = new SemaphoreSlim(maxConcurrency);

        // init segment
        string? initFile = null;
        if (rep.InitUrl is not null)
        {
            initFile = Path.Combine(partDir, "init.m4s");
            await DownloadToFileIfMissingAsync(rep.InitUrl, headers, initFile, ct).ConfigureAwait(false);
        }

        var jobs = rep.SegmentUrls.Select(async (url, index) =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var outPath = Path.Combine(partDir, $"seg{index:D5}.m4s");
                segFiles[index] = outPath;
                await DownloadToFileIfMissingAsync(url, headers, outPath, ct).ConfigureAwait(false);
                onSegment();
            }
            finally
            {
                sem.Release();
            }
        });
        await Task.WhenAll(jobs).ConfigureAwait(false);

        // Nối byte init + segment thành 1 file fMP4 cho track.
        var trackFile = Path.Combine(tempDir, fileName);
        await using (var outFs = new FileStream(trackFile, FileMode.Create, FileAccess.Write, FileShare.None,
                         81920, useAsync: true))
        {
            if (initFile is not null)
            {
                await AppendAsync(initFile, outFs, ct).ConfigureAwait(false);
            }
            foreach (var f in segFiles)
            {
                await AppendAsync(f, outFs, ct).ConfigureAwait(false);
            }
        }
        return trackFile;
    }

    private static async Task AppendAsync(string file, Stream dest, CancellationToken ct)
    {
        await using var inFs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await inFs.CopyToAsync(dest, ct).ConfigureAwait(false);
    }

    private async Task DownloadToFileIfMissingAsync(
        string url, VideoHeaders headers, string outPath, CancellationToken ct)
    {
        if (File.Exists(outPath) && new FileInfo(outPath).Length > 0)
        {
            return; // resume
        }
        await _retry.ExecuteAsync(async c =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            headers.ApplyTo(request);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, c)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadAsByteArrayAsync(c).ConfigureAwait(false);
            var part = outPath + ".part";
            await File.WriteAllBytesAsync(part, data, c).ConfigureAwait(false);
            File.Move(part, outPath, overwrite: true);
        }, ct).ConfigureAwait(false);
    }

    private async Task<(string Text, Uri FinalUri)> GetTextAsync(string url, VideoHeaders headers, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        headers.ApplyTo(request);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return (text, response.RequestMessage?.RequestUri ?? new Uri(url));
    }
}
