using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Plugins;

namespace DotCraft.AppServer;

public sealed record ChannelPluginConfigSnapshot(
    PluginConfigSnapshot Settings,
    IReadOnlyList<string> MissingRequiredFields);

public sealed partial class ChannelPluginWorkspace
{
    private const int MaxConfigBytes = 1_048_576;
    private static readonly JsonElement EmptyObject = JsonSerializer.SerializeToElement(new JsonObject());

    public void EnsureConfigFile(PluginChannelManifest module)
    {
        try
        {
            var path = _paths.Data.Resolve(module.ConfigFileName);
            AtomicConfigDocument.WithLock(path, () =>
            {
                if (!File.Exists(path))
                    AtomicConfigDocument.Write(path, new JsonObject());
            });
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw AppServerErrors.ChannelValidationFailed("ChannelPluginConfigUnavailable");
        }
    }

    public ChannelPluginConfigSnapshot ReadSnapshot(PluginChannelManifest module)
    {
        try
        {
            return ReadConfig(module);
        }
        catch (PluginConfigException ex)
        {
            throw AppServerErrors.ChannelValidationFailed(ex.Code == PluginConfigStore.DocumentInvalid
                ? "ChannelPluginConfigUnavailable"
                : ex.Code);
        }
    }

    internal ChannelPluginConfigSnapshot ReadConfig(PluginChannelManifest module)
    {
        try
        {
            var path = _paths.Data.Resolve(module.ConfigFileName);
            return Project(module, ReadConfigDocument(path));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            throw new PluginConfigException(PluginConfigStore.DocumentInvalid,
                "The workspace channel configuration is not a readable JSON object within the workspace.", ex);
        }
    }

    internal PluginConfigSnapshot MutateConfig(
        PluginChannelManifest module, string scope, IReadOnlyList<PluginConfigMutation> operations)
    {
        if (scope != "workspace")
            throw new PluginConfigException(PluginConfigStore.ScopeUnavailable, "Channel settings can only be saved in the workspace.");
        if (operations.Count == 0)
            throw new PluginConfigException(PluginConfigStore.MutationInvalid, "At least one mutation operation is required.");

        var descriptors = module.ConfigDescriptors.Where(descriptor => !descriptor.InteractiveSetupOnly)
            .ToDictionary(descriptor => descriptor.Key, StringComparer.OrdinalIgnoreCase);
        PluginConfigSnapshot? snapshot = null;
        try
        {
            var path = _paths.Data.Resolve(module.ConfigFileName);
            AtomicConfigDocument.WithLock(path, () =>
            {
                var root = ReadConfigDocument(path);
                var original = root.DeepClone();
                foreach (var operation in operations)
                {
                    if (!descriptors.TryGetValue(operation.Key, out var descriptor))
                        throw new PluginConfigException(PluginConfigStore.MutationInvalid, "The setting is not editable in this channel.");
                    ApplyOperation(root, descriptor, operation);
                }

                if (JsonSerializer.SerializeToUtf8Bytes(root).Length > MaxConfigBytes)
                    throw new PluginConfigException(PluginConfigStore.MutationInvalid, "The workspace channel configuration is too large.");

                if (!JsonNode.DeepEquals(original, root))
                    AtomicConfigDocument.Write(path, root);
                snapshot = Project(module, root).Settings;
            });
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            throw new PluginConfigException(PluginConfigStore.WriteFailed,
                "The workspace channel configuration could not be written.", ex);
        }

        return snapshot!;
    }

    private static JsonObject ReadConfigDocument(string path)
    {
        AtomicConfigDocument.RejectLinks(path);
        if (File.Exists(path) && new FileInfo(path).Length > MaxConfigBytes)
            throw new PluginConfigException(PluginConfigStore.DocumentInvalid, "The workspace channel configuration is too large.");
        try
        {
            return AtomicConfigDocument.Read(path);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new PluginConfigException(PluginConfigStore.DocumentInvalid,
                "The workspace channel configuration must be a JSON object.", ex);
        }
    }

