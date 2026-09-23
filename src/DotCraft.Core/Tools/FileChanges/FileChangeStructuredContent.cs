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
        return JsonSerializer.SerializeToElement(new Payload(PayloadKind, [entry]), Options);
    }

    internal static bool IsFileChange(JsonElement? structuredContent) =>
        structuredContent is { ValueKind: JsonValueKind.Object } content
        && content.TryGetProperty("kind", out var kind)
        && kind.ValueKind == JsonValueKind.String
        && kind.ValueEquals(PayloadKind);

    private sealed record Payload(string Kind, IReadOnlyList<Entry> Changes);

    private sealed record Entry(
        string Path,
        FileChangeKind Kind,
        string? Diff,
        int Additions,
        int Deletions,
        bool? Truncated);
}
