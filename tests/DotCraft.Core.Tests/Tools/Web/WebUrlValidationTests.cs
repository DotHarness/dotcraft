using DotCraft.Tools.Web;
using Xunit;

namespace DotCraft.Tests.Tools.Web;

public sealed class WebUrlValidationTests
{
    [Theory]
    [InlineData("ftp://93.184.216.34/file")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://user:pass@93.184.216.34/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:192.168.0.1]/")]
    [InlineData("http://2130706433/")]
    [InlineData("http://localhost/")]
    public async Task RejectsUnsafeUrls(string url)
    {
        Assert.NotNull(await WebHttp.ValidateUrlAsync(new Uri(url), CancellationToken.None));
    }

    [Fact]
    public async Task RejectsOverlongUrls()
    {
        var uri = new Uri("http://93.184.216.34/" + new string('a', WebHttp.MaxUrlLength));
        Assert.NotNull(await WebHttp.ValidateUrlAsync(uri, CancellationToken.None));
    }

    [Theory]
    [InlineData("http://93.184.216.34/page")]
    [InlineData("https://[2606:4700:4700::1111]/")]
    public async Task AcceptsPublicUrls(string url)
    {
        Assert.Null(await WebHttp.ValidateUrlAsync(new Uri(url), CancellationToken.None));
    }
}
