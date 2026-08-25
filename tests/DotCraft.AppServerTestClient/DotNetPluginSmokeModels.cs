using System.Text.Json.Serialization;

namespace DotCraft.AppServerTestClient;

internal static class DotNetPluginSmokeStatuses
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

internal sealed record DotNetPluginSmokeProviderSelection(
    string Protocol,
    string ProviderId,
    string Model);

internal sealed class DotNetPluginSmokeReport
{
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset FinishedAt { get; set; }

    public string Status { get; set; } = DotNetPluginSmokeStatuses.Skipped;

    public string? Protocol { get; set; }

    public string? ProviderId { get; set; }

    public string? Model { get; set; }

    public string? Phase { get; set; }

    public string? ErrorCode { get; set; }

    public bool CleanupIncomplete { get; set; }

    public bool WorkspaceRetained { get; set; }

    public long DurationMs { get; set; }

    [JsonIgnore]
    public int ExitCode => Status == DotNetPluginSmokeStatuses.Passed
        ? 0
        : Status == DotNetPluginSmokeStatuses.Skipped
            ? 2
            : 1;
}

internal sealed class DotNetPluginSmokeException(string phase, string errorCode)
    : Exception(errorCode)
{
    public string Phase { get; } = phase;

    public string ErrorCode { get; } = errorCode;
}

internal sealed record DotNetPluginTrustBaseline(bool ConsumerTrusted, bool ProviderTrusted);
