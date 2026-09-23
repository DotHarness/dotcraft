using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;

namespace DotCraft.Configuration;

internal static class ChatGptCodexModelCatalog
{
    private const string BuiltInResourceName = "DotCraft.Resources.chatgpt-codex-models.json";
    private const string CacheFileName = "model-catalog-cache.json";
    private const string ClientVersion = "0.155.0";
    private const int CacheVersion = 3;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly Lazy<IReadOnlyList<CodexModelInfo>> BuiltInModels = new(LoadBuiltInModels);

    public static string DefaultModel => ModelProviderDefaults.DefaultChatGptCodexModel;

    internal static bool ResolveUseResponsesLite(
        EffectiveModelRuntime runtime,
        string? accountId) =>
        ResolveModel(runtime, accountId)?.UseResponsesLite ?? false;

    private static CodexModelInfo? ResolveModel(EffectiveModelRuntime runtime, string? accountId)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var cache = ModelCatalogCache.Load(ResolveCachePath(runtime));
        var cacheKey = BuildCacheKey(runtime.EndPoint, accountId, ClientVersion);
        return cache.TryGet(cacheKey, CacheTtl, requireFresh: false, out var cachedModels)
            ? FindModel(cachedModels, runtime.Model)
            : FindModel(BuiltInModels.Value, runtime.Model);
    }

    public static async Task<OpenAIModelCatalogResult> FetchAsync(
        EffectiveModelRuntime runtime,
        CancellationToken cancellationToken,
        OpenAIClientProvider openAIClientProvider)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(openAIClientProvider);

        var accountId = openAIClientProvider.ResolveChatGptAccountId(runtime);
        var clientVersion = ClientVersion;
        var cachePath = ResolveCachePath(runtime);
        var cacheKey = BuildCacheKey(runtime.EndPoint, accountId, clientVersion);
        var cache = ModelCatalogCache.Load(cachePath);

        if (cache.TryGet(cacheKey, CacheTtl, requireFresh: true, out var cachedModels))
            return Success(cachedModels);

        try
        {
            var response = await openAIClientProvider.FetchChatGptCodexModelsAsync(
                runtime,
                clientVersion,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == 200)
            {
                var models = ParseModelsResponse(response.Content);
                if (HasVisibleModels(models))
                {
                    cache.Set(cacheKey, runtime.EndPoint, accountId, clientVersion, response.ETag, models);
                    await cache.SaveAsync(cachePath, cancellationToken).ConfigureAwait(false);
                    return Success(models);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Model listing should not make a logged-in ChatGPT provider unusable while offline.
        }

        if (cache.TryGet(cacheKey, CacheTtl, requireFresh: false, out cachedModels))
            return Success(cachedModels);

        return Success(BuiltInModels.Value);
    }

    private static OpenAIModelCatalogResult Success(IReadOnlyList<CodexModelInfo> models) => new()
    {
        Success = true,
        Models = ToPickerEntries(models)
    };

    private static List<OpenAIModelCatalogEntry> ToPickerEntries(IReadOnlyList<CodexModelInfo> models)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return models
            .Where(model => IsVisible(model) && !string.IsNullOrWhiteSpace(model.Slug))
            .OrderBy(model => model.Priority)
            .ThenBy(model => model.Slug, StringComparer.OrdinalIgnoreCase)
            .Where(model => seen.Add(model.Slug.Trim()))
            .Select(model => new OpenAIModelCatalogEntry
            {
                Id = model.Slug.Trim(),
                OwnedBy = "openai-chatgpt",
                CreatedAt = DateTimeOffset.UnixEpoch
            })
            .ToList();
    }

    private static bool HasVisibleModels(IReadOnlyList<CodexModelInfo> models) =>
        models.Any(IsVisible);

    private static CodexModelInfo? FindModel(IReadOnlyList<CodexModelInfo> models, string model) =>
        models.FirstOrDefault(candidate =>
            string.Equals(candidate.Slug.Trim(), model.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool IsVisible(CodexModelInfo model) =>
        string.Equals(model.Visibility, "list", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveCachePath(EffectiveModelRuntime runtime)
    {
        if (!string.IsNullOrWhiteSpace(runtime.ProviderStateDirectory))
            return Path.Combine(runtime.ProviderStateDirectory, CacheFileName);
        return null;
    }

    private static string BuildCacheKey(string endpoint, string? accountId, string clientVersion)
    {
        var normalized = string.Join(
            "|",
            endpoint.TrimEnd('/').ToLowerInvariant(),
            accountId?.Trim().ToLowerInvariant() ?? string.Empty,
            clientVersion.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static IReadOnlyList<CodexModelInfo> LoadBuiltInModels()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(BuiltInResourceName);
        if (stream is null)
            return [];

        using var reader = new StreamReader(stream);
        try
        {
            return ParseModelsResponse(reader.ReadToEnd());
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static List<CodexModelInfo> ParseModelsResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("models", out var modelsElement)
            || modelsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<CodexModelInfo>();
        foreach (var modelElement in modelsElement.EnumerateArray())
        {
            if (modelElement.ValueKind != JsonValueKind.Object)
                continue;

            var slug = ReadString(modelElement, "slug");
            if (string.IsNullOrWhiteSpace(slug))
                continue;

            models.Add(new CodexModelInfo
            {
                Slug = slug,
                Visibility = ReadString(modelElement, "visibility") ?? string.Empty,
                Priority = ReadInt(modelElement, "priority") ?? int.MaxValue,
                UseResponsesLite = ReadBoolean(modelElement, "use_responses_lite")
            });
        }

        return models;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return null;
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return null;
    }

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    internal sealed class CodexModelInfo
    {
        public string Slug { get; set; } = string.Empty;

        public string Visibility { get; set; } = string.Empty;

        public int Priority { get; set; } = int.MaxValue;

        public bool UseResponsesLite { get; set; }
    }

    private sealed class ModelCatalogCache
    {
        public int Version { get; set; } = CacheVersion;

        public Dictionary<string, ModelCatalogCacheEntry> Entries { get; set; } = new(StringComparer.Ordinal);

        public static ModelCatalogCache Load(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return new ModelCatalogCache();

                var cache = JsonSerializer.Deserialize<ModelCatalogCache>(
                    File.ReadAllText(path, Encoding.UTF8),
                    CacheJsonOptions);
                if (cache is null || cache.Version != CacheVersion)
                    return new ModelCatalogCache();
                cache.Entries ??= new Dictionary<string, ModelCatalogCacheEntry>(StringComparer.Ordinal);
                return cache;
            }
            catch (IOException)
            {
                return new ModelCatalogCache();
            }
            catch (UnauthorizedAccessException)
            {
                return new ModelCatalogCache();
            }
            catch (JsonException)
            {
                return new ModelCatalogCache();
            }
        }

        public bool TryGet(
            string key,
            TimeSpan ttl,
            bool requireFresh,
            out IReadOnlyList<CodexModelInfo> models)
        {
            models = [];
            if (!Entries.TryGetValue(key, out var entry) || entry.Models is null || entry.Models.Count == 0)
                return false;

            if (requireFresh && DateTimeOffset.UtcNow - entry.FetchedAt > ttl)
                return false;

            models = entry.Models;
            return true;
        }

        public void Set(
            string key,
            string endpoint,
            string? accountId,
            string clientVersion,
            string? etag,
            IReadOnlyList<CodexModelInfo> models)
        {
            Entries[key] = new ModelCatalogCacheEntry
            {
                Endpoint = endpoint,
                AccountId = accountId,
                ClientVersion = clientVersion,
                ETag = etag,
                FetchedAt = DateTimeOffset.UtcNow,
                Models = models.ToList()
            };
        }

        public async Task SaveAsync(string? path, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(this, CacheJsonOptions);
                await File.WriteAllTextAsync(path, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class ModelCatalogCacheEntry
    {
        public string Endpoint { get; set; } = string.Empty;

        public string? AccountId { get; set; }

        public string ClientVersion { get; set; } = string.Empty;

        public string? ETag { get; set; }

        public DateTimeOffset FetchedAt { get; set; }

        public List<CodexModelInfo> Models { get; set; } = [];
    }
}
