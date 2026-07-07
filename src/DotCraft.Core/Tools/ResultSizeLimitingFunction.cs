using DotCraft.Tracing;
using DotCraft.Plugins;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Wraps an <see cref="AIFunction"/> to normalize empty results and enforce per-tool result size limits
/// (spill-to-disk with preview when exceeded). Intended as the outermost wrapper so hooks see full results.
/// </summary>
internal sealed class ResultSizeLimitingFunction : DelegatingAIFunction, IPluginFunctionTool, IDeferredToolMetadata, IGeneratedToolMetadata, IToolNamespaceMetadata
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

    public PluginFunctionDescriptor? PluginFunctionDescriptor =>
        InnerFunction is IPluginFunctionTool pluginFunction
            ? pluginFunction.PluginFunctionDescriptor
            : null;

    public bool DeferLoading =>
        DeferredToolMetadataResolver.TryGet(InnerFunction, out var metadata) && metadata.DeferLoading;

    public string? DeferredToolSource =>
        DeferredToolMetadataResolver.TryGet(InnerFunction, out var metadata) ? metadata.Source : null;

    public string? DeferredToolNamespace =>
        DeferredToolMetadataResolver.TryGet(InnerFunction, out var metadata) ? metadata.Namespace : null;

    public string? ToolNamespace =>
        ToolNamespaceMetadataResolver.TryGet(InnerFunction, out var toolNamespace) ? toolNamespace : null;

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
