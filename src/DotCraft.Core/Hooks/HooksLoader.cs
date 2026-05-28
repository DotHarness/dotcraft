using System.Text.Json;
using DotCraft.Diagnostics;

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
        PropertyNameCaseInsensitive = true
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

    private static HooksFileConfig LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return new HooksFileConfig();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<HooksFileConfig>(json, JsonOptions) ?? new HooksFileConfig();
        }
        catch (Exception ex)
        {
            // Invalid JSON or deserialization error — treat as empty config
            Console.Error.WriteLine($"[Hooks] Warning: failed to parse {path}: {ex.Message}");
            return new HooksFileConfig();
        }
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
}
