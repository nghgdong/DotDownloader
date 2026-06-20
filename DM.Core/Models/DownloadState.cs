namespace DM.Core.Models;

/// <summary>
/// Trạng thái vòng đời của một download task.
/// </summary>
public enum DownloadState
{
    Queued,
    Connecting,
    Downloading,
    Paused,
    Completed,
    Failed,
    Canceled
}
