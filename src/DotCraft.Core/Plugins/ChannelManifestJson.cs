using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotCraft.Plugins;

internal static partial class ChannelManifestJson
{
    private static readonly HashSet<string> ReservedKeySegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "__proto__", "prototype", "constructor",
    };

    public static IReadOnlyList<PluginChannelConfigDescriptor> ReadDescriptors(
        JsonElement manifest, IEnumerable<string> groupNames)
    {
        var knownGroups = groupNames.ToHashSet(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var descriptors = new List<PluginChannelConfigDescriptor>();

        foreach (var item in Elements(manifest, "configDescriptors"))
        {
            var key = String(item, "key");
            if (!key.Split('.').All(part => KeySegmentRegex().IsMatch(part) && !ReservedKeySegments.Contains(part))
                || !keys.Add(key))
                throw new FormatException("Channel configuration descriptor keys must be unique valid dotted keys.");

            var group = OptionalString(item, "group");
            if (group is not null && !knownGroups.Contains(group))
                throw new FormatException("Channel configuration descriptor refers to an unknown group.");

            var dataKind = String(item, "dataKind");
            if (dataKind is not ("string" or "secret" or "path" or "enum" or "boolean" or "number" or "object" or "list"))
                throw new FormatException("Channel configuration descriptor dataKind is not supported.");

            var required = Boolean(item, "required");
            var masked = Boolean(item, "masked");
            var interactiveSetupOnly = Boolean(item, "interactiveSetupOnly");

            if (key.Equals("dotcraft", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("dotcraft.", StringComparison.OrdinalIgnoreCase))
                continue;

            var options = ReadOptions(item);
            var optionGroups = ReadOptionGroups(item, options.Keys);
            var allowCustomValue = OptionalBoolean(item, "allowCustomValue");
            if (allowCustomValue == true && dataKind != "enum")
                throw new FormatException("Only enum channel descriptors can allow custom values.");

            var defaultValue = Value(item, "defaultValue");
            var publicDefault = defaultValue.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                && !masked && dataKind != "secret"
                ? (JsonElement?)defaultValue.Clone()
                : null;

            var metadata = Display(item);
            metadata.TryAdd("description", string.Empty);
            metadata.Add("key", key);
            metadata.Add("dataKind", dataKind);
            metadata.Add("required", required);
            metadata.Add("masked", masked);
            metadata.Add("interactiveSetupOnly", interactiveSetupOnly);
            Add(metadata, "group", group);
            Add(metadata, "advanced", OptionalBoolean(item, "advanced"));
            Add(metadata, "allowCustomValue", allowCustomValue);
            Add(metadata, "docsPath", StringMap(item, "docsPath"));
            Add(metadata, "enumValues", OptionalStrings(item, "enumValues"));
            Add(metadata, "options", Present(item, "options") ? options.Values : null);
            Add(metadata, "optionGroups", Present(item, "optionGroups") ? optionGroups.Values : null);
            Add(metadata, "defaultValue", publicDefault);

            var descriptor = new PluginChannelConfigDescriptor(
                key, required, masked, interactiveSetupOnly, dataKind, publicDefault, JsonSerializer.SerializeToElement(metadata));
            if (publicDefault is { } settingDefault && !descriptor.AcceptsSettingValue(descriptor.ToSettingsField(), settingDefault))
                throw new FormatException("A channel setting default does not match its declared type or options.");
            descriptors.Add(descriptor);
        }

        foreach (var key in keys)
        {
            for (var separator = key.LastIndexOf('.'); separator >= 0; separator = key.LastIndexOf('.', separator - 1))
                if (keys.Contains(key[..separator]))
                    throw new FormatException("Channel configuration descriptors cannot contain overlapping keys.");
        }

        return descriptors;
    }

    public static Dictionary<string, JsonElement> ReadGroups(JsonElement manifest) =>
        ReadNamedItems(manifest, "configGroups", "id", Display);

    private static Dictionary<string, JsonElement> ReadOptions(JsonElement descriptor) =>
        ReadNamedItems(descriptor, "options", "value", option =>
        {
            var fields = Display(option);
            Add(fields, "preview", OptionalString(option, "preview"));
            return fields;
        });

    private static Dictionary<string, JsonElement> ReadOptionGroups(JsonElement descriptor, IEnumerable<string> optionKeys)
    {
        var knownOptions = optionKeys.ToHashSet(StringComparer.Ordinal);
        if (Present(descriptor, "optionGroups") && !Present(descriptor, "options"))
            throw new FormatException("Channel configuration option groups require options.");

        return ReadNamedItems(descriptor, "optionGroups", "id", group =>
        {
            var fields = Display(group);
            var values = Strings(group, "values");
            if (values.Any(value => !knownOptions.Contains(value)))
                throw new FormatException("Channel configuration option group refers to an unknown option.");
            fields.Add("values", values);
            return fields;
        });
    }

    private static Dictionary<string, JsonElement> ReadNamedItems(
        JsonElement parent, string field, string idField, Func<JsonElement, Dictionary<string, object>> project)
    {
        var named = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in Elements(parent, field))
        {
            var id = String(item, idField);
            var fields = project(item);
            fields.Add(idField, id);
            if (!named.TryAdd(id, JsonSerializer.SerializeToElement(fields)))
                throw new FormatException($"Channel {field} must have unique {idField}s.");
        }
        return named;
    }

    public static JsonElement? ReadInterface(JsonElement manifest)
    {
        if (!Present(manifest, "interface"))
            return null;

        var source = Object(manifest, "interface");
        var fields = new Dictionary<string, object>();
        foreach (var field in new[] { "shortDescription", "longDescription", "previewPrompt" })
        {
            Add(fields, field, OptionalString(source, field));
            var localizedField = "localized" + char.ToUpperInvariant(field[0]) + field[1..];
            Add(fields, localizedField, StringMap(source, localizedField));
        }
        Add(fields, "setupSteps", OptionalStrings(source, "setupSteps"));
        if (Present(source, "localizedSetupSteps"))
        {
            var steps = Object(source, "localizedSetupSteps").EnumerateObject().ToDictionary(
                property => property.Name,
                property => StringsFromValue(property.Value, "localizedSetupSteps"),
                StringComparer.Ordinal);
            fields.Add("localizedSetupSteps", steps);
        }

        return JsonSerializer.SerializeToElement(fields);
    }

    private static Dictionary<string, object> Display(JsonElement item)
    {
        var result = new Dictionary<string, object> { ["displayLabel"] = String(item, "displayLabel") };
        Add(result, "description", OptionalString(item, "description"));
        Add(result, "localizedDisplayLabel", StringMap(item, "localizedDisplayLabel"));
        Add(result, "localizedDescription", StringMap(item, "localizedDescription"));
        return result;
    }

    public static IReadOnlyDictionary<string, string>? StringMap(JsonElement parent, string field)
    {
        if (!Present(parent, field))
            return null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in Object(parent, field).EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String || !result.TryAdd(property.Name, property.Value.GetString()!))
                throw InvalidValue(field);
        }
        return result;
    }

    public static JsonElement Object(JsonElement parent, string field)
    {
        var value = Value(parent, field);
        return value.ValueKind == JsonValueKind.Object ? value : throw InvalidValue(field);
    }

    public static string String(JsonElement parent, string field)
    {
        var text = OptionalString(parent, field);
        return !string.IsNullOrWhiteSpace(text) && text == text.Trim() ? text : throw InvalidValue(field);
    }

    public static bool Boolean(JsonElement parent, string field) =>
        OptionalBoolean(parent, field) ?? throw InvalidValue(field);

    public static IReadOnlyList<string> Strings(JsonElement parent, string field) =>
        StringsFromValue(Value(parent, field), field);

    private static string? OptionalString(JsonElement parent, string field)
    {
        var value = Value(parent, field);
        return value.ValueKind switch
        {
            JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            _ => throw InvalidValue(field),
        };
    }

    private static bool? OptionalBoolean(JsonElement parent, string field) =>
        Value(parent, field).ValueKind switch
        {
            JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw InvalidValue(field),
        };

    private static IReadOnlyList<string>? OptionalStrings(JsonElement parent, string field) =>
        Present(parent, field) ? Strings(parent, field) : null;

    private static IReadOnlyList<string> StringsFromValue(JsonElement value, string field) =>
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : throw InvalidValue(field)).ToArray()
            : throw InvalidValue(field);

    private static IEnumerable<JsonElement> Elements(JsonElement parent, string field) =>
        Value(parent, field) is var value && value.ValueKind == JsonValueKind.Undefined
            ? []
            : value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : throw InvalidValue(field);

    private static bool Present(JsonElement parent, string field) => Value(parent, field).ValueKind != JsonValueKind.Undefined;

    private static JsonElement Value(JsonElement parent, string field) => parent.ValueKind == JsonValueKind.Object
        ? parent.TryGetProperty(field, out var value) ? value : default
        : throw InvalidValue(field);

    private static void Add(Dictionary<string, object> fields, string name, object? value)
    {
        if (value is not null)
            fields.Add(name, value);
    }

    private static FormatException InvalidValue(string field) => new($"Channel '{field}' has an invalid or missing value.");

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_-]{0,127}\z")]
    private static partial Regex KeySegmentRegex();
}
