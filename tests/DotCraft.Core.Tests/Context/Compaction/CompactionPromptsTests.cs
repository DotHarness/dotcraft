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
    public void GetCompactPrompt_UsesBoundedHandoffSummaryContract()
    {
        var prompt = CompactionPrompts.GetCompactPrompt();
        Assert.Contains("Do NOT call any tools", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4,000-6,000", prompt);
        Assert.Contains("12,000", prompt);
        Assert.Contains("handoff summary", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<analysis>", prompt);
        Assert.DoesNotContain("List ALL user messages", prompt);
        Assert.DoesNotContain("full code snippets", prompt);
        Assert.Contains("<summary>", prompt);
    }

    [Fact]
    public void GetPartialCompactPrompt_ReturnsEnglishOnlyPrompt()
    {
        var prompt = CompactionPrompts.GetPartialCompactPrompt();
        Assert.Contains("older portion of this conversation", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Recent", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetCompactUserSummaryMessage_UsesEnglishOnlyText()
    {
        var msg = CompactionPrompts.GetCompactUserSummaryMessage(
            "<summary>X</summary>",
            transcriptPath: "transcript.md",
            recentMessagesPreserved: true);

        Assert.Contains("This session is being continued", msg);
        Assert.Contains("Summary:", msg);
        Assert.Contains("read the full transcript at: transcript.md", msg);
        Assert.Contains("Recent messages are preserved", msg);
    }

    [Fact]
    public void GetCompactUserSummaryMessage_AttachesRecentPreservedNote()
    {
        var msg = CompactionPrompts.GetCompactUserSummaryMessage(
            "<summary>X</summary>",
            transcriptPath: null,
            recentMessagesPreserved: true);
        Assert.Contains("This session is being continued", msg);
        Assert.Contains("Recent messages are preserved", msg);
    }

    [Fact]
    public void GetCompactUserSummaryMessage_OmitsTranscriptHintWhenNotProvided()
    {
        var msg = CompactionPrompts.GetCompactUserSummaryMessage(
            "<summary>X</summary>",
            transcriptPath: null,
            recentMessagesPreserved: false);
        Assert.DoesNotContain("read the full transcript", msg, StringComparison.OrdinalIgnoreCase);
    }
}
