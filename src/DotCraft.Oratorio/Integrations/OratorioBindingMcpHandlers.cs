using System.Text;
using System.Text.Json;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk.DynamicTools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotCraft.Oratorio.Integrations;

/// <summary>Projects Oratorio board tools and MCP App resources through SDK handlers.</summary>
internal sealed class OratorioBindingMcpHandlers(
    IServiceScopeFactory scopeFactory,
    OratorioDynamicToolCatalog dynamicTools,
    OratorioBindingMcpRuntime runtime)
{
    private readonly IReadOnlySet<string> _allowedTools = dynamicTools.BoardDescriptors
        .Select(descriptor => descriptor.LocalName)
        .ToHashSet(StringComparer.Ordinal);

    public ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ListToolsResult
        {
            Tools = OratorioBindingMcpCatalog.McpBoardTools(dynamicTools).ToList()
        });

    public async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        if (!runtime.TryResolve(request.User, out var grant))
            return Failure("AppBindingRevoked", "The Oratorio App Binding authority is no longer active.");

        var parameters = request.Params
            ?? throw new McpProtocolException("Missing tools/call parameters.", McpErrorCode.InvalidParams);
        var arguments = JsonSerializer.SerializeToElement(
            parameters.Arguments ?? new Dictionary<string, JsonElement>(),
            DynamicToolJson.Options);
        var call = new DynamicToolCallParams
        {
            ThreadId = string.Empty,
            TurnId = string.Empty,
            CallId = request.JsonRpcRequest.Id.ToString(),
            Tool = parameters.Name,
            Arguments = arguments
        };

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, grant.Lifetime);
        using var scope = scopeFactory.CreateScope();
        var result = await dynamicTools.InvokeAsync(
            call,
            new OratorioToolInvocationContext(
                scope.ServiceProvider,
                call,
                OratorioToolSurface.AppBinding,
                BindingGrant: new OratorioAppBindingGrantContext(grant.BindingId, grant.AuthorityRevision)),
            _allowedTools,
            linked.Token);

        var content = result.ContentItems?
            .Select(item => (ContentBlock)new TextContentBlock
            {
                Text = item.Text ?? string.Empty
            })
            .ToList() ?? [];
        if (content.Count == 0 && !result.Success)
        {
            content.Add(new TextContentBlock
            {
                Text = $"{result.ErrorCode ?? "ToolError"}: {result.ErrorMessage ?? "The Oratorio tool failed."}"
            });
        }

        return new CallToolResult
        {
            Content = content,
            StructuredContent = result.StructuredContent,
            IsError = !result.Success
        };
    }

    public ValueTask<ListResourcesResult> ListResourcesAsync(
        RequestContext<ListResourcesRequestParams> request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ListResourcesResult
        {
            Resources = OratorioBindingMcpCatalog.McpAppResources().ToList()
        });

    public ValueTask<ListResourceTemplatesResult> ListResourceTemplatesAsync(
        RequestContext<ListResourceTemplatesRequestParams> request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ListResourceTemplatesResult());

    public async ValueTask<ReadResourceResult> ReadResourceAsync(
        RequestContext<ReadResourceRequestParams> request,
        CancellationToken cancellationToken)
    {
        var uri = request.Params?.Uri
            ?? throw new McpProtocolException("Missing resources/read URI.", McpErrorCode.InvalidParams);
        var fileName = OratorioBindingMcpCatalog.ResolveUiFile(uri);
        if (fileName is null)
            throw new McpProtocolException("Unknown MCP App resource.", McpErrorCode.InvalidParams);

        var resourceName = $"DotCraft.Oratorio.UiResources.{fileName}";
        await using var stream = typeof(OratorioBindingMcpHandlers).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new McpProtocolException("MCP App resource is unavailable.", McpErrorCode.InternalError);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var html = await reader.ReadToEndAsync(cancellationToken);
        return new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = uri,
                    MimeType = "text/html;profile=mcp-app",
                    Text = html,
                    Meta = OratorioBindingMcpCatalog.ResourceMeta()
                }
            ]
        };
    }

    private static CallToolResult Failure(string code, string message) => new()
    {
        Content = [new TextContentBlock { Text = $"{code}: {message}" }],
        StructuredContent = JsonSerializer.SerializeToElement(new { error = new { code, message } }),
        IsError = true
    };
}
