using System.Text.Json;

namespace DotCraft.Tools;

/// <summary>
/// Marks a definition whose name and input schema a provider reserves; it is sent verbatim and non-strict.
/// </summary>
internal static class ReservedToolSchema
{
    public const string Annotation = "dotcraft/reservedSchema";

    public static bool IsReserved(ToolDefinition definition) =>
        definition.Annotations.TryGetValue(Annotation, out var value) && value.ValueKind == JsonValueKind.True;
}
