using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Configuration;

namespace DotCraft.AppServerTestClient;

internal static class DeferredLoadingSmokeScenarios
{
    public const string NativeDeferredToolSearch = "native-deferred-tool-search";
}

internal static class DeferredLoadingSmokeStatuses
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

internal static class DeferredLoadingSmokeTools
{
    public const string Echo = "EchoDeferredSmoke";
    public const string AddNumbers = "AddDeferredSmokeNumbers";
    public const string SuccessToken = "DEFERRED_SMOKE_OK";
}

internal sealed class DeferredLoadingSmokeMatrix
{
    public List<DeferredLoadingSmokeProviderCase> Providers { get; set; } = [];

    public static DeferredLoadingSmokeMatrix Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<DeferredLoadingSmokeMatrix>(stream, DeferredLoadingSmokeJson.Options)
               ?? new DeferredLoadingSmokeMatrix();
    }
}

internal sealed class DeferredLoadingSmokeProviderCase
{
    public string Protocol { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;
}

internal sealed class DeferredLoadingSmokeReport
{
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset FinishedAt { get; set; }

    public string WorkRoot { get; set; } = string.Empty;

    public List<DeferredLoadingSmokeCaseReport> Cases { get; set; } = [];

    public DeferredLoadingSmokeSummary Summary { get; set; } = new();

    [JsonIgnore]
    public int ExitCode => Summary.Failed > 0
        ? 1
        : Summary.Runnable == 0
            ? 2
            : 0;

    public void FinalizeSummary(DateTimeOffset finishedAt)
    {
        FinishedAt = finishedAt;
        Summary = DeferredLoadingSmokeSummary.FromCases(Cases);
    }
}

internal sealed class DeferredLoadingSmokeSummary
{
    public int Total { get; set; }

    public int Runnable { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }

    public int Skipped { get; set; }

    public int ExitCode { get; set; }

    public static DeferredLoadingSmokeSummary FromCases(IReadOnlyList<DeferredLoadingSmokeCaseReport> cases)
    {
        var summary = new DeferredLoadingSmokeSummary
        {
            Total = cases.Count,
            Passed = cases.Count(static c => c.Status == DeferredLoadingSmokeStatuses.Passed),
            Failed = cases.Count(static c => c.Status == DeferredLoadingSmokeStatuses.Failed),
            Skipped = cases.Count(static c => c.Status == DeferredLoadingSmokeStatuses.Skipped)
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

internal sealed class DeferredLoadingSmokeCaseReport
{
    public string Protocol { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Scenario { get; set; } = DeferredLoadingSmokeScenarios.NativeDeferredToolSearch;

    public string Status { get; set; } = DeferredLoadingSmokeStatuses.Skipped;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspacePath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceDbPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferredToolLoadingObserved { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WireShape { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetToolName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; set; }

    public static DeferredLoadingSmokeCaseReport Skipped(
        string protocol,
        string providerId,
        string model,
        string reason) =>
        new()
        {
            Protocol = protocol,
            ProviderId = providerId,
            Model = model,
            Status = DeferredLoadingSmokeStatuses.Skipped,
            Message = reason
        };
}

internal sealed record DeferredLoadingSmokeProviderSelection(
    string Protocol,
    string ProviderId,
    string Model);

internal static class DeferredLoadingSmokeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static readonly string[] SupportedProtocols =
    [
        ModelProviderProtocols.OpenAIResponses,
        ModelProviderProtocols.Anthropic
    ];
}
