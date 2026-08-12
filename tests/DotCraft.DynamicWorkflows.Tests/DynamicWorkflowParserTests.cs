using DotCraft.DynamicWorkflows;

namespace DotCraft.DynamicWorkflows.Tests;

public sealed class DynamicWorkflowParserTests
{
    private readonly DynamicWorkflowParser _parser = new();

    [Fact]
    public void Parse_LiteralMetadataAndTopLevelAwait_ReturnsExecutableBody()
    {
        var result = _parser.Parse("""
            export const meta = {
              name: "review",
              description: "Review a change",
              whenToUse: "When review is needed",
              phases: ["inspect", "report"]
            };
            const value = await Promise.resolve(42);
            return { value };
            """);

        Assert.Equal("review", result.Metadata.Name);
        Assert.Equal(["inspect", "report"], result.Metadata.Phases);
        Assert.StartsWith("(async () =>", result.ExecutableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("export const meta", result.ExecutableSource, StringComparison.Ordinal);
        Assert.Equal(64, result.SourceHash.Length);
    }

    [Theory]
    [InlineData("const meta = { name: 'x', description: 'y' }; return 1;", "meta_required")]
    [InlineData("export const meta = { name: makeName(), description: 'y' }; return 1;", "meta_invalid")]
    [InlineData("export const meta = { ['name']: 'x', description: 'y' }; return 1;", "meta_invalid")]
    [InlineData("export const meta = { name: 'x', name: 'z', description: 'y' }; return 1;", "meta_invalid")]
    [InlineData("export const meta = { ...base, name: 'x', description: 'y' }; return 1;", "meta_invalid")]
    [InlineData("export const meta = { name: 'x', description: 'y' }; return Date.now();", "prohibited_syntax")]
    [InlineData("export const meta = { name: 'x', description: 'y' }; return Math.random();", "prohibited_syntax")]
    [InlineData("export const meta = { name: 'x', description: 'y' }; return eval('1');", "prohibited_syntax")]
    public void Parse_InvalidSource_ReturnsStableCode(string source, string code)
    {
        var error = Assert.Throws<DynamicWorkflowValidationException>(() => _parser.Parse(source));
        Assert.Equal(code, error.Code);
    }
}
