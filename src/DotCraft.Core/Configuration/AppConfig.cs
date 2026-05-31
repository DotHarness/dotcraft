using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Plugins;
using System.Text.RegularExpressions;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Dreams;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Configuration;

[ConfigSection("", DisplayName = "Core", Order = 0)]
public sealed class AppConfig
{
    /// <summary>
    /// Workspace-selected provider id. Empty means no model provider is selected.
    /// </summary>
    [ConfigField(Reload = ReloadBehavior.ProcessRestart, HasReload = true)]
    public string ProviderId { get; set; } = string.Empty;

    [ConfigField(Reload = ReloadBehavior.ProcessRestart, HasReload = true)]
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Network timeout applied to each model provider client request.
    /// </summary>
    [ConfigField(Min = 1, Hint = "seconds; default 600 (10 minutes)", Reload = ReloadBehavior.ProcessRestart, HasReload = true)]
    public int NetworkTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Personal provider registry. Workspaces select providers by <see cref="ProviderId"/>.
    /// Credentials and endpoints live here, not in workspace root config.
    /// </summary>
    [ConfigField(Ignore = true)]
    public Dictionary<string, ModelProviderConfig> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Controls provider reasoning/thinking behavior.
    /// Disabled by default; enable when you want providers that support reasoning to use it.
    /// </summary>
    [ConfigField(Ignore = true)]
    public ReasoningConfig Reasoning { get; set; } = new();

    /// <summary>
    /// Controls provider prompt-cache markers for OpenAI protocol Claude routes.
    /// </summary>
    [ConfigField(Ignore = true)]
    public PromptCachingConfig PromptCaching { get; set; } = new();

    /// <summary>
    /// Maximum number of subagents that can run concurrently.
    /// Controls parallel API call pressure. Excess subagents will queue and wait.
    /// </summary>
    public int SubagentMaxConcurrency { get; set; } = 3;

    /// <summary>
    /// Maximum number of pending requests per session in the gateway queue.
    /// When exceeded, the oldest waiting request is evicted and the user is notified.
    /// Set to 0 to disable the limit (unlimited queue).
    /// </summary>
    [ConfigField(Min = 0, Hint = "0 = unlimited")]
    public int MaxSessionQueueSize { get; set; } = 3;

    /// <summary>
    /// Context compaction pipeline settings: cold-cache microcompact,
    /// partial-summary, and reactive-retry configuration consumed by
    /// <see cref="DotCraft.Context.Compaction.CompactionPipeline"/>.
    /// </summary>
    [ConfigField(Ignore = true)]
    public CompactionConfig Compaction { get; set; } = new();

    /// <summary>
    /// True when the loaded configuration explicitly set <see cref="CompactionConfig.ContextWindow"/>.
    /// </summary>
    [JsonIgnore]
    internal bool CompactionContextWindowExplicit { get; set; }

    /// <summary>
    /// Global config path used to locate sibling model context-window catalogs.
    /// </summary>
    [JsonIgnore]
    internal string? GlobalConfigPath { get; set; }

    /// <summary>
    /// Workspace config path used to locate sibling model context-window catalogs.
    /// </summary>
    [JsonIgnore]
    internal string? WorkspaceConfigPath { get; set; }

    /// <summary>
    /// Long-term memory consolidation settings. These settings are independent
    /// from context compaction.
    /// </summary>
    [ConfigField(Ignore = true)]
    public MemoryConfig Memory { get; set; } = new();

    /// <summary>
    /// Background Dreams settings.
    /// </summary>
    [ConfigField(Ignore = true)]
    public DreamsConfig Dreams { get; set; } = new();

    /// <summary>
    /// Model used for memory consolidation. When empty, uses <see cref="Model"/> (same as main agent).
    /// When set, use this model for consolidation only (e.g. a non-thinking model to avoid tool_choice restrictions in thinking mode).
    /// </summary>
    [ConfigField(Hint = "Model for memory consolidation. Empty = use main Model. Set to a non-thinking model if main model does not support tool_choice in thinking mode.")]
    public string ConsolidationModel { get; set; } = string.Empty;

    /// <summary>
    /// Enable debug mode to display full tool call arguments without truncation.
    /// Can be toggled at runtime by administrators using /debug command in QQ/WeCom bots.
    /// </summary>
    public bool DebugMode { get; set; }

    /// <summary>
    /// Filter which tools are globally available in all modes.
    /// Empty list means all tools are enabled.
    /// </summary>
    [ConfigField(Hint = "JSON array of tool names. Built-in tools use PascalCase (e.g. Shell, ReadFile); MCP/manual tools often use snake_case. Empty = all enabled.")]
    public List<string> EnabledTools { get; set; } = [];

    [ConfigField(Ignore = true)]
    public ToolsConfig Tools { get; set; } = new();

    [ConfigField(Ignore = true)]
    public PluginsConfig Plugins { get; set; } = new();

    [ConfigField(Ignore = true)]
    public SecurityConfig Security { get; set; } = new();

    [ConfigField(Ignore = true)]
    public PermissionsConfig Permissions { get; set; } = new();

    [ConfigField(Ignore = true)]
    public HeartbeatConfig Heartbeat { get; set; } = new();

    [ConfigField(Ignore = true)]
    public CronConfig Cron { get; set; } = new();

    [ConfigField(Ignore = true)]
    public GoalsConfig Goals { get; set; } = new();

