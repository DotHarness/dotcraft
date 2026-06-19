using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DotCraft.Gen;

/// <summary>
/// Source generator that discovers DotCraft modules marked with [DotCraftModule] attribute
/// and config section types marked with [ConfigSection] attribute, and generates:
///   1. Per-module partial class overrides for Name and Priority (in every assembly).
///   2. A static ModuleRegistrations class with explicit new() calls (only in the app
///      assembly, gated by the DotCraftGenerateModuleRegistrations MSBuild property).
///   3. A static ConfigSchemaRegistrations class that returns generated config schema
///      metadata (only in the app assembly, same gate as #2).
/// </summary>
[Generator]
public sealed class ModuleDiscoveryGenerator : IIncrementalGenerator
{
    private const string ModuleAttributeFqn = "DotCraft.Modules.DotCraftModuleAttribute";
    private const string HostFactoryAttributeFqn = "DotCraft.Modules.HostFactoryAttribute";
    private const string ConfigSectionAttributeFqn = "DotCraft.Configuration.ConfigSectionAttribute";
    private const string ConfigFieldAttributeFqn = "DotCraft.Configuration.ConfigFieldAttribute";
    private const string JsonExtensionDataAttributeFqn = "System.Text.Json.Serialization.JsonExtensionDataAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // --- Phase A: local module/factory discovery (runs in every assembly) ---

