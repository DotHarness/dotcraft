using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.Contracts;

/// <summary>
/// Represents a JSON property that may be missing independently from the value it carries.
/// </summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly struct Optional<T> : IEquatable<Optional<T>>
{
    private readonly T? _value;

    /// <summary>Creates a present optional value, including an explicit <see langword="null"/>.</summary>
    public Optional(T? value)
    {
        IsSet = true;
        _value = value;
    }

    /// <summary>Whether the property was present.</summary>
    public bool IsSet { get; }

    /// <summary>The present value.</summary>
    /// <exception cref="InvalidOperationException">The value is missing.</exception>
    public T? Value => IsSet
        ? _value
        : throw new InvalidOperationException("The optional value is not set.");

    /// <summary>Creates a present optional value.</summary>
    public static Optional<T> FromValue(T? value) => new(value);

    /// <summary>Converts a value to a present optional.</summary>
    public static implicit operator Optional<T>(T? value) => new(value);

    /// <inheritdoc />
    public bool Equals(Optional<T> other) =>
        IsSet == other.IsSet && (!IsSet || EqualityComparer<T?>.Default.Equals(_value, other._value));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => IsSet ? HashCode.Combine(true, _value) : 0;
}

/// <summary>System.Text.Json converter factory for <see cref="Optional{T}"/>.</summary>
public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(typeof(OptionalJsonConverter<>).MakeGenericType(valueType))!;
    }

    private sealed class OptionalJsonConverter<TValue> : JsonConverter<Optional<TValue>>
    {
        public override Optional<TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<TValue>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Optional<TValue> value, JsonSerializerOptions options)
        {
            if (!value.IsSet)
                throw new JsonException("An unset Optional value must be omitted by the containing property.");
            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