    [ConfigField(Ignore = true)]
    public SkillsConfig Skills { get; set; } = new();

    [ConfigField(Ignore = true)]
    public SubAgentConfig SubAgent { get; set; } = new();

    [ConfigField(Ignore = true)]
    public WelcomeSuggestionsConfig WelcomeSuggestions { get; set; } = new();

    [ConfigField(Ignore = true)]
    public HooksConfig Hooks { get; set; } = new();

    [ConfigField(Ignore = true)]
    public TracingConfig Tracing { get; set; } = new();

    [ConfigField(Ignore = true)]
    public DashBoardConfig DashBoard { get; set; } = new();

    [ConfigField(Ignore = true)]
    public LoggingConfig Logging { get; set; } = new();

    [ConfigField(Ignore = true)]
    public StreamDebugConfig StreamDebug { get; set; } = new();

    [ConfigField(Ignore = true)]
    [JsonConverter(typeof(McpServerConfigListConverter))]
    public List<McpServerConfig> McpServers { get; set; } = [];

    [ConfigField(Ignore = true)]
    [JsonConverter(typeof(LspServerConfigListConverter))]
    public List<LspServerConfig> LspServers { get; set; } = [];

    [ConfigField(Ignore = true)]
    [JsonConverter(typeof(ExternalChannelConfigListConverter))]
    public List<ExternalChannelEntry> ExternalChannels { get; set; } = [];

    [ConfigField(Ignore = true)]
    [JsonConverter(typeof(SubAgentProfileListConverter))]
    public List<SubAgentProfile> SubAgentProfiles { get; set; } = [];

    /// <summary>
    /// Captures all module-specific config sections that are not defined in Core.
    /// Module projects (QQ, WeCom, etc.) store their config here when they
    /// are not directly referenced by Core.
    /// </summary>
    [JsonExtensionData]
    [ConfigField(Ignore = true)]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    // Section cache: avoids repeated deserialization on hot paths
    [JsonIgnore]
    private readonly ConcurrentDictionary<string, object> _sectionCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a typed config section by JSON key (case-insensitive).
    /// The section is deserialized from <see cref="ExtensionData"/> on first access and cached.
    /// If the key is not present in the config file, a default instance is returned.
    /// </summary>
    /// <typeparam name="T">The config section type. Must have a parameterless constructor.</typeparam>
    /// <param name="key">The JSON key, e.g. "WeComBot", "Automations".</param>
    public T GetSection<T>(string key) where T : class, new()
    {
        return (T)_sectionCache.GetOrAdd(key, _ =>
        {
            if (ExtensionData != null)
            {
                foreach (var (k, v) in ExtensionData)
                {
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                        return v.Deserialize<T>(SerializerOptions) ?? new T();
                }
            }
            return new T();
        });
    }

    /// <summary>
    /// Stores a typed config section in the cache, overriding any value deserialized from config.json.
    /// Use this for programmatic config overrides (e.g. forcing ACP mode from a command-line flag).
    /// </summary>
    public void SetSection<T>(string key, T value) where T : class, new()
    {
        _sectionCache[key] = value;
    }

