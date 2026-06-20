using DM.Core.Persistence;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class MetadataStoreTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "dm-tests", Guid.NewGuid().ToString("N"));
    private readonly MetadataStore _store = new();

    public MetadataStoreTests() => Directory.CreateDirectory(_tempDir);

    private string FilePath(string name = "f.bin") => Path.Combine(_tempDir, name);

    private static DownloadMetadata Sample(long downloaded) => new()
    {
        Url = "https://example.com/f.bin",
        TotalBytes = 1000,
        SupportsRange = true,
        Segments = new()
        {
            new SegmentMetadata { Start = 0, End = 499, Downloaded = downloaded },
            new SegmentMetadata { Start = 500, End = 999, Downloaded = 0 }
        }
    };

    [Fact]
    public async Task Save_Then_Load_RoundTrips()
    {
        var file = FilePath();
        await _store.SaveAsync(file, Sample(123));

        var loaded = await _store.LoadAsync(file);

        loaded.Should().NotBeNull();
        loaded!.Url.Should().Be("https://example.com/f.bin");
        loaded.TotalBytes.Should().Be(1000);
        loaded.Segments.Should().HaveCount(2);
        loaded.Segments[0].Downloaded.Should().Be(123);
    }

    [Fact]
    public async Task Save_Leaves_No_Tmp_File()
    {
        var file = FilePath();
        await _store.SaveAsync(file, Sample(1));

        File.Exists(MetadataStore.MetaPath(file)).Should().BeTrue();
        File.Exists(MetadataStore.MetaPath(file) + ".tmp").Should().BeFalse();
    }

    [Fact]
    public async Task Load_Returns_Null_When_Missing()
    {
        (await _store.LoadAsync(FilePath("nope.bin"))).Should().BeNull();
    }

    [Fact]
    public async Task Interrupted_Save_Does_Not_Corrupt_Existing_Meta()
    {
        var file = FilePath();
        // 1) Lưu metadata hợp lệ (bản A).
        await _store.SaveAsync(file, Sample(500));

        // 2) Mô phỏng app bị kill GIỮA lúc Save: file .tmp đã ghi dở (rác), chưa kịp Move.
        var tmp = MetadataStore.MetaPath(file) + ".tmp";
        await File.WriteAllTextAsync(tmp, "{ this is half-written garbage ");

        // 3) File .dmmeta thật vẫn nguyên vẹn → load ra đúng bản A.
        var loaded = await _store.LoadAsync(file);
        loaded.Should().NotBeNull();
        loaded!.Segments[0].Downloaded.Should().Be(500);

        // 4) Lần Save kế (bản B) thành công và dọn sạch .tmp rác.
        await _store.SaveAsync(file, Sample(750));
        (await _store.LoadAsync(file))!.Segments[0].Downloaded.Should().Be(750);
        File.Exists(tmp).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_Removes_Meta_And_Tmp()
    {
        var file = FilePath();
        await _store.SaveAsync(file, Sample(1));
        await File.WriteAllTextAsync(MetadataStore.MetaPath(file) + ".tmp", "x");

        _store.Delete(file);

        File.Exists(MetadataStore.MetaPath(file)).Should().BeFalse();
        File.Exists(MetadataStore.MetaPath(file) + ".tmp").Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
