using Microsoft.AspNetCore.Builder;
using ShapeLand.ContentBuild;

namespace ShapeLand.GameServer.Tests;

/// <summary>The development option that serves a web build from the game server's own port.</summary>
public sealed class WebBuildTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shapeland-web-{Guid.NewGuid():N}");
    private WebApplication _server = null!;
    private HttpClient _http = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Build"));
        Directory.CreateDirectory(Path.Combine(_root, "StreamingAssets"));
        await File.WriteAllTextAsync(Path.Combine(_root, "index.html"), "<title>ShapeLand</title>");
        await File.WriteAllBytesAsync(Path.Combine(_root, "Build", "web.data"), [1, 2, 3]);
        await File.WriteAllBytesAsync(Path.Combine(_root, "Build", "web.wasm.unityweb"), [6, 7]);
        await File.WriteAllBytesAsync(Path.Combine(_root, "StreamingAssets", "content.bin"), [4, 5]);

        var (content, errors) = ShapeLandBuild.Build(Path.Combine(EndToEndTests.RepoRoot(), "shapeland", "content"), saveRegistry: false);
        Assert.Empty(errors);
        var contentFile = Path.Combine(_root, "server-content.bin");
        await using (var file = File.Create(contentFile))
        {
            content!.Save(file);
        }

        _server = ShapeLandServer.Build([], builder =>
        {
            builder.Configuration["ShapeLand:ContentFile"] = contentFile;
            builder.Configuration["ShapeLand:WebRoot"] = _root;
            builder.Configuration["Urls"] = "http://127.0.0.1:0";
        });
        await _server.StartAsync(TestContext.Current.CancellationToken);
        _http = new HttpClient { BaseAddress = new Uri(_server.Urls.First()) };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.StopAsync();
        await _server.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ServesThePageAndItsFiles()
    {
        var token = TestContext.Current.CancellationToken;
        Assert.Contains("ShapeLand", await _http.GetStringAsync("/", token));

        using var data = await _http.GetAsync("/Build/web.data", token);
        Assert.Equal("application/octet-stream", data.Content.Headers.ContentType?.MediaType);
        Assert.Equal([1, 2, 3], await data.Content.ReadAsByteArrayAsync(token));

        using var content = await _http.GetAsync("/StreamingAssets/content.bin", token);
        Assert.Equal([4, 5], await content.Content.ReadAsByteArrayAsync(token));
    }

    [Fact]
    public async Task SendsBrotliFilesEncodedOnlyToBrowsersThatAcceptIt()
    {
        var token = TestContext.Current.CancellationToken;
        using var accepting = new HttpRequestMessage(HttpMethod.Get, "/Build/web.wasm.unityweb");
        accepting.Headers.AcceptEncoding.ParseAdd("gzip, deflate, br");
        using var encoded = await _http.SendAsync(accepting, token);
        Assert.Equal(["br"], encoded.Content.Headers.ContentEncoding);

        // Firefox over plain HTTP doesn't ask for br: the bytes go as they are, for Unity's loader to unpack.
        using var refusing = new HttpRequestMessage(HttpMethod.Get, "/Build/web.wasm.unityweb");
        refusing.Headers.AcceptEncoding.ParseAdd("gzip, deflate");
        using var raw = await _http.SendAsync(refusing, token);
        Assert.Empty(raw.Content.Headers.ContentEncoding);
        Assert.Equal("application/octet-stream", raw.Content.Headers.ContentType?.MediaType);
        Assert.Equal([6, 7], await raw.Content.ReadAsByteArrayAsync(token));
    }
}
