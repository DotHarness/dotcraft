using System.Net;
using System.Text;
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
        var result = await Fetch(_ => Html(HttpStatusCode.NotFound, "<html><body><h1>Not here</h1><p>Try the index.</p></body></html>"));

        Assert.True(result.Success);
        Assert.StartsWith($"Fetched {PageUrl} (HTTP 404, markdown).", result.Content);
        Assert.Contains("Not here", result.Content);
        Assert.Equal(PageUrl, result.StructuredContent!.Value.GetProperty("url").GetString());
    }

    [Fact]
    public async Task StreamedBodyOverByteCapIsTruncated()
    {
        var config = new AppConfig.WebToolsConfig { MaxResponseBytes = 10 };
        var result = await Fetch(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent("0123456789abcdefghij", "text/plain")
        }, config);

        Assert.True(result.StructuredContent!.Value.GetProperty("truncated").GetBoolean());
        Assert.EndsWith("\n\n0123456789", result.Content);
    }

    [Fact]
    public async Task DeclaredLengthOverByteCapIsError()
    {
        var config = new AppConfig.WebToolsConfig { MaxResponseBytes = 10 };
        var result = await Fetch(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("0123456789abcdefghij", Encoding.UTF8, "text/plain")
        }, config);

        Assert.False(result.Success);
        Assert.Contains("limit", result.Content);
    }

    [Fact]
    public async Task UnsupportedContentTypeIsError()
    {
        var result = await Fetch(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x25, 0x50, 0x44, 0x46]) { Headers = { { "Content-Type", "application/pdf" } } }
        });

        Assert.False(result.Success);
        Assert.Contains("application/pdf", result.Content);
    }

    [Fact]
    public async Task UnknownCharsetIsError()
    {
        var result = await Fetch(_ =>
        {
            var content = new ByteArrayContent("hello"u8.ToArray());
            content.Headers.TryAddWithoutValidation("Content-Type", "text/plain; charset=x-made-up");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        Assert.False(result.Success);
        Assert.Contains("x-made-up", result.Content);
    }

    [Fact]
    public async Task DeclaredCharsetIsUsedForDecoding()
    {
        var result = await Fetch(_ =>
        {
            var content = new ByteArrayContent(Encoding.GetEncoding("gb2312").GetBytes("中文内容"));
            content.Headers.TryAddWithoutValidation("Content-Type", "text/plain; charset=gb2312");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        Assert.EndsWith("\n\n中文内容", result.Content);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    [InlineData(1000, 8)]
    public async Task MaxCharsIsClampedToConfiguredLimit(int requested, int expectedLength)
    {
        var config = new AppConfig.WebToolsConfig { MaxChars = 8 };
        var result = await Fetch(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("abcdefghijklmnop", Encoding.UTF8, "text/plain")
        }, config, requested);

        var structured = result.StructuredContent!.Value;
        Assert.Equal(expectedLength, structured.GetProperty("length").GetInt32());
        Assert.True(structured.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task RedirectIsFollowedAndReportedAsFinalUrl()
    {
        var result = await Fetch(request => request.RequestUri!.AbsolutePath == "/docs/page"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("/docs/moved", UriKind.Relative) } }
            : Html(HttpStatusCode.OK, "<p>Moved content</p>"));

        Assert.Equal("http://93.184.216.34/docs/moved", result.StructuredContent!.Value.GetProperty("finalUrl").GetString());
        Assert.StartsWith($"Fetched http://93.184.216.34/docs/moved (redirected from {PageUrl})", result.Content);
        Assert.Contains("Moved content", result.Content);
    }

    [Fact]
    public async Task RedirectToPrivateAddressIsRejected()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("http://169.254.169.254/latest/meta-data/") }
        });
        var result = await Fetch(handler);

        Assert.False(result.Success);
        Assert.Contains("169.254.169.254", result.Content);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TooManyRedirectsIsError()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri(PageUrl) }
        });
        var result = await Fetch(handler);

        Assert.False(result.Success);
        Assert.Contains("redirects", result.Content);
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task PrivateUrlIsRejectedBeforeSending()
    {
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("must not send"));
        var tools = new WebTools(new AppConfig.WebToolsConfig(), null, new HttpClient(handler));

        Assert.False((await tools.WebFetch("http://127.0.0.1:8080/admin")).Success);
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

    private static Task<ToolExecutionResult> Fetch(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        AppConfig.WebToolsConfig? config = null,
        int? maxChars = null) =>
        Fetch(new StubHttpHandler(respond), config, maxChars);

    private static Task<ToolExecutionResult> Fetch(
        StubHttpHandler handler,
        AppConfig.WebToolsConfig? config = null,
        int? maxChars = null) =>
        new WebTools(config ?? new AppConfig.WebToolsConfig(), null, new HttpClient(handler)).WebFetch(PageUrl, maxChars: maxChars);

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
