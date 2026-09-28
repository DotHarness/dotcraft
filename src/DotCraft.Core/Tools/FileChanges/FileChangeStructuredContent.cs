using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Tools;

internal static class FileChangeStructuredContent
{
    private const string PayloadKind = "fileChange";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static JsonElement Build(FileChangeRecord change)
    {
        var diff = UnifiedDiffRenderer.Render(change.DisplayPath, change.Before, change.After, UnifiedDiffLimits.PerCall);
        var entry = new Entry(
            change.DisplayPath,
            change.Kind,
            diff.Diff,
            diff.Additions,
            diff.Deletions,
            diff.Truncated ? true : null);
        return JsonSerializer.SerializeToElement(new Payload(PayloadKind, [entry], "applied"), Options);
    }

    internal static JsonElement Outcome(string writeState, string? warning = null) =>
        JsonSerializer.SerializeToElement(new Payload(PayloadKind, [], writeState,
            warning is null ? null : [new Warning("file_change_report_failed", warning)]), Options);

    internal static bool IsFileChange(JsonElement? structuredContent) =>
        structuredContent is { ValueKind: JsonValueKind.Object } content
        && content.TryGetProperty("kind", out var kind)
        && kind.ValueKind == JsonValueKind.String
        && kind.ValueEquals(PayloadKind);

    private sealed record Payload(string Kind, IReadOnlyList<Entry> Changes,
        string WriteState, IReadOnlyList<Warning>? Warnings = null);

    private sealed record Warning(string Code, string Message);

    private sealed record Entry(
        string Path,
        FileChangeKind Kind,
        string? Diff,
        int Additions,
        int Deletions,
        bool? Truncated);
}
