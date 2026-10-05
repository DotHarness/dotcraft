using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Configuration;

public sealed class ConfigurationService
{
    private readonly IAppConfigMonitor _monitor;
    private readonly string _userConfigPath;
    private readonly string _workspaceConfigPath;
    private readonly IReadOnlyList<ConfigFieldDescriptor> _fields;
    private readonly Dictionary<string, ConfigFieldDescriptor> _fieldsByKeyPath;
    private readonly ConfigFieldDescriptor[] _objectFields;
    private readonly IReadOnlySet<string> _sensitiveKeys;
    private readonly JsonObject _defaults = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Dictionary<string, Func<IReadOnlyList<string>, CancellationToken, Task>> _subsystemHandlers =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<AppConfig, string?>> _validators = new(StringComparer.Ordinal);

    public ConfigurationService(
        IConfigDescriptorRegistry registry,
        IAppConfigMonitor monitor,
        string userConfigPath,
        string workspaceConfigPath)
    {
        _monitor = monitor;
        _userConfigPath = Path.GetFullPath(userConfigPath);
        _workspaceConfigPath = Path.GetFullPath(workspaceConfigPath);
        _fields = registry.Fields;
        _fieldsByKeyPath = _fields.ToDictionary(f => f.KeyPath, StringComparer.Ordinal);
        _objectFields = _fields.Where(f => IsObjectValued(f.ValueType)).ToArray();
        _sensitiveKeys = _fields
            .Where(f => f.Sensitive)
            .Select(f => ConfigJsonPath.Split(f.KeyPath)[^1])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var field in _fields)
            ConfigJsonPath.Set(_defaults, ConfigJsonPath.Split(field.KeyPath), Serialize(field, field.DefaultValue));
    }

    public void RegisterSubsystemHandler(string subsystemKey, Func<IReadOnlyList<string>, CancellationToken, Task> handler) =>
        _subsystemHandlers[subsystemKey] = handler;

    public void RegisterValidator(string keyPath, Func<AppConfig, string?> validator) =>
        _validators[keyPath] = validator;

    public ConfigReadResult Read(bool includeLayers = false)
    {
        var user = ReadLayer(_userConfigPath);
        var workspace = ReadLayer(_workspaceConfigPath);
        var userInfo = LayerInfo(ConfigLayerType.User, user);
        var workspaceInfo = LayerInfo(ConfigLayerType.Workspace, workspace);

        var origins = new Dictionary<string, ConfigLayerInfo>(StringComparer.Ordinal);
        foreach (var field in _fields)
        {
            var path = ConfigJsonPath.Split(field.KeyPath);
            if (ConfigJsonPath.TryGet(workspace, path, out _))
                origins[field.KeyPath] = workspaceInfo;
            else if (ConfigJsonPath.TryGet(user, path, out _))
                origins[field.KeyPath] = userInfo;
        }

        IReadOnlyList<ConfigLayerSnapshot>? layers = includeLayers
            ? [new(userInfo, Mask(user)), new(workspaceInfo, Mask(workspace))]
            : null;
        return new ConfigReadResult(Mask(Effective(user, workspace)), origins, layers);
    }

    public async Task<ConfigWriteResult> WriteAsync(
        IReadOnlyList<ConfigEdit> edits,
        string? filePath,
        string? expectedVersion,
        string source,
        CancellationToken cancellationToken = default)
    {
        var target = ResolveTarget(filePath);
        return await CommitAsync(target, source, current =>
        {
            var currentVersion = ConfigJsonPath.Version(current);
            if (expectedVersion != null && !string.Equals(expectedVersion, currentVersion, StringComparison.Ordinal))
            {
                throw new ConfigWriteException(
                    ConfigWriteErrorCode.ConfigVersionConflict,
                    $"The {LayerName(target)} configuration changed since version '{expectedVersion}'.");
            }

            if (edits.Count == 0)
                throw ValidationError("At least one edit is required.");
            var paths = edits.Select(ResolveEditPath).ToArray();

            var edited = (JsonObject)current.DeepClone();
            for (var i = 0; i < edits.Count; i++)
                ApplyEdit(edited, paths[i], edits[i]);

            return new LayerEdit(edited, edits.Select(e => e.KeyPath).ToArray());
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<ConfigWriteResult> ReplaceLayerAsync(
        ConfigLayerType layer,
        JsonObject document,
        string source,
        CancellationToken cancellationToken = default) =>
        CommitAsync(layer, source, current =>
        {
            var replacement = (JsonObject)document.DeepClone();
            RestoreMasked(replacement, current);
            return new LayerEdit(replacement, null);
        }, cancellationToken);

    private async Task<ConfigWriteResult> CommitAsync(
        ConfigLayerType target,
        string source,
        Func<JsonObject, LayerEdit> edit,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var targetPath = target == ConfigLayerType.User ? _userConfigPath : _workspaceConfigPath;
            WriteOutcome outcome = null!;
            AtomicConfigDocument.WithLock(targetPath, () => outcome = CommitLocked(target, targetPath, edit));

            foreach (var (field, value) in outcome.Changed)
                field.Set(_monitor.Current, value);
            var changedKeyPaths = outcome.Changed.Select(c => c.Field.KeyPath).ToArray();
            try
            {
                await DispatchSubsystemsAsync(outcome.Changed.Select(c => c.Field), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (changedKeyPaths.Length > 0)
                    _monitor.NotifyChanged(source, changedKeyPaths);
            }

            return new ConfigWriteResult(
                outcome.Overridden == null ? ConfigWriteStatus.Ok : ConfigWriteStatus.OkOverridden,
                outcome.Version,
                targetPath,
                outcome.Overridden);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private WriteOutcome CommitLocked(ConfigLayerType target, string targetPath, Func<JsonObject, LayerEdit> edit)
    {
        var current = AtomicConfigDocument.Read(targetPath);
        var other = ReadLayer(target == ConfigLayerType.User ? _workspaceConfigPath : _userConfigPath);
        var (edited, editedKeyPaths) = edit(current);

        var (previousUser, previousWorkspace) = target == ConfigLayerType.User ? (current, other) : (other, current);
        var (user, workspace) = target == ConfigLayerType.User ? (edited, other) : (other, edited);
        var previous = TryBuild(previousUser, previousWorkspace) ?? _monitor.Current;
        var next = Build(user, workspace);
        var changed = Diff(previous, next);
        Validate(next, changed, editedKeyPaths ?? changed.Select(c => c.Field.KeyPath).ToArray());

        var version = ConfigJsonPath.Version(current);
        if (!JsonNode.DeepEquals(current, edited))
        {
            AtomicConfigDocument.Write(targetPath, edited);
            version = ConfigJsonPath.Version(edited);
        }

        var overridden = target == ConfigLayerType.User && editedKeyPaths != null
            ? FindOverride(editedKeyPaths, user, workspace)
            : null;
        return new WriteOutcome(changed, version, overridden);
    }

    private static void RestoreMasked(JsonNode? node, JsonNode? current)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToArray())
                {
                    var existing = current is JsonObject currentObject ? AtomicConfigDocument.Value(currentObject, key) : null;
                    if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text) && text == "***")
                    {
                        if (existing is null)
                            obj.Remove(key);
                        else
                            obj[key] = existing.DeepClone();
                    }
                    else
                    {
                        RestoreMasked(value, existing);
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    RestoreMasked(array[i], current is JsonArray currentArray && i < currentArray.Count ? currentArray[i] : null);
                break;
        }
    }

    private string[] ResolveEditPath(ConfigEdit edit)
    {
        var path = ConfigJsonPath.Split(edit.KeyPath);
        if (_fieldsByKeyPath.TryGetValue(edit.KeyPath, out var field))
        {
            if (field.Sensitive)
            {
                throw new ConfigWriteException(
                    ConfigWriteErrorCode.ConfigValidationError,
                    $"'{edit.KeyPath}' is sensitive and cannot be written as a configuration value.");
            }

            return path;
        }

        if (path.All(segment => segment.Length > 0)
            && _objectFields.Any(f => edit.KeyPath.StartsWith(f.KeyPath + ".", StringComparison.Ordinal)))
        {
            return path;
        }

        throw new ConfigWriteException(
            ConfigWriteErrorCode.ConfigSchemaUnknownKey,
            $"'{edit.KeyPath}' is not a configuration key.");
    }

    private static void ApplyEdit(JsonObject layer, string[] path, ConfigEdit edit)
    {
        try
        {
            if (edit.Value is null)
                ConfigJsonPath.Remove(layer, path);
            else if (edit.MergeStrategy == ConfigMergeStrategy.Upsert)
                ConfigJsonPath.Upsert(layer, path, edit.Value.DeepClone());
            else
                ConfigJsonPath.Set(layer, path, edit.Value.DeepClone());
        }
        catch (InvalidDataException)
        {
            throw new ConfigWriteException(
                ConfigWriteErrorCode.ConfigValidationError,
                $"'{edit.KeyPath}' cannot be written because a parent value is not an object.");
        }
    }

    private List<(ConfigFieldDescriptor Field, object? Value)> Diff(AppConfig previous, AppConfig next)
    {
        var changed = new List<(ConfigFieldDescriptor, object?)>();
        foreach (var field in _fields)
        {
            var before = TrySerialize(field, previous);
            object? value;
            JsonNode? after;
            try
            {
                value = field.Get(next);
                after = Serialize(field, value);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                if (before.Failed)
                    continue;
                throw ValidationError($"'{field.KeyPath}' is invalid: {ex.Message}");
            }

            if (before.Failed || !JsonNode.DeepEquals(before.Node, after))
                changed.Add((field, value));
        }

        return changed;
    }

    private static (bool Failed, JsonNode? Node) TrySerialize(ConfigFieldDescriptor field, AppConfig config)
    {
        try
        {
            return (false, Serialize(field, field.Get(config)));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return (true, null);
        }
    }

    private void Validate(
        AppConfig candidate,
        IEnumerable<(ConfigFieldDescriptor Field, object? Value)> changed,
        IReadOnlyList<string> editedKeyPaths)
    {
        foreach (var (field, value) in changed)
        {
            if (CheckField(field, value) is { } error)
                throw ValidationError(error);
        }

        foreach (var (keyPath, validator) in _validators)
        {
            if (editedKeyPaths.Any(p => p == keyPath || p.StartsWith(keyPath + ".", StringComparison.Ordinal))
                && validator(candidate) is { } error)
            {
                throw ValidationError(error);
            }
        }
    }

    private static string? CheckField(ConfigFieldDescriptor field, object? value)
    {
        if (value is null)
            return null;

        if (value is int or long or short or byte or double or float or decimal)
        {
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (field.Min is { } min && number < min)
                return $"'{field.KeyPath}' must be at least {min.ToString(CultureInfo.InvariantCulture)}.";
            if (field.Max is { } max && number > max)
                return $"'{field.KeyPath}' must be at most {max.ToString(CultureInfo.InvariantCulture)}.";
        }

        if (field.Options is { Count: > 0 } options
            && value is string or Enum
            && !options.Contains(value.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            return $"'{field.KeyPath}' must be one of: {string.Join(", ", options)}.";
        }

        return null;
    }

    private ConfigOverriddenMetadata? FindOverride(
        IReadOnlyList<string> keyPaths,
        JsonObject user,
        JsonObject workspace)
    {
        foreach (var keyPath in keyPaths)
        {
            var path = ConfigJsonPath.Split(keyPath);
            if (!ConfigJsonPath.TryGet(workspace, path, out _))
                continue;
            ConfigJsonPath.TryGet(Mask(Effective(user, workspace)), path, out var effectiveValue);
            return new ConfigOverriddenMetadata(
                $"'{keyPath}' is overridden by the workspace configuration.",
                LayerInfo(ConfigLayerType.Workspace, workspace),
                effectiveValue?.DeepClone());
        }

        return null;
    }

    private async Task DispatchSubsystemsAsync(IEnumerable<ConfigFieldDescriptor> changed, CancellationToken cancellationToken)
    {
        var groups = changed
            .Where(f => f.Reload == ReloadBehavior.SubsystemRestart && f.SubsystemKey != null)
            .GroupBy(f => f.SubsystemKey!, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (_subsystemHandlers.TryGetValue(group.Key, out var handler))
                await handler(group.Select(f => f.KeyPath).ToArray(), cancellationToken).ConfigureAwait(false);
        }
    }

    private ConfigLayerType ResolveTarget(string? filePath)
    {
        if (filePath == null)
            return ConfigLayerType.Workspace;

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fullPath = Path.GetFullPath(filePath);
        if (string.Equals(fullPath, _workspaceConfigPath, comparison))
            return ConfigLayerType.Workspace;
        if (string.Equals(fullPath, _userConfigPath, comparison))
            return ConfigLayerType.User;

        throw new ConfigWriteException(
            ConfigWriteErrorCode.ConfigLayerReadonly,
            $"'{filePath}' is not a writable configuration layer.");
    }

    private JsonObject Effective(JsonObject user, JsonObject workspace)
    {
        var effective = AppConfig.MergeNodes(_defaults, AppConfig.MergeNodes(user, workspace));
        AppConfig.ExpandEnvironmentVariables(effective);
        return (JsonObject)effective;
    }

    private static AppConfig Build(JsonObject user, JsonObject workspace)
    {
        try
        {
            var merged = AppConfig.MergeNodes(user, workspace);
            AppConfig.ExpandEnvironmentVariables(merged);
            return merged.Deserialize<AppConfig>(AppConfig.SerializerOptions) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw ValidationError($"The configuration is invalid: {ex.Message}");
        }
    }

    private static AppConfig? TryBuild(JsonObject user, JsonObject workspace)
    {
        try
        {
            return Build(user, workspace);
        }
        catch (ConfigWriteException)
        {
            return null;
        }
    }

    private JsonObject Mask(JsonObject node)
    {
        var copy = (JsonObject)node.DeepClone();
        ConfigSchemaUtilities.MaskSensitiveKeys(copy, _sensitiveKeys);
        return copy;
    }

    private ConfigLayerInfo LayerInfo(ConfigLayerType type, JsonObject content) =>
        new(type, type == ConfigLayerType.User ? _userConfigPath : _workspaceConfigPath, ConfigJsonPath.Version(content));

    private static JsonObject ReadLayer(string path) =>
        File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject
              ?? throw new InvalidDataException("The configuration root must be an object.")
            : new JsonObject();

    private static JsonNode? Serialize(ConfigFieldDescriptor field, object? value) =>
        JsonSerializer.SerializeToNode(value, field.ValueType, AppConfig.SerializerOptions);

    private static bool IsObjectValued(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(TimeSpan) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid))
        {
            return false;
        }

        return typeof(IDictionary).IsAssignableFrom(type) || !typeof(IEnumerable).IsAssignableFrom(type);
    }

    private static string LayerName(ConfigLayerType type) => type == ConfigLayerType.User ? "user" : "workspace";

    private static ConfigWriteException ValidationError(string message) =>
        new(ConfigWriteErrorCode.ConfigValidationError, message);

    private sealed record LayerEdit(JsonObject Edited, IReadOnlyList<string>? EditedKeyPaths);

    private sealed record WriteOutcome(
        IReadOnlyList<(ConfigFieldDescriptor Field, object? Value)> Changed,
        string Version,
        ConfigOverriddenMetadata? Overridden);
}
