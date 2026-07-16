using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Wraps an <see cref="AIFunction"/> to normalize empty results and enforce per-tool result size limits
/// (spill-to-disk with preview when exceeded). Intended as the outermost wrapper so hooks see full results.
/// </summary>
internal sealed class ResultSizeLimitingFunction : DelegatingAIFunction,
    IDeferredToolMetadata,
    IGeneratedToolMetadata,
    IToolNamespaceMetadata,
    ICanonicalToolIdentityMetadata,
    IOpenAIResponsesFunctionToolMetadata
{
    private readonly int _maxResultChars;
    private readonly string _workspacePath;
    private readonly int _previewLines;

    public ResultSizeLimitingFunction(
        AIFunction innerFunction,
        int maxResultChars,
        string workspacePath,
        int previewLines)
        : base(innerFunction)
    {
        _maxResultChars = maxResultChars;
        _workspacePath = workspacePath;
        _previewLines = previewLines;
    }

    public bool DeferLoading =>
        DeferredToolMetadataResolver.TryGet(InnerFunction, out var metadata) && metadata.DeferLoading;

    public string? DeferredToolSource =>
        DeferredToolMetadataResolver.TryGet(InnerFunction, out var metadata) ? metadata.Source : null;

    public string? DeferredToolNamespace =>
        DeferredToolMetadataResolver.TryGet(InnerFunction, out var metadata) ? metadata.Namespace : null;

    public string? ToolNamespace =>
        ToolNamespaceMetadataResolver.TryGet(InnerFunction, out var toolNamespace) ? toolNamespace : null;

    public string? ToolNamespaceDescription => ToolNamespaceMetadataResolver.GetDescription(InnerFunction);

    public ToolName CanonicalToolName =>
        CanonicalToolIdentityMetadataResolver.TryGet(InnerFunction, out var toolName, out _)
            ? toolName
            : new ToolName(
                ToolNamespaceMetadataResolver.TryGet(InnerFunction, out var toolNamespace)
                    ? toolNamespace
                    : null,
                InnerFunction.Name);

    public string ProviderFlatName =>
        CanonicalToolIdentityMetadataResolver.TryGet(InnerFunction, out _, out var providerFlatName)
            ? providerFlatName
            : ProviderToolProjector.Project([CanonicalToolName])[CanonicalToolName];

    public bool? Strict =>
        InnerFunction is IOpenAIResponsesFunctionToolMetadata metadata ? metadata.Strict : null;

    public bool StreamArgumentsEnabled =>
        !GeneratedToolMetadataResolver.TryGet(InnerFunction, out var metadata) || metadata.StreamArgumentsEnabled;

    public int? MaxResultChars =>
        GeneratedToolMetadataResolver.TryGet(InnerFunction, out var metadata) ? metadata.MaxResultChars : null;

    public string? Icon =>
        GeneratedToolMetadataResolver.TryGet(InnerFunction, out var metadata) ? metadata.Icon : null;

    public Func<IDictionary<string, object?>?, string>? DisplayFormatter =>
        GeneratedToolMetadataResolver.TryGet(InnerFunction, out var metadata) ? metadata.DisplayFormatter : null;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var result = await base.InvokeCoreAsync(arguments, cancellationToken);
        var sessionId = TracingChatClient.CurrentSessionKey;

        try
        {
            return ToolResultProcessor.Process(
                InnerFunction.Name,
                result,
                _maxResultChars,
                _workspacePath,
                sessionId,
                _previewLines);
        }
        catch
        {
            // Spill-to-disk failed (disk full, permissions, etc.).
            // Return the original result rather than losing a successful tool output.
            return result;
        }
    }
}
