using System.Text.Json;
using DotCraft.Plugins;
using DotCraft.Security;
using DotCraft.Tools;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.ExternalChannel.Messaging;

internal sealed class SendChannelMessageToolProvider(
    ChatBindingResolver resolver,
    PathBlacklist? pathBlacklist = null,
    IRemoteToolHostClientFactory? remoteToolHosts = null) : IThreadPluginToolSourceProvider
{
    public IReadOnlyList<IToolSource> CreateToolSourcesForThread(SessionThread thread)
    {
        if (!ChatBindingResolver.IsRootThread(thread)
            || resolver.Resolve(thread.Id, thread.OriginChannel, thread.ChannelContext) == null)
        {
            return [];
        }

        return [new SendChannelMessageToolSource(new SendChannelMessageRuntime(
            resolver,
            thread.Id,
            thread.OriginChannel,
            thread.ChannelContext,
            thread.WorkspacePath,
            pathBlacklist,
            remoteToolHosts?.Create()))];
    }
}

internal sealed class SendChannelMessageToolSource(SendChannelMessageRuntime runtime) : IToolSource
{
    internal const string ToolName = "SendChannelMessage";
    internal const string PresentationId = "core.channel";
    internal const string Description =
        "Send a message to a chat. People in the chat see only what you send with this tool.";

    internal static readonly JsonElement InputSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "text": { "type": "string" },
            "attachments": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "path": { "type": "string", "description": "Path of a file on this computer." },
                  "kind": { "type": "string", "enum": ["image", "file", "audio", "video"] },
                  "name": { "type": "string" }
                },
                "required": ["path"]
              }
            },
            "to": { "type": "string" }
          }
        }
        """).RootElement.Clone();

    public string SourceId => "channel-message";

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
            policyHints: new ToolPolicyHints(OpenWorld: true),
            presentation: new ToolPresentationDescriptor(new PresentationId(PresentationId)),
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
                ToolExposure.DirectModelOnly,
                ToolInvocationAudience.Model)
        ]);
    }
}
