using System.IO;
using System.Text.Json;
using DM.Core.Models;

namespace DM.App.Services;

/// <summary>
/// Lưu/đọc danh sách task ra <c>tasks.json</c>. Khi load, task đang dở (Downloading)
/// chuyển về Paused để người dùng chủ động resume.
/// </summary>
public sealed class TaskStore
{
    private sealed class Dto
    {
        public Guid Id { get; set; }
        public string Url { get; set; } = "";
        public string FilePath { get; set; } = "";
        public long TotalBytes { get; set; } = -1;
        public bool SupportsRange { get; set; }
        public DownloadState State { get; set; }
        public string? Category { get; set; }
        public string? StreamType { get; set; }
        public DateTimeOffset? ScheduledAt { get; set; }
    }

    private readonly string _path;

    public TaskStore(string? path = null)
        => _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DotDownloader", "tasks.json");

    public List<DownloadTask> Load()
    {
        var result = new List<DownloadTask>();
        try
        {
            if (!File.Exists(_path))
            {
                return result;
            }
            var dtos = JsonSerializer.Deserialize<List<Dto>>(File.ReadAllText(_path)) ?? new();
            foreach (var d in dtos)
            {
                var state = d.State == DownloadState.Downloading ? DownloadState.Paused : d.State;
                result.Add(new DownloadTask
                {
                    Id = d.Id,
                    Url = d.Url,
                    FilePath = d.FilePath,
                    TotalBytes = d.TotalBytes,
                    SupportsRange = d.SupportsRange,
                    State = state,
                    Category = d.Category,
                    StreamType = d.StreamType,
                    ScheduledAt = d.ScheduledAt
                });
            }
        }
        catch
        {
            // hỏng → bỏ qua, trả danh sách rỗng
        }
        return result;
    }

    public void Save(IEnumerable<DownloadTask> tasks)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var dtos = tasks.Select(t => new Dto
            {
                Id = t.Id,
                Url = t.Url,
                FilePath = t.FilePath,
                TotalBytes = t.TotalBytes,
                SupportsRange = t.SupportsRange,
                State = t.State,
                Category = t.Category,
                StreamType = t.StreamType,
                ScheduledAt = t.ScheduledAt
            });
            File.WriteAllText(_path, JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // không chặn app vì lỗi lưu
        }
    }
}
