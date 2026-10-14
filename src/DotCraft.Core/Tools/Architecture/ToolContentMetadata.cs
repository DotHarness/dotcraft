namespace DotCraft.Tools;

/// <summary>Marks tools whose results are content items, such as text alongside images, rather than one value.</summary>
public static class ToolContentMetadata
{
    /// <summary>Annotation set to JSON <see langword="true"/> on a definition whose results are content items.</summary>
    public const string ReturnsContentAnnotation = "dotcraft/returnsContent";

    public static bool ReturnsContent(ToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Annotations.TryGetValue(ReturnsContentAnnotation, out var value)
               && value.ValueKind == System.Text.Json.JsonValueKind.True;
    }
}
