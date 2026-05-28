using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace DotCraft.Automations.Local;

/// <summary>
/// Serializes <see cref="DateTimeOffset"/> as a single ISO-8601 scalar. YamlDotNet's default
/// serializer expands <see cref="DateTimeOffset"/> into a large mapping, which is noisy in task.md.
/// </summary>
public sealed class DateTimeOffsetIsoYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) =>
        type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var nullable = type == typeof(DateTimeOffset?);

        if (parser.Current is Scalar scalar)
        {
            var s = scalar.Value;
            parser.MoveNext();
            if (nullable && IsYamlNullScalar(s))
                return null;
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                return dto;
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dto))
                return dto;
            throw new FormatException($"Invalid DateTimeOffset string in task.md: {s}");
        }

        // Backward compatibility: older saves used YamlDotNet's default mapping for DateTimeOffset.
        if (parser.Current is MappingStart)
        {
            parser.MoveNext();
            DateTimeOffset? best = null;
            while (parser.Current is not MappingEnd)
            {
                if (parser.Current is not Scalar keyScalar)
                    throw new InvalidOperationException("Expected scalar mapping key for DateTimeOffset.");
                var key = keyScalar.Value;
                parser.MoveNext();
                if (parser.Current is not Scalar valueScalar)
                    throw new InvalidOperationException("Expected scalar mapping value for DateTimeOffset.");
                var val = valueScalar.Value;
                parser.MoveNext();
                if (key is "utc_date_time" or "date_time" or "local_date_time")
                {
                    if (DateTimeOffset.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                        best = parsed;
                }
            }

            parser.MoveNext(); // MappingEnd
            if (nullable)
                return best;
            return best ?? default;
        }

        throw new InvalidOperationException(
            $"Unexpected YAML node for DateTimeOffset (expected scalar or mapping): {parser.Current?.GetType().Name}");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(value);
        var dto = (DateTimeOffset)value;
        var text = dto.ToString("o", CultureInfo.InvariantCulture);
        // YamlDotNet requires at least one implicit flag when Tag is empty; Scalar(string) sets both.
        emitter.Emit(new Scalar(text));
    }

    private static bool IsYamlNullScalar(string s) =>
        string.IsNullOrEmpty(s)
        || s.Equals("null", StringComparison.OrdinalIgnoreCase)
        || s.Equals("~", StringComparison.Ordinal);
}
