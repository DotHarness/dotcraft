using DotCraft.Visualizations;
using Xunit;

namespace DotCraft.Core.Tests.Visualizations;

public sealed class VisualizationContentReferenceParserTests
{
    private const string ChartPath = "Sales_报告 v2.HTML";

    [Fact]
    public void Parse_RecognizesBothReferenceFormsInOrder()
    {
        var markdown = "Before\n" +
                       $"visualize{{\"path\":\"{ChartPath}\",\"title\":\"Sales by region\"}}\n" +
                       "After\n" +
                       $"  {(char)0xE200}visualize{(char)0xE202}{{\"path\":\"beta.html\",\"mode\":\"wide\"}}{(char)0xE201}  \r";

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
                Assert.Equal("beta.html", reference.Path);
                Assert.Null(reference.Title);
                Assert.Equal("wide", reference.Mode);
            });
    }

    [Theory]
    [InlineData("```\nvisualize{\"path\":\"chart.html\"}\n```")]
    [InlineData("visualize{\"path\":\"chart.html\"} text")]
    [InlineData("visualize{\"path\":\"chart.html\",}")]
    [InlineData("visualize{'path':'chart.html'}")]
    [InlineData("visualize{\"path\":\"chart.html\",\"mode\":\"full\"}")]
    [InlineData("visualize{\"path\":\"chart.html\",\"title\":1}")]
    [InlineData("visualize[\"chart.html\"]")]
    [InlineData("visualize{}")]
    public void Parse_LeavesInvalidLinesAsText(string markdown) =>
        Assert.Empty(VisualizationContentReferenceParser.Parse(markdown));

    [Fact]
    public void Parse_IgnoresUnknownFieldsWithoutDiscardingTheReference()
    {
        var result = Assert.Single(VisualizationContentReferenceParser.Parse(
            "visualize{\"path\":\"chart.html\",\"title\":\"Sales\",\"mode\":\"wide\",\"extra\":{\"count\":1},\"caption\":null}"));

        Assert.Equal(new VisualizationContentReference("chart.html", "Sales", "wide"), result);
    }

    [Fact]
    public void Parse_RejectsTitlesOverTheLimit()
    {
        var title = new string('a', VisualizationContentReferenceParser.MaxTitleLength + 1);

        Assert.Empty(VisualizationContentReferenceParser.Parse($"visualize{{\"path\":\"chart.html\",\"title\":\"{title}\"}}"));
    }
}
