using DotCraft.Tools.Web;
using Xunit;

namespace DotCraft.Tests.Tools.Web;

public sealed class HtmlContentTests
{
    private static readonly Uri BaseUri = new("https://docs.example.org/guide/start.html");

    private const string Page = """
        <!DOCTYPE html>
        <html>
        <head><title>Guide</title><style>body { color: red; }</style></head>
        <body>
          <nav><a href="/">Home</a></nav>
          <h1>Getting started</h1>
          <p>Read the <a href="../api/index.html">API reference</a> first.</p>
          <pre><code class="language-csharp">var x = 1;
        if (x &gt; 0)
            Console.WriteLine(x);</code></pre>
          <table>
            <thead><tr><th>Name</th><th>Value</th></tr></thead>
            <tbody><tr><td>Timeout</td><td>30</td></tr></tbody>
          </table>
          <div hidden>Hidden attribute text</div>
          <div aria-hidden="true">Aria hidden text</div>
          <span style="display: none">Display none text</span>
          <p style="visibility:hidden">Visibility hidden text</p>
          <script>alert("script text")</script>
          <noscript>Noscript text</noscript>
          <template><p>Template text</p></template>
          <iframe src="https://ads.example/"></iframe>
          <img src="images/logo.png" alt="Logo">
        </body>
        </html>
        """;

    [Fact]
    public void MarkdownKeepsCodeBlocksTablesAndAbsoluteLinks()
    {
        var markdown = HtmlContent.ToMarkdown(Page, BaseUri);

        Assert.Contains("# Getting started", markdown);
        Assert.Contains("[API reference](https://docs.example.org/api/index.html)", markdown);
        Assert.Contains("```", markdown);
        Assert.Contains("var x = 1;\nif (x > 0)\n    Console.WriteLine(x);", markdown);
        Assert.Contains("| Name | Value |", markdown);
        Assert.Contains("| Timeout | 30 |", markdown);
        Assert.Contains("https://docs.example.org/guide/images/logo.png", markdown);
        Assert.Contains("[Home](https://docs.example.org/)", markdown);
        AssertNoHiddenContent(markdown);
    }

    [Fact]
    public void MarkdownDropsSourceIndentationNoise()
    {
        var markdown = HtmlContent.ToMarkdown(
            "<body>\n\t\t<div>\n\t\t\t<a href=\"#main\">Skip</a>\t<a href=\"#nav\">Nav</a>\n\t\t\t \n\t\t</div>\n\t\t<p>Body   text</p>\n</body>",
            BaseUri);

        Assert.DoesNotContain('\t', markdown);
        Assert.DoesNotContain("\n\n\n", markdown);
        Assert.Contains("[Skip](https://docs.example.org/guide/start.html#main) [Nav](https://docs.example.org/guide/start.html#nav)", markdown);
        Assert.Contains("Body text", markdown);
    }

    [Fact]
    public void TextModeReturnsReadableTextWithoutHiddenContent()
    {
        var text = HtmlContent.ToText(Page, BaseUri);

        Assert.Contains("Getting started", text);
        Assert.Contains("Read the API reference first.", text);
        Assert.Contains("if (x > 0)", text);
        Assert.DoesNotContain("<", text.Replace("x > 0", string.Empty));
        AssertNoHiddenContent(text);
    }

    private static void AssertNoHiddenContent(string output)
    {
        Assert.DoesNotContain("Hidden attribute text", output);
        Assert.DoesNotContain("Aria hidden text", output);
        Assert.DoesNotContain("Display none text", output);
        Assert.DoesNotContain("Visibility hidden text", output);
        Assert.DoesNotContain("script text", output);
        Assert.DoesNotContain("Noscript text", output);
        Assert.DoesNotContain("Template text", output);
        Assert.DoesNotContain("color: red", output);
        Assert.DoesNotContain("ads.example", output);
    }
}
