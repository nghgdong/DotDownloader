using DM.Core.Video;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class MpdParserTests
{
    private static readonly Uri Base = new("https://dash.example.com/v/manifest.mpd");

    [Fact]
    public void Parses_SegmentTemplate_With_Duration()
    {
        var xml = """
            <?xml version="1.0"?>
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" mediaPresentationDuration="PT0H0M6.0S">
              <Period>
                <AdaptationSet contentType="video" mimeType="video/mp4">
                  <Representation id="v0" bandwidth="800000" width="640" height="360">
                    <SegmentTemplate timescale="1" duration="2" startNumber="1"
                      initialization="init-$RepresentationID$.m4s" media="seg-$RepresentationID$-$Number%03d$.m4s"/>
                  </Representation>
                </AdaptationSet>
                <AdaptationSet contentType="audio" mimeType="audio/mp4">
                  <Representation id="a0" bandwidth="128000">
                    <SegmentTemplate timescale="1" duration="2" startNumber="1"
                      initialization="init-$RepresentationID$.m4s" media="seg-$RepresentationID$-$Number%03d$.m4s"/>
                  </Representation>
                </AdaptationSet>
              </Period>
            </MPD>
            """;

        var m = MpdParser.Parse(xml, Base);

        m.Video.Should().NotBeNull();
        m.Video!.Id.Should().Be("v0");
        m.Video.Width.Should().Be(640);
        m.Video.InitUrl.Should().Be("https://dash.example.com/v/init-v0.m4s");
        m.Video.SegmentUrls.Should().HaveCount(3); // ceil(6/2)
        m.Video.SegmentUrls[0].Should().Be("https://dash.example.com/v/seg-v0-001.m4s");
        m.Video.SegmentUrls[2].Should().Be("https://dash.example.com/v/seg-v0-003.m4s");

        m.Audio.Should().NotBeNull();
        m.Audio!.Id.Should().Be("a0");
        m.Audio.SegmentUrls.Should().HaveCount(3);
    }

    [Fact]
    public void Parses_SegmentTimeline_With_Repeat()
    {
        var xml = """
            <?xml version="1.0"?>
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" mediaPresentationDuration="PT6S">
              <Period>
                <AdaptationSet mimeType="video/mp4">
                  <Representation id="v" bandwidth="1000">
                    <SegmentTemplate timescale="10" startNumber="1" media="$Number$.m4s" initialization="i.m4s">
                      <SegmentTimeline><S t="0" d="20" r="2"/></SegmentTimeline>
                    </SegmentTemplate>
                  </Representation>
                </AdaptationSet>
              </Period>
            </MPD>
            """;

        var m = MpdParser.Parse(xml, Base);

        m.Video!.SegmentUrls.Should().HaveCount(3); // r=2 → 3 segment
        m.Video.SegmentUrls[0].Should().Be("https://dash.example.com/v/1.m4s");
        m.Video.SegmentUrls[2].Should().Be("https://dash.example.com/v/3.m4s");
    }

    [Theory]
    [InlineData("PT1H2M3.5S", 3723.5)]
    [InlineData("PT0H0M6.0S", 6.0)]
    [InlineData("PT30S", 30)]
    [InlineData("PT10M0.000S", 600)]
    public void ParseDuration_Handles_Iso8601(string iso, double expected)
    {
        MpdParser.ParseDuration(iso).Should().BeApproximately(expected, 0.001);
    }
}
