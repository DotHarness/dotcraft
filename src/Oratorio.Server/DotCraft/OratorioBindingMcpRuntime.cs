using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Protocol.AppServer;

namespace Oratorio.Server.DotCraft;

/// <summary>Owns the live, binding-scoped MCP authorities. Bearers and sessions never reach disk.</summary>
public sealed class OratorioBindingMcpRuntime(
    IServiceScopeFactory scopeFactory,
    OratorioDynamicToolCatalog dynamicTools)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, BindingAuthority> _bindings = new(StringComparer.Ordinal);

    public string Issue(string bindingId, long authorityRevision)
    {
        var bearer = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _bindings.AddOrUpdate(
            bindingId,
            _ => new BindingAuthority(authorityRevision, bearer),
            (_, current) =>
            {
                current.Cancel();
                return new BindingAuthority(authorityRevision, bearer);
            });
        return bearer;
    }

    public void Revoke(string bindingId)
    {
        if (_bindings.TryRemove(bindingId, out var authority)) authority.Cancel();
    }

    public async Task HandleAsync(HttpContext http, string bindingId)
    {
        if (!_bindings.TryGetValue(bindingId, out var authority) ||
            !TryReadBearer(http, out var bearer) ||
            !FixedTimeEquals(authority.Bearer, bearer))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (HttpMethods.IsDelete(http.Request.Method))
        {
            var session = http.Request.Headers["Mcp-Session-Id"].ToString();
            if (!string.IsNullOrWhiteSpace(session)) authority.Sessions.TryRemove(session, out _);
            http.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        if (!HttpMethods.IsPost(http.Request.Method))
        {
            http.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        JsonDocument document;
        try { document = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted); }
        catch (JsonException)
        {
            await WriteErrorAsync(http, null, -32700, "Parse error.");
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            var id = root.TryGetProperty("id", out var idElement) ? idElement.Clone() : (JsonElement?)null;
            var method = root.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(method))
            {
                await WriteErrorAsync(http, id, -32600, "Invalid request.");
                return;
            }

            if (method == "notifications/initialized")
            {
                http.Response.StatusCode = StatusCodes.Status202Accepted;
                return;
            }

            if (method == "initialize")
            {
                var requestedVersion = root.TryGetProperty("params", out var initializeParams) &&
                    initializeParams.TryGetProperty("protocolVersion", out var versionElement)
                    ? versionElement.GetString()
                    : null;
                var sessionId = Guid.NewGuid().ToString("N");
                authority.Sessions[sessionId] = 0;
                http.Response.Headers["Mcp-Session-Id"] = sessionId;
                await WriteResultAsync(http, id, new
                {
                    protocolVersion = requestedVersion ?? "2025-06-18",
                    capabilities = new { tools = new { listChanged = false }, resources = new { subscribe = false, listChanged = false } },
                    serverInfo = new { name = "oratorio.board", version = "1" },
                    instructions = OratorioBindingMcpCatalog.BoardNamespaceDescription
                });
                return;
            }

            var requestedSession = http.Request.Headers["Mcp-Session-Id"].ToString();
            if (string.IsNullOrWhiteSpace(requestedSession) || !authority.Sessions.ContainsKey(requestedSession))
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            switch (method)
            {
                case "ping":
                    await WriteResultAsync(http, id, new { });
                    return;
                case "tools/list":
                    await WriteResultAsync(http, id, new { tools = OratorioBindingMcpCatalog.McpBoardTools(dynamicTools) });
                    return;
                case "resources/list":
                    await WriteResultAsync(http, id, new { resources = OratorioBindingMcpCatalog.McpAppResources() });
                    return;
                case "resources/templates/list":
                    await WriteResultAsync(http, id, new { resourceTemplates = Array.Empty<object>() });
                    return;
                case "resources/read":
                    await ReadResourceAsync(http, id, root);
                    return;
                case "tools/call":
                    await CallToolAsync(http, id, root, bindingId, authority);
                    return;
                default:
                    await WriteErrorAsync(http, id, -32601, $"Method '{method}' is not supported.");
                    return;
            }
        }
    }

    private async Task CallToolAsync(HttpContext http, JsonElement? id, JsonElement root, string bindingId, BindingAuthority authority)
    {
        if (!_bindings.TryGetValue(bindingId, out var currentAuthority) ||
            !ReferenceEquals(currentAuthority, authority) ||
            currentAuthority.AuthorityRevision != authority.AuthorityRevision)
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var parameters = root.GetProperty("params");
        var name = parameters.GetProperty("name").GetString() ?? string.Empty;
        var arguments = parameters.TryGetProperty("arguments", out var args)
            ? args.Clone()
            : JsonSerializer.SerializeToElement(new { });
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, authority.Token);
        using var scope = scopeFactory.CreateScope();
        var call = new DynamicToolCallParams
        {
            ThreadId = string.Empty,
            TurnId = string.Empty,
            CallId = string.Empty,
            Tool = name,
            Arguments = arguments
        };
        var grant = new OratorioAppBindingGrantContext(bindingId, authority.AuthorityRevision);
        var result = await dynamicTools.InvokeAsync(
            call,
            new OratorioToolInvocationContext(
                scope.ServiceProvider,
                call,
                OratorioToolSurface.AppBinding,
                BindingGrant: grant),
            dynamicTools.BoardDescriptors.Select(x => x.LocalName).ToHashSet(StringComparer.Ordinal),
            linked.Token);
        var content = result.ContentItems?.Select(item => new { type = item.Type, text = item.Text ?? string.Empty }).ToArray();
        if (content is not { Length: > 0 } && !result.Success)
            content = [new { type = "text", text = $"{result.ErrorCode ?? "ToolError"}: {result.ErrorMessage ?? "The Oratorio tool failed."}" }];
        await WriteResultAsync(http, id, new
        {
            content = content ?? [],
            structuredContent = result.StructuredContent,
            isError = !result.Success
        });
    }

    private static async Task ReadResourceAsync(HttpContext http, JsonElement? id, JsonElement root)
    {
        var uri = root.GetProperty("params").GetProperty("uri").GetString();
        var fileName = OratorioBindingMcpCatalog.ResolveUiFile(uri);
        if (fileName is null)
        {
            await WriteErrorAsync(http, id, -32602, "Unknown MCP App resource.");
            return;
        }
        var resourceName = $"Oratorio.Server.UiResources.{fileName}";
        await using var stream = typeof(OratorioBindingMcpRuntime).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            await WriteErrorAsync(http, id, -32603, "MCP App resource is unavailable.");
            return;
        }
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var html = await reader.ReadToEndAsync(http.RequestAborted);
        await WriteResultAsync(http, id, new
        {
            contents = new[] { new { uri, mimeType = "text/html;profile=mcp-app", text = html, _meta = new { ui = new { prefersBorder = true } } } }
        });
    }

    private static bool TryReadBearer(HttpContext http, out string bearer)
    {
        const string prefix = "Bearer ";
        var value = http.Request.Headers.Authorization.ToString();
        bearer = value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? value[prefix.Length..].Trim() : string.Empty;
        return bearer.Length > 0;
    }

    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    private static Task WriteResultAsync(HttpContext http, JsonElement? id, object result) =>
        http.Response.WriteAsJsonAsync(new { jsonrpc = "2.0", id, result }, JsonOptions, http.RequestAborted);

    private static Task WriteErrorAsync(HttpContext http, JsonElement? id, int code, string message) =>
        http.Response.WriteAsJsonAsync(new { jsonrpc = "2.0", id, error = new { code, message } }, JsonOptions, http.RequestAborted);

    private sealed class BindingAuthority(long authorityRevision, string bearer)
    {
        private readonly CancellationTokenSource _lifetime = new();
        public long AuthorityRevision { get; } = authorityRevision;
        public string Bearer { get; } = bearer;
        public CancellationToken Token => _lifetime.Token;
        public ConcurrentDictionary<string, byte> Sessions { get; } = new(StringComparer.Ordinal);
        public void Cancel() => _lifetime.Cancel();
    }
}
