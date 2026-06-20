using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DM.Server;
using DM.Server.Models;
using FluentAssertions;
using Xunit;

namespace DM.Core.Tests;

public class LocalServerTests
{
    private const string Token = "test-token";

    private static HttpClient ClientFor(LocalServer server, bool withToken = true)
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}") };
        if (withToken)
        {
            client.DefaultRequestHeaders.Add(ServerInfo.TokenHeader, Token);
        }
        return client;
    }

    private static LocalServer NewServer(Func<DownloadRequest, Guid>? onDownload = null, Func<int>? active = null)
        => new(Token, onDownload ?? (_ => Guid.NewGuid()), active);

    [Fact]
    public async Task Ping_With_Token_Returns_Version_And_Port()
    {
        await using var server = NewServer(active: () => 2);
        await server.StartAsync(startPort: 52010);
        using var client = ClientFor(server);

        var response = await client.GetAsync("/api/ping");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("ok").GetBoolean().Should().BeTrue();
        root.GetProperty("version").GetString().Should().Be(ServerInfo.Version);
        root.GetProperty("activeDownloads").GetInt32().Should().Be(2);
        root.GetProperty("port").GetInt32().Should().Be(server.Port);
    }

    [Fact]
    public async Task Request_Without_Token_Returns_401()
    {
        await using var server = NewServer();
        await server.StartAsync(startPort: 52020);
        using var client = ClientFor(server, withToken: false);

        var response = await client.GetAsync("/api/ping");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Download_With_Valid_Url_Returns_TaskId_And_Invokes_Callback()
    {
        DownloadRequest? captured = null;
        var fixedId = Guid.NewGuid();
        await using var server = NewServer(onDownload: req => { captured = req; return fixedId; });
        await server.StartAsync(startPort: 52030);
        using var client = ClientFor(server);

        var response = await client.PostAsJsonAsync("/api/download", new
        {
            url = "https://example.com/file.zip",
            fileName = "file.zip",
            type = "file"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("taskId").GetGuid().Should().Be(fixedId);

        captured.Should().NotBeNull();
        captured!.Url.Should().Be("https://example.com/file.zip");
        captured.FileName.Should().Be("file.zip");
    }

    [Fact]
    public async Task Download_With_Invalid_Url_Returns_400()
    {
        await using var server = NewServer();
        await server.StartAsync(startPort: 52040);
        using var client = ClientFor(server);

        var response = await client.PostAsJsonAsync("/api/download", new { url = "not a url" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Download_Without_Token_Returns_401()
    {
        await using var server = NewServer();
        await server.StartAsync(startPort: 52050);
        using var client = ClientFor(server, withToken: false);

        var response = await client.PostAsJsonAsync("/api/download", new { url = "https://example.com/x" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Auto_Picks_Next_Port_When_Busy()
    {
        await using var first = NewServer();
        await first.StartAsync(startPort: 52060);

        await using var second = NewServer();
        await second.StartAsync(startPort: 52060); // cùng cổng bắt đầu → phải +1

        first.Port.Should().Be(52060);
        second.Port.Should().Be(52061);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("8.8.8.8", false)]
    public void IsLoopback_Classifies_Addresses(string ip, bool expected)
    {
        LocalServer.IsLoopback(IPAddress.Parse(ip)).Should().Be(expected);
    }

    [Fact]
    public void IsLoopback_Null_Is_Rejected()
    {
        LocalServer.IsLoopback(null).Should().BeFalse();
    }

    [Fact]
    public void TokensMatch_Is_Exact_And_Rejects_Empty()
    {
        LocalServer.TokensMatch("abc", "abc").Should().BeTrue();
        LocalServer.TokensMatch("abc", "abd").Should().BeFalse();
        LocalServer.TokensMatch("", "abc").Should().BeFalse();
        LocalServer.TokensMatch(null, "abc").Should().BeFalse();
    }
}
