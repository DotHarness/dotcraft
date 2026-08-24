using System.Text.Json.Serialization;

namespace DotCraft.AppServerTestClient;

internal static class DotnetPluginSmokeStatuses
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

internal sealed record DotnetPluginSmokeProviderSelection(
    string Protocol,
    string ProviderId,
    string Model);

internal sealed class DotnetPluginSmokeReport
{
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset FinishedAt { get; set; }

    public string Status { get; set; } = DotnetPluginSmokeStatuses.Skipped;

    public string? Protocol { get; set; }

    public string? ProviderId { get; set; }

    public string? Model { get; set; }

    public string? Phase { get; set; }

    public string? ErrorCode { get; set; }

    public bool CleanupIncomplete { get; set; }

    public bool WorkspaceRetained { get; set; }

    public long DurationMs { get; set; }

    [JsonIgnore]
    public int ExitCode => Status == DotnetPluginSmokeStatuses.Passed
        ? 0
        : Status == DotnetPluginSmokeStatuses.Skipped
            ? 2
            : 1;
}

internal sealed class DotnetPluginSmokeException(string phase, string errorCode)
    : Exception(errorCode)
{
    public string Phase { get; } = phase;

    public string ErrorCode { get; } = errorCode;
}

internal sealed record DotnetPluginTrustBaseline(bool ConsumerTrusted, bool ProviderTrusted);
