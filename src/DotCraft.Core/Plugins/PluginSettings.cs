using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Workspaces;

namespace DotCraft.Plugins;

/// <summary>One validated field in a plugin settings schema.</summary>
public sealed record PluginSettingsField
{
    public required string Key { get; init; }

    public required string Type { get; init; }

    public JsonElement? DefaultValue { get; init; }

    public IReadOnlyList<string> Options { get; init; } = [];

    public double? Min { get; init; }

    public double? Max { get; init; }
}

/// <summary>A validated plugin settings schema.</summary>
public sealed record PluginSettingsSchema
{
    public required string Path { get; init; }

    public IReadOnlyList<PluginSettingsField> Fields { get; init; } = [];
}

/// <summary>A settings mutation applied to one persistence scope.</summary>
public sealed record PluginConfigMutation(string Op, string Key, JsonElement? Value = null);

/// <summary>A fresh layered plugin configuration snapshot.</summary>
public sealed record PluginConfigSnapshot(
    PluginSettingsSchema Schema,
    JsonElement Personal,
    JsonElement Workspace,
    JsonElement Value,
    IReadOnlyList<string> WritableScopes);

/// <summary>A stable plugin configuration failure.</summary>
public sealed class PluginConfigException : Exception
{
    public PluginConfigException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Reads and atomically mutates the dedicated plugin configuration documents.</summary>
public sealed class PluginConfigStore
{
    public const string FileName = "plugin-config.json";
    public const string ConfigurationNotDeclared = "PluginConfigurationNotDeclared";
    public const string ScopeUnavailable = "PluginConfigurationScopeUnavailable";
    public const string DocumentInvalid = "PluginConfigurationDocumentInvalid";
    public const string NamespaceInvalid = "PluginConfigurationNamespaceInvalid";
    public const string MutationInvalid = "PluginConfigurationMutationInvalid";
    public const string WriteFailed = "PluginConfigurationWriteFailed";

    private static readonly TimeSpan WriteLockTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonElement EmptyObject = JsonSerializer.Deserialize<JsonElement>("{}"u8);
    private readonly DotCraftPaths _paths;

    public PluginConfigStore(DotCraftPaths paths)
    {
        _paths = paths;
    }

    public string? PersonalPath => _paths.UserData.ResolveOrNull(FileName);

    public string WorkspacePath => _paths.Data.Resolve(FileName);

    public PluginConfigSnapshot Get(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var schema = manifest.Settings
            ?? throw new PluginConfigException(
                ConfigurationNotDeclared,
                $"Plugin '{manifest.Id}' does not declare a settings schema.");
        var personal = PersonalPath is { } personalPath
            ? ReadNamespace(personalPath, manifest.Id, schema)
            : EmptyObject.Clone();
        var workspace = ReadNamespace(WorkspacePath, manifest.Id, schema);
        var value = BuildDefaults(schema);
        value = MergeObjects(value, JsonNode.Parse(personal.GetRawText())!.AsObject());
        value = MergeObjects(value, JsonNode.Parse(workspace.GetRawText())!.AsObject());
        var scopes = PersonalPath == null
            ? new[] { "workspace" }
            : new[] { "personal", "workspace" };
        return new PluginConfigSnapshot(
            schema,
            personal,
            workspace,
            ToElement(value),
            scopes);
    }

    public PluginConfigSnapshot Mutate(
        PluginManifest manifest,
        string scope,
        IReadOnlyList<PluginConfigMutation> operations)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(operations);
        var schema = manifest.Settings
            ?? throw new PluginConfigException(
                ConfigurationNotDeclared,
                $"Plugin '{manifest.Id}' does not declare a settings schema.");
        if (operations.Count == 0)
            throw new PluginConfigException(MutationInvalid, "At least one mutation operation is required.");

        var path = scope switch
        {
            "personal" => PersonalPath ?? throw new PluginConfigException(
                ScopeUnavailable,
                "Personal plugin configuration is unavailable because UserDataPath is not configured."),
            "workspace" => WorkspacePath,
            _ => throw new PluginConfigException(
                MutationInvalid,
                "Plugin configuration scope must be 'personal' or 'workspace'.")
        };

