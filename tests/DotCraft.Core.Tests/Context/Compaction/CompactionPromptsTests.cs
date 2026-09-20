using DotCraft.Context.Compaction;
using Xunit;

namespace DotCraft.Tests.Context.Compaction;

public sealed class CompactionPromptsTests
{
    [Fact]
    public void FormatCompactSummary_StripsAnalysisBlock()
    {
        var raw = "<analysis>internal thoughts</analysis><summary>the important part</summary>";
        var formatted = CompactionPrompts.FormatCompactSummary(raw);

        Assert.DoesNotContain("<analysis>", formatted);
        Assert.DoesNotContain("internal thoughts", formatted);
        Assert.Contains("the important part", formatted);
    }

}
