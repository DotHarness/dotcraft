using DotCraft.Agents;
using DotCraft.Tools;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>
/// Handles the <c>tool/list</c> wire method (spec Section 18A): the built-in tool catalog used by
/// clients to render a tool picker for Agent Profile <c>tools.allow</c> / <c>tools.deny</c> lists.
///
/// The catalog is derived from server reflection (<see cref="BuiltInToolCatalog"/>) and carries no
/// workspace, thread, or per-connection state, so this handler has no dependencies. Plan-mode
/// availability is annotated from <see cref="ModeToolPolicy.PlanDeniedToolNames"/>.
/// </summary>
internal sealed class ToolRequestHandler : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.ToolList, HandleListAsync);
    }

    private Task<AppServerTypedResult<Contract.ToolListResult>> HandleListAsync(
        AppServerTypedRequest<Contract.ToolListParams> request,
        CancellationToken ct)
    {
        _ = ct;
        var mode = request.Params.Mode.IsSet ? request.Params.Mode.Value : null;
        var planOnly = string.Equals(mode, "plan", StringComparison.OrdinalIgnoreCase);

        var tools = BuiltInToolCatalog.Enumerate()
            .Select(descriptor => new Contract.ToolInfo
            {
                Name = descriptor.Name,
                Description = descriptor.Description,
                Icon = descriptor.Icon,
                Source = "builtin",
                PlanMode = !ModeToolPolicy.PlanDeniedToolNames.Contains(descriptor.Name)
            })
            .Where(tool => !planOnly || (tool.PlanMode.IsSet && tool.PlanMode.Value))
            .ToList();

        return Task.FromResult(AppServerTypedResult<Contract.ToolListResult>.FromResult(
            new Contract.ToolListResult { Tools = tools }));
    }
}
