using DotCraft.TraceViewer.ViewModels;
using Xunit;

namespace DotCraft.TraceViewer.Tests;

public sealed class SessionTitleFormatterTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 16, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Format_preserves_ordinary_user_text()
    {
        var title = SessionTitleFormatter.Format("  Inspect   the trace  ", StartedAt);

        Assert.Equal("Inspect the trace", title.FullText);
        Assert.Equal("Inspect the trace", title.CompactText);
    }

    [Fact]
    public void Format_removes_matching_trailing_runtime_context()
    {
        var title = SessionTitleFormatter.Format(
            "Inspect the trace\n" + RuntimeContextBlock(),
            StartedAt);

        Assert.Equal("Inspect the trace", title.FullText);
    }

    [Fact]
    public void Format_preserves_user_authored_reminder_like_text()
    {
        const string request = "Explain <system-reminder>keep this example</system-reminder> please";

        var title = SessionTitleFormatter.Format(request, StartedAt);

        Assert.Equal(request, title.FullText);
    }

    [Fact]
    public void Format_preserves_unclosed_reminder_instead_of_truncating_user_text()
    {
        const string request = "Show this literal <system-reminder> example";

        var title = SessionTitleFormatter.Format(request, StartedAt);

        Assert.Equal(request, title.FullText);
    }

    [Fact]
    public void Format_uses_date_fallback_when_runtime_context_is_the_only_content()
    {
        var title = SessionTitleFormatter.Format(RuntimeContextBlock(), StartedAt);

        Assert.StartsWith("Session on ", title.FullText, StringComparison.Ordinal);
    }

    private static string RuntimeContextBlock() =>
        """
        <system-reminder>
        ## Environment
        CurrentDate: 2030-01-02
        TimeZone: Etc/UTC

        ## Mode
        CurrentMode: Agent

        ## Mode Action
        Agent mode is active.
        </system-reminder>
        """;
}
