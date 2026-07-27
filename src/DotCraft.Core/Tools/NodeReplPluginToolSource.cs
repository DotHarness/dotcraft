using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Protocol.AppServer;
using DotCraft.Configuration;
using DotCraft.Plugins;

namespace DotCraft.Tools;

/// <summary>
/// Provides the Desktop persistent Node REPL as a plugin-native source with a live proxy binding.
/// </summary>
/// <param name="config">The effective workspace configuration.</param>
/// <param name="proxy">The live Desktop Node REPL proxy.</param>
/// <param name="botPath">The workspace craft directory used for plugin discovery.</param>
/// <param name="isPluginInstalled">An optional deterministic plugin discovery override.</param>
public sealed class NodeReplPluginToolSource(
    AppConfig config,
    INodeReplProxy proxy,
    string botPath = "",
    Func<string, string, bool>? isPluginInstalled = null) : IToolSource
{
    private static readonly string[] RuntimePluginIds = [PluginIds.Browser, PluginIds.Chrome];

    /// <inheritdoc />
    public string SourceId => "node-repl";

    /// <inheritdoc />
    public int Priority => 120;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!proxy.IsAvailable)
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);

        var resolvedBotPath = string.IsNullOrWhiteSpace(botPath)
            ? Path.Combine(context.WorkspacePath, ".craft")
            : botPath;
        var runtimePluginId = RuntimePluginIds.FirstOrDefault(pluginId =>
            config.Plugins.IsPluginEnabled(pluginId, defaultEnabled: true)
            && (isPluginInstalled?.Invoke(context.WorkspacePath, pluginId)
                ?? PluginRuntimeConfigurator.IsPluginInstalledAndEnabled(
                    config,
                    context.WorkspacePath,
                    resolvedBotPath,
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

    private static PluginFunctionDescriptor CreateDescriptor(string pluginId) =>
        new()
        {
            PluginId = pluginId,
            FunctionId = "NodeReplJs",
            Namespace = "node_repl",
            Name = "NodeReplJs",
            Description = "Evaluate JavaScript in the Desktop persistent Node REPL for the current thread. The runtime supports top-level state, agent.browser, display(), and screenshot image output.",
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["code"] = new JsonObject { ["type"] = "string" },
                    ["timeoutSeconds"] = new JsonObject { ["type"] = "integer" }
                },
                ["required"] = new JsonArray("code")
            }
        };

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
