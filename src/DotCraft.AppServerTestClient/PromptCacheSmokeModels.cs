using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Configuration;

namespace DotCraft.AppServerTestClient;

internal static class PromptCacheSmokeScenarios
{
    public const string PromptCacheBaseline = "prompt-cache-baseline";
}

internal static class PromptCacheSmokeStatuses
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

internal sealed class PromptCacheSmokeMatrix
{
    public List<PromptCacheSmokeProviderCase> Providers { get; set; } = [];

    public static PromptCacheSmokeMatrix Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<PromptCacheSmokeMatrix>(stream, PromptCacheSmokeJson.Options)
               ?? new PromptCacheSmokeMatrix();
    }
}

internal sealed class PromptCacheSmokeProviderCase
{
    public string Protocol { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;
}

internal sealed class PromptCacheSmokeReport
{
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset FinishedAt { get; set; }

    public string WorkRoot { get; set; } = string.Empty;

    public List<PromptCacheSmokeCaseReport> Cases { get; set; } = [];

    public PromptCacheSmokeSummary Summary { get; set; } = new();

    [JsonIgnore]
    public int ExitCode => Summary.Failed > 0
        ? 1
        : Summary.Runnable == 0
            ? 2
            : 0;

    public void FinalizeSummary(DateTimeOffset finishedAt)
    {
        FinishedAt = finishedAt;
        Summary = PromptCacheSmokeSummary.FromCases(Cases);
    }
}

internal sealed class PromptCacheSmokeSummary
{
    public int Total { get; set; }

    public int Runnable { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }

    public int Skipped { get; set; }

    public int ExitCode { get; set; }

    public static PromptCacheSmokeSummary FromCases(IReadOnlyList<PromptCacheSmokeCaseReport> cases)
    {
        var summary = new PromptCacheSmokeSummary
        {
            Total = cases.Count,
            Passed = cases.Count(static c => c.Status == PromptCacheSmokeStatuses.Passed),
            Failed = cases.Count(static c => c.Status == PromptCacheSmokeStatuses.Failed),
            Skipped = cases.Count(static c => c.Status == PromptCacheSmokeStatuses.Skipped)
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

internal sealed class PromptCacheSmokeCaseReport
{
    public string Protocol { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Scenario { get; set; } = PromptCacheSmokeScenarios.PromptCacheBaseline;

    public string Status { get; set; } = PromptCacheSmokeStatuses.Skipped;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspacePath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceDbPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CacheHitRequired { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CacheHit { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? InputTokens { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CachedInputTokens { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CacheWriteInputTokens { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CacheHitRate { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ContextCompactionCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; set; }

    public static PromptCacheSmokeCaseReport Skipped(
        string protocol,
        string providerId,
        string model,
        string reason) =>
        new()
        {
            Protocol = protocol,
            ProviderId = providerId,
            Model = model,
            Status = PromptCacheSmokeStatuses.Skipped,
            Message = reason
        };
}

internal sealed record PromptCacheSmokeProviderSelection(
    string Protocol,
    string ProviderId,
    string Model);

internal static class PromptCacheSmokeJson
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
