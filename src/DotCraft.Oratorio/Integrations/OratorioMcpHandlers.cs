using System.Text.Json;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk.DynamicTools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotCraft.Oratorio.Integrations;

internal sealed class OratorioMcpHandlers(
    IServiceScopeFactory scopeFactory,
    OratorioDynamicToolCatalog dynamicTools)
{
    private readonly IReadOnlySet<string> _allowedTools = dynamicTools.McpDescriptors
        .Select(descriptor => descriptor.LocalName)
        .ToHashSet(StringComparer.Ordinal);

    public ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ListToolsResult
        {
            Tools = OratorioMcpCatalog.McpTools(dynamicTools).ToList()
        });

    public async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
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

        using var scope = scopeFactory.CreateScope();
        var result = await dynamicTools.InvokeAsync(
            call,
            new OratorioToolInvocationContext(
                scope.ServiceProvider,
                call,
                OratorioToolSurface.Mcp),
            _allowedTools,
            cancellationToken);

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
            Resources = OratorioMcpCatalog.McpAppResources().ToList()
        });

    public ValueTask<ListResourceTemplatesResult> ListResourceTemplatesAsync(
        RequestContext<ListResourceTemplatesRequestParams> request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ListResourceTemplatesResult());

    public ValueTask<ReadResourceResult> ReadResourceAsync(
        RequestContext<ReadResourceRequestParams> request,
        CancellationToken cancellationToken) =>
        OratorioMcpCatalog.ReadUiResourceAsync(request.Params?.Uri, cancellationToken);
}
