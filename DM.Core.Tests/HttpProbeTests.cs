using System.Net;
using System.Net.Http.Headers;
using DM.Core.Net;
using DM.Core.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class HttpProbeTests
{
    private static HttpProbe ProbeReturning(HttpResponseMessage response)
    {
        var handler = new MockHttpMessageHandler(_ => response);
        return new HttpProbe(new HttpClient(handler));
    }

    [Fact]
    public async Task Probe_Parses_ContentLength_And_AcceptRanges()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };
        response.Content.Headers.ContentLength = 123456;
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        response.Headers.AcceptRanges.Add("bytes");
        response.Content.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") { FileName = "file.zip" };

        var probe = ProbeReturning(response);

        var result = await probe.ProbeAsync("https://example.com/download");

        result.TotalBytes.Should().Be(123456);
        result.SupportsRange.Should().BeTrue();
        result.HasKnownSize.Should().BeTrue();
        result.SuggestedFileName.Should().Be("file.zip");
        result.ContentType.Should().Be("application/zip");
    }

    [Fact]
    public async Task Probe_Treats_Missing_ContentLength_As_Unknown()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty)
        };
        response.Content.Headers.ContentLength = null;

        var probe = ProbeReturning(response);

        var result = await probe.ProbeAsync("https://example.com/stream");

        result.TotalBytes.Should().Be(-1);
        result.HasKnownSize.Should().BeFalse();
        result.SupportsRange.Should().BeFalse();
    }

    [Fact]
    public async Task Probe_Falls_Back_To_Url_For_FileName()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };

        var probe = ProbeReturning(response);

        var result = await probe.ProbeAsync("https://example.com/files/report%202024.pdf");

        result.SuggestedFileName.Should().Be("report 2024.pdf");
    }

    [Fact]
    public async Task Probe_Sends_Head_Request()
    {
        var handler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) });
        var probe = new HttpProbe(new HttpClient(handler));

        await probe.ProbeAsync("https://example.com/x");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Head);
    }
}