        var localModules = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ModuleAttributeFqn,
                predicate: static (node, _) => IsClassWithAttributes(node),
                transform: static (ctx, _) => GetModuleClassInfo(ctx))
            .Where(static m => m is not null)
            .Collect();

        var localFactories = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HostFactoryAttributeFqn,
                predicate: static (node, _) => IsClassWithAttributes(node),
                transform: static (ctx, _) => GetHostFactoryClassInfo(ctx))
            .Where(static f => f is not null)
            .Collect();

        // Always generate partial class overrides for Name / Priority
        context.RegisterSourceOutput(localModules, static (ctx, modules) =>
        {
            GenerateModuleProperties(ctx, modules);
        });

        // --- Phase B: explicit registration class (app assembly only) ---

        var shouldGenerateRegistrations = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
            {
                options.GlobalOptions.TryGetValue(
                    "build_property.DotCraftGenerateModuleRegistrations", out var value);
                return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            });

        // Track [ConfigSection] types incrementally so that adding a new config section
        // class in any DotCraft assembly (e.g. DotCraft.Core) correctly invalidates the
        // Phase B output. Without this, config section discovery relies solely on
        // CompilationProvider, which may not re-trigger in IDE / hot-reload scenarios.
        var localConfigSections = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ConfigSectionAttributeFqn,
                predicate: static (node, _) => IsClassWithAttributes(node),
                transform: static (ctx, _) => ctx.TargetSymbol.ToDisplayString())
            .Collect();

        var registrationInput = localModules
            .Combine(localFactories)
            .Combine(localConfigSections)
            .Combine(context.CompilationProvider)
            .Combine(shouldGenerateRegistrations);

        context.RegisterSourceOutput(registrationInput, static (ctx, data) =>
        {
            var shouldGenerate = data.Right;
            if (!shouldGenerate)
                return;

            var (((localMods, localFacts), _), compilation) = data.Left;
            GenerateRegistrationClass(ctx, localMods, localFacts, compilation);
            GenerateConfigSchemaRegistrations(ctx, compilation);
        });
    }

    #region Syntax predicates & transforms

    private static bool IsClassWithAttributes(SyntaxNode node)
    {
        return node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax classDecl
            && classDecl.AttributeLists.Count > 0;
    }

    private static ModuleInfo? GetModuleClassInfo(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        AttributeData? moduleAttr = null;
        foreach (var attr in context.Attributes)
        {
            if (attr.AttributeClass?.ToDisplayString() == ModuleAttributeFqn)
            {
                moduleAttr = attr;
                break;
            }
        }

        if (moduleAttr == null)
            return null;

        return ExtractModuleInfo(symbol, moduleAttr);
    }

    private static HostFactoryInfo? GetHostFactoryClassInfo(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        AttributeData? factoryAttr = null;
        foreach (var attr in context.Attributes)
        {
            if (attr.AttributeClass?.ToDisplayString() == HostFactoryAttributeFqn)
            {
                factoryAttr = attr;
                break;
            }
        }

        if (factoryAttr == null)
            return null;

        return ExtractHostFactoryInfo(symbol, factoryAttr);
    }

    #endregion

    #region Attribute data extraction helpers

    private static ModuleInfo ExtractModuleInfo(INamedTypeSymbol symbol, AttributeData attr)
    {
        string moduleName = attr.ConstructorArguments.Length > 0
            ? attr.ConstructorArguments[0].Value?.ToString() ?? "unknown"
            : "unknown";

        int priority = 0;
        string? description = null;
        bool canBePrimaryHost = false;

        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg.Key == "Priority")
                priority = namedArg.Value.Value as int? ?? 0;
            else if (namedArg.Key == "Description")
                description = namedArg.Value.Value?.ToString();
            else if (namedArg.Key == "CanBePrimaryHost")
                canBePrimaryHost = namedArg.Value.Value as bool? ?? false;
        }

        return new ModuleInfo(
            ClassName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClassNamespace: symbol.ContainingNamespace.ToDisplayString(),
            ClassNameOnly: symbol.Name,
            ModuleName: moduleName,
            Priority: priority,
            Description: description,
            CanBePrimaryHost: canBePrimaryHost);
    }

    private static HostFactoryInfo ExtractHostFactoryInfo(INamedTypeSymbol symbol, AttributeData attr)
    {
        string moduleName = attr.ConstructorArguments.Length > 0
            ? attr.ConstructorArguments[0].Value?.ToString() ?? "unknown"
            : "unknown";

        return new HostFactoryInfo(
            ClassName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ModuleName: moduleName);
    }

    #endregion

    #region Phase A: partial class generation

    private static void GenerateModuleProperties(
        SourceProductionContext context,
        ImmutableArray<ModuleInfo?> modules)
    {
        foreach (var module in modules.Where(m => m != null).Select(m => m!))
        {
            var sb = new StringBuilder();
            sb.AppendLine($$"""
// <auto-generated>
//     This code was generated by DotCraft.Gen.
//     Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
// </auto-generated>
#nullable enable

namespace {{module.ClassNamespace}};

partial class {{module.ClassNameOnly}}
{
    /// <summary>
    /// Gets the unique name of the module.
    /// </summary>
    public override string Name => "{{module.ModuleName}}";

    /// <summary>
    /// Gets the priority of the module for host selection.
    /// </summary>
    public override int Priority => {{module.Priority}};

    /// <summary>
    /// Gets whether this module can be selected as the primary startup host.
    /// </summary>
    public override bool CanBePrimaryHost => {{module.CanBePrimaryHost.ToString().ToLowerInvariant()}};
}
""");

            var fileName = $"{module.ClassNameOnly}.ModuleProperties.g.cs";
            context.AddSource(fileName, SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }

    #endregion

    #region Phase B: registration class generation

    private static void GenerateRegistrationClass(
        SourceProductionContext context,
        ImmutableArray<ModuleInfo?> localModules,
        ImmutableArray<HostFactoryInfo?> localFactories,
        Compilation compilation)
    {
        var allModules = new List<ModuleInfo>();
        var allFactories = new List<HostFactoryInfo>();

        foreach (var m in localModules)
            if (m != null) allModules.Add(m);
        foreach (var f in localFactories)
            if (f != null) allFactories.Add(f);

        // Discover modules/factories from referenced DotCraft assemblies
        foreach (var assemblySymbol in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!assemblySymbol.Name.StartsWith("DotCraft", StringComparison.Ordinal))
                continue;
            ScanNamespaceForModules(assemblySymbol.GlobalNamespace, allModules, allFactories);
        }

        if (allModules.Count == 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("//     This code was generated by DotCraft.Gen.");
        sb.AppendLine("//     Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace DotCraft.Modules;");
        sb.AppendLine();
        sb.AppendLine("internal static class ModuleRegistrations");
        sb.AppendLine("{");
        sb.AppendLine("    public static void RegisterAll(ModuleRegistry registry)");
        sb.AppendLine("    {");

        foreach (var module in allModules.OrderBy(m => m.ModuleName))
        {
            var factory = allFactories.FirstOrDefault(f => f.ModuleName == module.ModuleName);
            if (factory != null)
            {
                sb.AppendLine($"        registry.RegisterModule(new {module.ClassName}(), new {factory.ClassName}());");
            }
            else
            {
                sb.AppendLine($"        registry.RegisterModule(new {module.ClassName}());");
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("ModuleRegistrations.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// Generates ConfigSchemaRegistrations.GetConfigSchema() and a provider factory by
    /// scanning all referenced DotCraft assemblies for types annotated with [ConfigSection].
    /// </summary>
    private static void GenerateConfigSchemaRegistrations(
        SourceProductionContext context,
        Compilation compilation)
    {
        var configTypes = new List<INamedTypeSymbol>();

        // Scan current compilation (DotCraft.App itself and referenced config types).
        ScanNamespaceForConfigSections(compilation.GlobalNamespace, configTypes);

        // Scan referenced DotCraft assemblies
        foreach (var assemblySymbol in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!assemblySymbol.Name.StartsWith("DotCraft", StringComparison.Ordinal))
                continue;
            ScanNamespaceForConfigSections(assemblySymbol.GlobalNamespace, configTypes);
        }

        var distinctConfigTypes = configTypes
            .GroupBy(static type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .ToList();
        var schemaTypes = distinctConfigTypes
            .OrderBy(GetConfigSectionOrder)
            .ToList();

        if (distinctConfigTypes.Count == 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("//     This code was generated by DotCraft.Gen.");
        sb.AppendLine("//     Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace DotCraft.Configuration;");
        sb.AppendLine();
        sb.AppendLine("internal static class ConfigSchemaRegistrations");
        sb.AppendLine("{");
        sb.AppendLine("    private static readonly global::System.Collections.Generic.IReadOnlyList<global::DotCraft.Configuration.ConfigSchemaSection> Schema =");
        sb.AppendLine("    [");
        for (var i = 0; i < schemaTypes.Count; i++)
        {
            sb.AppendLine($"        CreateConfigSchemaSection{i}(),");
        }

        sb.AppendLine("    ];");
        sb.AppendLine();
        sb.AppendLine("    public static global::System.Collections.Generic.IReadOnlyList<global::DotCraft.Configuration.ConfigSchemaSection> GetConfigSchema()");
        sb.AppendLine("        => Schema;");
        sb.AppendLine();
        sb.AppendLine("    public static global::DotCraft.Configuration.IConfigSchemaProvider CreateSchemaProvider()");
        sb.AppendLine("        => new GeneratedConfigSchemaProvider();");
        sb.AppendLine();
        sb.AppendLine("    private sealed class GeneratedConfigSchemaProvider : global::DotCraft.Configuration.IConfigSchemaProvider");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly global::System.Collections.Generic.IReadOnlyList<global::DotCraft.Configuration.ConfigSchemaSection> _schema = ConfigSchemaRegistrations.GetConfigSchema();");
        sb.AppendLine();
        sb.AppendLine("        public global::System.Collections.Generic.IReadOnlyList<global::DotCraft.Configuration.ConfigSchemaSection> GetConfigSchema()");
        sb.AppendLine("            => _schema;");
        sb.AppendLine("    }");

        for (var i = 0; i < schemaTypes.Count; i++)
            GenerateConfigSchemaSectionFactory(sb, schemaTypes[i], i);

        sb.AppendLine("}");

        context.AddSource("ConfigSchemaRegistrations.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void GenerateConfigSchemaSectionFactory(
        StringBuilder sb,
        INamedTypeSymbol type,
        int index)
    {
        var sectionAttr = FindAttribute(type, ConfigSectionAttributeFqn);
        if (sectionAttr == null)
            return;

        var sectionKey = sectionAttr.ConstructorArguments.Length > 0
            ? sectionAttr.ConstructorArguments[0].Value?.ToString() ?? string.Empty
            : string.Empty;
        var displayName = GetNamedString(sectionAttr, "DisplayName");
        var order = GetNamedInt(sectionAttr, "Order", 100);
        var rootKey = GetNamedString(sectionAttr, "RootKey");
        var hasSectionDefaultReload = GetNamedBool(sectionAttr, "HasDefaultReload", false);
        var sectionDefaultReload = GetNamedEnumInt(sectionAttr, "DefaultReload", 0);
        var sectionDefaultSubsystemKey = GetNamedString(sectionAttr, "DefaultSubsystemKey");
        var fields = GetConfigFields(type, hasSectionDefaultReload, sectionDefaultReload, sectionDefaultSubsystemKey);
        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var canCreateDefaults = CanCreateDefaultInstance(type);
        var defaultVariable = canCreateDefaults ? "defaults" : null;

        sb.AppendLine();
        sb.AppendLine($"    private static global::DotCraft.Configuration.ConfigSchemaSection CreateConfigSchemaSection{index}()");
        sb.AppendLine("    {");
        if (canCreateDefaults)
            sb.AppendLine($"        var defaults = new {typeName}();");
        sb.AppendLine("        return new global::DotCraft.Configuration.ConfigSchemaSection");
        sb.AppendLine("        {");
        sb.AppendLine($"            Section = {Literal(displayName ?? rootKey ?? sectionKey)},");
        sb.AppendLine($"            Order = {order.ToString(System.Globalization.CultureInfo.InvariantCulture)},");

        if (!string.IsNullOrWhiteSpace(rootKey))
        {
            sb.AppendLine($"            RootKey = {Literal(rootKey)},");
            sb.AppendLine("            ItemFields =");
            GenerateFieldList(sb, fields, defaultVariable, indent: "            ");
            sb.AppendLine("            Fields = []");
        }
        else
        {
            var path = BuildPath(sectionKey);
            if (path.Length > 0)
                sb.AppendLine($"            Path = {StringArrayExpression(path)},");
            sb.AppendLine("            Fields =");
            GenerateFieldList(sb, fields, defaultVariable, indent: "            ");
        }

        sb.AppendLine("        };");
        sb.AppendLine("    }");
    }

    private static int GetConfigSectionOrder(INamedTypeSymbol type)
        => GetNamedInt(FindAttribute(type, ConfigSectionAttributeFqn), "Order", 100);

    private static void GenerateFieldList(
        StringBuilder sb,
        IReadOnlyList<ConfigFieldInfo> fields,
        string? defaultVariable,
        string indent)
    {
        sb.AppendLine($"{indent}new global::System.Collections.Generic.List<global::DotCraft.Configuration.ConfigSchemaField>");
        sb.AppendLine($"{indent}{{");
        foreach (var field in fields)
            GenerateField(sb, field, defaultVariable, indent + "    ");
        sb.AppendLine($"{indent}}},");
    }

    private static void GenerateField(
        StringBuilder sb,
        ConfigFieldInfo field,
        string? defaultVariable,
        string indent)
    {
        sb.AppendLine($"{indent}new global::DotCraft.Configuration.ConfigSchemaField");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    Key = {Literal(field.Key)},");
        if (field.DisplayName != null)
            sb.AppendLine($"{indent}    DisplayName = {Literal(field.DisplayName)},");
        sb.AppendLine($"{indent}    Type = {Literal(field.FieldType)},");
        if (field.Sensitive)
            sb.AppendLine($"{indent}    Sensitive = true,");
        if (field.Options is { Count: > 0 })
            sb.AppendLine($"{indent}    Options = {StringArrayExpression(field.Options)},");
        if (field.Min.HasValue)
            sb.AppendLine($"{indent}    Min = {field.Min.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        if (field.Max.HasValue)
            sb.AppendLine($"{indent}    Max = {field.Max.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        if (field.Hint != null)
            sb.AppendLine($"{indent}    Hint = {Literal(field.Hint)},");
        if (field.Reload != 0)
            sb.AppendLine($"{indent}    Reload = (global::DotCraft.Configuration.ReloadBehavior){field.Reload.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        if (field.SubsystemKey != null)
            sb.AppendLine($"{indent}    SubsystemKey = {Literal(field.SubsystemKey)},");
        if (defaultVariable != null)
            sb.AppendLine($"{indent}    DefaultValue = {BuildDefaultValueExpression(field, defaultVariable)},");
        sb.AppendLine($"{indent}}},");
    }

    private static IReadOnlyList<ConfigFieldInfo> GetConfigFields(
        INamedTypeSymbol type,
        bool hasSectionDefaultReload,
        int sectionDefaultReload,
        string? sectionDefaultSubsystemKey)
    {
        var fields = new List<ConfigFieldInfo>();
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.DeclaredAccessibility != Accessibility.Public || property.IsIndexer)
                continue;

            var fieldAttr = FindAttribute(property, ConfigFieldAttributeFqn);
            if (GetNamedBool(fieldAttr, "Ignore", false))
                continue;
            if (FindAttribute(property.Type, ConfigSectionAttributeFqn) != null)
                continue;
            if (property.Name == "ExtensionData" || FindAttribute(property, JsonExtensionDataAttributeFqn) != null)
                continue;

            var fieldType = GetNamedString(fieldAttr, "FieldType")
                ?? (GetNamedBool(fieldAttr, "Sensitive", false) ? "password" : InferFieldType(property.Type));
            var options = GetNamedStringArray(fieldAttr, "Options") ?? InferOptions(property.Type);
            var (reload, subsystemKey) = ResolveReload(
                fieldAttr,
                hasSectionDefaultReload,
                sectionDefaultReload,
                sectionDefaultSubsystemKey);
            var min = GetNamedInt(fieldAttr, "Min", int.MinValue);
            var max = GetNamedInt(fieldAttr, "Max", int.MaxValue);

            fields.Add(new ConfigFieldInfo(
                property,
                property.Name,
                GetNamedString(fieldAttr, "DisplayName"),
                fieldType,
                GetNamedBool(fieldAttr, "Sensitive", false),
                options,
                min == int.MinValue ? null : min,
                max == int.MaxValue ? null : max,
                GetNamedString(fieldAttr, "Hint"),
                reload,
                subsystemKey));
        }

        return fields;
    }

    private static (int Reload, string? SubsystemKey) ResolveReload(
        AttributeData? fieldAttr,
        bool hasSectionDefaultReload,
        int sectionDefaultReload,
        string? sectionDefaultSubsystemKey)
    {
        var hasFieldReload = GetNamedBool(fieldAttr, "HasReload", false);
        var reload = hasFieldReload
            ? GetNamedEnumInt(fieldAttr, "Reload", 0)
            : hasSectionDefaultReload
                ? sectionDefaultReload
                : 0;
        var subsystemKey = hasFieldReload
            ? GetNamedString(fieldAttr, "SubsystemKey")
            : hasSectionDefaultReload
                ? sectionDefaultSubsystemKey
                : null;

        return (reload, string.IsNullOrWhiteSpace(subsystemKey) ? null : subsystemKey!.Trim());
    }

    private static string InferFieldType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Boolean)
            return "bool";
        if (type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Double or SpecialType.System_Single)
            return "number";
        if (type.SpecialType == SpecialType.System_String)
            return "text";
        if (type.TypeKind == TypeKind.Enum)
            return "select";
        if (IsStringDictionary(type))
            return "keyValueMap";
        if (IsStringList(type))
            return "stringList";
        if (IsCollectionOrDictionary(type))
            return "json";
        return "text";
    }

    private static IReadOnlyList<string>? InferOptions(ITypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Enum || type is not INamedTypeSymbol enumType)
            return null;

        return enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(static field => field.HasConstantValue)
            .OrderBy(static field => Convert.ToInt64(field.ConstantValue, System.Globalization.CultureInfo.InvariantCulture))
            .ThenBy(static field => field.Name, StringComparer.Ordinal)
            .Select(static field => field.Name)
            .ToArray();
    }

    private static bool IsStringDictionary(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || !named.IsGenericType)
            return false;

        return named.Name == "Dictionary"
            && named.ContainingNamespace.ToDisplayString() == "System.Collections.Generic"
            && named.TypeArguments.Length == 2
            && named.TypeArguments[0].SpecialType == SpecialType.System_String
            && named.TypeArguments[1].SpecialType == SpecialType.System_String;
    }

    private static bool IsStringList(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
            return arrayType.ElementType.SpecialType == SpecialType.System_String;

        if (type is not INamedTypeSymbol named || !named.IsGenericType)
            return false;

        return (named.Name == "List" || named.Name == "IList" || named.Name == "IReadOnlyList")
            && named.ContainingNamespace.ToDisplayString() == "System.Collections.Generic"
            && named.TypeArguments.Length == 1
            && named.TypeArguments[0].SpecialType == SpecialType.System_String;
    }

    private static bool IsCollectionOrDictionary(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
            return true;

        if (type is not INamedTypeSymbol named || !named.IsGenericType)
            return false;

        return (named.Name == "List"
                || named.Name == "Dictionary"
                || named.Name == "IList"
                || named.Name == "IReadOnlyList")
            && named.ContainingNamespace.ToDisplayString() == "System.Collections.Generic";
    }

    private static string BuildDefaultValueExpression(ConfigFieldInfo field, string defaultVariable)
    {
        var propertyAccess = $"{defaultVariable}.{field.Property.Name}";
        var type = field.Property.Type;
        if (type.SpecialType == SpecialType.System_String)
            return $"global::DotCraft.Configuration.ConfigSchemaUtilities.NormalizeStringDefault({propertyAccess})";
        if (type.TypeKind == TypeKind.Enum)
            return $"global::DotCraft.Configuration.ConfigSchemaUtilities.NormalizeEnumDefault({propertyAccess})";
        if (IsCollectionOrDictionary(type))
            return $"global::DotCraft.Configuration.ConfigSchemaUtilities.NormalizeCollectionDefault({propertyAccess})";
        return propertyAccess;
    }

    private static bool CanCreateDefaultInstance(INamedTypeSymbol type)
        => type.InstanceConstructors.Any(static ctor =>
            ctor.Parameters.Length == 0
            && ctor.DeclaredAccessibility == Accessibility.Public);

    private static string[] BuildPath(string key)
        => string.IsNullOrEmpty(key)
            ? []
            : key.Split('.').Where(static part => part.Length > 0).ToArray();

    private static string Literal(string? value)
        => value == null
            ? "null"
            : SymbolDisplay.FormatLiteral(value, quote: true);

    private static string StringArrayExpression(IEnumerable<string> values)
        => $"new[] {{ {string.Join(", ", values.Select(Literal))} }}";

    private static AttributeData? FindAttribute(ISymbol symbol, string metadataName)
        => symbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == metadataName);

    private static string? GetNamedString(AttributeData? attr, string name)
        => attr?.NamedArguments.FirstOrDefault(arg => arg.Key == name).Value.Value?.ToString();

    private static bool GetNamedBool(AttributeData? attr, string name, bool defaultValue)
    {
        var arg = attr?.NamedArguments.FirstOrDefault(item => item.Key == name);
        return arg.HasValue && arg.Value.Value.Value is bool value ? value : defaultValue;
    }

    private static int GetNamedInt(AttributeData? attr, string name, int defaultValue)
    {
        var arg = attr?.NamedArguments.FirstOrDefault(item => item.Key == name);
        return arg.HasValue && arg.Value.Value.Value is int value ? value : defaultValue;
    }

    private static int GetNamedEnumInt(AttributeData? attr, string name, int defaultValue)
    {
        var arg = attr?.NamedArguments.FirstOrDefault(item => item.Key == name);
        if (!arg.HasValue || arg.Value.Value.Value == null)
            return defaultValue;

        return Convert.ToInt32(arg.Value.Value.Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string>? GetNamedStringArray(AttributeData? attr, string name)
    {
        var arg = attr?.NamedArguments.FirstOrDefault(item => item.Key == name);
        if (!arg.HasValue || arg.Value.Value.Kind != TypedConstantKind.Array)
            return null;

        return arg.Value.Value.Values
            .Select(static item => item.Value?.ToString() ?? string.Empty)
            .ToArray();
    }

    #endregion

    #region Namespace scanning helpers

    /// <summary>
    /// Recursively walks a namespace to find types decorated with
    /// [DotCraftModule] or [HostFactory] in referenced assemblies.
    /// </summary>
    private static void ScanNamespaceForModules(
        INamespaceSymbol ns,
        List<ModuleInfo> modules,
        List<HostFactoryInfo> factories)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            foreach (var attr in type.GetAttributes())
            {
                var attrName = attr.AttributeClass?.ToDisplayString();

                if (attrName == ModuleAttributeFqn)
                {
                    modules.Add(ExtractModuleInfo(type, attr));
                }
                else if (attrName == HostFactoryAttributeFqn)
                {
                    factories.Add(ExtractHostFactoryInfo(type, attr));
                }
            }
        }

        foreach (var subNs in ns.GetNamespaceMembers())
        {
            ScanNamespaceForModules(subNs, modules, factories);
        }
    }

    /// <summary>
    /// Recursively walks a namespace to find types decorated with [ConfigSection].
    /// Also scans nested types (e.g. AppConfig.ReasoningConfig).
    /// </summary>
    private static void ScanNamespaceForConfigSections(
        INamespaceSymbol ns,
        List<INamedTypeSymbol> configTypes)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            ScanTypeForConfigSections(type, configTypes);
        }

        foreach (var subNs in ns.GetNamespaceMembers())
        {
            ScanNamespaceForConfigSections(subNs, configTypes);
        }
    }

    private static void ScanTypeForConfigSections(INamedTypeSymbol type, List<INamedTypeSymbol> configTypes)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == ConfigSectionAttributeFqn)
            {
                configTypes.Add(type);
                break;
            }
        }

        // Scan nested types (e.g. AppConfig.ReasoningConfig)
        foreach (var nested in type.GetTypeMembers())
        {
            ScanTypeForConfigSections(nested, configTypes);
        }
    }

    #endregion

    #region Data models

    private sealed class ModuleInfo
    {
        public string ClassName { get; }
        public string ClassNamespace { get; }
        public string ClassNameOnly { get; }
        public string ModuleName { get; }
        public int Priority { get; }
        public string? Description { get; }
        public bool CanBePrimaryHost { get; }

        public ModuleInfo(string ClassName, string ClassNamespace, string ClassNameOnly, string ModuleName, int Priority, string? Description, bool CanBePrimaryHost)
        {
            this.ClassName = ClassName;
            this.ClassNamespace = ClassNamespace;
            this.ClassNameOnly = ClassNameOnly;
            this.ModuleName = ModuleName;
            this.Priority = Priority;
            this.Description = Description;
            this.CanBePrimaryHost = CanBePrimaryHost;
        }
    }

    private sealed class HostFactoryInfo
    {
        public string ClassName { get; }
        public string ModuleName { get; }

        public HostFactoryInfo(string ClassName, string ModuleName)
        {
            this.ClassName = ClassName;
            this.ModuleName = ModuleName;
        }
    }

    private sealed class ConfigFieldInfo
    {
        public IPropertySymbol Property { get; }
        public string Key { get; }
        public string? DisplayName { get; }
        public string FieldType { get; }
        public bool Sensitive { get; }
        public IReadOnlyList<string>? Options { get; }
        public int? Min { get; }
        public int? Max { get; }
        public string? Hint { get; }
        public int Reload { get; }
        public string? SubsystemKey { get; }

        public ConfigFieldInfo(
            IPropertySymbol Property,
            string Key,
            string? DisplayName,
            string FieldType,
            bool Sensitive,
            IReadOnlyList<string>? Options,
            int? Min,
            int? Max,
            string? Hint,
            int Reload,
            string? SubsystemKey)
        {
            this.Property = Property;
            this.Key = Key;
            this.DisplayName = DisplayName;
            this.FieldType = FieldType;
            this.Sensitive = Sensitive;
            this.Options = Options;
            this.Min = Min;
            this.Max = Max;
            this.Hint = Hint;
            this.Reload = Reload;
            this.SubsystemKey = SubsystemKey;
        }
    }

    #endregion
}
