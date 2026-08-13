using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Configuration;

namespace DotCraft.AppServerTestClient;

internal static class StreamRetrySmokeStatuses
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

internal sealed class StreamRetrySmokeMatrix
{
    public List<StreamRetrySmokeProviderCase> Providers { get; set; } = [];

    public static StreamRetrySmokeMatrix Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<StreamRetrySmokeMatrix>(stream, CompactSmokeJson.Options)
               ?? new StreamRetrySmokeMatrix();
    }
}

internal sealed class StreamRetrySmokeProviderCase
{
    public string Protocol { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;
}

internal sealed record StreamRetrySmokeProviderSelection(
    string Protocol,
    string ProviderId,
    string Model,
    string UpstreamEndPoint);

internal sealed class StreamRetrySmokeReport
{
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset FinishedAt { get; set; }

    public string WorkRoot { get; set; } = string.Empty;

    public List<StreamRetrySmokeCaseReport> Cases { get; set; } = [];

    public StreamRetrySmokeSummary Summary { get; set; } = new();

    [JsonIgnore]
    public int ExitCode => Summary.Failed > 0
        ? 1
        : Summary.Runnable == 0
            ? 2
            : 0;

    public void FinalizeSummary(DateTimeOffset finishedAt)
    {
        FinishedAt = finishedAt;
        Summary = StreamRetrySmokeSummary.FromCases(Cases);
    }
}

internal sealed class StreamRetrySmokeSummary
{
    public int Total { get; set; }

    public int Runnable { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }

    public int Skipped { get; set; }

    public int ExitCode { get; set; }

    public static StreamRetrySmokeSummary FromCases(IReadOnlyList<StreamRetrySmokeCaseReport> cases)
    {
        var summary = new StreamRetrySmokeSummary
        {
            Total = cases.Count,
            Passed = cases.Count(static c => c.Status == StreamRetrySmokeStatuses.Passed),
            Failed = cases.Count(static c => c.Status == StreamRetrySmokeStatuses.Failed),
            Skipped = cases.Count(static c => c.Status == StreamRetrySmokeStatuses.Skipped)
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

internal sealed class StreamRetrySmokeCaseReport
{
    public string Protocol { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Status { get; set; } = StreamRetrySmokeStatuses.Skipped;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspacePath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UpstreamEndPoint { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProxyEndPoint { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StreamErrorCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FaultedRequests { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ForwardedRequests { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FinalAssistantText { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StreamRetrySmokeProxyRequestReport>? ProxyRequests { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; set; }

    public static StreamRetrySmokeCaseReport Skipped(
        string protocol,
        string providerId,
        string model,
        string reason) =>
        new()
        {
            Protocol = protocol,
            ProviderId = providerId,
            Model = model,
            Status = StreamRetrySmokeStatuses.Skipped,
            Message = reason
        };
}

internal sealed class StreamRetrySmokeProxySnapshot
{
    public int FaultedRequests { get; set; }

    public int ForwardedRequests { get; set; }

    public List<StreamRetrySmokeProxyRequestReport> Requests { get; set; } = [];
}

internal sealed class StreamRetrySmokeProxyRequestReport
{
    public string Kind { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UpstreamStatusCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }

    public long DurationMs { get; set; }
}

internal static class StreamRetrySmokeProtocols
{
    public static readonly string[] Supported =
    [
        ModelProviderProtocols.OpenAIChatCompletions,
        ModelProviderProtocols.OpenAIResponses,
        ModelProviderProtocols.Anthropic
    ];
}
