using System.Text.Json;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk.AppBinding;
using DotCraft.Sdk;
using DotCraft.Sdk.Wire;
using DotCraft.Oratorio.Api;
using SdkClient = DotCraft.Sdk.DotCraftClient;
using SdkClientOptions = DotCraft.Sdk.DotCraftRemoteOptions;

namespace DotCraft.Oratorio.Integrations;

public interface IDotCraftAppServerClientFactory
{
    Task<IDotCraftAppServerClient> ConnectAsync(string appServerUrl, CancellationToken ct, string? token = null);
}

public interface IDotCraftAppServerClient : IAsyncDisposable
{
    bool SupportsDynamicToolRebind { get; }
    bool SupportsRuntimeAdditionalContext { get; }
    DotCraftAppBindingClient AppBindings { get; }
    void SetDynamicToolHandler(Func<DynamicToolCallParams, CancellationToken, Task<DynamicToolCallResult>> handler);
    Task<string> StartThreadAsync(AppServerThreadStartRequest request, CancellationToken ct);
    Task ResumeThreadAsync(
        string threadId,
        IReadOnlyList<RuntimeDynamicToolDeclaration>? dynamicTools,
        IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry>? runtimeAdditionalContext,
        CancellationToken ct);
    Task SubscribeThreadAsync(string threadId, CancellationToken ct);
    Task<string?> StartTurnAsync(string threadId, string prompt, CancellationToken ct);
    Task<string?> StartTurnAsync(string threadId, IReadOnlyList<TurnInputPartDto> input, string? modelId, CancellationToken ct);
    Task<string?> EnqueueTurnAsync(string threadId, IReadOnlyList<TurnInputPartDto> input, CancellationToken ct);
    Task InterruptTurnAsync(string threadId, string turnId, CancellationToken ct);
    Task<AppServerThreadReadResult> ReadThreadAsync(string threadId, CancellationToken ct);
    Task<IReadOnlyList<ModelInfoDto>> ListModelsAsync(CancellationToken ct);
    IAsyncEnumerable<DotCraftRunEvent> ReadEventsAsync(CancellationToken ct);
}

public sealed record AppServerThreadStartRequest(
    string DisplayName,
    string BaseWorkspacePath,
    string ExecutionWorkspacePath,
    string ApprovalPolicy,
    string AgentInstructions,
    IReadOnlyList<RuntimeDynamicToolDeclaration>? DynamicTools = null,
    IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry>? RuntimeAdditionalContext = null);

public sealed record AppServerRuntimeAdditionalContextEntry(
    string Value,
    string Kind = "application");

public sealed record AppServerThreadReadResult(string ThreadId, IReadOnlyList<ConversationItemDto> Items);

public sealed class DotCraftAppServerClientFactory : IDotCraftAppServerClientFactory
{
    public async Task<IDotCraftAppServerClient> ConnectAsync(string appServerUrl, CancellationToken ct, string? token = null)
    {
        var client = await SdkClient.ConnectRemoteAsync(
            appServerUrl,
            CreateClientOptions(token),
            ct);
        return new DotCraftAppServerClient(client);
    }

    internal static SdkClientOptions CreateClientOptions(string? token = null) =>
        new()
        {
            Token = token,
            ClientName = "oratorio",
            ClientVersion = "0.5.2",
            AutoReconnect = false,
            ApprovalSupport = false,
            StreamingSupport = true
        };
}

public sealed class DotCraftAppServerClient(SdkClient client) : IDotCraftAppServerClient
{
    private IDisposable? _dynamicToolRegistration;

    public bool SupportsDynamicToolRebind => client.Capabilities.DynamicToolRebind;

    public bool SupportsRuntimeAdditionalContext => client.Capabilities.RuntimeAdditionalContext;

    public DotCraftAppBindingClient AppBindings => client.AppBindings;

    public void SetDynamicToolHandler(Func<DynamicToolCallParams, CancellationToken, Task<DynamicToolCallResult>> handler)
    {
        _dynamicToolRegistration?.Dispose();
        _dynamicToolRegistration = client.RegisterDynamicToolHandler(handler);
    }

    public async Task<string> StartThreadAsync(AppServerThreadStartRequest request, CancellationToken ct)
    {
        var executionWorkspaceOverride = SamePath(request.BaseWorkspacePath, request.ExecutionWorkspacePath)
            ? null
            : request.ExecutionWorkspacePath;
        var configuration = executionWorkspaceOverride is null
            ? new ThreadConfiguration
            {
                Mode = "agent",
                ApprovalPolicy = request.ApprovalPolicy,
                RequireApprovalOutsideWorkspace = true,
                AgentInstructions = request.AgentInstructions
            }
            : new ThreadConfiguration
            {
                Mode = "agent",
                ExecutionWorkspaceOverride = executionWorkspaceOverride,
                ApprovalPolicy = request.ApprovalPolicy,
                RequireApprovalOutsideWorkspace = true,
                AgentInstructions = request.AgentInstructions
            };
        var thread = await client.Threads.StartAsync(new ThreadStartParams
        {
            Identity = new SessionIdentity
            {
                ChannelName = "oratorio",
                UserId = "operator",
                WorkspacePath = request.BaseWorkspacePath,
                ChannelContext = "oratorio:dotcraft-bridge"
            },
            DisplayName = request.DisplayName,
            HistoryMode = "none",
            Config = configuration,
            DynamicTools = request.DynamicTools,
            AdditionalContext = ToSdkAdditionalContext(request.RuntimeAdditionalContext)
        }, ct);
        return thread.Id;
    }

