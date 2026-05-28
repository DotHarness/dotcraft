using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Configuration;

namespace DotCraft.AppServerTestClient;

internal static class CompactSmokeScenarios
{
    public const string ManualSnapshotPartial = "manual-snapshot-partial";
    public const string ManualLegacyPartial = "manual-legacy-partial";
    public const string AutoSnapshotFork = "auto-snapshot-fork";

    /// <summary>
    /// Lightweight cache observability scenario: drives a few normal agent turns that each invoke
    /// a deterministic tool call (no compaction). Reports aggregate prompt-cache hit ratio from
    /// the thread's trace_events. Use this to verify provider/gateway cache behaviour without
    /// paying the cost of the compaction smoke path.
    /// </summary>
    public const string PromptCacheBaseline = "prompt-cache-baseline";

    public static readonly string[] All =
    [
        ManualSnapshotPartial,
        ManualLegacyPartial,
        AutoSnapshotFork,
        PromptCacheBaseline
    ];
}

internal static class CompactSmokeStatuses
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

internal sealed class CompactSmokeMatrix
{
    public List<CompactSmokeProviderCase> Providers { get; set; } = [];

    public static CompactSmokeMatrix Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<CompactSmokeMatrix>(stream, CompactSmokeJson.Options)
               ?? new CompactSmokeMatrix();
    }
}

internal sealed class CompactSmokeProviderCase
{
    public string Protocol { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;
}

internal sealed class CompactSmokeReport
{
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset FinishedAt { get; set; }

    public string WorkRoot { get; set; } = string.Empty;

    public List<CompactSmokeCaseReport> Cases { get; set; } = [];

    public CompactSmokeSummary Summary { get; set; } = new();

    [JsonIgnore]
    public int ExitCode => Summary.Failed > 0
        ? 1
        : Summary.Runnable == 0
            ? 2
            : 0;

    public void FinalizeSummary(DateTimeOffset finishedAt)
    {
        FinishedAt = finishedAt;
        Summary = CompactSmokeSummary.FromCases(Cases);
    }
}

internal sealed class CompactSmokeSummary
{
    public int Total { get; set; }

    public int Runnable { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }

    public int Skipped { get; set; }

    public int ExitCode { get; set; }

    public static CompactSmokeSummary FromCases(IReadOnlyList<CompactSmokeCaseReport> cases)
    {
        var summary = new CompactSmokeSummary
        {
            Total = cases.Count,
            Passed = cases.Count(static c => c.Status == CompactSmokeStatuses.Passed),
            Failed = cases.Count(static c => c.Status == CompactSmokeStatuses.Failed),
            Skipped = cases.Count(static c => c.Status == CompactSmokeStatuses.Skipped)
        };
        summary.Runnable = summary.Passed + summary.Failed;
        summary.ExitCode = summary.Failed > 0
            ? 1
            : summary.Runnable == 0
                ? 2
                : 0;
        return summary;
    }
}

internal sealed class CompactSmokeCaseReport
{
    public string Protocol { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Scenario { get; set; } = string.Empty;

    public string Status { get; set; } = CompactSmokeStatuses.Skipped;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspacePath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceDbPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FallbackReason { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SnapshotSource { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SnapshotInvalidReason { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CacheHitRequired { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CacheHit { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CacheShapeApplied { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CacheShapeKind { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PromptCacheKeyPresent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CacheMarkerSource { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? InputTokens { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CachedInputTokens { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CacheWriteInputTokens { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CacheHitRate { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; set; }

    public static CompactSmokeCaseReport Skipped(
        string protocol,
        string providerId,
        string model,
        string scenario,
        string reason) =>
        new()
        {
            Protocol = protocol,
            ProviderId = providerId,
            Model = model,
            Scenario = scenario,
            Status = CompactSmokeStatuses.Skipped,
            Message = reason
        };
}

internal sealed record CompactSmokeProviderSelection(
    string Protocol,
    string ProviderId,
    string Model);

internal static class CompactSmokeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static readonly string[] SupportedProtocols =
    [
        ModelProviderProtocols.OpenAIChatCompletions,
        ModelProviderProtocols.OpenAIResponses,
        ModelProviderProtocols.Anthropic
    ];
}
