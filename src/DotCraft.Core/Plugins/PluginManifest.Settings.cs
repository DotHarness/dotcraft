using System.Text.Json;

namespace DotCraft.Plugins;

public static partial class PluginManifestParser
{
    private static PluginSettingsSchema? ParseSettings(
        string pluginRoot,
        string? value,
        string? pluginId,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var path = ResolveOptionalManifestPath(
            pluginRoot,
            value,
            "settings",
            pluginId,
            manifestPath,
            diagnostics);
        if (path == null)
            return null;
        if (!File.Exists(path))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "PluginSettingsSchemaMissing",
                "Plugin settings schema file is missing.",
                pluginId,
                path: path));
            return null;
        }

        RawPluginSettingsSchema? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawPluginSettingsSchema>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginSettingsSchema",
                $"Failed to read plugin settings schema: {exception.Message}",
                pluginId,
                path: path));
            return null;
        }

        if (raw?.Fields == null)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginSettingsSchema",
                "Plugin settings schema must be a JSON object with a fields array.",
                pluginId,
                path: path));
            return null;
        }

        var initialDiagnosticCount = diagnostics.Count;
        var fields = new List<PluginSettingsField>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawField in raw.Fields)
        {
            var key = rawField.Key?.Trim();
            var type = rawField.Type?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginSettingsField",
                    "Plugin settings field key is required.",
                    pluginId,
                    path: path));
                continue;
            }
            if (!keys.Add(key))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "DuplicatePluginSettingsField",
                    $"Plugin settings field '{key}' is duplicated case-insensitively.",
                    pluginId,
                    path: path));
                continue;
            }
            if (string.IsNullOrWhiteSpace(type) || !PluginSettingsValidation.IsSupportedType(type))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginSettingsFieldType",
                    $"Plugin settings field '{key}' has unsupported type '{type}'.",
                    pluginId,
                    path: path));
                continue;
            }
            if (type == "select"
                && (rawField.Options == null
                    || rawField.Options.Count == 0
                    || rawField.Options.Any(string.IsNullOrWhiteSpace)
                    || rawField.Options.Distinct(StringComparer.Ordinal).Count() != rawField.Options.Count))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginSettingsFieldOptions",
                    $"Select field '{key}' requires unique non-empty string options.",
                    pluginId,
                    path: path));
                continue;
            }
            if (type != "select" && rawField.Options is { Count: > 0 }
                || type != "number" && (rawField.Min != null || rawField.Max != null)
                || rawField.Min > rawField.Max)
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginSettingsFieldConstraints",
                    $"Plugin settings field '{key}' has constraints that do not apply to type '{type}'.",
                    pluginId,
                    path: path));
                continue;
            }

            var field = new PluginSettingsField
            {
                Key = key,
                Type = type,
                DefaultValue = rawField.DefaultValue.ValueKind == JsonValueKind.Undefined
                    ? null
                    : rawField.DefaultValue.Clone(),
                Options = rawField.Options?.ToArray() ?? [],
                Min = rawField.Min,
                Max = rawField.Max
            };
            if (field.DefaultValue is { } defaultValue)
            {
                try
                {
                    PluginSettingsValidation.ValidateValue(field, defaultValue, "InvalidPluginSettingsDefaultValue");
                }
                catch (PluginConfigException exception)
                {
                    diagnostics.Add(PluginDiagnostic.Error(
                        exception.Code,
                        exception.Message,
                        pluginId,
                        path: path));
                    continue;
                }
            }
            fields.Add(field);
        }

        return diagnostics.Count == initialDiagnosticCount
            ? new PluginSettingsSchema { Path = path, Fields = fields }
            : null;
    }

    private sealed class RawPluginSettingsSchema
    {
        public List<RawPluginSettingsField>? Fields { get; set; }
    }

    private sealed class RawPluginSettingsField
    {
        public string? Key { get; set; }

        public string? Type { get; set; }

        public JsonElement DefaultValue { get; set; }

        public List<string>? Options { get; set; }

        public double? Min { get; set; }

        public double? Max { get; set; }
    }
}
