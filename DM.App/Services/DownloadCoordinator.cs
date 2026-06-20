using System.Collections.Generic;
using DM.Core.Models;
using DM.Server.Models;

namespace DM.App.Services;

/// <summary>
/// Nơi tiếp nhận yêu cầu tải từ local server. Hiện là bản tối giản (giữ task trong RAM);
/// Phase 6 sẽ thay bằng hàng đợi tải thật (DownloadQueue) + engine.
/// </summary>
public sealed class DownloadCoordinator
{
    private readonly object _gate = new();
    private readonly List<DownloadTask> _tasks = new();

    /// <summary>Số task đang hoạt động (chưa hoàn tất/hủy) — báo qua /api/ping.</summary>
    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _tasks.Count(t =>
                    t.State is not (DownloadState.Completed or DownloadState.Canceled or DownloadState.Failed));
            }
        }
    }

    /// <summary>Callback cho LocalServer: tạo task từ request, đưa vào danh sách, trả taskId.</summary>
    public Guid Enqueue(DownloadRequest request)
    {
        var task = new DownloadTask
        {
            Url = request.Url,
            FilePath = request.FileName ?? string.Empty,
            Category = request.Type
        };

        lock (_gate)
        {
            _tasks.Add(task);
        }
        return task.Id;
    }
}
