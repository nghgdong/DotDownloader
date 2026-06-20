using DM.Core.Video;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class M3u8ParserTests
{
    private static readonly Uri Base = new("https://cdn.example.com/video/index.m3u8");

    [Fact]
    public void Detects_Master_And_Parses_Variants()
    {
        var content = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360
            360/index.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720
            https://other.example.com/720/index.m3u8
            """;

        var pl = M3u8Parser.Parse(content, Base);

        pl.IsMaster.Should().BeTrue();
        pl.Variants.Should().HaveCount(2);
        pl.Variants[0].Bandwidth.Should().Be(800000);
        pl.Variants[0].Width.Should().Be(640);
        pl.Variants[0].Height.Should().Be(360);
        pl.Variants[0].Uri.Should().Be("https://cdn.example.com/video/360/index.m3u8");
        pl.Variants[1].Uri.Should().Be("https://other.example.com/720/index.m3u8");
    }

    [Fact]
    public void Parses_Media_Segments_With_Absolute_Uris()
    {
        var content = """
            #EXTM3U
            #EXT-X-VERSION:3
            #EXT-X-MEDIA-SEQUENCE:0
            #EXTINF:9.009,
            seg0.ts
            #EXTINF:9.009,
            seg1.ts
            #EXT-X-ENDLIST
            """;

        var pl = M3u8Parser.Parse(content, Base);

        pl.IsMaster.Should().BeFalse();
        pl.Segments.Should().HaveCount(2);
        pl.Segments[0].Uri.Should().Be("https://cdn.example.com/video/seg0.ts");
        pl.Segments[0].Duration.Should().BeApproximately(9.009, 0.001);
        pl.Segments[0].MediaSequence.Should().Be(0);
        pl.Segments[1].MediaSequence.Should().Be(1);
        pl.Segments[0].Key.Should().BeNull();
    }

    [Fact]
    public void Parses_Aes128_Key_Applied_To_Following_Segments()
    {
        var content = """
            #EXTM3U
            #EXT-X-MEDIA-SEQUENCE:0
            #EXT-X-KEY:METHOD=AES-128,URI="https://cdn.example.com/key.bin",IV=0x00000000000000000000000000000001
            #EXTINF:4.0,
            seg0.ts
            #EXTINF:4.0,
            seg1.ts
            """;

        var pl = M3u8Parser.Parse(content, Base);

        pl.Segments.Should().HaveCount(2);
        var key = pl.Segments[0].Key!;
        key.Method.Should().Be("AES-128");
        key.IsAes128.Should().BeTrue();
        key.Uri.Should().Be("https://cdn.example.com/key.bin");
        key.Iv.Should().Equal(M3u8Parser.HexToBytes("0x00000000000000000000000000000001"));
        pl.Segments[1].Key.Should().BeSameAs(key);
    }

    [Fact]
    public void Key_Method_None_Clears_Encryption()
    {
        var content = """
            #EXTM3U
            #EXT-X-KEY:METHOD=AES-128,URI="k"
            #EXTINF:1,
            a.ts
            #EXT-X-KEY:METHOD=NONE
            #EXTINF:1,
            b.ts
            """;

        var pl = M3u8Parser.Parse(content, Base);

        pl.Segments[0].Key.Should().NotBeNull();
        pl.Segments[1].Key.Should().BeNull();
    }

    [Fact]
    public void Parses_ExtXMap_Init_Segment()
    {
        var content = """
            #EXTM3U
            #EXT-X-MAP:URI="init.mp4"
            #EXTINF:2,
            seg0.m4s
            """;

        var pl = M3u8Parser.Parse(content, Base);

        pl.InitSegmentUri.Should().Be("https://cdn.example.com/video/init.mp4");
        pl.Segments.Should().HaveCount(1);
    }

    [Fact]
    public void ParseAttributes_Respects_Quoted_Commas()
    {
        var attrs = M3u8Parser.ParseAttributes("METHOD=AES-128,URI=\"https://x/y?a=1,b=2\",IV=0xAB");
        attrs["METHOD"].Should().Be("AES-128");
        attrs["URI"].Should().Be("https://x/y?a=1,b=2");
        attrs["IV"].Should().Be("0xAB");
    }
}
