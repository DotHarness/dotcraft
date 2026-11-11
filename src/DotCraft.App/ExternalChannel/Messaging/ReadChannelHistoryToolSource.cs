using System.Text.Json;
using DotCraft.Plugins;
using DotCraft.Tools;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.ExternalChannel.Messaging;

internal sealed class ReadChannelHistoryToolProvider(ChatBindingResolver resolver) : IThreadPluginToolSourceProvider
{
    public IReadOnlyList<IToolSource> CreateToolSourcesForThread(SessionThread thread)
    {
        if (!ChatBindingResolver.IsRootThread(thread)
            || resolver.Resolve(thread.Id, thread.OriginChannel, thread.ChannelContext) == null)
        {
            return [];
        }

        return [new ReadChannelHistoryToolSource(new ReadChannelHistoryRuntime(
            resolver,
            thread.Id,
            thread.OriginChannel,
            thread.ChannelContext))];
    }
}

internal sealed class ReadChannelHistoryToolSource(ReadChannelHistoryRuntime runtime) : IToolSource
{
    internal const string ToolName = "ReadChannelHistory";
    internal const string Description =
        "Read earlier messages in the chat, oldest first. Use 'before' to continue with older ones.";

    internal static readonly JsonElement InputSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "limit": { "type": "integer", "minimum": 1, "maximum": 50, "description": "How many messages to return. Defaults to 20." },
            "before": { "type": "string", "description": "Cursor from an earlier result, to read messages before it." }
          }
        }
        """).RootElement.Clone();

    public string SourceId => "channel-history";

    public int Priority => 60;

    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitionId = new ToolDefinitionId(ToolSourceKind.CoreNative, SourceId, new SourceToolId(ToolName));
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(null, ToolName),
            Description,
            InputSchema,
            policyHints: new ToolPolicyHints(ReadOnly: true, OpenWorld: true),
            provenance: new ToolProvenance(ToolSourceKind.CoreNative, SourceId, "native"),
            policyScope: ToolPolicyScope.RuntimeManaged);
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"native:{SourceId}:{context.ThreadId}:{context.Revision}"),
            definitionId,
            runtime,
            ToolBindingLeases.AlwaysAvailable,
            $"native:{SourceId}",
            context.Revision);
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
        [
            new ToolRegistration(
                definition,
                binding,
                ToolProjectionShape.StandardPair,
                ToolExposure.Direct,
                ToolInvocationAudience.Model)
        ]);
    }
}
