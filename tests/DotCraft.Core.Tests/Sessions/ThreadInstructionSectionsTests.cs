using System.Text.Json.Nodes;
using DotCraft.Context.WorldState;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Core.Tests.Sessions;

public sealed class ThreadInstructionSectionsTests
{
    [Fact]
    public void Role_RendersOnce_ThenMarksAChangeAsReplacing_ThenRetiresItself()
    {
        var section = ThreadInstructionsSection.Role();

        var first = Next(section, "Guard releases.", null);
        Assert.Contains("Guard releases.", first.Text);
        Assert.DoesNotContain("replace", first.Text);

        var changed = Next(section, "Review code.", first.Snapshot);
        Assert.Contains("replace all previously", changed.Text);
        Assert.Contains("Review code.", changed.Text);

        Assert.True(JsonNode.DeepEquals(changed.Snapshot, Snapshot(section, "Review code.")));
        Assert.Contains("no longer apply", Next(section, null, changed.Snapshot).Text);
    }

    [Fact]
    public void Developer_WithNothingEverSet_SaysNothing()
    {
        Assert.Null(Next(ThreadInstructionsSection.Developer(), null, null).Text);
    }

    [Fact]
    public void ClientContext_UpdatesAndClears_AndSaysNothingWhenNeverBound()
    {
        var section = new ClientContextSection();

        var bound = Context(null) with { ClientContext = static () => "bound" };
        Assert.Equal("bound", section.RenderDiff(bound, PreviousSectionState.Absent));

        var cleared = Context(null) with { ClientContext = static () => string.Empty };
        var known = PreviousSectionState.Known(section.Snapshot(bound)!);
        Assert.Contains("no longer active", section.RenderDiff(cleared, known));
        Assert.Null(section.RenderDiff(cleared, PreviousSectionState.Absent));
    }

    [Fact]
    public void ClientContext_WithoutABinding_TakesNoSnapshot()
    {
        Assert.Null(new ClientContextSection().Snapshot(Context(null)));
    }

    private static (string? Text, JsonNode Snapshot) Next(ThreadInstructionsSection section, string? text, JsonNode? previous)
    {
        var state = previous == null ? PreviousSectionState.Absent : PreviousSectionState.Known(previous);
        var context = Context(text);
        return (section.RenderDiff(context, state), section.Snapshot(context));
    }

    private static JsonNode Snapshot(ThreadInstructionsSection section, string text) => section.Snapshot(Context(text));

    private static WorldStateContext Context(string? text) => new()
    {
        Thread = new SessionThread
        {
            Id = "thread",
            WorkspacePath = "/workspace",
            Configuration = new ThreadConfiguration { RoleInstructions = text, DeveloperInstructions = text }
        }
    };
}
