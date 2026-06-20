using DM.Core.Download;
using DM.Core.Models;

namespace DM.Core.Video;

/// <summary>
/// Điều phối tải video stream: HLS → tải+giải mã segment rồi ghép mp4; DASH → tải track
/// video+audio rồi mux. Dùng <see cref="HlsDownloader"/>/<see cref="DashDownloader"/> +
/// <see cref="FfmpegMuxer"/>.
/// </summary>
public sealed class VideoDownloadEngine
{
    private readonly HlsDownloader _hls;
    private readonly DashDownloader _dash;
    private readonly FfmpegMuxer _muxer;

    public VideoDownloadEngine(HttpClient? http = null, Ffmpeg? ffmpeg = null, RetryPolicy? retry = null)
    {
        _hls = new HlsDownloader(http, retry);
        _dash = new DashDownloader(http, retry);
        _muxer = new FfmpegMuxer(ffmpeg);
    }

    public async Task DownloadHlsAsync(
        string playlistUrl, VideoHeaders headers, string outputPath,
        IProgress<ProgressReport>? progress = null,
        Func<IReadOnlyList<HlsVariant>, HlsVariant>? selectVariant = null,
        CancellationToken ct = default)
    {
        var partsDir = outputPath + ".parts";
        try
        {
            var files = await _hls.DownloadAsync(playlistUrl, headers, partsDir, progress,
                    selectVariant: selectVariant, ct: ct)
                .ConfigureAwait(false);
            await _muxer.ConcatToMp4Async(files, outputPath, deleteSegments: true, ct).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDir(partsDir);
        }
    }

    public async Task DownloadDashAsync(
        string mpdUrl, VideoHeaders headers, string outputPath,
        IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
    {
        var partsDir = outputPath + ".parts";
        try
        {
            var (videoFile, audioFile) = await _dash.DownloadAsync(mpdUrl, headers, partsDir, progress, ct: ct)
                .ConfigureAwait(false);

            if (videoFile is not null && audioFile is not null)
            {
                await _muxer.MuxVideoAudioAsync(videoFile, audioFile, outputPath, deleteInputs: true, ct)
                    .ConfigureAwait(false);
            }
            else if (videoFile is not null)
            {
                await _muxer.ConcatToMp4Async(new[] { videoFile }, outputPath, deleteSegments: true, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException("DASH: không có track video để ghép.");
            }
        }
        finally
        {
            TryDeleteDir(partsDir);
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // dọn rác — không chặn
        }
    }
}
