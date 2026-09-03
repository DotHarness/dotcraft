using System.ComponentModel;
using System.Text.Json;

namespace DotCraft.Tools;

/// <summary>Tools for non-blocking user coordination and bounded waits.</summary>
public sealed class UserCoordinationTools
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    [GeneratedTool]
    [Description("Send a concise question or important update during ongoing work. Use for missing information, preferences, constraints, clarification, or authorization; critical blockers or findings that may change the task's direction; or replies to user questions or status requests. Use commentary for routine progress. Returns immediately without ending the turn; replies arrive as new user messages.")]
    public async Task<string> SendUserMessageAsync(
        [Description("The concise question or update to send to the user.")] string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Serialize(new { error = "SendUserMessageAsync.message must not be empty." });

        var context = UserCoordinationRuntimeScope.Current;
        if (context is null)
            return Serialize(new { error = "SendUserMessageAsync is only available inside an eligible Session Core turn." });

        await context.SendUserMessageAsync(message.Trim(), cancellationToken).ConfigureAwait(false);
        return Serialize(new { accepted = true });
    }

    [GeneratedTool]
    [Description("Wait for the requested duration, or return early when steer or mailbox input arrives for the active turn.")]
    public async Task<string> Sleep(
        [Description("Duration in milliseconds from 1 through 43200000.")] int durationMs,
        CancellationToken cancellationToken = default)
    {
        if (durationMs is < 1 or > 43_200_000)
            return Serialize(new { error = "clock/Sleep.durationMs must be between 1 and 43200000." });

        var context = UserCoordinationRuntimeScope.Current;
        if (context is null)
            return Serialize(new { error = "clock/Sleep is only available inside a Session Core turn." });

        var result = await context.SleepAsync(durationMs, cancellationToken).ConfigureAwait(false);
        return Serialize(result);
    }

    [GeneratedTool]
    [Description("Return the current UTC time.")]
    public string CurrentTime()
    {
        return Serialize(new { utc = DateTimeOffset.UtcNow.ToString("O") });
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
}

/// <summary>Result returned by the runtime-managed Sleep operation.</summary>
public sealed record UserCoordinationSleepResult(
    long ActualDurationMs,
    string Status);

/// <summary>Runtime callbacks used by user-coordination tools.</summary>
public sealed record UserCoordinationRuntimeContext(
    Func<string, CancellationToken, Task> SendUserMessageAsync,
    Func<int, CancellationToken, Task<UserCoordinationSleepResult>> SleepAsync);

/// <summary>Async-local scope for user coordination in an active Session Core turn.</summary>
public static class UserCoordinationRuntimeScope
{
    private static readonly AsyncLocal<UserCoordinationRuntimeContext?> CurrentContext = new();

    public static UserCoordinationRuntimeContext? Current => CurrentContext.Value;

    public static IDisposable Set(UserCoordinationRuntimeContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope(UserCoordinationRuntimeContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}
