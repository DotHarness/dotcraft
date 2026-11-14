using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.CodexGateway.Workspace;

namespace DotCraft.CodexGateway.Protocol;

public sealed partial class CodexGatewaySession
{
    private static object Projects(string method, JsonElement p, IReadOnlyList<CodexWorkspace> workspaces)
    {
        if (method == "project/read")
        {
            if (workspaces.Count == 0) throw new ProjectRequiredException();
            return new { project = CodexWorkspaceCatalog.Project(CodexWorkspaceCatalog.ById(workspaces, p.Required("projectId"))) };
        }
        var projects = workspaces.Select(route => JsonSerializer.SerializeToNode(CodexWorkspaceCatalog.Project(route))!.AsObject()).ToArray();
        var sort = p.Text("sortKey") ?? "position";
        if (sort is not ("position" or "recencyAt")) throw new ArgumentException("Unsupported project sort key.");
        var direction = p.Text("sortDirection") ?? (sort == "position" ? "asc" : "desc");
        if (direction is not ("asc" or "desc")) throw new ArgumentException("sortDirection must be asc or desc.");
        IEnumerable<JsonObject> ordered = sort == "position"
            ? (direction == "asc" ? projects.OrderBy(v => v["position"]!.GetValue<int>())
                : projects.OrderByDescending(v => v["position"]!.GetValue<int>()))
            : (direction == "asc" ? projects.OrderBy(v => v["recencyAt"]!.GetValue<long>())
                : projects.OrderByDescending(v => v["recencyAt"]!.GetValue<long>()));
        var scope = "projects:" + string.Join(",", workspaces.Select(w => w.Id)) + ":" + sort + ":" + direction;
        var offset = GatewayCursor.Read(p.Text("cursor"), scope)?.Offset ?? 0;
        var limit = p.Limit(100);
        var data = ordered.Skip(offset).Take(limit).ToArray();
        return new
        {
            data,
            nextCursor = offset + data.Length < projects.Length
                ? GatewayCursor.Write(new(scope, Offset: offset + data.Length)) : null
        };
    }
}
