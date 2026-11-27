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
    internal const string OratorioUrlEnvVar = "ORATORIO_URL";
    internal const string OratorioServiceTokenEnvVar = "ORATORIO_SERVICE_TOKEN";

    private readonly string? _explicitUrl;
    private readonly string? _explicitToken;
    private readonly OratorioDynamicToolCatalog _catalog = new(NullLogger<OratorioDynamicToolCatalog>.Instance);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private McpClient? _upstream;

    public OratorioMcpProxy(string? explicitUrl = null, string? explicitToken = null)
    {
        (_explicitUrl, _explicitToken) = ResolveConfiguredTarget(
            explicitUrl,
            explicitToken,
            Environment.GetEnvironmentVariable);
    }

    internal static (string? Url, string? Token) ResolveConfiguredTarget(
        string? explicitUrl,
        string? explicitToken,
        Func<string, string?> getEnvironmentVariable)
    {
        var rawUrl = string.IsNullOrWhiteSpace(explicitUrl)
            ? getEnvironmentVariable(OratorioUrlEnvVar)
            : explicitUrl;
        var url = string.IsNullOrWhiteSpace(rawUrl) ? null : rawUrl.Trim().TrimEnd('/');
        if (url is null)
            return (null, null);

        var rawToken = string.IsNullOrWhiteSpace(explicitToken)
            ? getEnvironmentVariable(OratorioServiceTokenEnvVar)
            : explicitToken;
        var token = string.IsNullOrWhiteSpace(rawToken) ? null : rawToken.Trim();
        return (url, token);
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

    public ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken) =>
        CallToolAsync(request.Params, cancellationToken);

    internal async ValueTask<CallToolResult> CallToolAsync(
        CallToolRequestParams? parameters,
        CancellationToken cancellationToken)
    {
        if (parameters is null)
            throw new McpProtocolException("Missing tools/call parameters.", McpErrorCode.InvalidParams);
        var arguments = parameters.Arguments?.ToDictionary(
            static pair => pair.Key,
            static pair => (object?)pair.Value,
            StringComparer.Ordinal);

        var client = await EnsureUpstreamAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await client.CallToolAsync(
                parameters.Name,
                arguments,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await ResetUpstreamAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<McpClient> EnsureUpstreamAsync(CancellationToken cancellationToken)
    {
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_upstream is not null)
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