    private static bool SamePath(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }

    public async Task ResumeThreadAsync(
        string threadId,
        IReadOnlyList<RuntimeDynamicToolDeclaration>? dynamicTools,
        IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry>? runtimeAdditionalContext,
        CancellationToken ct)
    {
        await client.Threads.ResumeAsync(new ThreadResumeParams
        {
            ThreadId = threadId,
            DynamicTools = dynamicTools,
            AdditionalContext = ToSdkAdditionalContext(runtimeAdditionalContext)
        }, ct);
    }

    public Task SubscribeThreadAsync(string threadId, CancellationToken ct) =>
        client.Threads.SubscribeAsync(threadId, cancellationToken: ct);

    public Task<string?> StartTurnAsync(string threadId, string prompt, CancellationToken ct) =>
        StartTurnAsync(
            threadId,
            [new TurnInputPartDto("text", prompt, null, null, null, null, null, null)],
            modelId: null,
            ct);

    public async Task<string?> StartTurnAsync(string threadId, IReadOnlyList<TurnInputPartDto> input, string? modelId, CancellationToken ct)
    {
        _ = modelId; // turn/start has no model field; model selection belongs to thread configuration.
        var result = await client.Turns.StartAsync(threadId, NormalizeInput(input), cancellationToken: ct);
        return result.Turn.Id;
    }

    public async Task<string?> EnqueueTurnAsync(string threadId, IReadOnlyList<TurnInputPartDto> input, CancellationToken ct)
    {
        var result = await client.Turns.EnqueueAsync(threadId, NormalizeInput(input), cancellationToken: ct);
        return result.QueuedInput.Id;
    }

    public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken ct) =>
        client.Turns.InterruptAsync(threadId, turnId, ct);

    public async Task<AppServerThreadReadResult> ReadThreadAsync(string threadId, CancellationToken ct)
    {
        var page = await client.Threads.ListItemsAsync(new ThreadItemsListParams
        {
            ThreadId = threadId,
            Limit = 200,
            SortDirection = "descending"
        }, ct);
        var items = page.Data
            .Reverse()
            .Select(entry => ToConversationItem(entry.Item))
            .ToArray();

        return new AppServerThreadReadResult(threadId, items);
    }

    public async Task<IReadOnlyList<ModelInfoDto>> ListModelsAsync(CancellationToken ct)
    {
        try
        {
            var catalog = await client.Models.GetCatalogAsync(cancellationToken: ct);
            var providerId = catalog.ProviderId.IsSet ? catalog.ProviderId.Value : null;
            return (catalog.Models.IsSet ? catalog.Models.Value : null)
                ?.Where(model => model.Id.IsSet && !string.IsNullOrWhiteSpace(model.Id.Value))
                .Select(model => new ModelInfoDto(model.Id.Value!, model.Id.Value!, providerId))
                .ToArray() ?? [];
        }
        catch (JsonRpcException ex) when (ex.RpcCode == -32601)
        {
            return [];
        }
    }

    public async IAsyncEnumerable<DotCraftRunEvent> ReadEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var notification in client.ReadNotificationsAsync(ct))
            yield return AppServerRunEventFactory.Parse(notification);
    }

    public async ValueTask DisposeAsync()
    {
        _dynamicToolRegistration?.Dispose();
        await client.DisposeAsync();
    }

    private static IReadOnlyDictionary<string, RuntimeAdditionalContextEntry>? ToSdkAdditionalContext(
        IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry>? additionalContext) =>
        additionalContext?.ToDictionary(
            entry => entry.Key,
            entry => new RuntimeAdditionalContextEntry
            {
                Value = entry.Value.Value,
                Kind = entry.Value.Kind
            },
            StringComparer.Ordinal);

    private static IReadOnlyList<InputPart> NormalizeInput(IReadOnlyList<TurnInputPartDto> input) =>
        input
            .Where(part => !string.IsNullOrWhiteSpace(part.Type))
            .Select(part => new InputPart
            {
                Type = part.Type,
                Text = part.Text,
                Name = part.Name,
                Path = part.Path,
                DisplayPath = part.DisplayPath,
                Url = part.Url,
                MimeType = part.MimeType,
                FileName = part.FileName
            })
            .ToArray();

    private static ConversationItemDto ToConversationItem(SessionItem item)
    {
        var payload = item.Payload.IsSet && item.Payload.Value is { } payloadElement
            ? payloadElement.Clone()
            : JsonSerializer.SerializeToElement(new { });
        return new ConversationItemDto(
            item.Id,
            item.TurnId,
            item.Type,
            item.Status,
            payload,
            item.CreatedAt,
            item.CompletedAt,
            Streaming: false);
    }
}
