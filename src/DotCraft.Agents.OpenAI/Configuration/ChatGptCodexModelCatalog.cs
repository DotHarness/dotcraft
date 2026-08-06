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
    private const int CacheVersion = 3;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly Lazy<IReadOnlyList<CodexModelInfo>> BuiltInModels = new(LoadBuiltInModels);

    public static string DefaultModel => ModelProviderDefaults.DefaultChatGptCodexModel;

    public static string ClientVersion => ResolveClientVersion(BuiltInModels.Value);

    internal static bool ResolveUseResponsesLite(
        EffectiveModelRuntime runtime,
        string? accountId) =>
        ResolveRuntimeMetadata(runtime, accountId).UseResponsesLite;

    internal static CodexModelRuntimeMetadata ResolveRuntimeMetadata(
        EffectiveModelRuntime runtime,
        string? accountId)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var cache = ModelCatalogCache.Load(ResolveCachePath(runtime));
        var cacheKey = BuildCacheKey(runtime.EndPoint, accountId, ClientVersion);
        if (cache.TryGet(cacheKey, CacheTtl, requireFresh: false, out var cachedModels))
            return ToRuntimeMetadata(FindModel(cachedModels, runtime.Model));

        return ToRuntimeMetadata(FindModel(BuiltInModels.Value, runtime.Model));
    }

    private static CodexModelRuntimeMetadata ToRuntimeMetadata(CodexModelInfo? model) =>
        new(
            model?.UseResponsesLite ?? false,
            model?.SupportsParallelToolCalls ?? false);

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

    private static string ResolveCachePath(EffectiveModelRuntime runtime)
    {
        if (!string.IsNullOrWhiteSpace(runtime.ProviderStateDirectory))
            return Path.Combine(runtime.ProviderStateDirectory, CacheFileName);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft",
            CacheFileName);
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

    private static string ResolveClientVersion(IReadOnlyList<CodexModelInfo> models)
    {
        var best = new Version(0, 0, 0);
        foreach (var model in models)
        {
            if (TryParseVersion(model.MinimalClientVersion, out var version) && version > best)
                best = version;
        }

        return $"{best.Major}.{best.Minor}.{best.Build}";
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var pieces = value.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length < 2 || pieces.Length > 4)
            return false;

        var numbers = new int[3];
        for (var i = 0; i < Math.Min(pieces.Length, 3); i++)
        {
            if (!int.TryParse(pieces[i], out numbers[i]))
                return false;
        }

        version = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    private static IReadOnlyList<CodexModelInfo> LoadBuiltInModels()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(BuiltInResourceName);
        if (stream is null)
            return HardcodedFallbackModels();

        using var reader = new StreamReader(stream);
        try
        {
            var models = ParseModelsResponse(reader.ReadToEnd());
            return models.Count > 0 ? models : HardcodedFallbackModels();
        }
        catch (JsonException)
        {
            return HardcodedFallbackModels();
        }
    }

    private static IReadOnlyList<CodexModelInfo> HardcodedFallbackModels() =>
    [
        new() { Slug = ModelProviderDefaults.DefaultChatGptCodexModel, Visibility = "list", Priority = 1, MinimalClientVersion = "0.144.0", UseResponsesLite = true, SupportsParallelToolCalls = true },
        new() { Slug = "gpt-5.6-terra", Visibility = "list", Priority = 2, MinimalClientVersion = "0.144.0", UseResponsesLite = true, SupportsParallelToolCalls = true },
        new() { Slug = "gpt-5.6-luna", Visibility = "list", Priority = 3, MinimalClientVersion = "0.144.0", UseResponsesLite = true, SupportsParallelToolCalls = true },
        new() { Slug = "gpt-5.5", Visibility = "list", Priority = 7, MinimalClientVersion = "0.124.0", SupportsParallelToolCalls = true },
        new() { Slug = "gpt-5.4", Visibility = "list", Priority = 16, MinimalClientVersion = "0.98.0", SupportsParallelToolCalls = true },
        new() { Slug = "gpt-5.4-mini", Visibility = "list", Priority = 23, MinimalClientVersion = "0.98.0", SupportsParallelToolCalls = true },
        new() { Slug = "gpt-5.3-codex", Visibility = "list", Priority = 24, MinimalClientVersion = "0.98.0" },
        new() { Slug = "gpt-5.2", Visibility = "list", Priority = 29, MinimalClientVersion = "0.0.1", SupportsParallelToolCalls = true }
    ];

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
                MinimalClientVersion = ReadMinimalClientVersion(modelElement),
                UseResponsesLite = ReadBoolean(modelElement, "use_responses_lite"),
                SupportsParallelToolCalls = ReadBoolean(modelElement, "supports_parallel_tool_calls")
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

    private static string ReadMinimalClientVersion(JsonElement modelElement)
    {
        if (!modelElement.TryGetProperty("minimal_client_version", out var value))
            return "0.0.0";

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? "0.0.0";

        if (value.ValueKind == JsonValueKind.Array)
        {
            var numbers = value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out _))
                .Select(item => item.GetInt32().ToString())
                .Take(3)
                .ToArray();
            return numbers.Length > 0 ? string.Join('.', numbers) : "0.0.0";
        }

        return "0.0.0";
    }

    internal sealed class CodexModelInfo
    {
        public string Slug { get; set; } = string.Empty;

        public string Visibility { get; set; } = string.Empty;

        public int Priority { get; set; } = int.MaxValue;

        public string MinimalClientVersion { get; set; } = "0.0.0";

        public bool UseResponsesLite { get; set; }

        public bool SupportsParallelToolCalls { get; set; }
    }

    internal readonly record struct CodexModelRuntimeMetadata(
        bool UseResponsesLite,
        bool SupportsParallelToolCalls);

    private sealed class ModelCatalogCache
    {
        public int Version { get; set; } = CacheVersion;

        public Dictionary<string, ModelCatalogCacheEntry> Entries { get; set; } = new(StringComparer.Ordinal);

        public static ModelCatalogCache Load(string path)
        {
            try
            {
                if (!File.Exists(path))
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

        public async Task SaveAsync(string path, CancellationToken cancellationToken)
        {
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
