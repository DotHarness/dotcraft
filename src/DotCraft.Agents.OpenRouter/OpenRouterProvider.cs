using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

public sealed partial class OpenRouterProvider : IModelProvider, IProviderModelRuntimeResolver,
    IModelCatalogProvider, IProviderAuthenticator, IProviderUsageReader, IProviderAccessTokenReader, IProviderRoutingCatalog
{
    private readonly ModelBackendContext _context;
    private readonly HttpClient _http;
    private readonly IProviderHttpTransport? _remote;
    private readonly OpenRouterRoutingPreferences _routing;
    private readonly SemaphoreSlim _refresh = new(1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string, string), ProviderRequestAdaptation> _adaptations = new();
    private volatile CatalogSnapshot _catalog;
    private readonly string _root;
    private readonly object _usageGate = new();
    private decimal? _lastRequestCost;

    public OpenRouterProvider(ModelBackendContext context, IProviderHttpTransport? transport = null,
        HttpClient? catalogClient = null)
    {
        _context = context;
        _remote = transport;
        _routing = OpenRouterRoutingPreferences.Read(context.Runtime.BackendSettingsJson);
        _root = OpenRouterModelCatalog.ApiRoot(context.Runtime.ConfiguredEndPoint ?? context.Runtime.EndPoint);
        _http = catalogClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(context.Runtime.NetworkTimeoutSeconds) };
        var bundled = OpenRouterModelCatalog.Bundled();
        _catalog = new(bundled.Models.ToDictionary(model => model.Id, StringComparer.Ordinal), "bundled", bundled.CapturedAt,
            bundled.CapturedAt.ToUnixTimeMilliseconds().ToString());
        if (context.Runtime.IsRemote && context.Runtime.RemoteModels is { } remoteModels)
        {
            _catalog = _catalog with
            {
                Models = remoteModels.Values.Select(value => value.Model).OfType<ModelCatalogEntry>().ToDictionary(model => model.Id, StringComparer.Ordinal),
                Source = "modelService", Revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(remoteModels)))
            };
        }
        else LoadCache();
    }

    public IReadOnlyCollection<string> Protocols => OpenRouterBackend.SupportedProtocols;
    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public EffectiveModelRuntime ResolveRuntime(EffectiveModelRuntime runtime)
    {
        var snapshot = _catalog;
        var configured = runtime.ConfiguredProtocol ?? runtime.Protocol;
        if (string.IsNullOrWhiteSpace(configured)) configured = "auto";
        if (!OpenRouterBackend.SupportedProtocols.Contains(configured))
            throw new ArgumentException($"Unsupported OpenRouter protocol '{configured}'.");
        var authMethod = string.IsNullOrWhiteSpace(runtime.AuthMethod) ? "apiKey" : runtime.AuthMethod;
        if (authMethod is not ("apiKey" or OpenRouterBackend.OAuth))
            throw new ArgumentException($"Unsupported OpenRouter authentication method '{runtime.AuthMethod}'.");
        var resolved = runtime with { IsBuiltInBackend = true, AuthMethod = authMethod, BackendGeneration = "openrouter:" + snapshot.Revision,
            ConfiguredProtocol = configured, ConfiguredEndPoint = runtime.ConfiguredEndPoint ?? runtime.EndPoint, EndPoint = _root };
        if (string.IsNullOrWhiteSpace(runtime.Model)) return resolved;
        var model = FindModel(snapshot, runtime.Model);
        var protocol = OpenRouterModelCatalog.SelectProtocol(model, configured);
        var capacity = runtime.ConfiguredMaxOutputTokens;
        var maxOutput = capacity is > 0 && model.MaxOutputTokens is > 0 ? Math.Min(capacity.Value, model.MaxOutputTokens.Value)
            : capacity ?? model.MaxOutputTokens;
        var adaptation = _adaptations.GetOrAdd((model.Id, protocol, snapshot.Revision), _ => new ProviderRequestAdaptation(false, false,
            protocol == "anthropic" && model.Reasoning is not null
                ? ModelThinkingAdapterCatalog.ResolveAnthropicThinkingAdapter(_root, model.Id)
                    ?? new ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData { ThinkingType = "adaptive", OutputConfigEffort = "fromReasoningEffort" }
                : null, null, model, protocol));
        return resolved with
        {
            Protocol = protocol, EndPoint = protocol == "anthropic" ? _root[..^3] : _root,
            ModelMetadata = model, MaxOutputTokens = maxOutput, RequestAdaptation = adaptation,
            Capabilities = ModelProviderCapabilities.ForProtocol(protocol) with { NativeDeferredToolLoading = false },
            SupportsImageGeneration = false, SupportsFreeformTools = false, UseResponsesLite = false
        };
    }

    public ModelCatalogEntry FindModel(string id) => FindModel(_catalog, id);
    private static ModelCatalogEntry FindModel(CatalogSnapshot snapshot, string id) => snapshot.Models.TryGetValue(id, out var model) ? model
        : throw new ArgumentException($"OpenRouter model '{id}' is not in the catalog. Refresh the model catalog before selecting it.");

    public IChatClient CreateChatClient(EffectiveModelRuntime runtime)
    {
        var resolved = ResolveRuntime(runtime);
        var key = resolved.IsRemote ? "remote" : ApiKey();
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("OpenRouter API key is not configured.");
        var transport = new OpenRouterHttpTransport(resolved, key, _remote);
        var wire = resolved with { ApiKey = key, AuthMethod = "apiKey" };
        IChatClient client = resolved.Protocol == "anthropic"
            ? new AnthropicClientProvider(transport).CreateChatClient(wire)
            : new OpenAIClientProvider(httpTransport: transport).CreateChatClient(wire);
        return new OpenRouterChatClient(client, resolved, _routing, cost => { lock (_usageGate) _lastRequestCost = cost; });
    }

    public async Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken cancellationToken)
    {
        if (runtime.IsRemote)
        {
            var state = runtime.RemoteCatalogState;
            var result = Result(state?.Success ?? true, state?.ErrorCode ?? ModelCatalogErrorCode.None, state?.ErrorMessage, state?.RefreshNotice);
            result.Source = state?.Source ?? "modelService";
            result.RefreshedAt = state?.RefreshedAt ?? _catalog.RefreshedAt;
            return result;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _context.Stopping);
        linked.CancelAfter(TimeSpan.FromSeconds(runtime.NetworkTimeoutSeconds));
        await _refresh.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var key = ApiKey();
            if (string.IsNullOrWhiteSpace(key)) return Result(false, ModelCatalogErrorCode.MissingApiKey, "OpenRouter API key is not configured.");
            var publicJson = await GetAsync("models", key, linked.Token).ConfigureAwait(false);
            string? notice = null;
            string personalized;
            try { personalized = await GetAsync("models/user", key, linked.Token).ConfigureAwait(false); }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                personalized = publicJson;
                notice = "personalizationUnavailable";
            }
            var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(personalized)));
            var current = _catalog;
            var models = revision == current.Revision ? current.Models
                : OpenRouterModelCatalog.Read(personalized).ToDictionary(model => model.Id, StringComparer.Ordinal);
            await SaveCacheAsync(personalized, linked.Token).ConfigureAwait(false);
            _catalog = new(models,
                notice is null ? "user" : "public", DateTimeOffset.UtcNow, revision, notice);
            if (revision != current.Revision) _adaptations.Clear();
            return Result(true, notice: notice);
        }
        catch (HttpRequestException ex)
        {
            var code = ex.StatusCode switch { HttpStatusCode.Unauthorized => ModelCatalogErrorCode.Unauthorized,
                HttpStatusCode.Forbidden => ModelCatalogErrorCode.Forbidden, null => ModelCatalogErrorCode.Network, _ => ModelCatalogErrorCode.Unknown };
            return Result(false, code, ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_context.Stopping.IsCancellationRequested)
        { return Result(false, ModelCatalogErrorCode.Timeout, "OpenRouter catalog refresh timed out."); }
        catch (JsonException) { return Result(false, ModelCatalogErrorCode.Unknown, "OpenRouter returned an invalid model catalog."); }
        finally { _refresh.Release(); }
    }

    public ModelCatalogResult ReadCatalog() => Result(true);

    private ModelCatalogResult Result(bool success, ModelCatalogErrorCode code = ModelCatalogErrorCode.None,
        string? error = null, string? notice = null)
    {
        var snapshot = _catalog;
        return new()
        {
            Success = success, ErrorCode = code, ErrorMessage = error, Source = snapshot.Source,
            RefreshedAt = snapshot.RefreshedAt, RefreshNotice = notice ?? (success ? snapshot.Notice : "usingCachedCatalog"), Models = snapshot.Models.Values.ToList()
        };
    }

    private async Task<string> GetAsync(string path, string key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _root + "/" + path);
        if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private string ApiKey()
    {
        if (_context.Runtime.AuthMethod != OpenRouterBackend.OAuth) return _context.Runtime.ApiKey;
        var credential = _context.Credentials.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        return credential?.Deserialize<OpenRouterCredential>(OpenRouterJson.Options)?.Key ?? string.Empty;
    }

    public Task<string?> TryGetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(ApiKey());
    }

    private string? CachePath()
    {
        if (string.IsNullOrWhiteSpace(_context.Runtime.ProviderStateDirectory)) return null;
        var identity = string.Join('\n', _context.Runtime.ProviderId, _root, _context.Runtime.AuthMethod, ApiKey());
        return Path.Combine(_context.Runtime.ProviderStateDirectory, "catalogs", "openrouter", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".json");
    }

    private void LoadCache()
    {
        var path = CachePath();
        if (path is null || !File.Exists(path)) return;
        try
        {
            var json = File.ReadAllText(path);
            _catalog = new(OpenRouterModelCatalog.Read(json).ToDictionary(model => model.Id, StringComparer.Ordinal), "cache",
                File.GetLastWriteTimeUtc(path), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private async Task SaveCacheAsync(string json, CancellationToken ct)
    {
        var path = CachePath();
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record CatalogSnapshot(IReadOnlyDictionary<string, ModelCatalogEntry> Models, string Source,
        DateTimeOffset RefreshedAt, string Revision, string? Notice = null);
}
