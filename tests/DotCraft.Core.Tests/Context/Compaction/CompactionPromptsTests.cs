using DotCraft.Context.Compaction;

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

    [Fact]
    public void GetCompactPrompt_UsesSummaryContractWithoutAnalysisTag()
    {
        var prompt = CompactionPrompts.GetCompactPrompt();
        Assert.DoesNotContain("<analysis>", prompt);
        Assert.Contains("<summary>", prompt);
    }

    [Fact]
    public void GetCompactUserSummaryMessage_IncludesSummaryAndTranscriptPath()
    {
        var msg = CompactionPrompts.GetCompactUserSummaryMessage(
            "<summary>X</summary>",
            transcriptPath: "transcript.md",
            recentMessagesPreserved: true);

        Assert.Contains("X", msg);
        Assert.Contains("transcript.md", msg);
    }

    [Fact]
    public void GetCompactUserSummaryMessage_OmitsTranscriptHintWhenNotProvided()
    {
        var msg = CompactionPrompts.GetCompactUserSummaryMessage(
            "<summary>X</summary>",
            transcriptPath: null,
            recentMessagesPreserved: false);
        Assert.DoesNotContain("transcript.md", msg);
    }
}