        WithDocumentLock(path, () =>
        {
            var root = ReadRootForWrite(path);
            var namespaceKey = FindKey(root, manifest.Id) ?? manifest.Id;
            var namespaceExists = root.TryGetPropertyValue(namespaceKey, out var namespaceNode);
            var target = (namespaceExists, namespaceNode) switch
            {
                (false, _) => new JsonObject(),
                (true, JsonObject obj) => obj,
                _ => throw new PluginConfigException(
                    NamespaceInvalid,
                    $"Configuration namespace '{manifest.Id}' must be a JSON object.")
            };
            ValidateNamespace(target, schema, manifest.Id);
            var fields = schema.Fields.ToDictionary(field => field.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var operation in operations)
            {
                if (!fields.TryGetValue(operation.Key, out var field))
                    throw new PluginConfigException(
                        MutationInvalid,
                        $"Plugin setting '{operation.Key}' is not declared by '{manifest.Id}'.");
                var existingKey = FindKey(target, field.Key);
                switch (operation.Op)
                {
                    case "set":
                        if (operation.Value is not { } value)
                            throw new PluginConfigException(
                                MutationInvalid,
                                $"Set operation for '{field.Key}' requires a value.");
                        PluginSettingsValidation.ValidateValue(field, value, MutationInvalid);
                        if (existingKey != null && !string.Equals(existingKey, field.Key, StringComparison.Ordinal))
                            target.Remove(existingKey);
                        target[field.Key] = JsonNode.Parse(value.GetRawText());
                        break;
                    case "unset":
                        if (existingKey != null)
                            target.Remove(existingKey);
                        break;
                    default:
                        throw new PluginConfigException(
                            MutationInvalid,
                            $"Unsupported plugin configuration operation '{operation.Op}'.");
                }
            }

            if (target.Count == 0)
                root.Remove(namespaceKey);
            else if (!namespaceExists)
                root[namespaceKey] = target;
            WriteAtomic(path, root);
        });

        return Get(manifest);
    }

    public PluginDiagnostic? GetDiagnostic(PluginManifest manifest)
    {
        if (manifest.Settings == null)
            return null;
        try
        {
            _ = Get(manifest);
            return null;
        }
        catch (PluginConfigException exception)
        {
            return PluginDiagnostic.Error(exception.Code, exception.Message, manifest.Id);
        }
    }

    private static JsonElement ReadNamespace(string path, string pluginId, PluginSettingsSchema schema)
    {
        if (!File.Exists(path))
            return EmptyObject.Clone();
        var root = ReadRoot(path);
        var key = FindKey(root, pluginId);
        if (key == null)
            return EmptyObject.Clone();
        if (root[key] is not JsonObject value)
            throw new PluginConfigException(
                NamespaceInvalid,
                $"Configuration namespace '{pluginId}' must be a JSON object.");
        ValidateNamespace(value, schema, pluginId);
        return ToElement(value);
    }

