using System.Text.Json;

namespace DM.Core.Persistence;

/// <summary>
/// Đọc/ghi file <c>.dmmeta</c> cạnh file đang tải. Ghi ATOMIC: ghi ra <c>.dmmeta.tmp</c>
/// rồi <see cref="File.Move(string, string, bool)"/> đè — KHÔNG bao giờ ghi trực tiếp lên
/// file <c>.dmmeta</c> đang dùng, nên app bị kill giữa chừng không làm hỏng metadata cũ.
/// </summary>
public sealed class MetadataStore
{
    public const string Extension = ".dmmeta";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static string MetaPath(string filePath) => filePath + Extension;

    private static string TmpPath(string filePath) => filePath + Extension + ".tmp";

    public async Task SaveAsync(string filePath, DownloadMetadata meta, CancellationToken ct = default)
    {
        var path = MetaPath(filePath);
        var tmp = TmpPath(filePath);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(meta, JsonOptions);

        // 1) Ghi toàn bộ ra file tạm và flush xuống đĩa.
        await using (var fs = new FileStream(
            tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
            await fs.FlushAsync(ct).ConfigureAwait(false);
        }

        // 2) Đổi tên đè lên file thật — thao tác nguyên tử trên cùng volume.
        File.Move(tmp, path, overwrite: true);
    }

    public async Task<DownloadMetadata?> LoadAsync(string filePath, CancellationToken ct = default)
    {
        var path = MetaPath(filePath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await JsonSerializer
                .DeserializeAsync<DownloadMetadata>(fs, JsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // File .dmmeta hỏng (hiếm — vì ghi atomic) → coi như không có.
            return null;
        }
    }

    public void Delete(string filePath)
    {
        var path = MetaPath(filePath);
        var tmp = TmpPath(filePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        if (File.Exists(tmp))
        {
            File.Delete(tmp);
        }
    }
}