    /// <summary>
    /// Checks whether a config section has <c>Enabled = true</c>, without requiring knowledge of
    /// the section's type. Used by cross-cutting modules (for example GatewayModule) that need
    /// to check enabled status of sections from other modules without depending on their types.
    /// </summary>
    /// <param name="key">The JSON key (case-insensitive), e.g. "WeComBot", "Automations".</param>
    public bool IsSectionEnabled(string key)
    {
        // Check the section cache first — SetSection may have overridden the value.
        if (_sectionCache.TryGetValue(key, out var cached))
        {
            var enabledProp = cached.GetType().GetProperty("Enabled");
            if (enabledProp != null && enabledProp.PropertyType == typeof(bool))
                return (bool)(enabledProp.GetValue(cached) ?? false);
        }

        if (ExtensionData == null) return false;
        foreach (var (k, v) in ExtensionData)
        {
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
            if (v.TryGetProperty("Enabled", out var enabled) || v.TryGetProperty("enabled", out enabled))
                return enabled.ValueKind == JsonValueKind.True;
            return false;
        }
        return false;
    }

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaultConfig = new AppConfig();
            ModelContextWindowCatalog.ApplyToConfig(defaultConfig, new JsonObject(), globalConfigPath: null, workspaceConfigPath: path);
            return defaultConfig;
        }

        var node = JsonNode.Parse(File.ReadAllText(path)) ?? new JsonObject();
        RemoveLegacyLanguage(node);
        ExpandEnvironmentVariables(node);
        var config = node.Deserialize<AppConfig>(SerializerOptions) ?? new AppConfig();
        ModelContextWindowCatalog.ApplyToConfig(config, node, globalConfigPath: null, workspaceConfigPath: path);
        return config;
    }

    public static AppConfig LoadWithGlobalFallback(string workspacePath) =>
        LoadWithGlobalFallback(
            workspacePath,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".craft",
                "config.json"));

    public static AppConfig LoadWithGlobalFallback(string workspacePath, string? globalConfigPath)
    {
        globalConfigPath = string.IsNullOrWhiteSpace(globalConfigPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".craft",
                "config.json")
            : globalConfigPath;

        // Load as JsonNode for proper merging
        JsonNode? globalNode = File.Exists(globalConfigPath)
            ? JsonNode.Parse(File.ReadAllText(globalConfigPath))
            : new JsonObject();

        JsonNode? workspaceNode = File.Exists(workspacePath)
            ? JsonNode.Parse(File.ReadAllText(workspacePath))
            : new JsonObject();
        RemoveLegacyLanguage(globalNode);
        RemoveLegacyLanguage(workspaceNode);

        // Merge workspace config into global config (workspace values take precedence)
        var mergedNode = MergeNodes(globalNode ?? new JsonObject(), workspaceNode ?? new JsonObject());
        RemoveLegacyLanguage(mergedNode);

        // Expand environment variable references before deserializing
        ExpandEnvironmentVariables(mergedNode);

        var config = mergedNode.Deserialize<AppConfig>(SerializerOptions) ?? new AppConfig();
        ModelContextWindowCatalog.ApplyToConfig(config, mergedNode, globalConfigPath, workspacePath);
        return config;
    }

    private static void RemoveLegacyLanguage(JsonNode? node)
    {
        if (node is not JsonObject obj) return;
        var key = obj
            .Select(property => property.Key)
            .FirstOrDefault(name => string.Equals(name, "Language", StringComparison.OrdinalIgnoreCase));
        if (key is not null)
            obj.Remove(key);
    }

    /// <summary>
    /// Recursively expands environment variable references in all string values of a JSON node tree.
    /// Supports two syntaxes:
    /// <list type="bullet">
    ///   <item><c>$VAR_NAME</c> — the entire string is replaced by the env var value.</item>
    ///   <item><c>${VAR_NAME}</c> — inline substitution; multiple references may appear in one string.</item>
    /// </list>
    /// If the referenced variable is not set, the original placeholder is preserved unchanged.
    /// </summary>
    internal static void ExpandEnvironmentVariables(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var child = obj[key];
                    if (child is JsonValue val && val.TryGetValue<string>(out var str))
                    {
                        var expanded = ExpandEnvString(str);
                        if (expanded != str)
                            obj[key] = JsonValue.Create(expanded);
                    }
                    else if (child is JsonObject or JsonArray)
                    {
                        ExpandEnvironmentVariables(child);
                    }
                }
                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    if (child is JsonValue val && val.TryGetValue<string>(out var str))
                    {
                        var expanded = ExpandEnvString(str);
                        if (expanded != str)
                            arr[i] = JsonValue.Create(expanded);
                    }
                    else if (child is JsonObject or JsonArray)
                    {
                        ExpandEnvironmentVariables(child);
                    }
                }
                break;
        }
    }

    // Matches ${VAR_NAME} inline placeholders.
    private static readonly Regex InlineEnvVarRegex =
        new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    // Matches a whole-string $VAR_NAME reference.
    private static readonly Regex WholeEnvVarRegex =
        new(@"^\$([A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.Compiled);

    private static string ExpandEnvString(string value)
    {
        // Whole-value reference: $VAR — replace entire string with env var value.
        var wholeMatch = WholeEnvVarRegex.Match(value);
        if (wholeMatch.Success)
            return Environment.GetEnvironmentVariable(wholeMatch.Groups[1].Value) ?? value;

        // Inline reference: ${VAR} — substitute each occurrence, keep placeholder if not set.
        return InlineEnvVarRegex.Replace(value, m =>
            Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
    }

    private static JsonNode MergeNodes(JsonNode baseNode, JsonNode overrideNode)
    {
        if (overrideNode is JsonObject overrideObj && baseNode is JsonObject baseObj)
        {
            var result = JsonSerializer.Deserialize<JsonObject>(baseObj.ToJsonString()) ?? [];

            foreach (var property in overrideObj)
            {
                if (result.TryGetPropertyValue(property.Key, out var existingValue))
                {
                    result[property.Key] = MergeNodes(existingValue ?? new JsonObject(), property.Value ?? new JsonObject());
                }
                else
                {
                    result[property.Key] = property.Value?.DeepClone();
                }
            }

            return result;
        }

        // For arrays and values, override node takes precedence if it exists
        return overrideNode.DeepClone();
    }

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [ConfigSection("Reasoning", DisplayName = "Reasoning", Order = 10)]
    public sealed class ReasoningConfig
    {
        /// <summary>
        /// Whether to request provider reasoning support.
        /// Unsupported providers or models may ignore this setting.
        /// </summary>
        [ConfigField(Hint = "Request provider reasoning/thinking support when available.")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Requested reasoning effort level when reasoning is enabled.
        /// </summary>
        public ReasoningEffort Effort { get; set; } = ReasoningEffort.Medium;

        /// <summary>
        /// Controls how much reasoning content is exposed in responses.
        /// The default exposes full summary.
        /// </summary>
        [ConfigField(Hint = "Controls whether reasoning content is exposed in responses.")]
        public ReasoningOutput Output { get; set; } = ReasoningOutput.Full;

        /// <summary>
        /// Converts the configuration to chat reasoning options.
        /// Returns <see langword="null"/> when reasoning is disabled.
        /// </summary>
        public ReasoningOptions? ToOptions()
        {
            if (!Enabled)
                return null;

            return new ReasoningOptions
            {
                Effort = Effort,
                Output = Output
            };
        }
    }

    public sealed class ModelProviderConfig
    {
        /// <summary>
        /// User-facing label for this provider.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Provider protocol. Supported values are "openai-chat-completions",
        /// "openai-responses", and "anthropic". Legacy "openai" is accepted as
        /// a read-only alias for "openai-chat-completions".
        /// </summary>
        public string Protocol { get; set; } = ModelProviderProtocols.OpenAIChatCompletions;

        /// <summary>
        /// Provider credential or secret reference.
        /// </summary>
        [ConfigField(Sensitive = true)]
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Authentication mechanism. <c>"apiKey"</c> (default) uses the static <see cref="ApiKey"/>;
        /// <c>"chatgptOAuth"</c> uses the OpenAI Sign-in with ChatGPT flow with tokens persisted in
        /// <c>~/.craft/auth.json</c>. Only meaningful for OpenAI protocols.
        /// </summary>
        [ConfigField(Hint = "Authentication method: 'apiKey' or 'chatgptOAuth' (OpenAI subscription).")]
        public string AuthMethod { get; set; } = ModelProviderAuthMethods.ApiKey;

        /// <summary>
        /// ChatGPT account id (populated by the OAuth flow). Read-only metadata for UI display.
        /// </summary>
        [ConfigField(Hint = "Populated by Sign in with ChatGPT; do not edit manually.")]
        public string ChatGptAccountId { get; set; } = string.Empty;

        /// <summary>
        /// ChatGPT plan tier extracted from the id_token (free, plus, pro, business, enterprise, edu).
        /// </summary>
        [ConfigField(Hint = "Populated by Sign in with ChatGPT; do not edit manually.")]
        public string ChatGptPlanType { get; set; } = string.Empty;

        /// <summary>
        /// Provider base URL. Empty values use the protocol's official default endpoint.
        /// </summary>
        public string EndPoint { get; set; } = string.Empty;

        /// <summary>
        /// Optional provider-specific timeout in seconds.
        /// </summary>
        public int? NetworkTimeoutSeconds { get; set; }

        /// <summary>
        /// Optional provider-specific maximum output token default.
        /// </summary>
        [ConfigField(Min = 1, Hint = "Default max output tokens for provider requests when the request does not override it.")]
        public int? MaxOutputTokens { get; set; }

        /// <summary>
        /// Optional provider-specific maximum stream reconnection attempts.
        /// </summary>
        [ConfigField(Min = 0, Hint = "Maximum stream reconnection attempts. 0 disables stream retry.")]
        public int? StreamMaxRetries { get; set; }

        /// <summary>
        /// Optional provider-specific streaming idle timeout in milliseconds.
        /// </summary>
        [ConfigField(Min = 1, Hint = "Streaming idle timeout in milliseconds.")]
        public int? StreamIdleTimeoutMs { get; set; }

        public ModelProviderConfig Clone() => new()
        {
            DisplayName = DisplayName,
            Protocol = Protocol,
            ApiKey = ApiKey,
            AuthMethod = AuthMethod,
            ChatGptAccountId = ChatGptAccountId,
            ChatGptPlanType = ChatGptPlanType,
            EndPoint = EndPoint,
            NetworkTimeoutSeconds = NetworkTimeoutSeconds,
            MaxOutputTokens = MaxOutputTokens,
            StreamMaxRetries = StreamMaxRetries,
            StreamIdleTimeoutMs = StreamIdleTimeoutMs
        };
    }

    [ConfigSection("PromptCaching", DisplayName = "Prompt Caching", Order = 11)]
    public sealed class PromptCachingConfig
    {
        /// <summary>
        /// Whether to add Anthropic prompt-cache markers for matching models.
        /// </summary>
        [ConfigField(Hint = "Add Anthropic prompt-cache markers for matching Claude models.")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Case-insensitive model-name fragments that should receive prompt-cache markers.
        /// </summary>
        [ConfigField(Hint = "Case-insensitive model name fragments. Empty disables model matching.")]
        public List<string> ModelPatterns { get; set; } = ["claude"];

        /// <summary>
        /// Prompt-cache marker placement strategy.
        /// </summary>
        [ConfigField(Hint = "Prompt cache placement strategy. Currently only ConversationTail is supported.")]
        public string Placement { get; set; } = "ConversationTail";

        /// <summary>
        /// Optional Anthropic cache TTL. Empty uses the provider default 5-minute cache.
        /// Use "1h" for the longer Anthropic prompt cache duration.
        /// </summary>
        [ConfigField(Hint = "Empty = provider default 5m; set to 1h for Anthropic's longer cache.")]
        public string Ttl { get; set; } = string.Empty;

        /// <summary>
        /// Returns true when prompt-cache markers should be applied for the model.
        /// </summary>
        public bool ShouldApply(string? model)
        {
            if (!Enabled ||
                !string.Equals(Placement, "ConversationTail", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(model) ||
                ModelPatterns.Count == 0)
            {
                return false;
            }

            foreach (var pattern in ModelPatterns)
            {
                if (!string.IsNullOrWhiteSpace(pattern) &&
                    model.Contains(pattern.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

    [ConfigSection("Tools.File", DisplayName = "Tools > File", Order = 20)]
    public sealed class FileToolsConfig
    {
        /// <summary>
        /// If true, operations outside workspace require user approval.
        /// If false, operations outside workspace are completely blocked.
        /// </summary>
        public bool RequireApprovalOutsideWorkspace { get; set; } = true;

        /// <summary>
        /// Maximum file size in bytes (default: 10 MB).
        /// </summary>
        [ConfigField(Min = 0, Hint = "bytes, default 10485760 (10 MB)")]
        public int MaxFileSize { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// Optional ripgrep executable path used to accelerate GrepFiles.
        /// Empty means use DOTCRAFT_RG_PATH, then rg on PATH, then the built-in fallback.
        /// </summary>
        [ConfigField(Hint = "Optional rg executable path. Empty = DOTCRAFT_RG_PATH, then PATH, then built-in fallback.")]
        public string RipgrepPath { get; set; } = string.Empty;

        /// <summary>
        /// Maximum time allowed for GrepFiles content search before returning a timeout result.
        /// Applies to ripgrep and the built-in fallback.
        /// </summary>
        [ConfigField(Min = 1, Hint = "seconds, default 30")]
        public int SearchTimeoutSeconds { get; set; } = 30;
    }

    [ConfigSection("Tools.Shell", DisplayName = "Tools > Shell", Order = 21)]
    public sealed class ShellToolsConfig
    {
        /// <summary>
        /// Command execution timeout in seconds.
        /// </summary>
        [ConfigField(Min = 0, Hint = "seconds")]
        public int Timeout { get; set; } = 300;

        /// <summary>
        /// Maximum output length in characters.
        /// </summary>
        [ConfigField(Min = 0, Hint = "characters")]
        public int MaxOutputLength { get; set; } = 10000;

        /// <summary>
        /// Background terminal/session management settings for host shell commands.
        /// </summary>
        public ShellBackgroundConfig Background { get; set; } = new();
    }

    [ConfigSection("Tools.Shell.Background", DisplayName = "Tools > Shell > Background", Order = 21)]
    public sealed class ShellBackgroundConfig
    {
        public bool Enabled { get; set; } = true;

        [ConfigField(Min = 0, Hint = "milliseconds")]
        public int DefaultYieldTimeMs { get; set; } = 1000;

        [ConfigField(Min = 1, Hint = "milliseconds")]
        public int MaxYieldTimeMs { get; set; } = 30000;

        [ConfigField(Min = 1)]
        public int MaxSessionsPerThread { get; set; } = 8;

        [ConfigField(Min = 1)]
        public int MaxSessionsPerWorkspace { get; set; } = 32;

        [ConfigField(Min = 0, Hint = "seconds")]
        public int IdleTimeoutSeconds { get; set; } = 1800;

        [ConfigField(Min = 1, Hint = "bytes")]
        public long OutputMaxBytes { get; set; } = 64L * 1024 * 1024;

        [ConfigField(Min = 0, Hint = "days")]
        public int OutputRetentionDays { get; set; } = 7;

        [ConfigField(Min = 0, Hint = "seconds")]
        public int StallWatchdogSeconds { get; set; } = 45;

        [ConfigField(Min = 1, Hint = "characters")]
        public int DefaultReadMaxOutputChars { get; set; } = 10000;
    }

    [ConfigSection("Tools.Web", DisplayName = "Tools > Web", Order = 22)]
    public sealed class WebToolsConfig
    {
        /// <summary>
        /// Maximum characters to extract from fetched content (default: 50000).
        /// </summary>
        [ConfigField(Min = 0)]
        public int MaxChars { get; set; } = 50000;

        /// <summary>
        /// Request timeout in seconds (default: 300).
        /// </summary>
        [ConfigField(Min = 0, Hint = "seconds")]
        public int Timeout { get; set; } = 300;

        /// <summary>
        /// Default maximum number of search results to return (default: 5, range: 1-10).
        /// </summary>
        [ConfigField(Min = 1, Max = 10)]
        public int SearchMaxResults { get; set; } = 5;

        /// <summary>
        /// Search provider: "Bing" (globally accessible) or "Exa" (AI-optimized, free via MCP).
        /// </summary>
        [ConfigField(FieldType = "select", Options = ["Exa", "Bing"])]
        public string SearchProvider { get; set; } = WebSearchProvider.Exa;
    }

    [ConfigSection("Tools.Sandbox", DisplayName = "Tools > Sandbox", Order = 23)]
    public sealed class SandboxConfig
    {
        /// <summary>
        /// Enable sandbox mode. When enabled, shell and file tools execute
        /// inside an isolated OpenSandbox container instead of the host machine.
        /// Requires a running OpenSandbox server.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// OpenSandbox server address (host:port).
        /// </summary>
        public string Domain { get; set; } = "localhost:5880";

        /// <summary>
        /// OpenSandbox API key (optional, depends on server configuration).
        /// </summary>
        [ConfigField(Sensitive = true)]
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Use HTTPS to connect to the OpenSandbox server.
        /// </summary>
        public bool UseHttps { get; set; }

        /// <summary>
        /// Docker image used for sandbox containers.
        /// </summary>
        public string Image { get; set; } = "ubuntu:latest";

        /// <summary>
        /// Sandbox auto-termination timeout in seconds (server-side TTL).
        /// </summary>
        [ConfigField(Min = 0)]
        public int TimeoutSeconds { get; set; } = 600;

        /// <summary>
        /// CPU resource limit for the sandbox container.
        /// </summary>
        public string Cpu { get; set; } = "1";

        /// <summary>
        /// Memory resource limit for the sandbox container.
        /// </summary>
        public string Memory { get; set; } = "512Mi";

        /// <summary>
        /// Network policy: "deny" (block all egress), "allow" (no restrictions),
        /// "custom" (allow only domains listed in AllowedEgressDomains).
        /// </summary>
        [ConfigField(FieldType = "select", Options = ["allow", "deny", "custom"])]
        public string NetworkPolicy { get; set; } = "allow";

        /// <summary>
        /// Domains allowed for outbound network access when NetworkPolicy is "custom".
        /// </summary>
        [ConfigField(Hint = "JSON array of domain strings")]
        public List<string> AllowedEgressDomains { get; set; } = [];

        /// <summary>
        /// Seconds of inactivity before an idle sandbox is automatically destroyed.
        /// Set to 0 to disable idle cleanup.
        /// </summary>
        [ConfigField(Min = 0)]
        public int IdleTimeoutSeconds { get; set; } = 300;

        /// <summary>
        /// Whether to sync the host workspace into the sandbox on creation.
        /// </summary>
        public bool SyncWorkspace { get; set; } = true;

        /// <summary>
        /// Relative paths (from workspace root) to exclude when syncing the workspace into the sandbox.
        /// Entries are matched as path prefixes: a pattern of "foo/bar" excludes the file "foo/bar"
        /// and everything inside the directory "foo/bar/".
        /// Defaults protect sensitive DotCraft runtime data from leaking into the sandbox.
        /// </summary>
        [ConfigField(Hint = "JSON array of relative paths to exclude from workspace sync (prefix matching). Default covers all sensitive .craft/ runtime data. Extend rather than replace.")]
        public List<string> SyncExclude { get; set; } =
        [
            ".craft/config.json",   // API keys and all runtime settings
            ".craft/sessions",      // full conversation history
            ".craft/memory",        // long-term user/project facts
            ".craft/dashboard",     // LLM trace data and token usage records
            ".craft/security",      // persisted approval records (authorized paths/commands)
            ".craft/logs",          // ACP communication debug logs
            ".craft/plans",         // per-session task planning history
        ];
    }

    [ConfigSection("Tools.Lsp", DisplayName = "Tools > LSP", Order = 24)]
    public sealed class LspToolsConfig
    {
        /// <summary>
        /// Enables the built-in LSP tool.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Maximum file size in bytes read before opening the document in LSP.
        /// </summary>
        [ConfigField(Min = 1, Hint = "bytes, default 10485760 (10 MB)")]
        public int MaxFileSize { get; set; } = 10 * 1024 * 1024;
    }

    [ConfigSection("Tools.DeferredLoading", DisplayName = "Tools > Deferred Loading", Order = 24)]
    public sealed class DeferredLoadingConfig
    {
        /// <summary>
        /// Controls deferred tool loading. Default: Auto.
        /// </summary>
        public DeferredLoadingStrategy Strategy { get; set; } = DeferredLoadingStrategy.Auto;

        /// <summary>
        /// MCP tool names that are always loaded upfront, even when deferred loading is enabled.
        /// Use this for high-frequency tools that should be available immediately.
        /// </summary>
        [ConfigField(Hint = "JSON array of MCP tool names to always load upfront. MCP tools typically use snake_case; use exact name as exposed by the server.")]
        public List<string> AlwaysLoadedTools { get; set; } = [];

        /// <summary>
        /// Maximum number of results returned by SearchTools per query. Default: 5.
        /// </summary>
        [ConfigField(Min = 1, Max = 20)]
        public int MaxSearchResults { get; set; } = 5;

        /// <summary>
        /// Minimum MCP tool count required to activate deferred loading.
        /// If the total number of MCP tools is below this threshold, all tools load normally.
        /// Default: 10.
        /// </summary>
        [ConfigField(Min = 1, Hint = "Deferred loading only activates when total MCP tools >= this value")]
        public int DeferThreshold { get; set; } = 10;
    }

    public enum DeferredLoadingStrategy
    {
        Off,
        Auto,
        Simulated,
        Native
    }

    public sealed class ToolsConfig
    {
        public FileToolsConfig File { get; set; } = new();
        
        public ShellToolsConfig Shell { get; set; } = new();
        
        public WebToolsConfig Web { get; set; } = new();

        public SandboxConfig Sandbox { get; set; } = new();

        public LspToolsConfig Lsp { get; set; } = new();

        public DeferredLoadingConfig DeferredLoading { get; set; } = new();

        /// <summary>
        /// Global tool result size limits and spill-to-disk preview settings.
        /// </summary>
        public ToolResultLimitsConfig ResultLimits { get; set; } = new();
    }

    [ConfigSection("Plugins", DisplayName = "Plugins", Order = 26)]
    public sealed class PluginsConfig
    {
        /// <summary>
        /// Plugin ids explicitly enabled for this workspace.
        /// </summary>
        [ConfigField(Hint = "JSON array of plugin ids. Built-in ids include browser and external-channel.")]
        public List<string> EnabledPlugins { get; set; } = [];

        /// <summary>
        /// Plugin ids explicitly disabled for this workspace.
        /// </summary>
        [ConfigField(Hint = "JSON array of plugin ids. Disabled entries override enabled/default entries.")]
        public List<string> DisabledPlugins { get; set; } = [];

        /// <summary>
        /// Additional local plugin roots or plugin container directories.
        /// Relative paths are resolved against the workspace root.
        /// </summary>
        [ConfigField(Hint = "JSON array of local plugin roots or directories containing plugins.")]
        public List<string> PluginRoots { get; set; } = [];

        public bool IsPluginEnabled(string pluginId, bool defaultEnabled)
        {
            var canonicalPluginId = PluginIds.Canonicalize(pluginId);
            if (DisabledPlugins.Any(id => PluginIds.EqualsCanonical(id, canonicalPluginId)))
                return false;

            if (EnabledPlugins.Any(id => PluginIds.EqualsCanonical(id, canonicalPluginId)))
                return true;

            return defaultEnabled;
        }
    }

    [ConfigSection("Tools.ResultLimits", DisplayName = "Tools > Result limits", Order = 24)]
    public sealed class ToolResultLimitsConfig
    {
        /// <summary>
        /// Default maximum tool result length in characters before spill-to-disk (per-tool overrides via ToolAttribute.MaxResultChars).
        /// </summary>
        [ConfigField(Min = 0, Hint = "characters; 0 disables limiting for tools that use the global default")]
        public int MaxToolResultChars { get; set; } = 50_000;

        /// <summary>
        /// Number of head and tail lines included in the preview when a result is spilled to disk.
        /// </summary>
        [ConfigField(Min = 1, Max = 500)]
        public int SpillPreviewLines { get; set; } = 40;
    }

    [ConfigSection("Security", DisplayName = "Security", Order = 80)]
    public sealed class SecurityConfig
    {
        [ConfigField(Hint = "JSON array of path strings")]
        public List<string> BlacklistedPaths { get; set; } = [];
    }

    [ConfigSection("Permissions", DisplayName = "Permissions", Order = 25)]
    public sealed class PermissionsConfig
    {
        [ConfigField(Hint = "Workspace default approval policy for threads using the default policy. One of: default, autoApprove.")]
        [JsonConverter(typeof(ApprovalPolicyJsonConverter))]
        public ApprovalPolicy DefaultApprovalPolicy { get; set; } = ApprovalPolicy.Default;
    }

    [ConfigSection("Heartbeat", DisplayName = "Heartbeat", Order = 70)]
    public sealed class HeartbeatConfig
    {
        public bool Enabled { get; set; }
        
        [ConfigField(Min = 1)]
        public int IntervalSeconds { get; set; } = 1800;
        
        public bool NotifyAdmin { get; set; } = true;
    }

    [ConfigSection("Cron", DisplayName = "Cron", Order = 60)]
    public sealed class CronConfig
    {
        public bool Enabled { get; set; } = true;
        
        public string StorePath { get; set; } = "cron/jobs.json";
    }

    [ConfigSection("Goals", DisplayName = "Goals", Order = 62)]
    public sealed class GoalsConfig
    {
        /// <summary>
        /// Enables persistent thread goals, runtime prompt injection, goal tools, and AppServer goal APIs.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// When enabled, active goals automatically start a server-managed continuation turn once the thread is idle.
        /// </summary>
        public bool AutoContinueEnabled { get; set; } = true;
    }

    [ConfigSection("Skills", DisplayName = "Skills", Order = 58)]
    public sealed class SkillsConfig
    {
        /// <summary>
        /// Skill directory names disabled for this workspace (not injected into agent context).
        /// </summary>
        [ConfigField(Hint = "JSON array of skill names to disable for this workspace", Reload = ReloadBehavior.Hot, HasReload = true)]
        public List<string> DisabledSkills { get; set; } = [];

        /// <summary>
        /// Optional agent self-learning behavior for creating and updating workspace skills.
        /// </summary>
        [ConfigField(Ignore = true)]
        public SelfLearningConfig SelfLearning { get; set; } = new();
    }

    [ConfigSection("Skills.SelfLearning", DisplayName = "Skills > Self-Learning", Order = 58)]
    public sealed class SelfLearningConfig
    {
        /// <summary>
        /// Master switch. When disabled, no skill mutation tools are exposed.
        /// </summary>
        [ConfigField(Hint = "Allow the agent to create and update workspace skills", Reload = ReloadBehavior.ProcessRestart, HasReload = false)]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Controls whether self-learning updates are routed to workspace-local skill variants.
        /// </summary>
        [ConfigField(Hint = "Skill variant write mode: enabled or disabled. Future modes may add passive evidence collection.", Reload = ReloadBehavior.ProcessRestart, HasReload = false)]
        public string VariantMode { get; set; } = "enabled";

        /// <summary>
        /// Maximum SKILL.md content length in characters.
        /// </summary>
        [ConfigField(Min = 1, Hint = "Maximum SKILL.md size in characters")]
        public int MaxSkillContentChars { get; set; } = DotCraft.Skills.SkillFrontmatter.DefaultMaxSkillContentChars;

        /// <summary>
        /// Maximum supporting file size in bytes.
        /// </summary>
        [ConfigField(Min = 1, Hint = "Maximum supporting file size in bytes")]
        public int MaxSupportingFileBytes { get; set; } = DotCraft.Skills.SkillFrontmatter.DefaultMaxSupportingFileBytes;
    }

    [ConfigSection("SubAgent", DisplayName = "SubAgent", Order = 58)]
    public sealed class SubAgentConfig
    {
        /// <summary>
        /// Workspace-scoped SubAgent profile names disabled for prompt exposure and execution.
        /// The protected default profile remains enabled even if listed here.
        /// </summary>
        [ConfigField(Hint = "JSON array of SubAgent profile names to disable for this workspace", Reload = ReloadBehavior.Hot, HasReload = true)]
        public List<string> DisabledProfiles { get; set; } = [];

        /// <summary>
        /// Whether external CLI subagents may resume previously saved external sessions for this workspace.
        /// When disabled, DotCraft always launches a fresh external CLI session.
        /// </summary>
        [ConfigField(Hint = "Reuse saved external CLI sessions for supported SubAgent profiles in this workspace", Reload = ReloadBehavior.Hot, HasReload = true)]
        public bool EnableExternalCliSessionResume { get; set; }

        /// <summary>
        /// Optional model used by DotCraft-managed native SubAgents.
        /// Empty means native SubAgents inherit the current thread's effective MainAgent model.
        /// </summary>
        [ConfigField(Hint = "Model for native SubAgents. Empty = use the current thread's effective MainAgent model.", Reload = ReloadBehavior.Hot, HasReload = true)]
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Maximum allowed depth for session-backed SubAgent spawning. The first child has depth 1.
        /// </summary>
        [ConfigField(Min = 1, Hint = "Maximum SubAgent spawn depth. Default 1 allows root -> SubAgent only.", Reload = ReloadBehavior.Hot, HasReload = true)]
        public int MaxDepth { get; set; } = 1;

        /// <summary>
        /// Workspace-configured SubAgent roles. Entries override built-in roles by name.
        /// </summary>
        [ConfigField(Ignore = true)]
        public List<SubAgentRoleConfig> Roles { get; set; } = [];
    }

    [ConfigSection("WelcomeSuggestions", DisplayName = "Welcome Suggestions", Order = 59)]
    public sealed class WelcomeSuggestionsConfig
    {
        [ConfigField(Hint = "Enable personalized welcome suggestions on the Desktop welcome screen.", Reload = ReloadBehavior.Hot, HasReload = true)]
        public bool Enabled { get; set; } = true;
    }

    [ConfigSection("Hooks", DisplayName = "Hooks", Order = 85)]
    public sealed class HooksConfig
    {
        /// <summary>
        /// Whether hooks are enabled (default: true when hooks.json exists).
        /// Set to false to globally disable all hooks.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    [ConfigSection("Tracing", DisplayName = "Tracing", Order = 45)]
    public sealed class TracingConfig
    {
        /// <summary>
        /// Whether tracing is enabled. When disabled, no trace events are recorded regardless of Dashboard state.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    [ConfigSection("DashBoard", DisplayName = "Dashboard", Order = 50)]
    public sealed class DashBoardConfig
    {
        public bool Enabled { get; set; } = true;

        [ConfigField(Min = 1, Max = 65535)]
        public int Port { get; set; } = 8080;

        public string Host { get; set; } = "127.0.0.1";

        /// <summary>
        /// Dashboard login username. When both Username and Password are set,
        /// all dashboard routes require authentication via a login page.
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Dashboard login password.
        /// </summary>
        [ConfigField(Sensitive = true)]
        public string Password { get; set; } = string.Empty;
    }

    [ConfigSection("Logging", DisplayName = "Logging", Order = 90)]
    public sealed class LoggingConfig
    {
        /// <summary>
        /// Enable file logging. Default: true.
        /// </summary>
        [ConfigField(Hint = "Write log entries to a daily-rotated file under the logs directory")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Also write logs to the console (stdout). Default: false.
        /// </summary>
        [ConfigField(Hint = "Also print log entries to stdout (useful for debugging)")]
        public bool Console { get; set; }

        /// <summary>
        /// Minimum log level: Trace, Debug, Information, Warning, Error, Critical. Default: Information.
        /// </summary>
        [ConfigField(Hint = "Minimum log level to record", FieldType = "select", Options = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"])]
        public string MinLevel { get; set; } = "Information";

        /// <summary>
        /// Log directory relative to the .craft path. Default: "logs".
        /// </summary>
        [ConfigField(Hint = "Log directory relative to the .craft path")]
        public string Directory { get; set; } = "logs";

        /// <summary>
        /// Number of days to retain log files before deletion. Set to 0 to disable cleanup. Default: 7.
        /// </summary>
        [ConfigField(Hint = "Days to keep log files before auto-deletion; 0 = keep forever")]
        public int RetentionDays { get; set; } = 7;
    }

    [ConfigSection("StreamDebug", DisplayName = "Stream Debug", Order = 95)]
    public sealed class StreamDebugConfig
    {
        /// <summary>
        /// Enables dedicated stream debug logging to a standalone file under the logs directory.
        /// </summary>
        [ConfigField(Hint = "Write detailed stream delta diagnostics to a dedicated file under logs")]
        public bool Enabled { get; set; }

        /// <summary>
        /// Optional thread filter. When set, only events for this thread are written.
        /// </summary>
        [ConfigField(Hint = "Only log this thread ID (empty = all threads)")]
        public string ThreadIdFilter { get; set; } = string.Empty;

        /// <summary>
        /// Optional turn filter. When set, only events for this turn are written.
        /// </summary>
        [ConfigField(Hint = "Only log this turn ID (empty = all turns)")]
        public string TurnIdFilter { get; set; } = string.Empty;

        /// <summary>
        /// Include full text payloads (delta/snapshot). Disable when handling sensitive content.
        /// </summary>
        [ConfigField(Hint = "Include full text in stream debug records")]
        public bool IncludeFullText { get; set; } = true;
    }
}
