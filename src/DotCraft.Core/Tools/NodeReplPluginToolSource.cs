using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Plugins;

namespace DotCraft.Tools;

internal interface INodeReplToolDeclaration
{
    [ToolDeclaration(Name = "NodeReplJs")]
    [Description("Execute JavaScript in the current thread's persistent Node REPL with top-level await and dynamic imports. Use nodeRepl.write(value) for text and await nodeRepl.emitImage(image) for images. Outer timeout or cancellation clears JavaScript state.")]
    void Evaluate(
        [Description("JavaScript code to execute.")] string code,
        [Description("Optional execution timeout in seconds. Defaults to 30 seconds.")] int timeoutSeconds = 0);
}

/// <summary>
/// Provides the Desktop persistent Node REPL as a plugin-native source with a live proxy binding.
/// </summary>
/// <param name="config">The effective workspace configuration.</param>
/// <param name="proxy">The live Desktop Node REPL proxy.</param>
/// <param name="dataPath">The resolved workspace data directory used for plugin discovery.</param>
/// <param name="isPluginInstalled">An optional deterministic plugin discovery override.</param>
public sealed class NodeReplPluginToolSource(
    AppConfig config,
    INodeReplProxy proxy,
    string dataPath,
    Func<string, string, bool>? isPluginInstalled = null) : IToolSource, IThreadForkToolBindingSource
{
    private static readonly string[] BrowserPluginIds = [PluginIds.Browser, PluginIds.Chrome];
    private static readonly string[] ComputerPluginIds = [PluginIds.Computer];
    private readonly string _dataPath = !string.IsNullOrWhiteSpace(dataPath)
        ? dataPath
        : throw new ArgumentException("A data path is required.", nameof(dataPath));

    /// <inheritdoc />
    public string SourceId => "node-repl";

    /// <inheritdoc />
    public int Priority => 120;

    bool IThreadForkToolBindingSource.TryForkThreadBinding(string parentThreadId, string childThreadId)
        => proxy is IThreadForkToolBindingSource forkable
           && forkable.TryForkThreadBinding(parentThreadId, childThreadId);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!proxy.IsAvailable)
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);

        IEnumerable<string> candidates = [
            .. proxy.IsBrowserUseAvailable ? BrowserPluginIds : [],
            .. proxy.IsComputerUseAvailable ? ComputerPluginIds : []
        ];
        var runtimePluginId = candidates.FirstOrDefault(pluginId =>
            config.Plugins.IsPluginEnabled(pluginId, defaultEnabled: true)
            && (isPluginInstalled?.Invoke(context.WorkspacePath, pluginId)
                ?? PluginRuntimeConfigurator.IsPluginInstalledAndEnabled(
                    config,
                    context.WorkspacePath,
                    _dataPath,
                    pluginId)));
        if (runtimePluginId is null)
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);

        var source = new PluginToolSource(
            runtimePluginId,
            [new PluginToolRegistration(CreateDescriptor(runtimePluginId), new NodeReplPluginToolInvoker(proxy))],
            bindingLease: new NodeReplPluginLease(config, proxy, runtimePluginId),
            priority: Priority);
        return source.GetRegistrationsAsync(context, cancellationToken);
    }

    private static PluginFunctionDescriptor CreateDescriptor(string pluginId)
    {
        var declaration = DotCraft.GeneratedTools.Core.GeneratedToolDeclarations.INodeReplToolDeclaration_Evaluate_Declaration;
        return new PluginFunctionDescriptor
        {
            PluginId = pluginId,
            FunctionId = declaration.Name,
            Namespace = "node_repl",
            Name = declaration.Name,
            Description = declaration.Description,
            InputSchema = JsonNode.Parse(declaration.InputSchema.GetRawText())!.AsObject()
        };
    }

    private sealed class NodeReplPluginToolInvoker(INodeReplProxy proxy) : IPluginToolInvoker
    {
        public async ValueTask<PluginFunctionInvocationResult> InvokeAsync(
            PluginToolInvocationContext context,
            CancellationToken cancellationToken)
        {
            var code = context.Arguments["code"]?.GetValue<string>();
            int? timeoutSeconds = null;
            if (context.Arguments.TryGetPropertyValue("timeoutSeconds", out var timeoutNode)
                && timeoutNode?.GetValueKind() == JsonValueKind.Number)
            {
                timeoutSeconds = timeoutNode.GetValue<int>();
            }

            var result = await proxy.EvaluateAsync(
                code ?? string.Empty,
                timeoutSeconds,
                cancellationToken,
                new NodeReplEvaluationMetadata
                {
                    ThreadId = context.Invocation.ThreadId,
                    SessionId = context.Invocation.ThreadId,
                    TurnId = context.Invocation.TurnId,
                    ProtocolVersion = 1
                });
            if (result is null)
            {
                return PluginFunctionInvocationResult.Failed(
                    "NodeReplUnavailable",
                    "Node REPL browser runtime is not available for this thread.");
            }

            var contentItems = new List<PluginFunctionContentItem>();
            var textParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Text))
                textParts.Add(result.Text);
            if (!string.IsNullOrWhiteSpace(result.ResultText))
                textParts.Add(result.ResultText);
            if (result.Logs.Count > 0)
                textParts.Add(string.Join("\n", result.Logs));
            if (!string.IsNullOrWhiteSpace(result.Error))
                textParts.Add("Error: " + result.Error);

            contentItems.Add(new PluginFunctionContentItem
            {
                Type = "text",
                Text = textParts.Count > 0
                    ? string.Join("\n", textParts)
                    : "(Node REPL completed with no text output)"
            });
            foreach (var image in result.Images.Where(image => !string.IsNullOrWhiteSpace(image.DataBase64)))
            {
                contentItems.Add(new PluginFunctionContentItem
                {
                    Type = "image",
                    DataBase64 = image.DataBase64,
                    MediaType = image.MediaType
                });
            }

            return new PluginFunctionInvocationResult
            {
                Success = string.IsNullOrWhiteSpace(result.Error),
                ErrorCode = string.IsNullOrWhiteSpace(result.Error) ? null : "NodeReplError",
                ErrorMessage = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error,
                ContentItems = contentItems
            };
        }
    }

    private sealed class NodeReplPluginLease(
        AppConfig config,
        INodeReplProxy proxy,
        string pluginId) : IToolBindingLease
    {
        public ValueTask<ToolBindingLeaseResult> CheckAsync(
            ToolInvocationContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var available = proxy.IsAvailable
                && config.Plugins.IsPluginEnabled(pluginId, defaultEnabled: true);
            return ValueTask.FromResult(available
                ? ToolBindingLeaseResult.Available
                : ToolBindingLeaseResult.Unavailable("The Node REPL plugin runtime is disconnected or disabled."));
        }
    }
}
