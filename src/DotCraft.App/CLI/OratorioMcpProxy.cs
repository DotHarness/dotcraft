using DotCraft.Hub;
using DotCraft.Oratorio.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotCraft.CLI;

internal sealed class OratorioMcpProxy : IAsyncDisposable
{
    private readonly string? _explicitUrl;
    private readonly string? _explicitToken;
    private readonly OratorioDynamicToolCatalog _catalog = new(NullLogger<OratorioDynamicToolCatalog>.Instance);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private McpClient? _upstream;

    public OratorioMcpProxy(string? explicitUrl = null, string? explicitToken = null)
    {
        _explicitUrl = string.IsNullOrWhiteSpace(explicitUrl) ? null : explicitUrl.Trim().TrimEnd('/');
        _explicitToken = string.IsNullOrWhiteSpace(explicitToken) ? null : explicitToken.Trim();
    }

    public static async Task<int> RunStdioAsync(
        string? explicitUrl,
        string? explicitToken,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var proxy = new OratorioMcpProxy(explicitUrl, explicitToken);
            var builder = Host.CreateEmptyApplicationBuilder(settings: null);
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(proxy);
            builder.Services.AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation { Name = "oratorio", Version = "1" };
                    options.ServerInstructions = OratorioMcpCatalog.ServerInstructions;
                })
                .WithStdioServerTransport()
                .WithListToolsHandler((request, ct) =>
                    request.Services!.GetRequiredService<OratorioMcpProxy>().ListToolsAsync(ct))
                .WithCallToolHandler((request, ct) =>
                    request.Services!.GetRequiredService<OratorioMcpProxy>().CallToolAsync(request, ct))
                .WithListResourcesHandler((request, ct) =>
                    request.Services!.GetRequiredService<OratorioMcpProxy>().ListResourcesAsync(ct))
                .WithListResourceTemplatesHandler((_, _) =>
                    ValueTask.FromResult(new ListResourceTemplatesResult()))
                .WithReadResourceHandler((request, ct) =>
                    request.Services!.GetRequiredService<OratorioMcpProxy>().ReadResourceAsync(request, ct));

            using var host = builder.Build();
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync($"Oratorio MCP bridge failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    public ValueTask<ListToolsResult> ListToolsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ListToolsResult
        {
            Tools = OratorioMcpCatalog.McpTools(_catalog).ToList()
        });
    }

    public ValueTask<ListResourcesResult> ListResourcesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ListResourcesResult
        {
            Resources = OratorioMcpCatalog.McpAppResources().ToList()
        });
    }

    public ValueTask<ReadResourceResult> ReadResourceAsync(
        RequestContext<ReadResourceRequestParams> request,
        CancellationToken cancellationToken) =>
        OratorioMcpCatalog.ReadUiResourceAsync(request.Params?.Uri, cancellationToken);

    public async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        var parameters = request.Params
            ?? throw new McpProtocolException("Missing tools/call parameters.", McpErrorCode.InvalidParams);
        var arguments = parameters.Arguments?.ToDictionary(
            static pair => pair.Key,
            static pair => (object?)pair.Value,
            StringComparer.Ordinal);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var client = await EnsureUpstreamAsync(forceReconnect: attempt > 0, cancellationToken).ConfigureAwait(false);
            try
            {
                return await client.CallToolAsync(
                    parameters.Name,
                    arguments,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (attempt == 0 && !cancellationToken.IsCancellationRequested)
            {
                await ResetUpstreamAsync().ConfigureAwait(false);
            }
        }

        throw new McpProtocolException("Oratorio MCP service is unavailable.", McpErrorCode.InternalError);
    }

    private async Task<McpClient> EnsureUpstreamAsync(bool forceReconnect, CancellationToken cancellationToken)
    {
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceReconnect && _upstream is not null)
                return _upstream;

            await DisposeUpstreamLockedAsync().ConfigureAwait(false);

            string baseUrl;
            string? bearer;
            if (_explicitUrl is not null)
            {
                baseUrl = _explicitUrl;
                bearer = _explicitToken;
            }
            else
            {
                var hub = new HubClient();
                var service = await hub.EnsureManagedServiceAsync("oratorio", cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(service.Endpoint))
                    throw new InvalidOperationException(service.LastError ?? "Hub did not return an Oratorio endpoint.");
                baseUrl = service.Endpoint.TrimEnd('/');
                bearer = service.AccessToken;
            }

            var endpoint = baseUrl.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase)
                ? baseUrl
                : $"{baseUrl}/mcp";
            var transportOptions = new HttpClientTransportOptions
            {
                Endpoint = new Uri(endpoint, UriKind.Absolute),
                Name = "oratorio",
                TransportMode = HttpTransportMode.AutoDetect,
                AdditionalHeaders = string.IsNullOrWhiteSpace(bearer)
                    ? null
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Authorization"] = $"Bearer {bearer}"
                    }
            };

            var transport = new HttpClientTransport(transportOptions);
            _upstream = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
            return _upstream;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task ResetUpstreamAsync()
    {
        await _connectLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeUpstreamLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task DisposeUpstreamLockedAsync()
    {
        var current = _upstream;
        _upstream = null;
        if (current is not null)
            await current.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await ResetUpstreamAsync().ConfigureAwait(false);
        _connectLock.Dispose();
    }
}
