using System.Net;
using System.Text;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools.Web;

public sealed class WebFetchTests
{
    private const string PageUrl = "http://93.184.216.34/docs/page";

    [Fact]
    public async Task NonSuccessStatusReturnsBodyAsResult()
    {
        var root = await Fetch(_ => Html(HttpStatusCode.NotFound, "<html><body><h1>Not here</h1><p>Try the index.</p></body></html>"));

        Assert.False(root.TryGetProperty("error", out _));
        Assert.Equal(404, root.GetProperty("status").GetInt32());
        Assert.Equal("markdown", root.GetProperty("extractor").GetString());
        Assert.Contains("Not here", root.GetProperty("text").GetString());
        Assert.Equal(PageUrl, root.GetProperty("url").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("notice").GetString()));
    }

    [Fact]
    public async Task StreamedBodyOverByteCapIsTruncated()
    {
        var config = new AppConfig.WebToolsConfig { MaxResponseBytes = 10 };
        var root = await Fetch(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent("0123456789abcdefghij", "text/plain")
        }, config);

        Assert.True(root.GetProperty("truncated").GetBoolean());
        Assert.Equal("0123456789", root.GetProperty("text").GetString());
        Assert.Equal("raw", root.GetProperty("extractor").GetString());
    }

    [Fact]
    public async Task DeclaredLengthOverByteCapIsError()
    {
        var config = new AppConfig.WebToolsConfig { MaxResponseBytes = 10 };
        var root = await Fetch(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("0123456789abcdefghij", Encoding.UTF8, "text/plain")
        }, config);

        Assert.Contains("limit", root.GetProperty("error").GetString());
        Assert.Equal(PageUrl, root.GetProperty("url").GetString());
    }

    [Fact]
    public async Task UnsupportedContentTypeIsError()
    {
        var root = await Fetch(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x25, 0x50, 0x44, 0x46]) { Headers = { { "Content-Type", "application/pdf" } } }
        });

        Assert.Contains("application/pdf", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task UnknownCharsetIsError()
    {
        var root = await Fetch(_ =>
        {
            var content = new ByteArrayContent("hello"u8.ToArray());
            content.Headers.TryAddWithoutValidation("Content-Type", "text/plain; charset=x-made-up");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        Assert.Contains("x-made-up", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task DeclaredCharsetIsUsedForDecoding()
    {
        var root = await Fetch(_ =>
        {
            var content = new ByteArrayContent(Encoding.GetEncoding("gb2312").GetBytes("中文内容"));
            content.Headers.TryAddWithoutValidation("Content-Type", "text/plain; charset=gb2312");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        Assert.Equal("中文内容", root.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    [InlineData(1000, 8)]
    public async Task MaxCharsIsClampedToConfiguredLimit(int requested, int expectedLength)
    {
        var config = new AppConfig.WebToolsConfig { MaxChars = 8 };
        var root = await Fetch(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("abcdefghijklmnop", Encoding.UTF8, "text/plain")
        }, config, requested);

        Assert.Equal(expectedLength, root.GetProperty("length").GetInt32());
        Assert.True(root.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task RedirectIsFollowedAndReportedAsFinalUrl()
    {
        var root = await Fetch(request => request.RequestUri!.AbsolutePath == "/docs/page"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("/docs/moved", UriKind.Relative) } }
            : Html(HttpStatusCode.OK, "<p>Moved content</p>"));

        Assert.Equal("http://93.184.216.34/docs/moved", root.GetProperty("finalUrl").GetString());
        Assert.Contains("Moved content", root.GetProperty("text").GetString());
    }

    [Fact]
    public async Task RedirectToPrivateAddressIsRejected()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("http://169.254.169.254/latest/meta-data/") }
        });
        var root = await Fetch(handler);

        Assert.Contains("169.254.169.254", root.GetProperty("error").GetString());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TooManyRedirectsIsError()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri(PageUrl) }
        });
        var root = await Fetch(handler);

        Assert.Contains("redirects", root.GetProperty("error").GetString());
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task PrivateUrlIsRejectedBeforeSending()
    {
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("must not send"));
        var tools = new WebTools(new AppConfig.WebToolsConfig(), null, new HttpClient(handler));

        using var output = JsonDocument.Parse(await tools.WebFetch("http://127.0.0.1:8080/admin"));

        Assert.True(output.RootElement.TryGetProperty("error", out _));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UserCancellationIsRethrown()
    {
        var tools = new WebTools(new AppConfig.WebToolsConfig(), null, new HttpClient(new StubHttpHandler(_ => Html(HttpStatusCode.OK, "<p>x</p>"))));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.WebFetch(PageUrl, cancellationToken: cts.Token));
    }

    [Fact]
    public void UnknownSearchProviderFailsAtConstruction()
    {
        var ex = Assert.Throws<ArgumentException>(() => new WebTools(new AppConfig.WebToolsConfig { SearchProvider = "Google" }));
        Assert.Contains("Google", ex.Message);
    }

    private static Task<JsonElement> Fetch(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        AppConfig.WebToolsConfig? config = null,
        int? maxChars = null) =>
        Fetch(new StubHttpHandler(respond), config, maxChars);

    private static async Task<JsonElement> Fetch(
        StubHttpHandler handler,
        AppConfig.WebToolsConfig? config = null,
        int? maxChars = null)
    {
        var tools = new WebTools(config ?? new AppConfig.WebToolsConfig(), null, new HttpClient(handler));
        using var document = JsonDocument.Parse(await tools.WebFetch(PageUrl, maxChars: maxChars));
        return document.RootElement.Clone();
    }

    private static HttpResponseMessage Html(HttpStatusCode status, string html) =>
        new(status) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _bytes;

        public UnknownLengthContent(string text, string mediaType)
        {
            _bytes = Encoding.UTF8.GetBytes(text);
            Headers.TryAddWithoutValidation("Content-Type", mediaType);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(_bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
