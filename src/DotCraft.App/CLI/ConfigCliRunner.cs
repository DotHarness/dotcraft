using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppServer;
using DotCraft.Configuration;

namespace DotCraft.CLI;

internal sealed record ConfigSchemaCommandOptions(
    string? Section,
    bool Json);

internal sealed record ConfigShowCommandOptions(
    string? WorkspacePath,
    bool Json,
    string? GlobalConfigPath = null);

internal static class ConfigCliRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web)
    {
        WriteIndented = true
    };

    public static Task<int> SchemaAsync(
        ConfigSchemaCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct = default) => ExecuteAsync(
            () => SchemaCoreAsync(options, output, error, ct), error);

    public static Task<int> ShowAsync(
        ConfigShowCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct = default) => ExecuteAsync(
            () => ShowCoreAsync(options, output, error, ct), error);

    private static async Task<int> ExecuteAsync(Func<Task<int>> action, TextWriter error)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Config command cancelled.").ConfigureAwait(false);
            return 130;
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> SchemaCoreAsync(
        ConfigSchemaCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<ConfigSchemaSection> sections = ConfigSchemaRegistrations.GetConfigSchema();
        var filter = options.Section?.Trim();
        if (!string.IsNullOrEmpty(filter))
        {
            sections = sections.Where(section => Matches(section, filter)).ToArray();
            if (sections.Count == 0)
            {
                await error.WriteLineAsync($"Unknown configuration section: {filter}").ConfigureAwait(false);
                return 1;
            }
        }

        if (options.Json)
        {
            var contract = sections.Select(ConfigSchemaContractMapper.ToContract).ToArray();
            await output.WriteLineAsync(JsonSerializer.Serialize(contract, JsonOptions)).ConfigureAwait(false);
            return 0;
        }

        await WriteSchemaTextAsync(sections, output).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> ShowCoreAsync(
        ConfigShowCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var workspacePath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(options.WorkspacePath)
                ? Directory.GetCurrentDirectory()
                : options.WorkspacePath);
        var craftPath = Path.Combine(workspacePath, ".craft");
        if (!Directory.Exists(craftPath))
        {
            await error.WriteLineAsync($"No DotCraft workspace at {workspacePath}: '.craft' directory not found.")
                .ConfigureAwait(false);
            return 1;
        }

        var configPath = Path.Combine(craftPath, "config.json");
        if (!File.Exists(configPath))
        {
            await error.WriteLineAsync($"No workspace configuration at {configPath}.").ConfigureAwait(false);
            return 1;
        }

        var config = options.GlobalConfigPath is null
            ? AppConfig.LoadWithGlobalFallback(configPath)
            : AppConfig.LoadWithGlobalFallback(configPath, options.GlobalConfigPath);

        // LoadWithGlobalFallback expands $VAR before deserializing, so every credential in the merged view is real.
        var merged = JsonSerializer.SerializeToNode(config, AppConfig.SerializerOptions) as JsonObject
            ?? new JsonObject();
        ConfigSchemaUtilities.MaskSensitiveKeys(
            merged,
            ConfigSchemaUtilities.BuildSensitiveKeys(ConfigSchemaRegistrations.GetConfigSchema()));

        await output.WriteLineAsync(merged.ToJsonString(JsonOptions)).ConfigureAwait(false);
        return 0;
    }

    private static bool Matches(ConfigSchemaSection section, string filter) =>
        string.Equals(section.Section, filter, StringComparison.OrdinalIgnoreCase)
        || string.Equals(DescribePath(section), filter, StringComparison.OrdinalIgnoreCase);

    private static async Task WriteSchemaTextAsync(
        IReadOnlyList<ConfigSchemaSection> sections,
        TextWriter output)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            if (i > 0)
                await output.WriteLineAsync().ConfigureAwait(false);

            var section = sections[i];
            await output.WriteLineAsync(section.Section).ConfigureAwait(false);
            await output.WriteLineAsync($"  Path: {DescribePath(section)}").ConfigureAwait(false);

            var itemFields = section.ItemFields;
            if (itemFields is { Count: > 0 })
            {
                await output.WriteLineAsync("  Each entry:").ConfigureAwait(false);
                foreach (var field in itemFields)
                    await output.WriteLineAsync($"    {FormatField(field)}").ConfigureAwait(false);
                continue;
            }

            foreach (var field in section.Fields)
                await output.WriteLineAsync($"    {FormatField(field)}").ConfigureAwait(false);
        }
    }

    private static string DescribePath(ConfigSchemaSection section)
    {
        if (!string.IsNullOrEmpty(section.RootKey))
            return section.RootKey;

        return section.Path is { Length: > 0 } path ? string.Join('.', path) : "(root)";
    }

    private static string FormatField(ConfigSchemaField field)
    {
        var parts = new List<string>(5)
        {
            field.Key,
            field.Type,
            $"reload={JsonNamingPolicy.CamelCase.ConvertName(field.Reload.ToString())}"
        };
        if (field.Sensitive)
            parts.Add("[sensitive]");
        if (field.DefaultValue is { } defaultValue)
            parts.Add($"default={FormatDefault(defaultValue)}");
        return string.Join("  ", parts);
    }

    private static string FormatDefault(object value) => value switch
    {
        string text => text,
        bool flag => flag ? "true" : "false",
        _ => JsonSerializer.Serialize(value, AppConfig.SerializerOptions)
    };
}
