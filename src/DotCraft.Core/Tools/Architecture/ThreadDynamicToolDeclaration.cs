using System.Text.Json;

namespace DotCraft.Tools;

public sealed record ThreadDynamicToolDeclaration(
    string? Namespace,
    string? NamespaceDescription,
    string Name,
    string Description,
    JsonElement InputSchema,
    bool DeferLoading,
    JsonElement? Approval)
{
    internal static IReadOnlyList<ThreadDynamicToolDeclaration> Freeze(
        IReadOnlyList<ThreadDynamicToolDeclaration>? declarations) =>
        declarations is not { Count: > 0 }
            ? []
            : Array.AsReadOnly(declarations
                .OrderBy(static tool => tool.Namespace, StringComparer.Ordinal)
                .ThenBy(static tool => tool.Name, StringComparer.Ordinal)
                .Select(static tool => tool with
                {
                    InputSchema = tool.InputSchema.Clone(),
                    Approval = tool.Approval?.Clone()
                })
                .ToArray());

    public static bool Match(
        IReadOnlyList<ThreadDynamicToolDeclaration> left,
        IReadOnlyList<ThreadDynamicToolDeclaration> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            var a = left[index];
            var b = right[index];
            if (a.Namespace != b.Namespace || a.NamespaceDescription != b.NamespaceDescription
                || a.Name != b.Name || a.Description != b.Description || a.DeferLoading != b.DeferLoading
                || !JsonElement.DeepEquals(a.InputSchema, b.InputSchema)
                || !ApprovalMatches(a.Approval, b.Approval))
                return false;
        }

        return true;
    }

    private static bool ApprovalMatches(JsonElement? left, JsonElement? right) =>
        left is { } a && right is { } b ? JsonElement.DeepEquals(a, b) : left is null && right is null;
}
