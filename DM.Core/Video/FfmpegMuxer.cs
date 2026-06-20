namespace DM.Core.Video;

/// <summary>
/// Ghép segment thành mp4 bằng FFmpeg <c>-c copy</c> (không re-encode). Với HLS: nối byte
/// các segment (TS/fMP4) thành 1 file rồi remux. Với DASH: mux track video + audio.
/// </summary>
public sealed class FfmpegMuxer
{
    private readonly Ffmpeg _ffmpeg;

    public FfmpegMuxer(Ffmpeg? ffmpeg = null) => _ffmpeg = ffmpeg ?? new Ffmpeg();

    /// <summary>Nối các file segment (theo thứ tự) thành <paramref name="outputPath"/>.mp4.</summary>
    public async Task ConcatToMp4Async(
        IReadOnlyList<string> segmentFiles,
        string outputPath,
        bool deleteSegments = true,
        CancellationToken ct = default)
    {
        if (segmentFiles.Count == 0)
        {
            throw new InvalidOperationException("Không có segment để ghép.");
        }

        EnsureDirectory(outputPath);

        // Nối byte các segment thành 1 file trung gian (TS/fMP4 nối byte hợp lệ để FFmpeg đọc).
        var combined = outputPath + ".concat.bin";
        await using (var outFs = new FileStream(combined, FileMode.Create, FileAccess.Write, FileShare.None,
                         81920, useAsync: true))
        {
            foreach (var f in segmentFiles)
            {
                await using var inFs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, useAsync: true);
                await inFs.CopyToAsync(outFs, ct).ConfigureAwait(false);
            }
        }

        try
        {
            await _ffmpeg.RunAsync(
                new[] { "-y", "-i", combined, "-c", "copy", "-movflags", "+faststart", outputPath }, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDelete(combined);
        }

        if (deleteSegments)
        {
            foreach (var f in segmentFiles)
            {
                TryDelete(f);
            }
        }
    }

    /// <summary>Mux một track video và một track audio (đã nối) thành mp4 đồng bộ.</summary>
    public async Task MuxVideoAudioAsync(
        string videoFile, string audioFile, string outputPath, bool deleteInputs = true,
        CancellationToken ct = default)
    {
        EnsureDirectory(outputPath);
        await _ffmpeg.RunAsync(new[]
        {
            "-y", "-i", videoFile, "-i", audioFile,
            "-c", "copy", "-map", "0:v:0", "-map", "1:a:0",
            "-movflags", "+faststart", outputPath
        }, ct).ConfigureAwait(false);

        if (deleteInputs)
        {
            TryDelete(videoFile);
            TryDelete(audioFile);
        }
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // không chặn luồng vì lỗi dọn rác
        }
    }
}