    private static JsonObject ReadRoot(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new JsonException("The document root is not a JSON object.");
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new PluginConfigException(
                DocumentInvalid,
                // The path identifies a host file and reaches AppServer clients through
                // error.data.detail, so it stays out of the message and in the host log.
                "The plugin configuration document is invalid and was not modified.",
                exception);
        }
    }

    private static JsonObject ReadRootForWrite(string path) =>
        File.Exists(path) ? ReadRoot(path) : new JsonObject();

    private static void ValidateNamespace(JsonObject value, PluginSettingsSchema schema, string pluginId)
    {
        var fields = schema.Fields.ToDictionary(field => field.Key, StringComparer.OrdinalIgnoreCase);
        var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value)
        {
            if (!observed.Add(property.Key))
                throw new PluginConfigException(
                    NamespaceInvalid,
                    $"Configuration namespace '{pluginId}' contains duplicate field '{property.Key}'.");
            if (!fields.TryGetValue(property.Key, out var field))
                throw new PluginConfigException(
                    NamespaceInvalid,
                    $"Configuration namespace '{pluginId}' contains unknown field '{property.Key}'.");
            if (property.Value == null)
                PluginSettingsValidation.ValidateValue(field, JsonSerializer.SerializeToElement<object?>(null), NamespaceInvalid);
            else
                PluginSettingsValidation.ValidateValue(field, ToElement(property.Value), NamespaceInvalid);
        }
    }

    private static JsonObject BuildDefaults(PluginSettingsSchema schema)
    {
        var result = new JsonObject();
        foreach (var field in schema.Fields)
        {
            if (field.DefaultValue is { } defaultValue)
                result[field.Key] = JsonNode.Parse(defaultValue.GetRawText());
        }
        return result;
    }

    private static JsonObject MergeObjects(JsonObject lower, JsonObject upper)
    {
        foreach (var property in upper)
        {
            var existingKey = FindKey(lower, property.Key);
            if (existingKey != null
                && lower[existingKey] is JsonObject lowerObject
                && property.Value is JsonObject upperObject)
            {
                MergeObjects(lowerObject, upperObject);
                continue;
            }
            if (existingKey != null && !string.Equals(existingKey, property.Key, StringComparison.Ordinal))
                lower.Remove(existingKey);
            lower[property.Key] = property.Value?.DeepClone();
        }
        return lower;
    }

    private static void WithDocumentLock(string path, Action action)
    {
        var identity = OperatingSystem.IsWindows() ? Path.GetFullPath(path).ToUpperInvariant() : Path.GetFullPath(path);
        var name = $"DotCraft.PluginConfig.{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))}";
        using var mutex = new Mutex(initiallyOwned: false, name);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(WriteLockTimeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
            if (!acquired)
                throw new PluginConfigException(WriteFailed, "The plugin configuration document is busy.");
            action();
        }
        catch (PluginConfigException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PluginConfigException(WriteFailed, "The plugin configuration document could not be written.", exception);
        }
        finally
        {
            if (acquired)
                mutex.ReleaseMutex();
        }
    }

    private static void WriteAtomic(string path, JsonObject root)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempPath, json + Environment.NewLine, new UTF8Encoding(false));
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    private static string? FindKey(JsonObject parent, string key)
    {
        foreach (var property in parent)
        {
            if (string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase))
                return property.Key;
        }
        return null;
    }

    private static JsonElement ToElement(JsonNode node) =>
        JsonSerializer.SerializeToElement(node);
}

internal static class PluginSettingsValidation
{
    private static readonly HashSet<string> SupportedTypes =
    [
        "text", "textarea", "number", "bool", "select", "stringList", "keyValueMap", "json"
    ];

    public static bool IsSupportedType(string type) => SupportedTypes.Contains(type);

    public static void ValidateValue(PluginSettingsField field, JsonElement value, string errorCode)
    {
        var valid = field.Type switch
        {
            "text" or "textarea" => value.ValueKind == JsonValueKind.String,
            "number" => IsValidNumber(field, value),
            "bool" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "select" => value.ValueKind == JsonValueKind.String
                        && field.Options.Contains(value.GetString()!, StringComparer.Ordinal),
            "stringList" => value.ValueKind == JsonValueKind.Array
                            && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String),
            "keyValueMap" => value.ValueKind == JsonValueKind.Object
                             && value.EnumerateObject().All(property => property.Value.ValueKind == JsonValueKind.String),
            "json" => true,
            _ => false
        };
        if (!valid)
            throw new PluginConfigException(errorCode, $"Value for plugin setting '{field.Key}' is invalid for type '{field.Type}'.");
    }

    private static bool IsValidNumber(PluginSettingsField field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
            return false;
        return (field.Min == null || number >= field.Min)
               && (field.Max == null || number <= field.Max);
    }
}

/// <summary>Resolves the one host-side data directory shared by plugin backends.</summary>
public static class PluginDataPaths
{
    public static string Resolve(DotCraftPaths paths, string pluginId)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return paths.UserData.ResolveOrNull("plugins", pluginId, "data")
               ?? paths.Data.Resolve("plugin-data", pluginId);
    }
}
