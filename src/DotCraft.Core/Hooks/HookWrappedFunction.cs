using DotCraft.Agents;
using DotCraft.Plugins;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Hooks;

/// <summary>
/// Wraps an <see cref="AIFunction"/> with pre/post hook execution using
/// <see cref="DelegatingAIFunction"/> from Microsoft.Extensions.AI.
/// <para>
/// Before execution: runs <see cref="HookEvent.PreToolUse"/> hooks.
/// If any hook returns exit code 2, the tool call is blocked and an error string is returned.
/// </para>
/// <para>
/// After execution: runs <see cref="HookEvent.PostToolUse"/> (on success) or
/// <see cref="HookEvent.PostToolUseFailure"/> (on exception) hooks.
/// </para>
/// </summary>
internal sealed class HookWrappedFunction : DelegatingAIFunction, IPluginFunctionTool, IDeferredToolMetadata, IGeneratedToolMetadata, IToolNamespaceMetadata
{
    private readonly HookRunner _hookRunner;

    public HookWrappedFunction(AIFunction innerFunction, HookRunner hookRunner)
        : base(innerFunction)
    {
        _hookRunner = hookRunner;
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
        var toolName = InnerFunction.Name;

        // ── Phase 1: PreToolUse ──
        var preInput = new HookInput
        {
            SessionId = TracingChatClient.CurrentSessionKey,
            ToolName = toolName,
            ToolArgs = ArgumentsToDict(arguments)
        };

        var preResult = await _hookRunner.RunAsync(
            HookEvent.PreToolUse, preInput, cancellationToken);

        if (preResult.Blocked)
        {
            return $"Tool call blocked by hook: {preResult.BlockReason ?? "no reason given"}";
        }

        // ── Phase 2: Execute actual tool ──
        try
        {
            var result = await base.InvokeCoreAsync(arguments, cancellationToken);

            // ── Phase 3a: PostToolUse (success) ──
            var postInput = new HookInput
            {
                SessionId = TracingChatClient.CurrentSessionKey,
                ToolName = toolName,
                ToolArgs = ArgumentsToDict(arguments),
                ToolResult = ImageContentSanitizingChatClient.DescribeResult(result)
            };

            var postResult = await _hookRunner.RunAsync(
                HookEvent.PostToolUse, postInput, cancellationToken);
            ToolHookFeedbackScope.Current?.Add(HookEvent.PostToolUse, postResult);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw; // Don't run failure hooks on cancellation
        }
        catch (Exception ex)
        {
            // ── Phase 3b: PostToolUseFailure ──
            var failInput = new HookInput
            {
                SessionId = TracingChatClient.CurrentSessionKey,
                ToolName = toolName,
                ToolArgs = ArgumentsToDict(arguments),
                Error = ex.Message
            };

            var failResult = await _hookRunner.RunAsync(
                HookEvent.PostToolUseFailure, failInput, cancellationToken);
            ToolHookFeedbackScope.Current?.Add(HookEvent.PostToolUseFailure, failResult);

            throw; // Re-throw original exception
        }
    }

    /// <summary>
    /// Converts <see cref="AIFunctionArguments"/> to a simple dictionary for JSON serialization.
    /// </summary>
    private static Dictionary<string, object?> ArgumentsToDict(AIFunctionArguments arguments)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var kvp in arguments)
        {
            dict[kvp.Key] = kvp.Value;
        }
        return dict;
    }
}
