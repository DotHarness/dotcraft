using DotCraft.Visualizations;
using Xunit;

namespace DotCraft.Core.Tests.Visualizations;

public sealed class VisualizationContentReferenceParserTests
{
    private const string ChartPath = "/workspace/.craft/visualizations/thread_test/alpha-chart.html";

    [Fact]
    public void Parse_RecognizesBothReferenceFormsInOrder()
    {
        var markdown = "Before\n" +
                       $"visualize{{\"path\":\"{ChartPath}\",\"title\":\"Sales by region\"}}\n" +
                       "After\n" +
                       $"  {(char)0xE200}visualize{(char)0xE202}{{\"path\":\"C:\\\\work\\\\beta.html\",\"mode\":\"wide\"}}{(char)0xE201}  \r";

        var result = VisualizationContentReferenceParser.Parse(markdown);

        Assert.Collection(result,
            reference =>
            {
                Assert.Equal(ChartPath, reference.Path);
                Assert.Equal("Sales by region", reference.Title);
                Assert.Null(reference.Mode);
            },
            reference =>
            {
                Assert.Equal("C:\\work\\beta.html", reference.Path);
                Assert.Null(reference.Title);
                Assert.Equal("wide", reference.Mode);
            });
    }

    [Theory]
    [InlineData("```\nvisualize{\"path\":\"/w/chart.html\"}\n```")]
    [InlineData("text visualize{\"path\":\"/w/chart.html\"}")]
    [InlineData("visualize{\"path\":\"/w/chart.html\"} text")]
    [InlineData("visualize{\"path\":\"/w/chart.html\",}")]
    [InlineData("visualize{'path':'/w/chart.html'}")]
    [InlineData("visualize{\"path\":\"/w/chart.html\",\"extra\":1}")]
    [InlineData("visualize{\"path\":\"/w/chart.html\",\"mode\":\"full\"}")]
    [InlineData("visualize{\"path\":\"/w/chart.html\",\"title\":\" \"}")]
    [InlineData("visualize{\"path\":\"/w/chart.html\",\"title\":1}")]
    [InlineData("visualize[\"/w/chart.html\"]")]
    [InlineData("visualize{}")]
    [InlineData("visualize{\"path\":\"chart.html\"}")]
    [InlineData("visualize{\"path\":\"/w/../chart.html\"}")]
    [InlineData("visualize{\"path\":\"/w/Chart.html\"}")]
    public void Parse_LeavesInvalidLinesAsText(string markdown) =>
        Assert.Empty(VisualizationContentReferenceParser.Parse(markdown));

    [Fact]
    public void Parse_RejectsTitlesOverTheLimit()
    {
        var title = new string('a', VisualizationContentReferenceParser.MaxTitleLength + 1);

        Assert.Empty(VisualizationContentReferenceParser.Parse($"visualize{{\"path\":\"/w/chart.html\",\"title\":\"{title}\"}}"));
    }

    [Theory]
    [InlineData("/w/chart.html", true)]
    [InlineData("C:\\w\\sales-by-region.html", true)]
    [InlineData("C:/w/chart.html", true)]
    [InlineData("\\\\server\\share\\chart.html", true)]
    [InlineData("chart.html", false)]
    [InlineData("w/chart.html", false)]
    [InlineData("/w/./chart.html", false)]
    [InlineData("/w/chart.htm", false)]
    [InlineData("/w/chart_1.html", false)]
    public void IsValidPath_UsesStableContract(string path, bool expected) =>
        Assert.Equal(expected, VisualizationContentReferenceParser.IsValidPath(path));
}
