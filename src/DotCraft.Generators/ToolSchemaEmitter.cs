using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotCraft.Generators;

internal static class ToolSchemaEmitter
{
    private const string DescriptionAttributeFqn = "System.ComponentModel.DescriptionAttribute";
    private const string RequiredAttributeFqn = "System.ComponentModel.DataAnnotations.RequiredAttribute";
    private const string RangeAttributeFqn = "System.ComponentModel.DataAnnotations.RangeAttribute";
    private const string MinLengthAttributeFqn = "System.ComponentModel.DataAnnotations.MinLengthAttribute";
    private const string MaxLengthAttributeFqn = "System.ComponentModel.DataAnnotations.MaxLengthAttribute";
    private const string RegularExpressionAttributeFqn = "System.ComponentModel.DataAnnotations.RegularExpressionAttribute";
    private const string JsonPropertyNameAttributeFqn = "System.Text.Json.Serialization.JsonPropertyNameAttribute";
    private const string JsonIgnoreAttributeFqn = "System.Text.Json.Serialization.JsonIgnoreAttribute";
    private const string JsonStringEnumMemberNameAttributeFqn = "System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute";

    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string BuildFunctionSchema(ToolFunctionGenerator.ToolInfo tool)
    {
        var properties = new List<string>();
        var required = new List<string>();
        foreach (var parameter in tool.Parameters.Where(static parameter => !parameter.IsCancellationToken))
        {
            properties.Add($"{Quote(parameter.SchemaName)}:{BuildTypeSchema(
                parameter.TypeSymbol,
                parameter.HasDefaultValue,
                parameter.DefaultValue,
                parameter.Description,
                parameter.Symbol,
                emitDefault: tool.GenerateFunction,
                visiting: new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default))}");
            if (!parameter.HasDefaultValue || HasAttribute(parameter.Symbol, RequiredAttributeFqn))
                required.Add(Quote(parameter.SchemaName));
        }

        var entries = new List<string>
        {
            "\"type\":\"object\"",
            $"\"properties\":{{{string.Join(",", properties)}}}"
        };
        if (required.Count > 0)
            entries.Add($"\"required\":[{string.Join(",", required)}]");
        if (tool.DisallowAdditionalProperties)
            entries.Add("\"additionalProperties\":false");
        return "{" + string.Join(",", entries) + "}";
    }

    public static string GetEnumJsonName(IFieldSymbol field) =>
        FindAttribute(field, JsonStringEnumMemberNameAttributeFqn)?.ConstructorArguments.FirstOrDefault().Value?.ToString()
        ?? field.Name;

    private static string BuildTypeSchema(
        ITypeSymbol type,
        bool hasDefault,
        object? defaultValue,
        string? description,
        ISymbol annotatedSymbol,
        bool emitDefault,
        HashSet<ITypeSymbol> visiting)
    {
        var typeWithoutNullable = UnwrapNullable(type);
        var isNullable = !SymbolEqualityComparer.Default.Equals(typeWithoutNullable, type)
            || type.NullableAnnotation == NullableAnnotation.Annotated;
        var entries = new List<string>();
        if (!string.IsNullOrWhiteSpace(description))
            entries.Add($"\"description\":{Quote(description!)}");

        if (IsAnyJson(typeWithoutNullable))
        {
            // An unconstrained JSON value intentionally has no type keyword.
        }
        else if (typeWithoutNullable.TypeKind == TypeKind.Enum)
        {
            entries.Add("\"type\":\"string\"");
            var names = typeWithoutNullable.GetMembers().OfType<IFieldSymbol>()
                .Where(static field => field.HasConstantValue)
                .Select(field => Quote(GetEnumJsonName(field)));
            entries.Add($"\"enum\":[{string.Join(",", names)}]");
        }
        else if (TryGetCollectionElement(typeWithoutNullable, out var element))
        {
            entries.Add("\"type\":\"array\"");
            entries.Add($"\"items\":{BuildTypeSchema(
                element,
                hasDefault: false,
                defaultValue: null,
                description: null,
                annotatedSymbol,
                emitDefault: false,
                visiting: visiting)}");
        }
        else if (IsJsonArray(typeWithoutNullable))
        {
            entries.Add("\"type\":\"array\"");
        }
        else if (IsString(typeWithoutNullable))
        {
            entries.Add(isNullable ? "\"type\":[\"string\",\"null\"]" : "\"type\":\"string\"");
        }
        else if (IsBoolean(typeWithoutNullable))
        {
            entries.Add(isNullable ? "\"type\":[\"boolean\",\"null\"]" : "\"type\":\"boolean\"");
        }
        else if (IsInteger(typeWithoutNullable))
        {
            entries.Add(isNullable ? "\"type\":[\"integer\",\"null\"]" : "\"type\":\"integer\"");
        }
        else if (IsNumber(typeWithoutNullable))
        {
            entries.Add(isNullable ? "\"type\":[\"number\",\"null\"]" : "\"type\":\"number\"");
        }
        else if (IsJsonObject(typeWithoutNullable))
        {
            entries.Add("\"type\":\"object\"");
        }
        else
        {
            entries.Add("\"type\":\"object\"");
            if (visiting.Add(typeWithoutNullable))
            {
                var objectSchema = BuildObjectSchema(typeWithoutNullable, visiting);
                entries.AddRange(objectSchema);
                visiting.Remove(typeWithoutNullable);
            }
        }

        AppendConstraints(entries, typeWithoutNullable, annotatedSymbol);
        if (hasDefault && emitDefault)
            entries.Add($"\"default\":{FormatJsonDefault(defaultValue, typeWithoutNullable)}");
        return "{" + string.Join(",", entries) + "}";
    }

    private static IEnumerable<string> BuildObjectSchema(ITypeSymbol type, HashSet<ITypeSymbol> visiting)
    {
        var properties = new List<string>();
        var required = new List<string>();
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.GetMethod == null || property.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (ShouldIgnore(property))
                continue;

            var name = FindAttribute(property, JsonPropertyNameAttributeFqn)?.ConstructorArguments.FirstOrDefault().Value?.ToString()
                ?? ToCamelCase(property.Name);
            var description = GetDescription(property);
            properties.Add($"{Quote(name)}:{BuildTypeSchema(
                property.Type,
                hasDefault: false,
                defaultValue: null,
                description,
                property,
                emitDefault: false,
                visiting: visiting)}");
            if (property.IsRequired || HasAttribute(property, RequiredAttributeFqn))
                required.Add(Quote(name));
        }

        if (properties.Count > 0)
            yield return $"\"properties\":{{{string.Join(",", properties)}}}";
        if (required.Count > 0)
            yield return $"\"required\":[{string.Join(",", required)}]";
    }

    private static void AppendConstraints(List<string> entries, ITypeSymbol type, ISymbol symbol)
    {
        if (FindAttribute(symbol, RangeAttributeFqn) is { } range && TryReadRange(range, out var minimum, out var maximum))
        {
            if (!IsClrMinimum(type, minimum))
                entries.Add($"\"minimum\":{minimum}");
            if (!IsClrMaximum(type, maximum))
                entries.Add($"\"maximum\":{maximum}");
        }

        if (FindAttribute(symbol, MinLengthAttributeFqn) is { } minLength
            && minLength.ConstructorArguments.FirstOrDefault().Value is int min)
        {
            entries.Add(IsString(type) ? $"\"minLength\":{min}" : $"\"minItems\":{min}");
        }

        if (FindAttribute(symbol, MaxLengthAttributeFqn) is { } maxLength
            && maxLength.ConstructorArguments.FirstOrDefault().Value is int max)
        {
            entries.Add(IsString(type) ? $"\"maxLength\":{max}" : $"\"maxItems\":{max}");
        }

        if (FindAttribute(symbol, RegularExpressionAttributeFqn) is { } regex
            && regex.ConstructorArguments.FirstOrDefault().Value?.ToString() is { } pattern)
        {
            entries.Add($"\"pattern\":{Quote(pattern)}");
        }
    }

    internal static bool TryReadRange(AttributeData attribute, out string minimum, out string maximum)
    {
        minimum = maximum = string.Empty;
        if (attribute.ConstructorArguments.Length == 2)
        {
            minimum = FormatNumber(attribute.ConstructorArguments[0].Value);
            maximum = FormatNumber(attribute.ConstructorArguments[1].Value);
            return minimum.Length > 0 && maximum.Length > 0;
        }

        if (attribute.ConstructorArguments.Length == 3)
        {
            minimum = FormatNumber(attribute.ConstructorArguments[1].Value);
            maximum = FormatNumber(attribute.ConstructorArguments[2].Value);
            return minimum.Length > 0 && maximum.Length > 0;
        }

        return false;
    }

    private static string FormatNumber(object? value)
    {
        if (value == null)
            return string.Empty;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? text
            : string.Empty;
    }

    private static bool IsClrMinimum(ITypeSymbol type, string value) => type.SpecialType switch
    {
        SpecialType.System_Int16 => value == short.MinValue.ToString(CultureInfo.InvariantCulture),
        SpecialType.System_Int32 => value == int.MinValue.ToString(CultureInfo.InvariantCulture),
        SpecialType.System_Int64 => value == long.MinValue.ToString(CultureInfo.InvariantCulture),
        _ => false
    };

    private static bool IsClrMaximum(ITypeSymbol type, string value) => type.SpecialType switch
    {
        SpecialType.System_Int16 => value == short.MaxValue.ToString(CultureInfo.InvariantCulture),
        SpecialType.System_Int32 => value == int.MaxValue.ToString(CultureInfo.InvariantCulture),
        SpecialType.System_Int64 => value == long.MaxValue.ToString(CultureInfo.InvariantCulture),
        _ => false
    };

    private static string FormatJsonDefault(object? value, ITypeSymbol type)
    {
        if (value == null)
            return "null";
        if (type.TypeKind == TypeKind.Enum)
        {
            var field = type.GetMembers().OfType<IFieldSymbol>()
                .FirstOrDefault(candidate => candidate.HasConstantValue && Equals(candidate.ConstantValue, value));
            return Quote(field == null ? value.ToString() ?? string.Empty : GetEnumJsonName(field));
        }

        return value switch
        {
            string text => Quote(text),
            bool flag => flag ? "true" : "false",
            char character => Quote(character.ToString()),
            float number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null"
        };
    }

    private static bool ShouldIgnore(IPropertySymbol property)
    {
        var attribute = FindAttribute(property, JsonIgnoreAttributeFqn);
        if (attribute == null)
            return false;
        var condition = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == "Condition").Value.Value;
        return condition is not int value || value != 0;
    }

    private static bool IsAnyJson(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_Object
        || IsNamed(type, "System.Text.Json", "JsonElement")
        || IsNamed(type, "System.Text.Json.Nodes", "JsonNode")
        || IsNamed(type, "System.Text.Json.Nodes", "JsonValue");

    internal static bool IsJsonObject(ITypeSymbol type) => IsNamed(type, "System.Text.Json.Nodes", "JsonObject");

    private static bool IsJsonArray(ITypeSymbol type) => IsNamed(type, "System.Text.Json.Nodes", "JsonArray");

    private static bool IsNamed(ITypeSymbol type, string ns, string name) =>
        type is INamedTypeSymbol named
        && named.Name == name
        && named.ContainingNamespace.ToDisplayString() == ns;

    internal static bool TryGetCollectionElement(ITypeSymbol type, out ITypeSymbol element)
    {
        if (type is IArrayTypeSymbol array)
        {
            element = array.ElementType;
            return true;
        }

        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            var definition = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (definition is
                "global::System.Collections.Generic.List<T>" or
                "global::System.Collections.Generic.IReadOnlyList<T>" or
                "global::System.Collections.Generic.IEnumerable<T>")
            {
                element = named.TypeArguments[0];
                return true;
            }
        }

        element = type;
        return false;
    }

    internal static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named
            && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T
            && named.TypeArguments.Length == 1)
        {
            return named.TypeArguments[0];
        }
        return type;
    }

    internal static bool IsSupportedJsonType(ITypeSymbol type)
    {
        var unwrapped = UnwrapNullable(type);
        return IsAnyJson(unwrapped) || IsJsonObject(unwrapped) || IsJsonArray(unwrapped);
    }

    internal static bool IsString(ITypeSymbol type) => type.SpecialType == SpecialType.System_String;

    internal static bool IsBoolean(ITypeSymbol type) => type.SpecialType == SpecialType.System_Boolean;

    internal static bool IsInteger(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64;

    internal static bool IsNumber(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;

    private static string GetDescription(ISymbol symbol) =>
        FindAttribute(symbol, DescriptionAttributeFqn)?.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? string.Empty;

    internal static AttributeData? FindAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == metadataName);

    private static bool HasAttribute(ISymbol symbol, string metadataName) => FindAttribute(symbol, metadataName) != null;

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) || char.IsLower(value[0])
            ? value
            : char.ToLowerInvariant(value[0]) + value.Substring(1);

    private static string Quote(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}
