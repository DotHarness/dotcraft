using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using DotCraft.Configuration;
using DotCraft.Diagnostics;
using DotCraft.Plugins;

namespace DotCraft.Hooks;

/// <summary>
/// Discovers and merges hook configurations from global (~/.craft/hooks.json)
/// and workspace (.craft/hooks.json) locations.
/// Workspace hooks are appended after global hooks (additive merge per event).
/// </summary>
public sealed class HooksLoader(string craftPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Path to workspace hooks config: {craftPath}/hooks.json
    /// </summary>
    public string WorkspaceHooksPath { get; } = Path.Combine(craftPath, "hooks.json");

    /// <summary>
    /// Path to global/user hooks config: ~/.craft/hooks.json
    /// </summary>
    public string GlobalHooksPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft", "hooks.json");

    /// <summary>
    /// Loads and merges hook configurations.
    /// Global hooks are loaded first (lower priority), then workspace hooks are appended (higher priority).
    /// </summary>
    public HooksFileConfig Load()
    {
        var globalConfig = LoadFromFile(GlobalHooksPath);
        var workspaceConfig = LoadFromFile(WorkspaceHooksPath);
        var merged = Merge(globalConfig, workspaceConfig);

        if (DebugModeService.IsEnabled())
        {
            Console.Error.WriteLine($"[Hooks] Global config: {GlobalHooksPath} ({(File.Exists(GlobalHooksPath) ? "found" : "not found")})");
            Console.Error.WriteLine($"[Hooks] Workspace config: {WorkspaceHooksPath} ({(File.Exists(WorkspaceHooksPath) ? "found" : "not found")})");
            Console.Error.WriteLine($"[Hooks] Merged {merged.Hooks.Count} event(s): {string.Join(", ", merged.Hooks.Keys)}");
        }

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

        AppendConfigSource(
            GlobalHooksPath,
            HookSources.User,
            config,
            hooksGloballyEnabled,
            runtimeConfig,
            metadata,
            warnings,
            errors,
            ref displayOrder);
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

        var pluginHooks = PluginHookLoader.LoadEnabledPluginHooks(
            config,
            workspacePath,
            craftPath,
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
        catch (Exception ex)
        {
            // Invalid JSON or deserialization error — treat as empty config
            Console.Error.WriteLine($"[Hooks] Warning: failed to parse {path}: {ex.Message}");
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
                    if (!string.Equals(hook.Type, "command", StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add($"Skipping unsupported hook type '{hook.Type}' in {sourcePath}.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(hook.Command))
                    {
                        warnings.Add($"Skipping empty hook command in {sourcePath}.");
                        continue;
                    }

                    var timeout = Math.Max(1, hook.Timeout);
                    var key = source == HookSources.Plugin && pluginId != null && sourceRelativePath != null
                        ? HookKeys.ForPlugin(pluginId, sourceRelativePath, eventName, groupIndex, hookIndex)
                        : HookKeys.ForConfig(sourcePath, eventName, groupIndex, hookIndex);
                    var currentHash = ComputeHash(eventName, group.Matcher, hook, timeout);
                    appConfig.Hooks.State.TryGetValue(key, out var state);
                    var isManaged = false;
                    var stateEnabled = state?.Enabled != false;
                    var enabled = hooksGloballyEnabled && stateEnabled;
                    var trustStatus = ResolveTrustStatus(isManaged, currentHash, state?.TrustedHash);
                    var command = ExpandPluginVariables(hook.Command, pluginRoot, pluginDataPath);
                    var environment = BuildHookEnvironment(pluginRoot, pluginDataPath);
                    foreach (var (envKey, value) in hook.EnvironmentVariables)
                    {
                        if (!string.IsNullOrWhiteSpace(envKey) && value != null)
                            environment[envKey] = ExpandPluginVariables(value, pluginRoot, pluginDataPath);
                    }

                    var entry = new HookEntry
                    {
                        Key = key,
                        Type = "command",
                        Command = command,
                        If = hook.If,
                        Shell = hook.Shell,
                        Timeout = timeout,
                        StatusMessage = hook.StatusMessage,
                        Async = hook.Async,
                        AsyncRewake = hook.AsyncRewake,
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
                        HandlerType = "command",
                        Matcher = string.IsNullOrEmpty(group.Matcher) ? null : group.Matcher,
                        Condition = hook.If,
                        Command = command,
                        TimeoutSec = timeout,
                        ExecutionMode = hook.Async || hook.AsyncRewake ? "async" : "sync",
                        AsyncRewake = hook.AsyncRewake,
                        RewakeMessage = hook.RewakeMessage,
                        RewakeSummary = hook.RewakeSummary,
                        Shell = hook.Shell,
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

    private static string ComputeHash(string eventName, string? matcher, HookEntry hook, int timeout)
    {
        var normalized = string.Join(
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

        var separator = Path.DirectorySeparatorChar.ToString();
        return command
            .Replace("${DOTCRAFT_PLUGIN_ROOT}/", (pluginRoot ?? string.Empty) + separator, StringComparison.Ordinal)
            .Replace(@"${DOTCRAFT_PLUGIN_ROOT}\", (pluginRoot ?? string.Empty) + separator, StringComparison.Ordinal)
            .Replace("${DOTCRAFT_PLUGIN_DATA}/", (pluginDataPath ?? string.Empty) + separator, StringComparison.Ordinal)
            .Replace(@"${DOTCRAFT_PLUGIN_DATA}\", (pluginDataPath ?? string.Empty) + separator, StringComparison.Ordinal)
            .Replace("${CLAUDE_PLUGIN_ROOT}/", (pluginRoot ?? string.Empty) + separator, StringComparison.Ordinal)
            .Replace(@"${CLAUDE_PLUGIN_ROOT}\", (pluginRoot ?? string.Empty) + separator, StringComparison.Ordinal)
            .Replace("${CLAUDE_PLUGIN_DATA}/", (pluginDataPath ?? string.Empty) + separator, StringComparison.Ordinal)
            .Replace(@"${CLAUDE_PLUGIN_DATA}\", (pluginDataPath ?? string.Empty) + separator, StringComparison.Ordinal)
            .Replace("${DOTCRAFT_PLUGIN_ROOT}", pluginRoot ?? string.Empty, StringComparison.Ordinal)
            .Replace("${DOTCRAFT_PLUGIN_DATA}", pluginDataPath ?? string.Empty, StringComparison.Ordinal)
            .Replace("${CLAUDE_PLUGIN_ROOT}", pluginRoot ?? string.Empty, StringComparison.Ordinal)
            .Replace("${CLAUDE_PLUGIN_DATA}", pluginDataPath ?? string.Empty, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> BuildHookEnvironment(string? pluginRoot, string? pluginDataPath)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(pluginRoot))
        {
            env["DOTCRAFT_PLUGIN_ROOT"] = pluginRoot;
            env["CLAUDE_PLUGIN_ROOT"] = pluginRoot;
        }
        if (!string.IsNullOrWhiteSpace(pluginDataPath))
        {
            env["DOTCRAFT_PLUGIN_DATA"] = pluginDataPath;
            env["CLAUDE_PLUGIN_DATA"] = pluginDataPath;
            env["CLAUDE_CONFIG_DIR"] = Path.Combine(pluginDataPath, "claude-compat");
            env["SECURITY_WARNINGS_STATE_DIR"] = Path.Combine(pluginDataPath, "security");
        }
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
