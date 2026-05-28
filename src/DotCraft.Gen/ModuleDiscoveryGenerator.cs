using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DotCraft.Gen;

/// <summary>
/// Source generator that discovers DotCraft modules marked with [DotCraftModule] attribute
/// and config section types marked with [ConfigSection] attribute, and generates:
///   1. Per-module partial class overrides for Name and Priority (in every assembly).
///   2. A static ModuleRegistrations class with explicit new() calls (only in the app
///      assembly, gated by the DotCraftGenerateModuleRegistrations MSBuild property).
///   3. A static ConfigSchemaRegistrations class that returns all [ConfigSection] types
///      (only in the app assembly, same gate as #2).
/// </summary>
[Generator]
public sealed class ModuleDiscoveryGenerator : IIncrementalGenerator
{
    private const string ModuleAttributeFqn = "DotCraft.Modules.DotCraftModuleAttribute";
    private const string HostFactoryAttributeFqn = "DotCraft.Modules.HostFactoryAttribute";
    private const string ConfigSectionAttributeFqn = "DotCraft.Configuration.ConfigSectionAttribute";

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
    /// Generates ConfigSchemaRegistrations.GetAllConfigTypes() by scanning all referenced
    /// DotCraft assemblies for types annotated with [ConfigSection].
    /// </summary>
    private static void GenerateConfigSchemaRegistrations(
        SourceProductionContext context,
        Compilation compilation)
    {
        var configTypes = new List<string>();

        // Scan current compilation (DotCraft.App itself and referenced config types).
        ScanNamespaceForConfigSections(compilation.GlobalNamespace, configTypes);

        // Scan referenced DotCraft assemblies
        foreach (var assemblySymbol in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!assemblySymbol.Name.StartsWith("DotCraft", StringComparison.Ordinal))
                continue;
            ScanNamespaceForConfigSections(assemblySymbol.GlobalNamespace, configTypes);
        }

        if (configTypes.Count == 0)
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
        sb.AppendLine("    public static global::System.Type[] GetAllConfigTypes() =>");
        sb.AppendLine("    [");

        foreach (var typeFqn in configTypes.Distinct().OrderBy(t => t))
        {
            sb.AppendLine($"        typeof({typeFqn}),");
        }

        sb.AppendLine("    ];");
        sb.AppendLine("}");

        context.AddSource("ConfigSchemaRegistrations.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
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
        List<string> configTypes)
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

    private static void ScanTypeForConfigSections(INamedTypeSymbol type, List<string> configTypes)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == ConfigSectionAttributeFqn)
            {
                configTypes.Add(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
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

    #endregion
}
