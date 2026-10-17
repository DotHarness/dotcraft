using System.Text;
using DotCraft.Tools.Web;
using Xunit;

namespace DotCraft.Tests.Tools.Web;

public sealed class BingSearchTests
{
    private const string Target = "https://learn.microsoft.com/zh-cn/dotnet/csharp/tour-of-csharp/";

    private static string CkLink(string target)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(target)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"https://www.bing.com/ck/a?!&amp;&amp;p=abc123&amp;ptn=3&amp;u=a1{encoded}&amp;ntb=1";
    }

    [Fact]
    public void ParsesRedirectLinksAndIgnoresBreadcrumbCite()
    {
        var html = $"""
            <ol id="b_results">
              <li class="b_algo" data-id="">
                <div class="b_tpcn"><cite>https://learn.microsoft.com › zh-cn › dotnet › csharp</cite></div>
                <h2><a href="{CkLink(Target)}" h="ID=SERP,5103.1">C# 语言之旅 &amp; 概述</a></h2>
                <div class="b_caption"><p class="b_lineclamp2">C# 是一种 <strong>新式</strong> 编程语言。</p></div>
              </li>
              <li class="b_algo">
                <h2><a href="https://example.org/direct">Direct result</a></h2>
                <div class="b_caption"><p>Direct snippet</p></div>
              </li>
              <li class="b_ad"><h2><a href="https://ads.example/">Ad</a></h2></li>
            </ol>
            """;

        var results = BingSearch.ParseResults(html, 10);

        Assert.Equal(2, results.Count);
        Assert.Equal(Target, results[0].Url);
        Assert.Equal("C# 语言之旅 & 概述", results[0].Title);
        Assert.Equal("C# 是一种 新式 编程语言。", results[0].Snippet);
        Assert.Equal("https://example.org/direct", results[1].Url);
        Assert.DoesNotContain(results, r => r.Url.Contains('›'));
    }

    [Fact]
    public void SkipsUndecodableBingLinksAndHonorsLimit()
    {
        var html = """
            <li class="b_algo"><h2><a href="https://www.bing.com/ck/a?!&amp;&amp;p=1&amp;ntb=1">No target</a></h2></li>
            <li class="b_algo"><h2><a href="/search?q=related">Relative</a></h2></li>
            <li class="b_algo"><h2><a href="https://one.example/">One</a></h2></li>
            <li class="b_algo"><h2><a href="https://two.example/">Two</a></h2></li>
            """;

        var results = BingSearch.ParseResults(html, 1);

        var result = Assert.Single(results);
        Assert.Equal("https://one.example/", result.Url);
    }
}