    private static ChannelPluginConfigSnapshot Project(PluginChannelManifest module, JsonObject config)
    {
        var fields = new List<PluginSettingsField>();
        var workspace = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var effective = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var secrets = new List<string>();
        var missing = new List<string>();
        foreach (var descriptor in module.ConfigDescriptors)
        {
            if (descriptor.InteractiveSetupOnly)
                continue;

            var field = descriptor.ToSettingsField();
            fields.Add(field);
            JsonElement? value = null;
            if (!TryGetSetting(config, descriptor.Key, out var stored))
            {
                value = field.DefaultValue;
            }
            else if (stored != null)
            {
                var candidate = JsonSerializer.SerializeToElement(stored);
                if (descriptor.AcceptsSettingValue(field, candidate))
                {
                    value = candidate;
                    if (!descriptor.IsSecret)
                        workspace.Add(descriptor.Key, candidate);
                }
            }

            if (descriptor.Required && !IsPopulated(value))
                missing.Add(descriptor.Key);
            if (descriptor.IsSecret)
            {
                if (IsPopulated(value))
                    secrets.Add(descriptor.Key);
            }
            else if (value is { } visible)
            {
                effective.Add(descriptor.Key, visible);
            }
        }

        return new ChannelPluginConfigSnapshot(
            new PluginConfigSnapshot(
                new PluginSettingsSchema { Path = module.ManifestPath, Fields = fields },
                EmptyObject,
                JsonSerializer.SerializeToElement(workspace),
                JsonSerializer.SerializeToElement(effective),
                ["workspace"])
            { ConfiguredSecrets = secrets },
            missing);
    }

    private static bool IsPopulated(JsonElement? value) => value?.ValueKind switch
    {
        null or JsonValueKind.Null or JsonValueKind.Undefined => false,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.Value.GetString()),
        JsonValueKind.Array => value.Value.GetArrayLength() > 0,
        JsonValueKind.Object => value.Value.EnumerateObject().Any(),
        _ => true
    };

    private static bool TryGetSetting(JsonObject root, string key, out JsonNode? value)
    {
        value = root;
        foreach (var segment in key.Split('.'))
        {
            if (value is not JsonObject parent)
            {
                value = null;
                return true;
            }
            if (!parent.TryGetPropertyValue(segment, out value))
                return false;
        }
        return true;
    }

    private static void ApplyOperation(JsonObject root, PluginChannelConfigDescriptor descriptor, PluginConfigMutation operation)
    {
        switch (operation.Op)
        {
            case "set":
                if (operation.Value is not { } value || !descriptor.AcceptsSettingValue(descriptor.ToSettingsField(), value))
                    throw new PluginConfigException(PluginConfigStore.MutationInvalid, $"Invalid value for channel setting '{descriptor.Key}'.");
                var parent = GetSettingParent(root, descriptor.Key, create: true, out var key)!;
                parent[key] = JsonNode.Parse(value.GetRawText());
                break;
            case "unset":
                GetSettingParent(root, descriptor.Key, create: false, out var removeKey)?.Remove(removeKey);
                break;
            default:
                throw new PluginConfigException(PluginConfigStore.MutationInvalid, "Unsupported plugin configuration operation.");
        }
    }

    private static JsonObject? GetSettingParent(JsonObject root, string key, bool create, out string property)
    {
        var segments = key.Split('.');
        property = segments[^1];
        foreach (var segment in segments.Take(segments.Length - 1))
        {
            if (root[segment] is JsonObject child)
            {
                root = child;
                continue;
            }
            if (!create)
                return null;
            if (root.ContainsKey(segment))
                throw new PluginConfigException(PluginConfigStore.NamespaceInvalid,
                    "An existing platform configuration section is not a JSON object.");

            var next = new JsonObject();
            root[segment] = next;
            root = next;
        }
        return root;
    }
}
