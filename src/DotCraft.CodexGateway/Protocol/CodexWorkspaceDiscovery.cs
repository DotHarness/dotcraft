using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.CodexGateway.Workspace;

namespace DotCraft.CodexGateway.Protocol;

internal sealed class CodexWorkspaceDiscovery(string stateDirectory, CodexWorkspaceCatalog catalog)
{
    private readonly string _resourcePath = Path.GetFullPath(Path.Combine(stateDirectory, ".codex-global-state.json"));

    public bool TryHandle(string method, JsonElement parameters, IReadOnlyList<CodexWorkspace> workspaces, out object? result)
    {
        result = null;
        switch (method)
        {
            case "fs/readFile":
            case "fs/writeFile":
                ValidateResource(parameters);
                if (method == "fs/readFile")
                    result = new { dataBase64 = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(catalog.ReadState(workspaces))) };
                else
                {
                    var encoded = parameters.Required("dataBase64");
                    byte[] data;
                    try { data = Convert.FromBase64String(encoded); }
                    catch (FormatException exception) { throw new ArgumentException("Invalid base64 file content.", exception); }
                    var state = JsonNode.Parse(data) as JsonObject
                        ?? throw new ArgumentException("Project state must be a JSON object.");
                    catalog.WriteState(state, workspaces);
                    result = new { };
                }
                return true;
            case "plugin/installed":
                result = new { marketplaces = Array.Empty<object>(), marketplaceLoadErrors = Array.Empty<object>() };
                return true;
            case "skills/list":
                result = new { data = Array.Empty<object>() };
                return true;
            case "threadSection/list":
                result = new { data = Array.Empty<object>(), nextCursor = (string?)null };
                return true;
            default:
                return false;
        }
    }

    private void ValidateResource(JsonElement parameters)
    {
        var path = parameters.Required("path");
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFullPath(path), _resourcePath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Only the gateway workspace discovery resource is available.");
    }
}

internal sealed record GatewayCursor(string Scope, long? Timestamp = null, string? Id = null, int Offset = 0, bool Inclusive = false)
{
    public static string Write(GatewayCursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor));

    public static GatewayCursor? Read(string? value, string scope)
    {
        if (value is null) return null;
        try
        {
            var result = JsonSerializer.Deserialize<GatewayCursor>(Convert.FromBase64String(value));
            if (result is null || result.Scope != scope || result.Offset < 0)
                throw new ArgumentException("Cursor does not match this gateway query.");
            return result;
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            throw new ArgumentException("Invalid gateway cursor.", exception);
        }
    }
}
