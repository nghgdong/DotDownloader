using DM.Core.Util;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class CategoryClassifierTests
{
    [Theory]
    [InlineData("movie.mp4", FileCategory.Video)]
    [InlineData("clip.MKV", FileCategory.Video)]
    [InlineData("report.pdf", FileCategory.Document)]
    [InlineData("song.mp3", FileCategory.Music)]
    [InlineData("archive.zip", FileCategory.Compressed)]
    [InlineData("setup.exe", FileCategory.Program)]
    [InlineData("data.xyz", FileCategory.Other)]
    [InlineData("noext", FileCategory.Other)]
    public void Classify_Maps_Extension_To_Category(string name, FileCategory expected)
    {
        CategoryClassifier.Classify(name).Should().Be(expected);
    }

    [Fact]
    public void Classify_Handles_Url_With_Query()
    {
        CategoryClassifier.Classify("https://x.com/a/b/file.mp4?token=abc&x=1").Should().Be(FileCategory.Video);
    }

    [Fact]
    public void ResolveTargetPath_Puts_File_In_Category_Folder()
    {
        var path = CategoryClassifier.ResolveTargetPath(@"C:\Downloads", "song.mp3");
        path.Should().Be(Path.Combine(@"C:\Downloads", "Music", "song.mp3"));
    }

    [Fact]
    public void ResolveTargetPath_Honors_Folder_Override()
    {
        var overrides = new Dictionary<FileCategory, string> { [FileCategory.Video] = @"D:\Movies" };
        var path = CategoryClassifier.ResolveTargetPath(@"C:\Downloads", "movie.mp4", overrides);
        path.Should().Be(Path.Combine(@"D:\Movies", "movie.mp4"));
    }

    [Fact]
    public void ResolveTargetPath_Sanitizes_FileName()
    {
        var path = CategoryClassifier.ResolveTargetPath(@"C:\Downloads", "bad:name?.txt");
        Path.GetFileName(path).Should().NotContainAny(":", "?");
    }
}
