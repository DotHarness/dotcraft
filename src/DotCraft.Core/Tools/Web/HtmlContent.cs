using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using ReverseMarkdown;

namespace DotCraft.Tools.Web;

internal static partial class HtmlContent
{
    private static readonly HashSet<string> RemovedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "script", "style", "noscript", "template", "iframe", "object", "embed", "svg"
    };

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "dd", "details", "div", "dl", "dt", "fieldset",
        "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr",
        "li", "main", "nav", "ol", "p", "pre", "section", "summary", "table", "tr", "ul"
    };

    private static readonly Converter MarkdownConverter = new(new Config
    {
        GithubFlavored = true,
        UnknownTags = Config.UnknownTagsOption.Bypass,
        RemoveComments = true,
        SmartHrefHandling = true,
        Base64Images = Config.Base64ImageHandling.Skip,
        OutputLineEnding = "\n"
    });

    public static string ToMarkdown(string html, Uri baseUri)
    {
        var lines = MarkdownConverter.Convert(Clean(html, baseUri).DocumentNode.OuterHtml).Split('\n');
        var inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                lines[i] = line.Trim();
                continue;
            }

            if (inFence)
                continue;
            var indent = line.Length - line.TrimStart(' ').Length;
            lines[i] = (line[..indent] + InlineWhitespace().Replace(line[indent..], " ").Trim()).TrimEnd();
        }

        return ExtraBlankLines().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    public static string ToText(string html, Uri baseUri)
    {
        var builder = new StringBuilder();
        AppendText(Clean(html, baseUri).DocumentNode, builder, preformatted: false);
        var lines = builder.ToString().Split('\n').Select(line => line.TrimEnd());
        return ExtraBlankLines().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    private static HtmlDocument Clean(string html, Uri baseUri)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var removed = document.DocumentNode
            .Descendants()
            .Where(node => node.NodeType == HtmlNodeType.Comment
                           || (node.NodeType == HtmlNodeType.Element && (RemovedTags.Contains(node.Name) || IsHidden(node))))
            .ToList();
        foreach (var node in removed)
            node.Remove();

        foreach (var node in document.DocumentNode.Descendants().Where(n => n.NodeType == HtmlNodeType.Element))
        {
            Absolutize(node, "href", baseUri);
            Absolutize(node, "src", baseUri);
        }

        return document;
    }

    private static bool IsHidden(HtmlNode node)
    {
        if (node.Attributes.Contains("hidden"))
            return true;
        if (string.Equals(node.GetAttributeValue("aria-hidden", string.Empty), "true", StringComparison.OrdinalIgnoreCase))
            return true;
        var style = node.GetAttributeValue("style", string.Empty);
        if (string.IsNullOrEmpty(style))
            return false;
        var compact = string.Concat(style.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
        return compact.Contains("display:none") || compact.Contains("visibility:hidden");
    }

    private static void Absolutize(HtmlNode node, string attribute, Uri baseUri)
    {
        var value = node.GetAttributeValue(attribute, string.Empty);
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (Uri.TryCreate(baseUri, HtmlEntity.DeEntitize(value).Trim(), out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            node.SetAttributeValue(attribute, absolute.AbsoluteUri);
    }

    private static void AppendText(HtmlNode node, StringBuilder builder, bool preformatted)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            var text = HtmlEntity.DeEntitize(((HtmlTextNode)node).Text);
            if (preformatted)
            {
                builder.Append(text);
                return;
            }

            var collapsed = Whitespace().Replace(text, " ");
            if (collapsed.StartsWith(' ') && (builder.Length == 0 || builder[^1] is ' ' or '\n'))
                collapsed = collapsed[1..];
            builder.Append(collapsed);
            return;
        }

        if (node.NodeType != HtmlNodeType.Element && node.NodeType != HtmlNodeType.Document)
            return;

        var name = node.Name;
        if (name.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append('\n');
            return;
        }

        var block = BlockTags.Contains(name);
        if (block)
            BreakLine(builder);
        if (name.Equals("li", StringComparison.OrdinalIgnoreCase))
            builder.Append("- ");

        var childPreformatted = preformatted || name.Equals("pre", StringComparison.OrdinalIgnoreCase);
        foreach (var child in node.ChildNodes)
            AppendText(child, builder, childPreformatted);

        if (name is "td" or "th")
            builder.Append('\t');
        if (block)
            BreakLine(builder);
    }

    private static void BreakLine(StringBuilder builder)
    {
        while (builder.Length > 0 && builder[^1] == ' ')
            builder.Length--;
        if (builder.Length > 0 && builder[^1] != '\n')
            builder.Append('\n');
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex InlineWhitespace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();
}
