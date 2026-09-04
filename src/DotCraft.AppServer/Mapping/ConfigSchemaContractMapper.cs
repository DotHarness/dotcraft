using System.Text.Json;
using DotCraft.Configuration;
using Contract = DotCraft.Protocol.AppServer;
using ConfigSchemaField = DotCraft.Configuration.ConfigSchemaField;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;

namespace DotCraft.AppServer;

/// <summary>Maps generated configuration schema metadata to the stable AppServer contract at the protocol boundary.</summary>
public static class ConfigSchemaContractMapper
{
    /// <summary>Maps one configuration section descriptor to its wire contract.</summary>
    public static Contract.ConfigSchemaSection ToContract(ConfigSchemaSection value) => new()
    {
        Section = value.Section,
        Order = value.Order,
        Path = OmitIfNull<IReadOnlyList<string>>(value.Path),
        RootKey = OmitIfNull(value.RootKey),
        ItemFields = value.ItemFields is null
            ? default
            : new Protocol.Optional<IReadOnlyList<Contract.ConfigSchemaField>?>(
                value.ItemFields.Select(ToContract).ToArray()),
        Fields = value.Fields.Select(ToContract).ToArray()
    };

    /// <summary>Maps one configuration field descriptor to its wire contract.</summary>
    public static Contract.ConfigSchemaField ToContract(ConfigSchemaField value) => new()
    {
        Key = value.Key,
        DisplayName = OmitIfNull(value.DisplayName),
        Type = value.Type,
        Sensitive = value.Sensitive,
        Options = OmitIfNull<IReadOnlyList<string>>(value.Options),
        Min = OmitIfNull(value.Min),
        Max = OmitIfNull(value.Max),
        Hint = OmitIfNull(value.Hint),
        Reload = JsonNamingPolicy.CamelCase.ConvertName(value.Reload.ToString()),
        SubsystemKey = OmitIfNull(value.SubsystemKey),
        DefaultValue = ToOptionalJson(value.DefaultValue)
    };

    private static Protocol.Optional<JsonElement?> ToOptionalJson(object? value)
    {
        if (value is null)
            return default;
        if (value is JsonElement element)
            return element.ValueKind == JsonValueKind.Undefined
                ? default
                : Protocol.Optional<JsonElement?>.FromValue(element.Clone());

        return Protocol.Optional<JsonElement?>.FromValue(
            JsonSerializer.SerializeToElement(value, AppConfig.SerializerOptions));
    }

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : new Protocol.Optional<T?>(value);
}
