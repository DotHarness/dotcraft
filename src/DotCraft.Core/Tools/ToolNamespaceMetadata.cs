using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

internal interface IToolNamespaceMetadata
{
    string? ToolNamespace { get; }

    string? ToolNamespaceDescription => null;
}

/// <summary>
/// Carries the canonical composite identity independently from the flat alias used by
/// providers that cannot represent namespaces.
/// </summary>
internal interface ICanonicalToolIdentityMetadata : IToolNamespaceMetadata
{
    ToolName CanonicalToolName { get; }

    string ProviderFlatName { get; }

    string IToolNamespaceMetadata.ToolNamespace => CanonicalToolName.Namespace!;
}

internal interface IOpenAIResponsesFunctionToolMetadata
{
    bool? Strict { get; }
}

internal static class ToolNamespaceMetadataResolver
{
    public static bool TryGet(AITool tool, out string toolNamespace)
    {
        switch (tool)
        {
            case IToolNamespaceMetadata namespaced
                when TryNormalize(namespaced.ToolNamespace, out toolNamespace):
                return true;

            case IDeferredToolMetadata deferred
                when TryNormalize(deferred.DeferredToolNamespace, out toolNamespace):
                return true;

            default:
                toolNamespace = string.Empty;
                return false;
        }
    }

    public static string? GetDescription(AITool tool) =>
        tool is IToolNamespaceMetadata metadata ? metadata.ToolNamespaceDescription : null;

    private static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        normalized = value.Trim();
        return true;
    }
}

internal static class ToolNamespaceDescriptionResolver
{
    public static string Resolve(
        string namespaceName,
        IEnumerable<string?> descriptions,
        out bool hasConflict)
    {
        var distinct = descriptions
            .Where(static description => !string.IsNullOrWhiteSpace(description))
            .Select(static description => description!.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        hasConflict = distinct.Length > 1;
        return distinct.Length == 1
            ? distinct[0]
            : $"Tools in the {namespaceName} namespace.";
    }
}

internal static class CanonicalToolIdentityMetadataResolver
{
    public static bool TryGet(AITool tool, out ToolName toolName, out string providerFlatName)
    {
        if (tool is ICanonicalToolIdentityMetadata metadata)
        {
            toolName = metadata.CanonicalToolName;
            providerFlatName = metadata.ProviderFlatName;
            return true;
        }

        toolName = default;
        providerFlatName = string.Empty;
        return false;
    }
}
