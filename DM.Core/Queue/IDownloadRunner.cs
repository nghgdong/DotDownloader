using DM.Core.Models;

namespace DM.Core.Queue;

/// <summary>
/// Trừu tượng "chạy một tải" để <see cref="DownloadQueue"/> không phụ thuộc cứng vào engine
/// (dễ test bằng runner giả, dễ thay engine).
/// </summary>
public interface IDownloadRunner
{
    Task RunAsync(DownloadTask task, IProgress<ProgressReport>? progress, CancellationToken ct);
}
