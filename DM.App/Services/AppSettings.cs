using System.IO;
using System.Text.Json;

namespace DM.App.Services;

/// <summary>Cấu hình app, lưu <c>settings.json</c> trong %APPDATA%/DotDownloader.</summary>
public sealed class AppSettings
{
    public int SegmentCount { get; set; } = 8;
    public int MaxConcurrent { get; set; } = 3;

    /// <summary>Giới hạn tốc độ toàn cục (byte/s). 0 = không giới hạn. (Thực thi ở Phase 8 — T8.1.)</summary>
    public long SpeedLimitBytesPerSec { get; set; }

    public int Port { get; set; } = 51820;

    public string DownloadDirectory { get; set; } = DefaultDownloadDir();

    /// <summary>Override thư mục theo category (tên category → đường dẫn). Trống = baseDir/Category.</summary>
    public Dictionary<string, string> CategoryFolders { get; set; } = new();

    public static string DefaultDownloadDir()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "DotDownloader");

    private static string ConfigPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DotDownloader", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = ConfigPath();
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            }
        }
        catch
        {
            // hỏng → dùng mặc định
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var path = ConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // không chặn app vì lỗi lưu cấu hình
        }
    }
}
