using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Workspaces;

namespace DotCraft.Hooks;

/// <summary>
/// Discovers and merges hook configurations from the configured user and workspace data locations.
/// Workspace hooks are appended after global hooks (additive merge per event).
/// </summary>
public sealed class HooksLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Resolved DotCraft path context.
    /// </summary>
    private readonly DotCraftPaths? _paths;

    public HooksLoader(DotCraftPaths paths)
        : this(paths.Data.RootPath, paths.UserData.ResolveOrNull("hooks.json"))
    {
        _paths = paths;
    }

    public HooksLoader(string dataPath, string? userHooksPath = null)
    {
        WorkspaceHooksPath = Path.Combine(dataPath, "hooks.json");
        UserHooksPath = userHooksPath;
    }

    public string WorkspaceHooksPath { get; }

    /// <summary>
    /// Path to the configured user hooks file, or <see langword="null"/> when user data is disabled.
    /// </summary>
    public string? UserHooksPath { get; }

    /// <summary>
    /// Loads and merges hook configurations.
    /// Global hooks are loaded first (lower priority), then workspace hooks are appended (higher priority).
    /// </summary>
    public HooksFileConfig Load()
    {
        var globalConfig = UserHooksPath is null ? new HooksFileConfig() : LoadFromFile(UserHooksPath);
        var workspaceConfig = LoadFromFile(WorkspaceHooksPath);
        var merged = Merge(globalConfig, workspaceConfig);

        return merged;
    }

    /// <summary>
    /// Discovers config and plugin hooks, returning both client metadata and runnable trusted hooks.
    /// </summary>
    public HookDiscoveryResult Discover(
        AppConfig config,
        string workspacePath,
        IReadOnlyList<string>? builtInPluginSourceRoots = null)
    {
        var warnings = new List<string>();
        var errors = new List<HookErrorInfo>();
        var runtimeConfig = new HooksFileConfig();
        var metadata = new List<HookMetadata>();
        var displayOrder = 0;
        var hooksGloballyEnabled = config.Hooks.Enabled;

        if (UserHooksPath is not null)
        {
            AppendConfigSource(
                UserHooksPath,
                HookSources.User,
                config,
                hooksGloballyEnabled,
                runtimeConfig,
                metadata,
                warnings,
                errors,
                ref displayOrder);
        }
        AppendConfigSource(
            WorkspaceHooksPath,
            HookSources.Workspace,
            config,
            hooksGloballyEnabled,
            runtimeConfig,
            metadata,
            warnings,
            errors,
            ref displayOrder);

        var paths = _paths ?? new DotCraftPaths(
            workspacePath,
            Path.GetDirectoryName(WorkspaceHooksPath)!,
            UserHooksPath is null ? null : Path.GetDirectoryName(UserHooksPath));
        var pluginHooks = PluginHookLoader.LoadEnabledPluginHooks(
            config,
            paths,
            builtInPluginSourceRoots,
            out var pluginDiagnostics);
        foreach (var diagnostic in pluginDiagnostics.Where(d => d.Code == "InvalidPluginHooks"))
        {
            warnings.Add(diagnostic.Message);
            if (!string.IsNullOrWhiteSpace(diagnostic.Path))
            {
                errors.Add(new HookErrorInfo
                {
                    Path = diagnostic.Path,
                    Message = diagnostic.Message
                });
            }
        }

        foreach (var source in pluginHooks)
        {
            AppendHookConfig(
                source.Hooks,
                source.SourcePath,
                HookSources.Plugin,
                source.PluginId,
                source.SourceRelativePath,
                source.PluginRoot,
                source.PluginDataPath,
                source.IsManagedBuiltIn,
                config,
                hooksGloballyEnabled,
                runtimeConfig,
                metadata,
                warnings,
                ref displayOrder);
        }

        return new HookDiscoveryResult
        {
            RuntimeConfig = runtimeConfig,
            Hooks = metadata,
            Warnings = warnings,
            Errors = errors
        };
    }

    private static HooksFileConfig LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return new HooksFileConfig();

        try
        {
            var json = File.ReadAllText(path);
            return Normalize(JsonSerializer.Deserialize<HooksFileConfig>(json, JsonOptions) ?? new HooksFileConfig());
        }
        catch (Exception)
        {
            // Invalid JSON or deserialization error — treat as empty config
            return new HooksFileConfig();
        }
    }

    private void AppendConfigSource(
        string path,
        string source,
        AppConfig config,
        bool hooksGloballyEnabled,
        HooksFileConfig runtimeConfig,
        List<HookMetadata> metadata,
        List<string> warnings,
        List<HookErrorInfo> errors,
        ref int displayOrder)
    {
        if (!File.Exists(path))
            return;

        HooksFileConfig parsed;
        try
        {
            parsed = Normalize(JsonSerializer.Deserialize<HooksFileConfig>(File.ReadAllText(path), JsonOptions)
                               ?? new HooksFileConfig());
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            var message = $"Failed to parse hooks config: {ex.Message}";
            warnings.Add(message);
            errors.Add(new HookErrorInfo { Path = path, Message = message });
            return;
        }

        AppendHookConfig(
            parsed,
            Path.GetFullPath(path),
            source,
            pluginId: null,
            sourceRelativePath: null,
            pluginRoot: null,
            pluginDataPath: null,
            isManagedBuiltInPlugin: false,
            config,
            hooksGloballyEnabled,
            runtimeConfig,
            metadata,
            warnings,
            ref displayOrder);
    }

    private static void AppendHookConfig(
        HooksFileConfig sourceConfig,
        string sourcePath,
        string source,
        string? pluginId,
        string? sourceRelativePath,
        string? pluginRoot,
        string? pluginDataPath,
        bool isManagedBuiltInPlugin,
        AppConfig appConfig,
        bool hooksGloballyEnabled,
        HooksFileConfig runtimeConfig,
        List<HookMetadata> metadata,
        List<string> warnings,
        ref int displayOrder)
    {
        foreach (var eventName in HookKeys.ValidEventNames)
        {
            if (!sourceConfig.Hooks.TryGetValue(eventName, out var groups) || groups.Count == 0)
                continue;

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex] ?? new HookMatcherGroup();
                for (var hookIndex = 0; hookIndex < group.Hooks.Count; hookIndex++)
                {
                    var hook = group.Hooks[hookIndex] ?? new HookEntry();
                    var isCommand = string.Equals(hook.Type, "command", StringComparison.OrdinalIgnoreCase);
                    var isMcpTool = string.Equals(hook.Type, "mcp_tool", StringComparison.OrdinalIgnoreCase);
                    if (!isCommand && !isMcpTool)
                    {
                        warnings.Add($"Skipping unsupported hook type '{hook.Type}' in {sourcePath}.");
                        continue;
                    }

                    if (isCommand && string.IsNullOrWhiteSpace(hook.Command))
                    {
                        warnings.Add($"Skipping empty hook command in {sourcePath}.");
                        continue;
                    }

                    if (isMcpTool)
                    {
                        if (string.Equals(eventName, nameof(HookEvent.SessionEnd), StringComparison.OrdinalIgnoreCase))
                        {
                            warnings.Add($"Skipping unsupported SessionEnd mcp_tool hook in {sourcePath}.");
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(hook.Server) || string.IsNullOrWhiteSpace(hook.Tool))
                        {
                            warnings.Add($"Skipping mcp_tool hook missing 'server' or 'tool' in {sourcePath}.");
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(hook.Command))
                        {
                            warnings.Add($"Skipping mcp_tool hook with non-empty 'command' in {sourcePath}.");
                            continue;
                        }
                    }

                    var isSessionEnd = string.Equals(eventName, nameof(HookEvent.SessionEnd), StringComparison.OrdinalIgnoreCase);
                    var effectiveAsync = !isSessionEnd && hook.Async;
                    var effectiveAsyncRewake = !isSessionEnd && isCommand && hook.AsyncRewake;
                    if (isSessionEnd && (hook.Async || hook.AsyncRewake))
                    {
                        warnings.Add($"Running async SessionEnd hook synchronously in {sourcePath}.");
                    }

                    var handlerType = isMcpTool ? "mcp_tool" : "command";
                    var timeout = Math.Max(1, hook.Timeout);
                    var key = source == HookSources.Plugin && pluginId != null && sourceRelativePath != null
                        ? HookKeys.ForPlugin(pluginId, sourceRelativePath, eventName, groupIndex, hookIndex)
                        : HookKeys.ForConfig(sourcePath, eventName, groupIndex, hookIndex);
                    var currentHash = ComputeHash(eventName, group.Matcher, hook, handlerType, timeout);
                    appConfig.Hooks.State.TryGetValue(key, out var state);
                    var isManaged = isMcpTool && source == HookSources.Plugin && isManagedBuiltInPlugin;
                    var stateEnabled = state?.Enabled != false;
                    var enabled = hooksGloballyEnabled && stateEnabled;
                    var trustStatus = ResolveTrustStatus(isManaged, currentHash, state?.TrustedHash);
                    var command = isCommand ? ExpandPluginVariables(hook.Command, pluginRoot, pluginDataPath) : string.Empty;
                    var mcpServer = isMcpTool ? hook.Server!.Trim() : null;
                    var mcpTool = isMcpTool ? hook.Tool!.Trim() : null;
                    var environment = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (isCommand)
                    {
                        foreach (var (envKey, value) in hook.EnvironmentVariables)
                        {
                            if (!string.IsNullOrWhiteSpace(envKey) && value != null)
                                environment[envKey] = ExpandPluginVariables(value, pluginRoot, pluginDataPath);
                        }
                        foreach (var (envKey, value) in BuildHookEnvironment(pluginRoot, pluginDataPath))
                        {
                            if (OperatingSystem.IsWindows())
                            {
                                foreach (var existingKey in environment.Keys
                                             .Where(existingKey => existingKey.Equals(envKey, StringComparison.OrdinalIgnoreCase))
                                             .ToList())
                                    environment.Remove(existingKey);
                            }
                            environment[envKey] = value;
                        }
                    }

                    var entry = new HookEntry
                    {
                        Key = key,
                        Type = handlerType,
                        Command = command,
                        Server = mcpServer,
                        Tool = mcpTool,
                        Input = hook.Input?.DeepClone() as System.Text.Json.Nodes.JsonObject,
                        SkipIfDormant = isMcpTool && hook.SkipIfDormant,
                        If = hook.If,
                        Shell = isCommand ? hook.Shell : null,
                        Timeout = timeout,
                        StatusMessage = hook.StatusMessage,
                        Async = effectiveAsync,
                        AsyncRewake = effectiveAsyncRewake,
                        RewakeMessage = hook.RewakeMessage,
                        RewakeSummary = hook.RewakeSummary,
                        Once = hook.Once,
                        SourcePath = sourcePath,
                        Source = source,
                        PluginId = pluginId,
                        EnvironmentVariables = environment
                    };

                    metadata.Add(new HookMetadata
                    {
                        Key = key,
                        EventName = eventName,
                        HandlerType = handlerType,
                        Matcher = string.IsNullOrEmpty(group.Matcher) ? null : group.Matcher,
                        Condition = hook.If,
                        Command = isCommand ? command : null,
                        McpServer = mcpServer,
                        McpTool = mcpTool,
                        SkipIfDormant = isMcpTool && hook.SkipIfDormant,
                        TimeoutSec = timeout,
                        ExecutionMode = entry.Async || entry.AsyncRewake ? "async" : "sync",
                        AsyncRewake = entry.AsyncRewake,
                        RewakeMessage = hook.RewakeMessage,
                        RewakeSummary = hook.RewakeSummary,
                        Shell = entry.Shell,
                        Once = hook.Once,
                        StatusMessage = hook.StatusMessage,
                        SourcePath = sourcePath,
                        Source = source,
                        PluginId = pluginId,
                        DisplayOrder = displayOrder,
                        Enabled = enabled,
                        IsManaged = isManaged,
                        CurrentHash = currentHash,
                        TrustStatus = trustStatus
                    });

                    if (enabled && (isManaged || string.Equals(trustStatus, HookTrustStatuses.Trusted, StringComparison.Ordinal)))
                    {
                        AppendRuntimeHook(runtimeConfig, eventName, group.Matcher, entry);
                    }

                    displayOrder++;
                }
            }
        }
    }

    private static void AppendRuntimeHook(HooksFileConfig runtimeConfig, string eventName, string matcher, HookEntry entry)
    {
        if (!runtimeConfig.Hooks.TryGetValue(eventName, out var groups))
        {
            groups = [];
            runtimeConfig.Hooks[eventName] = groups;
        }

        groups.Add(new HookMatcherGroup
        {
            Matcher = matcher,
            Hooks = [entry]
        });
    }

    private static string ResolveTrustStatus(bool isManaged, string currentHash, string? trustedHash)
    {
        if (isManaged)
            return HookTrustStatuses.Managed;

        if (string.IsNullOrWhiteSpace(trustedHash))
            return HookTrustStatuses.Untrusted;

        return string.Equals(trustedHash, currentHash, StringComparison.Ordinal)
            ? HookTrustStatuses.Trusted
            : HookTrustStatuses.Modified;
    }

    private static string ComputeHash(string eventName, string? matcher, HookEntry hook, string handlerType, int timeout)
    {
        var normalized = handlerType == "mcp_tool"
            ? string.Join(
                "\n",
                HookKeys.ToSnakeCase(eventName),
                "mcp_tool",
                matcher ?? string.Empty,
                hook.If ?? string.Empty,
                hook.Server?.Trim() ?? string.Empty,
                hook.Tool?.Trim() ?? string.Empty,
                hook.Input?.ToJsonString() ?? string.Empty,
                hook.SkipIfDormant ? "skip_if_dormant" : string.Empty,
                timeout.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hook.StatusMessage ?? string.Empty,
                hook.Async ? "async" : "sync",
                hook.Once ? "once" : string.Empty)
            : string.Join(
                "\n",
                HookKeys.ToSnakeCase(eventName),
                matcher ?? string.Empty,
                hook.If ?? string.Empty,
                hook.Command,
                hook.Shell ?? string.Empty,
                timeout.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hook.StatusMessage ?? string.Empty,
                hook.Async ? "async" : "sync",
                hook.AsyncRewake ? "rewake" : string.Empty,
                hook.RewakeMessage ?? string.Empty,
                hook.RewakeSummary ?? string.Empty,
                hook.Once ? "once" : string.Empty);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ExpandPluginVariables(string command, string? pluginRoot, string? pluginDataPath)
    {
        if (string.IsNullOrWhiteSpace(pluginRoot) && string.IsNullOrWhiteSpace(pluginDataPath))
            return command;

        return PluginVariables.Expand(command, pluginRoot, pluginDataPath);
    }

    private static Dictionary<string, string> BuildHookEnvironment(string? pluginRoot, string? pluginDataPath)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(pluginRoot))
            env["PLUGIN_ROOT"] = pluginRoot;
        if (!string.IsNullOrWhiteSpace(pluginDataPath))
            env["PLUGIN_DATA"] = pluginDataPath;
        return env;
    }

    /// <summary>
    /// Merges two hook configs. Workspace hooks are appended after global hooks
    /// for each event (additive, not replacing).
    /// </summary>
    private static HooksFileConfig Merge(HooksFileConfig global, HooksFileConfig workspace)
    {
        var merged = new HooksFileConfig();

        // Copy all global entries
        foreach (var (eventName, groups) in global.Hooks)
        {
            merged.Hooks[eventName] = new List<HookMatcherGroup>(groups);
        }

        // Append workspace entries
        foreach (var (eventName, groups) in workspace.Hooks)
        {
            if (merged.Hooks.TryGetValue(eventName, out var existing))
            {
                existing.AddRange(groups);
            }
            else
            {
                merged.Hooks[eventName] = new List<HookMatcherGroup>(groups);
            }
        }

        return merged;
    }

    private static HooksFileConfig Normalize(HooksFileConfig config)
    {
        var normalized = new HooksFileConfig();
        foreach (var (eventName, groups) in config.Hooks)
            normalized.Hooks[eventName] = groups ?? [];
        return normalized;
    }
}
