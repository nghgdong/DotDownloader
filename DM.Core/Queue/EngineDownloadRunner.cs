using DM.Core.Download;
using DM.Core.Models;

namespace DM.Core.Queue;

/// <summary>Adapter chạy <see cref="DownloadEngine"/> qua <see cref="IDownloadRunner"/> cho queue.</summary>
public sealed class EngineDownloadRunner : IDownloadRunner
{
    private readonly DownloadEngine _engine;
    private readonly int _segmentCount;

    public EngineDownloadRunner(DownloadEngine engine, int segmentCount = DownloadEngine.DefaultSegmentCount)
    {
        _engine = engine;
        _segmentCount = segmentCount;
    }

    public Task RunAsync(DownloadTask task, IProgress<ProgressReport>? progress, CancellationToken ct)
        => _engine.DownloadAsync(task, _segmentCount, progress, ct);
}
