namespace DM.Core.Util;

public enum FileCategory
{
    Video,
    Document,
    Music,
    Compressed,
    Program,
    Other
}

/// <summary>
/// Phân loại file theo phần mở rộng → <see cref="FileCategory"/> → thư mục đích.
/// </summary>
public static class CategoryClassifier
{
    private static readonly Dictionary<string, FileCategory> Map = BuildMap();

    private static Dictionary<string, FileCategory> BuildMap()
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory c, params string[] exts)
        {
            foreach (var e in exts) map[e] = c;
        }
        Add(FileCategory.Video, "mp4", "mkv", "avi", "mov", "webm", "flv", "wmv", "m4v", "ts", "m3u8", "mpd");
        Add(FileCategory.Document, "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "epub", "odt", "csv");
        Add(FileCategory.Music, "mp3", "flac", "wav", "aac", "ogg", "m4a", "wma");
        Add(FileCategory.Compressed, "zip", "rar", "7z", "gz", "tar", "bz2", "xz", "iso");
        Add(FileCategory.Program, "exe", "msi", "dmg", "apk", "deb", "rpm", "bin", "appimage");
        return map;
    }

    public static FileCategory Classify(string fileNameOrUrl)
    {
        var ext = ExtractExtension(fileNameOrUrl);
        return Map.TryGetValue(ext, out var cat) ? cat : FileCategory.Other;
    }

    /// <summary>Đường dẫn đích = baseDir/&lt;tên category&gt;/fileName (có thể override thư mục từng category).</summary>
    public static string ResolveTargetPath(
        string baseDir, string fileName, IReadOnlyDictionary<FileCategory, string>? folderOverrides = null)
    {
        var category = Classify(fileName);
        var folder = folderOverrides is not null && folderOverrides.TryGetValue(category, out var custom)
            ? custom
            : Path.Combine(baseDir, category.ToString());
        return Path.Combine(folder, SanitizeFileName(fileName));
    }

    private static string ExtractExtension(string nameOrUrl)
    {
        var s = nameOrUrl;
        int q = s.IndexOfAny(new[] { '?', '#' });
        if (q >= 0)
        {
            s = s[..q];
        }
        var name = s.Split('/', '\\').LastOrDefault() ?? s;
        int dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : string.Empty;
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = fileName.Split('/', '\\').LastOrDefault() ?? fileName;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "download" : name;
    }
}
