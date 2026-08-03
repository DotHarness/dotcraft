using DotCraft.Protocol.InlineVisualizations;
using Xunit;

namespace DotCraft.Core.Tests.Protocol;

public sealed class InlineVisualizationDirectiveParserTests
{
    [Fact]
    public void Parse_RecognizesExactStandaloneDirectivesInOrder()
    {
        var markdown = "Before\n::dotcraft-inline-vis{file=\"alpha-chart.html\"}\nAfter\n::dotcraft-inline-vis{file=\"beta.html\"}";

        var result = InlineVisualizationDirectiveParser.Parse(markdown);

        Assert.Collection(result,
            item => { Assert.Equal("alpha-chart.html", item.File); Assert.Equal(0, item.Ordinal); },
            item => { Assert.Equal("beta.html", item.File); Assert.Equal(1, item.Ordinal); });
    }

    [Fact]
    public void Parse_IgnoresFencesInlineTextAndUnsafeNames()
    {
        var markdown = "```\n::dotcraft-inline-vis{file=\"hidden.html\"}\n```\n" +
                       "text ::dotcraft-inline-vis{file=\"inline.html\"}\n" +
                       "::dotcraft-inline-vis{file=\"../unsafe.html\"}\n" +
                       "::dotcraft-inline-vis{file='wrong-quotes.html'}";

        Assert.Empty(InlineVisualizationDirectiveParser.Parse(markdown));
    }

    [Theory]
    [InlineData("chart.html", true)]
    [InlineData("sales-by-region.html", true)]
    [InlineData("Sales.html", false)]
    [InlineData("../chart.html", false)]
    [InlineData("chart.htm", false)]
    public void IsValidFileName_UsesStableContract(string file, bool expected) =>
        Assert.Equal(expected, InlineVisualizationDirectiveParser.IsValidFileName(file));
}
