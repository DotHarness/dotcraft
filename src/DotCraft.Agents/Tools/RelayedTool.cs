using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// A tool declared on another machine and rebuilt here so a provider can project it. Invoking it
/// would run it in the wrong place, so it throws.
/// </summary>
public sealed class RelayedTool : AIFunction, ICanonicalToolIdentityMetadata
{
    private static readonly JsonElement EmptySchema = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly JsonElement _schema;
    private readonly JsonElement? _returnSchema;
    private readonly string? _namespaceDescription;

    public RelayedTool(
        ToolName canonicalName,
        string providerFlatName,
        string? description = null,
        JsonElement? schema = null,
        JsonElement? returnSchema = null,
        string? namespaceDescription = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerFlatName);
        CanonicalToolName = canonicalName;
        ProviderFlatName = providerFlatName;
        Description = description ?? string.Empty;
        _schema = schema ?? EmptySchema;
        _returnSchema = returnSchema;
        _namespaceDescription = namespaceDescription;
    }

    public ToolName CanonicalToolName { get; }

    public string ProviderFlatName { get; }

    /// <summary>The composite identity a provider projects, which a relayed declaration must carry itself.</summary>
    public static ToolName CanonicalNameOf(AIFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return CanonicalToolIdentityMetadataResolver.TryGet(function, out var name, out _)
            ? name
            : new ToolName(
                ToolNamespaceMetadataResolver.TryGet(function, out var toolNamespace) ? toolNamespace : null,
                function.Name);
    }

    /// <summary>The name a provider that cannot represent namespaces sees.</summary>
    public static string ProviderFlatNameOf(AIFunction function)
    {
        if (CanonicalToolIdentityMetadataResolver.TryGet(function, out _, out var flatName))
            return flatName;
        var canonical = CanonicalNameOf(function);
        return ProviderToolProjector.Project([canonical])[canonical];
    }

    /// <summary>The model-visible description of the tool's namespace, when it has one.</summary>
    public static string? NamespaceDescriptionOf(AITool tool) => ToolNamespaceMetadataResolver.GetDescription(tool);

    public override string Name => ProviderFlatName;

    public override string Description { get; }

    public override JsonElement JsonSchema => _schema;

    public override JsonElement? ReturnJsonSchema => _returnSchema;

    string? IToolNamespaceMetadata.ToolNamespaceDescription => _namespaceDescription;

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException($"The tool '{CanonicalToolName}' runs where it was declared.");
}
