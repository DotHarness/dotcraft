using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DotCraft.Generators;

public sealed partial class ModuleDiscoveryGenerator
{
    private const string JsonIgnoreAttributeFqn = "System.Text.Json.Serialization.JsonIgnoreAttribute";
    private const string JsonPropertyNameAttributeFqn = "System.Text.Json.Serialization.JsonPropertyNameAttribute";

    private static void GenerateConfigDescriptors(StringBuilder sb, IReadOnlyList<INamedTypeSymbol> configTypes)
    {
        const string registryType = "global::DotCraft.Configuration.IConfigDescriptorRegistry";
        const string descriptorType = "global::DotCraft.Configuration.ConfigFieldDescriptor";
        var sectionsByKey = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var type in configTypes)
        {
            var attr = FindAttribute(type, ConfigSectionAttributeFqn);
            if (attr == null || !string.IsNullOrWhiteSpace(GetNamedString(attr, "RootKey")))
                continue;
            var key = GetSectionKey(attr);
            if (!sectionsByKey.ContainsKey(key))
                sectionsByKey[key] = type;
        }

        sectionsByKey.TryGetValue(string.Empty, out var root);
        var rootTypeName = root?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        sb.AppendLine();
        sb.AppendLine($"    public static {registryType} CreateDescriptorRegistry()");
        sb.AppendLine("        => new GeneratedConfigDescriptorRegistry();");
        sb.AppendLine();
        sb.AppendLine($"    private sealed class GeneratedConfigDescriptorRegistry : {registryType}");
        sb.AppendLine("    {");
        sb.AppendLine($"        public global::System.Collections.Generic.IReadOnlyList<{descriptorType}> Fields {{ get; }} = CreateDescriptors();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    private static {descriptorType}[] CreateDescriptors()");
        sb.AppendLine("    {");
        if (root == null)
        {
            sb.AppendLine("        return [];");
            sb.AppendLine("    }");
            return;
        }

        sb.AppendLine($"        var defaults = new {rootTypeName}();");
        sb.AppendLine("        return");
        sb.AppendLine("        [");
        var keyPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in configTypes)
        {
            var attr = FindAttribute(type, ConfigSectionAttributeFqn)!;
            if (!string.IsNullOrWhiteSpace(GetNamedString(attr, "RootKey")))
                continue;
            var sectionKey = GetSectionKey(attr);
            var accessor = ResolveSectionAccessor(root, type, BuildPath(sectionKey), sectionsByKey);
            if (accessor == null)
                continue;

            var fields = GetConfigFields(
                type,
                GetNamedBool(attr, "HasDefaultReload", false),
                GetNamedEnumInt(attr, "DefaultReload", 0),
                GetNamedString(attr, "DefaultSubsystemKey"));
            foreach (var field in fields)
            {
                var property = field.Property;
                if (IsJsonIgnored(property)
                    || property.GetMethod?.DeclaredAccessibility != Accessibility.Public
                    || property.SetMethod is not { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false })
                {
                    continue;
                }

                var keyPath = sectionKey.Length == 0 ? GetJsonName(property) : $"{sectionKey}.{GetJsonName(property)}";
                if (!keyPaths.Add(keyPath))
                    continue;

                var valueType = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var member = $"{accessor}.{property.Name}";
                sb.AppendLine($"            new {descriptorType}");
                sb.AppendLine("            {");
                sb.AppendLine($"                KeyPath = {Literal(keyPath)},");
                sb.AppendLine($"                ValueType = typeof({valueType}),");
                sb.AppendLine($"                DefaultValue = defaults{member},");
                if (field.Sensitive)
                    sb.AppendLine("                Sensitive = true,");
                if (field.Min.HasValue)
                    sb.AppendLine($"                Min = {field.Min.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
                if (field.Max.HasValue)
                    sb.AppendLine($"                Max = {field.Max.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
                if (field.Options is { Count: > 0 })
                    sb.AppendLine($"                Options = {StringArrayExpression(field.Options)},");
                if (field.Reload != 0)
                    sb.AppendLine($"                Reload = (global::DotCraft.Configuration.ReloadBehavior){field.Reload.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
                if (field.SubsystemKey != null)
                    sb.AppendLine($"                SubsystemKey = {Literal(field.SubsystemKey)},");
                sb.AppendLine($"                Get = static c => c{member},");
                sb.AppendLine($"                Set = static (c, v) => c{member} = ({valueType})v!,");
                sb.AppendLine("            },");
            }
        }

        sb.AppendLine("        ];");
        sb.AppendLine("    }");
    }

    private static string? ResolveSectionAccessor(
        INamedTypeSymbol root,
        INamedTypeSymbol section,
        string[] path,
        IReadOnlyDictionary<string, INamedTypeSymbol> sectionsByKey)
    {
        var accessor = new StringBuilder();
        ITypeSymbol current = root;
        for (var i = 0; i < path.Length; i++)
        {
            var property = FindProperty(current, path[i]);
            if (property != null)
            {
                if (IsJsonIgnored(property) || property.GetMethod?.DeclaredAccessibility != Accessibility.Public)
                    return null;
                accessor.Append('.').Append(property.Name);
                current = property.Type;
                continue;
            }

            if (i != 0
                || HasPropertyNamed(root, path[0])
                || !sectionsByKey.TryGetValue(path[0], out var moduleSection)
                || !CanCreateDefaultInstance(moduleSection))
            {
                return null;
            }

            accessor.Append($".GetSection<{moduleSection.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>({Literal(path[0])})");
            current = moduleSection;
        }

        return SymbolEqualityComparer.Default.Equals(current, section) ? accessor.ToString() : null;
    }

    private static IPropertySymbol? FindProperty(ITypeSymbol type, string jsonName)
        => type.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => !p.IsStatic && !p.IsIndexer && GetJsonName(p) == jsonName);

    private static bool HasPropertyNamed(ITypeSymbol type, string name)
        => type.GetMembers()
            .OfType<IPropertySymbol>()
            .Any(p => string.Equals(GetJsonName(p), name, StringComparison.OrdinalIgnoreCase));

    private static string GetJsonName(IPropertySymbol property)
    {
        var attr = FindAttribute(property, JsonPropertyNameAttributeFqn);
        return attr is { ConstructorArguments.Length: > 0 } && attr.ConstructorArguments[0].Value is string name
            ? name
            : property.Name;
    }

    private static bool IsJsonIgnored(IPropertySymbol property)
    {
        var attr = FindAttribute(property, JsonIgnoreAttributeFqn);
        return attr != null && GetNamedEnumInt(attr, "Condition", 1) == 1;
    }

    private static string GetSectionKey(AttributeData sectionAttr)
        => sectionAttr.ConstructorArguments.Length > 0
            ? sectionAttr.ConstructorArguments[0].Value?.ToString() ?? string.Empty
            : string.Empty;
}
