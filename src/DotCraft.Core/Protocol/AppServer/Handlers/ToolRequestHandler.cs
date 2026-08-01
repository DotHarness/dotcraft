using DotCraft.Agents;
using DotCraft.Tools;

namespace DotCraft.Protocol.AppServer;

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
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.ToolList, HandleListAsync);
    }

    private Task<object?> HandleListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = AppServerParams.Get<ToolListParams>(msg);
        var planOnly = string.Equals(p.Mode, "plan", StringComparison.OrdinalIgnoreCase);

        var tools = BuiltInToolCatalog.Enumerate()
            .Select(descriptor => new ToolInfoWire
            {
                Name = descriptor.Name,
                Description = descriptor.Description,
                Icon = descriptor.Icon,
                Source = "builtin",
                PlanMode = !ModeToolPolicy.PlanDeniedToolNames.Contains(descriptor.Name)
            })
            .Where(tool => !planOnly || tool.PlanMode)
            .ToList();

        return Task.FromResult<object?>(new ToolListResult { Tools = tools });
    }
}
