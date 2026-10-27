using System.ComponentModel;
using System.Text.Json;

namespace DotCraft.Tools;

/// <summary>Tools for non-blocking user coordination and the current time.</summary>
public sealed class UserCoordinationTools
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    [GeneratedTool]
    [ToolPresentation("core.send-user-message-async")]
    [Description("Send a concise question or important update during ongoing work. Use for missing information, preferences, constraints, clarification, or authorization; critical blockers or findings that may change the task's direction; or replies to user questions or status requests. Use commentary for routine progress. Returns immediately without ending the turn; replies arrive as new user messages.")]
    public async Task<string> SendUserMessageAsync(
        [Description("The concise question or update to send to the user.")] string message,
        ToolInvocationContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Serialize(new { error = "SendUserMessageAsync.message must not be empty." });

        if (context.TurnServices is { IsTopLevelThread: false })
            return Serialize(new { error = "SendUserMessageAsync can only be used by a top-level thread." });
        if (context.TurnServices?.UserCoordination is not { } coordination)
            return Serialize(new { error = "SendUserMessageAsync is only available inside an eligible Session Core turn." });

        await coordination.SendUserMessageAsync(message.Trim(), cancellationToken).ConfigureAwait(false);
        return Serialize(new { accepted = true });
    }

    [GeneratedTool]
    [Description("Return the current UTC time.")]
    public string CurrentTime()
    {
        return Serialize(new { utc = DateTimeOffset.UtcNow.ToString("O") });
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
}

/// <summary>Runtime callbacks used by user-coordination tools.</summary>
public sealed record UserCoordinationRuntimeContext(
    Func<string, CancellationToken, Task> SendUserMessageAsync);
