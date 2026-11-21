using System.Text.Json;

namespace DotCraft.Plugins;

public sealed record PluginChannelManifest
{
    public required string ManifestPath { get; init; }
    public required string EntryPath { get; init; }
    public required string MinHostVersion { get; init; }
    public required string ModuleId { get; init; }
    public required string ChannelName { get; init; }
    public required string DisplayName { get; init; }
    public IReadOnlyDictionary<string, string>? LocalizedDisplayName { get; init; }
    public JsonElement? Interface { get; init; }
    public required string PackageName { get; init; }
    public required string ConfigFileName { get; init; }
    public required string Variant { get; init; }
    public required bool RequiresInteractiveSetup { get; init; }
    public required bool HasChannelTools { get; init; }
    public required bool CapabilitySetMayVaryByEnvironment { get; init; }
    public required string ChannelContractVersion { get; init; }
    public required IReadOnlyList<string> SupportedTransports { get; init; }
    public required IReadOnlyList<string> SupportedChannelProtocolVersions { get; init; }
    public required IReadOnlyList<PluginChannelConfigDescriptor> ConfigDescriptors { get; init; }
    public required IReadOnlyList<JsonElement> ConfigGroups { get; init; }

    public string? GetCompatibilityError(string? hostVersion = null)
    {
        var host = hostVersion is null ? PluginHostVersion.Current.ProductText : hostVersion.Split('-', '+')[0];
        if (!Version.TryParse(host, out var observed)
            || !Version.TryParse(MinHostVersion, out var minimum)
            || observed < minimum)
            return "ChannelPluginHostVersionUnsatisfied";
        if (ChannelContractVersion != "1.0.0")
            return "ChannelPluginContractUnsupported";
        if (!SupportedChannelProtocolVersions.Contains("0.2", StringComparer.Ordinal))
            return "ChannelPluginProtocolUnsupported";
        if (!SupportedTransports.Contains("websocket", StringComparer.Ordinal))
            return "ChannelPluginTransportUnsupported";
        return null;
    }
}

public sealed record PluginChannelConfigDescriptor(
    string Key,
    bool Required,
    bool Masked,
    bool InteractiveSetupOnly,
    string DataKind,
    JsonElement? DefaultValue,
    JsonElement Metadata)
{
    public bool IsSecret => Masked || DataKind == "secret";

    public PluginSettingsField ToSettingsField()
    {
        var options = DataKind == "enum" && Property("allowCustomValue").ValueKind != JsonValueKind.True
            ? EnumOptions()
            : [];
        return new PluginSettingsField
        {
            Key = Key,
            Title = StringProperty("displayLabel"),
            Description = StringProperty("description"),
            Type = DataKind switch
            {
                "enum" when options.Count > 0 => "select",
                "boolean" => "bool",
                "number" => "number",
                "list" or "object" => "json",
                _ => "text"
            },
            Options = options,
            DefaultValue = IsSecret ? null : DefaultValue
        };
    }

    public bool AcceptsSettingValue(PluginSettingsField field, JsonElement value) => DataKind switch
    {
        "list" => value.ValueKind == JsonValueKind.Array,
        "object" => value.ValueKind == JsonValueKind.Object,
        _ => PluginSettingsValidation.IsValidValue(field, value)
    };

    private IReadOnlyList<string> EnumOptions()
    {
        var choices = Property("options");
        if (choices.ValueKind == JsonValueKind.Array)
            return choices.EnumerateArray().Select(item => item.GetProperty("value").GetString()!).ToArray();
        choices = Property("enumValues");
        return choices.ValueKind == JsonValueKind.Array
            ? choices.EnumerateArray().Select(item => item.GetString()!).Distinct(StringComparer.Ordinal).ToArray()
            : [];
    }

    private string? StringProperty(string name) =>
        Property(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private JsonElement Property(string name) =>
        Metadata.ValueKind == JsonValueKind.Object && Metadata.TryGetProperty(name, out var value) ? value : default;
}
