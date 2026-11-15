using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;

namespace DotCraft.CodexGateway.Tests;

internal sealed class GatewayApprovalTool : IToolSource, IToolRuntime
{
    public const string Name = "GatewayApproval";
    private int _executions;
    public string SourceId => "gateway-approval-fixture";
    public int Executions => Volatile.Read(ref _executions);

    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context, CancellationToken cancellationToken = default)
    {
        var id = new ToolDefinitionId(ToolSourceKind.CoreNative, SourceId, new SourceToolId(Name));
        var definition = new ToolDefinition(id, new ToolName(null, Name), "Perform an approval-protected fixture operation.",
            JsonSerializer.SerializeToElement(new
            {
                type = "object", properties = new { path = new { type = "string" } }, required = new[] { "path" }
            }),
            annotations: new Dictionary<string, JsonElement>
            {
                ["dotcraft/nativeApproval"] = JsonSerializer.SerializeToElement(new
                {
                    kind = "file", operation = "write", targetArgument = "path"
                })
            },
            policyHints: new ToolPolicyHints(RequiresApproval: true));
        var binding = new ToolRuntimeBinding(new RuntimeBindingId(SourceId), id, this,
            ToolBindingLeases.AlwaysAvailable, SourceId, revision: 1);
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
            [new(definition, binding, ToolProjectionShape.StandardPair, ToolExposure.Direct,
                ToolInvocationAudience.Model | ToolInvocationAudience.Host)]);
    }

    public ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context, JsonObject arguments, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _executions);
        return ValueTask.FromResult(ToolExecutionResult.Succeeded("approved operation"));
    }
}
