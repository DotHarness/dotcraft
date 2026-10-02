namespace DotCraft.CodeMode.Tests;

public sealed class CodeModePragmaTests
{
    private static readonly CodeModeLimits Limits = new();

    [Fact]
    public void Pragma_SetsLimitsAndKeepsSourceLineNumbers()
    {
        var code = "// @exec: {\"timeout_ms\": 60000, \"max_output_tokens\": 500}\r\ntext(1);\ntext(2);";

        Assert.True(CodeModePragma.TryParse(code, Limits, out var program, out _));

        Assert.Equal(TimeSpan.FromSeconds(60), program.Timeout);
        Assert.Equal(500, program.MaxOutputTokens);
        Assert.Equal(["", "text(1);", "text(2);"], program.Source.Split('\n'));
    }

    [Fact]
    public void NoPragma_UsesDefaultsAndSourceVerbatim()
    {
        Assert.True(CodeModePragma.TryParse("text('// @exec: {}');", Limits, out var program, out _));

        Assert.Equal(Limits.DefaultTimeout, program.Timeout);
        Assert.Equal(Limits.DefaultMaxOutputTokens, program.MaxOutputTokens);
        Assert.Equal("text('// @exec: {}');", program.Source);
    }

    [Theory]
    [InlineData("// @exec: {\"timeout\": 5}\ntext(1);", "Unknown")]
    [InlineData("// @exec: {\"timeout_ms\": 0}\ntext(1);", "timeout_ms")]
    [InlineData("// @exec: {\"timeout_ms\": 1.5}\ntext(1);", "timeout_ms")]
    [InlineData("// @exec: {\"max_output_tokens\": 100001}\ntext(1);", "max_output_tokens")]
    [InlineData("// @exec: [1]\ntext(1);", "JSON object")]
    [InlineData("// @exec: {timeout_ms: 5}\ntext(1);", "JSON object")]
    [InlineData("// @exec: {}", "program")]
    [InlineData("   ", "non-empty")]
    public void InvalidPragma_FailsBeforeExecution(string code, string expected)
    {
        Assert.False(CodeModePragma.TryParse(code, Limits, out _, out var error));
        Assert.Contains(expected, error);
    }
}
