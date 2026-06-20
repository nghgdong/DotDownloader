using System.Diagnostics;

namespace DM.Core.Video;

public sealed class FfmpegException : Exception
{
    public int ExitCode { get; }
    public string StdErrTail { get; }

    public FfmpegException(int exitCode, string stdErrTail)
        : base($"FFmpeg thoát mã {exitCode}. stderr: {stdErrTail}")
    {
        ExitCode = exitCode;
        StdErrTail = stdErrTail;
    }
}

/// <summary>
/// Wrapper gọi FFmpeg qua <see cref="Process"/>. Resolve binary theo thứ tự:
/// đường dẫn truyền vào → env <c>DM_FFMPEG</c> → <c>tools/ffmpeg/</c> cạnh app → PATH.
/// Mục tiêu cuối (Phase 8) là bundle binary trong <c>tools/ffmpeg/</c>, KHÔNG phụ thuộc máy.
/// </summary>
public sealed class Ffmpeg
{
    public const string EnvVar = "DM_FFMPEG";

    private readonly string _path;

    public Ffmpeg(string? explicitPath = null) => _path = ResolvePath(explicitPath);

    public string Path => _path;

    public static string ResolvePath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        var fromEnv = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var bundled = System.IO.Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", exe);
        if (File.Exists(bundled))
        {
            return bundled;
        }

        // Fallback dev: dựa vào PATH (Process sẽ phân giải). Phase 8 sẽ bundle để bỏ fallback này.
        return "ffmpeg";
    }

    /// <summary>Kiểm tra FFmpeg gọi được (chạy <c>-version</c>).</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            await RunAsync(new[] { "-version" }, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Chạy FFmpeg với tham số cho sẵn. Ném <see cref="FfmpegException"/> nếu exit ≠ 0.</summary>
    public async Task<string> RunAsync(IEnumerable<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _path,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // -hide_banner -loglevel error: giảm nhiễu stderr.
        psi.ArgumentList.Add("-hide_banner");
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var stderr = await stderrTask.ConfigureAwait(false);
        await stdoutTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var tail = stderr.Length > 800 ? stderr[^800..] : stderr;
            throw new FfmpegException(process.ExitCode, tail);
        }
        return stderr;
    }
}
