using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotCraft.Generators;

[Generator]
public sealed class ToolFunctionGenerator : IIncrementalGenerator
{
    private const string ToolAttributeFqn = "DotCraft.Tools.ToolAttribute";
    private const string GeneratedToolAttributeFqn = "DotCraft.Tools.GeneratedToolAttribute";
    private const string DescriptionAttributeFqn = "System.ComponentModel.DescriptionAttribute";
    private const string StreamArgumentsAttributeFqn = "DotCraft.Agents.StreamArgumentsAttribute";

    private static readonly DiagnosticDescriptor UnsupportedToolParameter = new(
        "DCGEN001",
        "Unsupported generated tool parameter",
        "Generated tool '{0}' parameter '{1}' has unsupported type '{2}'",
        "DotCraft.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingDescription = new(
        "DCGEN002",
        "Generated tool description is required",
        "Generated tool '{0}' is missing DescriptionAttribute on {1}",
        "DotCraft.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateGeneratedFactoryName = new(
        "DCGEN004",
        "Duplicate generated tool factory name",
        "Generated tool factory name '{0}' is ambiguous for methods: {1}",
        "DotCraft.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var attributedTools = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ToolAttributeFqn,
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, _) => GetToolInfo(ctx, catalogVisibleDefault: true))
            .Where(static item => item != null);

        var explicitGeneratedTools = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratedToolAttributeFqn,
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, _) => GetToolInfo(ctx, catalogVisibleDefault: false))
            .Where(static item => item != null);

        var compilationTools = context.CompilationProvider.Select(static (compilation, _) =>
            (GeneratedNamespace: GetGeneratedToolsNamespace(compilation.AssemblyName), Tools: ScanCompilationForTools(compilation)));

        var normalTools = attributedTools.Collect().Combine(explicitGeneratedTools.Collect()).Combine(compilationTools);
        context.RegisterSourceOutput(normalTools, static (ctx, data) =>
        {
            var syntaxTools = data.Left;
            var tools = syntaxTools.Left
                .Concat(syntaxTools.Right)
                .Concat(data.Right.Tools)
                .Where(static item => item != null)
                .Select(static item => item!)
                .GroupBy(static item => item.Identity, StringComparer.Ordinal)
                .Select(static group => group.First())
                .OrderBy(static item => item.FactoryName, StringComparer.Ordinal)
                .ToList();
            GenerateToolFactories(ctx, data.Right.GeneratedNamespace, tools);
        });

    }

    private static ToolInfo? GetToolInfo(GeneratorAttributeSyntaxContext context, bool catalogVisibleDefault)
    {
        if (context.TargetSymbol is not IMethodSymbol method)
            return null;

        return CreateToolInfo(method, catalogVisibleDefault);
    }

    private static ToolInfo? CreateToolInfo(IMethodSymbol method, bool catalogVisibleDefault)
    {
        var toolAttribute = FindAttribute(method, ToolAttributeFqn);
        var generatedAttribute = FindAttribute(method, GeneratedToolAttributeFqn);
        var description = GetDescription(method);
        var catalogVisible = toolAttribute != null
            ? GetNamedBool(toolAttribute, "CatalogVisible", catalogVisibleDefault)
            : GetNamedBool(generatedAttribute, "CatalogVisible", catalogVisibleDefault);
        var streamArgumentsEnabled = GetStreamArgumentsEnabled(method);
        var displayType = GetNamedType(toolAttribute, "DisplayType");
        var displayMethod = GetNamedString(toolAttribute, "DisplayMethod");
        var maxResultChars = GetNamedInt(toolAttribute, "MaxResultChars", -1);
        var icon = GetNamedString(toolAttribute, "Icon") ?? string.Empty;
        var parameters = method.Parameters
            .Select(parameter => ParameterInfo.From(parameter))
            .ToList();

        return new ToolInfo(
            Identity: BuildMethodIdentity(method),
            Namespace: method.ContainingNamespace.ToDisplayString(),
            ContainingTypeName: method.ContainingType.Name,
            ContainingTypeFullName: method.ContainingType.ToDisplayString(TypeFormat),
            MethodName: method.Name,
            FactoryName: SanitizeIdentifier($"{method.ContainingType.Name}_{method.Name}"),
            WrapperTypeName: SanitizeIdentifier($"{method.ContainingType.Name}_{method.Name}_Function"),
            IsStatic: method.IsStatic,
            ReturnType: method.ReturnType.ToDisplayString(TypeFormat),
            FunctionName: method.Name,
            Description: description,
            Icon: icon,
            DisplayType: displayType?.ToDisplayString(TypeFormat),
            DisplayMethod: displayMethod,
            MaxResultChars: maxResultChars == -1 ? null : maxResultChars,
            StreamArgumentsEnabled: streamArgumentsEnabled,
            CatalogVisible: catalogVisible,
            Parameters: parameters,
            Location: method.Locations.FirstOrDefault());
    }

    private static ImmutableArray<ToolInfo?> ScanCompilationForTools(Compilation compilation)
    {
        var builder = ImmutableArray.CreateBuilder<ToolInfo?>();
        ScanNamespace(compilation.GlobalNamespace, builder);
        return builder.ToImmutable();

        static void ScanNamespace(INamespaceSymbol ns, ImmutableArray<ToolInfo?>.Builder builder)
        {
            foreach (var type in ns.GetTypeMembers())
                ScanType(type, builder);
            foreach (var child in ns.GetNamespaceMembers())
                ScanNamespace(child, builder);
        }

        static void ScanType(INamedTypeSymbol type, ImmutableArray<ToolInfo?>.Builder builder)
        {
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary)
                    continue;
                if (!method.Locations.Any(static location => location.IsInSource))
                    continue;
                var hasTool = FindAttribute(method, ToolAttributeFqn) != null;
                var hasGeneratedTool = FindAttribute(method, GeneratedToolAttributeFqn) != null;
                if (hasTool || hasGeneratedTool)
                    builder.Add(CreateToolInfo(method, hasTool));
            }

            foreach (var nested in type.GetTypeMembers())
                ScanType(nested, builder);
        }
    }

    private static string BuildMethodIdentity(IMethodSymbol method)
    {
        var parameters = string.Join(
            ",",
            method.Parameters.Select(static parameter => parameter.Type.ToDisplayString(TypeFormat)));
        return $"{method.ContainingType.ToDisplayString(TypeFormat)}.{method.MetadataName}({parameters})";
    }

    private static void GenerateToolFactories(SourceProductionContext context, string generatedNamespace, IReadOnlyList<ToolInfo> tools)
    {
        if (tools.Count == 0)
            return;

        foreach (var tool in tools)
        {
            ValidateTool(context, tool);
        }

        if (!ValidateUniqueFactoryNames(context, tools))
            return;

        var sb = new StringBuilder();
        AppendHeader(sb);
        sb.AppendLine($"[assembly: global::DotCraft.Tools.GeneratedToolCatalogAttribute(typeof(global::{generatedNamespace}.GeneratedToolCatalog))]");
        sb.AppendLine();
        sb.AppendLine($"namespace {generatedNamespace};");
        sb.AppendLine();
        sb.AppendLine("internal static class GeneratedToolCatalog");
        sb.AppendLine("{");
        foreach (var tool in tools)
        {
            sb.Append("    internal static readonly global::DotCraft.Tools.GeneratedToolDescriptor ");
            sb.Append(tool.DescriptorFieldName);
            sb.Append(" = new(");
            AppendGeneratedToolDescriptorArguments(sb, tool);
            sb.AppendLine(");");
        }
        sb.AppendLine();
        sb.AppendLine("    public static global::System.Collections.Generic.IReadOnlyList<global::DotCraft.Tools.GeneratedToolDescriptor> Descriptors { get; } =");
        sb.AppendLine("    [");
        foreach (var tool in tools)
        {
            sb.Append("        ");
            sb.Append(tool.DescriptorFieldName);
            sb.AppendLine(",");
        }
        sb.AppendLine("    ];");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("internal static partial class GeneratedToolFunctions");
        sb.AppendLine("{");

        foreach (var tool in tools)
        {
            if (tool.IsStatic)
            {
                sb.AppendLine($"    public static global::Microsoft.Extensions.AI.AIFunction {tool.FactoryName}() => new {tool.WrapperTypeName}();");
            }
            else
            {
                sb.AppendLine($"    public static global::Microsoft.Extensions.AI.AIFunction {tool.FactoryName}({tool.ContainingTypeFullName} target) => new {tool.WrapperTypeName}(target);");
            }
        }

        sb.AppendLine();
        foreach (var tool in tools)
            AppendToolWrapper(sb, generatedNamespace, tool);

        sb.AppendLine("}");

        context.AddSource("GeneratedToolFunctions.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void AppendToolWrapper(StringBuilder sb, string generatedNamespace, ToolInfo tool)
    {
        var schema = BuildFunctionSchema(tool);
        var targetField = tool.IsStatic ? string.Empty : $"        private readonly {tool.ContainingTypeFullName} _target = target;\n\n";
        var ctorParameter = tool.IsStatic ? string.Empty : $"{tool.ContainingTypeFullName} target";
        sb.AppendLine($"    private sealed class {tool.WrapperTypeName}({ctorParameter}) : global::DotCraft.Tools.GeneratedAIFunction(");
        sb.AppendLine($"        {Quote(tool.FunctionName)},");
        sb.AppendLine($"        {Quote(tool.Description)},");
        sb.AppendLine($"        {Quote(schema)},");
        sb.AppendLine($"        typeof({tool.ReturnType}),");
        sb.AppendLine($"        global::{generatedNamespace}.GeneratedToolCatalog.{tool.DescriptorFieldName})");
        sb.AppendLine("    {");
        if (!tool.IsStatic)
            sb.Append(targetField);

        sb.AppendLine("        protected override async global::System.Threading.Tasks.ValueTask<object?> InvokeCoreAsync(");
        sb.AppendLine("            global::Microsoft.Extensions.AI.AIFunctionArguments arguments,");
        sb.AppendLine("            global::System.Threading.CancellationToken cancellationToken)");
        sb.AppendLine("        {");

        foreach (var parameter in tool.Parameters.Where(static p => !p.IsCancellationToken))
        {
            var binder = parameter.HasDefaultValue ? "GetOptional" : "GetRequired";
            var defaultArgument = parameter.HasDefaultValue
                ? $", {FormatDefaultValue(parameter)}, JsonSerializerOptions"
                : ", JsonSerializerOptions";
            sb.AppendLine($"            var {parameter.Name} = global::DotCraft.Tools.GeneratedToolArgumentBinder.{binder}<{parameter.TypeName}>(arguments, {Quote(parameter.Name)}, {defaultArgument.TrimStart(',', ' ')});");
        }

        var args = string.Join(", ", tool.Parameters.Select(static parameter =>
            parameter.IsCancellationToken ? "cancellationToken" : parameter.Name));
        var invocation = tool.IsStatic
            ? $"{tool.ContainingTypeFullName}.{tool.MethodName}({args})"
            : $"_target.{tool.MethodName}({args})";
        var returnType = UnwrapTaskType(tool.ReturnType);

        if (IsTaskLike(tool.ReturnType))
        {
            if (returnType == null)
            {
                sb.AppendLine($"            await {invocation}.ConfigureAwait(false);");
                sb.AppendLine("            return null;");
            }
            else
            {
                sb.AppendLine($"            var result = await {invocation}.ConfigureAwait(false);");
                sb.AppendLine($"            return global::DotCraft.Tools.GeneratedToolArgumentBinder.MarshalResult(result, typeof({returnType}), JsonSerializerOptions);");
            }
        }
        else if (tool.ReturnType == "void")
        {
            sb.AppendLine($"            {invocation};");
            sb.AppendLine("            return null;");
        }
        else
        {
            sb.AppendLine($"            var result = {invocation};");
            sb.AppendLine($"            return global::DotCraft.Tools.GeneratedToolArgumentBinder.MarshalResult(result, typeof({tool.ReturnType}), JsonSerializerOptions);");
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void AppendGeneratedToolDescriptorArguments(StringBuilder sb, ToolInfo tool)
    {
        sb.Append(Quote(tool.FunctionName));
        sb.Append(", ");
        sb.Append(Quote(tool.Description));
        sb.Append(", ");
        sb.Append(Quote(tool.Icon));
        sb.Append(", ");
        sb.Append(FormatDisplayFormatter(tool));
        sb.Append(", ");
        sb.Append(tool.MaxResultChars.HasValue ? tool.MaxResultChars.Value.ToString(CultureInfo.InvariantCulture) : "null");
        sb.Append(", ");
        sb.Append(tool.StreamArgumentsEnabled ? "true" : "false");
        sb.Append(", ");
        sb.Append(tool.CatalogVisible ? "true" : "false");
    }

    private static bool ValidateUniqueFactoryNames(SourceProductionContext context, IReadOnlyList<ToolInfo> tools)
    {
        var duplicates = tools
            .GroupBy(static tool => tool.FactoryName, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .ToList();
        foreach (var group in duplicates)
        {
            var methods = string.Join(
                ", ",
                group.Select(static tool => tool.Identity).OrderBy(static value => value, StringComparer.Ordinal));
            foreach (var tool in group)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateGeneratedFactoryName,
                    tool.Location,
                    group.Key,
                    methods));
            }
        }

        return duplicates.Count == 0;
    }

    private static void ValidateTool(SourceProductionContext context, ToolInfo tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Description))
            context.ReportDiagnostic(Diagnostic.Create(MissingDescription, tool.Location, tool.FunctionName, "method"));

        foreach (var parameter in tool.Parameters.Where(static p => !p.IsCancellationToken))
        {
            if (string.IsNullOrWhiteSpace(parameter.Description))
                context.ReportDiagnostic(Diagnostic.Create(MissingDescription, parameter.Location, tool.FunctionName, $"parameter '{parameter.Name}'"));

            if (!IsSupportedToolParameter(parameter.TypeSymbol))
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedToolParameter, parameter.Location, tool.FunctionName, parameter.Name, parameter.TypeName));
        }
    }

    private static bool IsSupportedToolParameter(ITypeSymbol type)
    {
        if (IsCancellationToken(type))
            return true;
        var nonNullable = UnwrapNullable(type);
        if (IsPrimitiveLike(nonNullable) || nonNullable.TypeKind == TypeKind.Enum)
            return true;
        if (TryGetCollectionElement(nonNullable, out var element))
            return IsSupportedToolParameter(element);
        if (nonNullable is INamedTypeSymbol named && named.Name == "JsonObject" && named.ContainingNamespace.ToDisplayString() == "System.Text.Json.Nodes")
            return true;
        if (nonNullable is INamedTypeSymbol objectType && objectType.TypeKind == TypeKind.Class)
            return true;
        return false;
    }

    private static bool IsPrimitiveLike(ITypeSymbol type)
    {
        return type.SpecialType is
            SpecialType.System_String or
            SpecialType.System_Boolean or
            SpecialType.System_Int16 or
            SpecialType.System_Int32 or
            SpecialType.System_Int64 or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Decimal;
    }

    private static string BuildFunctionSchema(ToolInfo tool)
    {
        var properties = new List<string>();
        var required = new List<string>();
        foreach (var parameter in tool.Parameters.Where(static p => !p.IsCancellationToken))
        {
            properties.Add($"{Quote(parameter.Name)}:{BuildParameterSchema(parameter, dynamicSchema: false)}");
            if (!parameter.HasDefaultValue)
                required.Add(Quote(parameter.Name));
        }

        var sb = new StringBuilder();
        sb.Append("{\"type\":\"object\",\"properties\":{");
        sb.Append(string.Join(",", properties));
        sb.Append('}');
        if (required.Count > 0)
        {
            sb.Append(",\"required\":[");
            sb.Append(string.Join(",", required));
            sb.Append(']');
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string BuildParameterSchema(ParameterInfo parameter, bool dynamicSchema)
    {
        var core = BuildTypeSchema(parameter.TypeSymbol, parameter.HasDefaultValue, parameter.DefaultValue, dynamicSchema, parameter.Description);
        return core;
    }

    private static string BuildTypeSchema(
        ITypeSymbol type,
        bool hasDefault,
        object? defaultValue,
        bool dynamicSchema,
        string? description = null)
    {
        var typeWithoutNullable = UnwrapNullable(type);
        var isNullable = !SymbolEqualityComparer.Default.Equals(typeWithoutNullable, type)
            || type.NullableAnnotation == NullableAnnotation.Annotated;
        var emitNullableType = isNullable && !dynamicSchema;

        var entries = new List<string>();
        if (!dynamicSchema && !string.IsNullOrWhiteSpace(description))
            entries.Add($"\"description\":{Quote(description!)}");

        if (typeWithoutNullable.TypeKind == TypeKind.Enum)
        {
            entries.Add("\"type\":\"string\"");
            var names = typeWithoutNullable.GetMembers().OfType<IFieldSymbol>()
                .Where(static field => field.HasConstantValue)
                .Select(static field => Quote(field.Name));
            entries.Add($"\"enum\":[{string.Join(",", names)}]");
        }
        else if (TryGetCollectionElement(typeWithoutNullable, out var element))
        {
            entries.Add("\"type\":\"array\"");
            entries.Add($"\"items\":{BuildTypeSchema(element, false, null, dynamicSchema: false)}");
        }
        else if (IsString(typeWithoutNullable))
        {
            entries.Add(emitNullableType ? "\"type\":[\"string\",\"null\"]" : "\"type\":\"string\"");
        }
        else if (IsBoolean(typeWithoutNullable))
        {
            entries.Add(emitNullableType ? "\"type\":[\"boolean\",\"null\"]" : "\"type\":\"boolean\"");
        }
        else if (IsInteger(typeWithoutNullable))
        {
            entries.Add(emitNullableType ? "\"type\":[\"integer\",\"null\"]" : "\"type\":\"integer\"");
        }
        else if (IsNumber(typeWithoutNullable))
        {
            entries.Add(emitNullableType ? "\"type\":[\"number\",\"null\"]" : "\"type\":\"number\"");
        }
        else if (IsJsonObject(typeWithoutNullable))
        {
            entries.Add("\"type\":\"object\"");
        }
        else
        {
            entries.Add("\"type\":\"object\"");
            var properties = BuildObjectProperties(typeWithoutNullable);
            if (properties.Count > 0)
                entries.Add($"\"properties\":{{{string.Join(",", properties)}}}");
        }

        if (dynamicSchema && !string.IsNullOrWhiteSpace(description))
            entries.Add($"\"description\":{Quote(description!)}");
        if (!dynamicSchema && hasDefault)
            entries.Add($"\"default\":{FormatJsonDefault(defaultValue, typeWithoutNullable)}");
        return "{" + string.Join(",", entries) + "}";
    }

    private static List<string> BuildObjectProperties(ITypeSymbol type)
    {
        var result = new List<string>();
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.GetMethod == null)
                continue;
            if (property.DeclaredAccessibility != Accessibility.Public)
                continue;

            var name = ToCamelCase(property.Name);
            var description = GetDescription(property);
            result.Add($"{Quote(name)}:{BuildTypeSchema(property.Type, false, null, dynamicSchema: false, description)}");
        }

        return result;
    }

    private static string FormatDisplayFormatter(ToolInfo tool)
    {
        if (string.IsNullOrWhiteSpace(tool.DisplayType) || string.IsNullOrWhiteSpace(tool.DisplayMethod))
            return "null";

        return $"new global::System.Func<global::System.Collections.Generic.IDictionary<string, object?>?, string>({tool.DisplayType}.{tool.DisplayMethod})";
    }

    private static string FormatDefaultValue(ParameterInfo parameter)
    {
        if (!parameter.HasDefaultValue || parameter.DefaultValue == null)
            return "null";

        var type = UnwrapNullable(parameter.TypeSymbol);
        if (type.TypeKind == TypeKind.Enum)
        {
            var field = type.GetMembers().OfType<IFieldSymbol>()
                .FirstOrDefault(field => field.HasConstantValue && Equals(field.ConstantValue, parameter.DefaultValue));
            return field == null ? $"({parameter.TypeName}){parameter.DefaultValue}" : $"{type.ToDisplayString(TypeFormat)}.{field.Name}";
        }

        return parameter.DefaultValue switch
        {
            string s => Quote(s),
            bool b => b ? "true" : "false",
            char c => Quote(c.ToString()),
            long l => l.ToString(CultureInfo.InvariantCulture) + "L",
            float f => f.ToString(CultureInfo.InvariantCulture) + "F",
            double d => d.ToString(CultureInfo.InvariantCulture) + "D",
            decimal m => m.ToString(CultureInfo.InvariantCulture) + "M",
            _ => Convert.ToString(parameter.DefaultValue, CultureInfo.InvariantCulture) ?? "null"
        };
    }

    private static string FormatJsonDefault(object? value, ITypeSymbol type)
    {
        if (value == null)
            return "null";

        if (type.TypeKind == TypeKind.Enum)
        {
            var field = type.GetMembers().OfType<IFieldSymbol>()
                .FirstOrDefault(field => field.HasConstantValue && Equals(field.ConstantValue, value));
            return Quote(field?.Name ?? value.ToString() ?? string.Empty);
        }

        return value switch
        {
            string s => Quote(s),
            bool b => b ? "true" : "false",
            char c => Quote(c.ToString()),
            float f => f.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null"
        };
    }

    private static bool IsStringListType(ITypeSymbol type) =>
        TryGetCollectionElement(type, out var element) && IsString(UnwrapNullable(element));

    private static bool TryGetCollectionElement(ITypeSymbol type, out ITypeSymbol element)
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

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named &&
            named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
        {
            return named.TypeArguments[0];
        }

        return type;
    }

    private static bool IsNullableValueType(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T
        && named.TypeArguments.Length == 1;

    private static string? UnwrapTaskType(string typeName)
    {
        const string taskPrefix = "global::System.Threading.Tasks.Task<";
        const string valueTaskPrefix = "global::System.Threading.Tasks.ValueTask<";
        if (typeName.StartsWith(taskPrefix, StringComparison.Ordinal) && typeName.EndsWith(">", StringComparison.Ordinal))
            return typeName.Substring(taskPrefix.Length, typeName.Length - taskPrefix.Length - 1);
        if (typeName.StartsWith(valueTaskPrefix, StringComparison.Ordinal) && typeName.EndsWith(">", StringComparison.Ordinal))
            return typeName.Substring(valueTaskPrefix.Length, typeName.Length - valueTaskPrefix.Length - 1);
        return null;
    }

    private static bool IsTaskLike(string typeName) =>
        typeName == "global::System.Threading.Tasks.Task" ||
        typeName == "global::System.Threading.Tasks.ValueTask" ||
        typeName.StartsWith("global::System.Threading.Tasks.Task<", StringComparison.Ordinal) ||
        typeName.StartsWith("global::System.Threading.Tasks.ValueTask<", StringComparison.Ordinal);

    private static bool IsCancellationToken(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Threading.CancellationToken";

    private static bool IsJsonObject(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.Name == "JsonObject"
        && named.ContainingNamespace.ToDisplayString() == "System.Text.Json.Nodes";

    private static bool IsString(ITypeSymbol type) => type.SpecialType == SpecialType.System_String;

    private static bool IsBoolean(ITypeSymbol type) => type.SpecialType == SpecialType.System_Boolean;

    private static bool IsInteger(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64;

    private static bool IsNumber(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;

    private static string GetDescription(ISymbol symbol) =>
        FindAttribute(symbol, DescriptionAttributeFqn)?.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? string.Empty;

    private static AttributeData? FindAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == metadataName);

    private static string? GetNamedString(AttributeData? attribute, string name) =>
        attribute?.NamedArguments.FirstOrDefault(arg => arg.Key == name).Value.Value?.ToString();

    private static INamedTypeSymbol? GetNamedType(AttributeData? attribute, string name) =>
        attribute?.NamedArguments.FirstOrDefault(arg => arg.Key == name).Value.Value as INamedTypeSymbol;

    private static int GetNamedInt(AttributeData? attribute, string name, int defaultValue)
    {
        var value = attribute?.NamedArguments.FirstOrDefault(arg => arg.Key == name).Value.Value;
        return value is int i ? i : defaultValue;
    }

    private static bool GetNamedBool(AttributeData? attribute, string name, bool defaultValue)
    {
        var value = attribute?.NamedArguments.FirstOrDefault(arg => arg.Key == name).Value.Value;
        return value is bool b ? b : defaultValue;
    }

    private static bool GetStreamArgumentsEnabled(IMethodSymbol method)
    {
        var attribute = FindAttribute(method, StreamArgumentsAttributeFqn);
        if (attribute?.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is bool b)
            return b;
        return true;
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) || char.IsLower(value[0])
            ? value
            : char.ToLowerInvariant(value[0]) + value.Substring(1);

    private static string GetGeneratedToolsNamespace(string? assemblyName)
    {
        var segments = string.IsNullOrWhiteSpace(assemblyName)
            ? ["Assembly"]
            : assemblyName!.Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0 && string.Equals(segments[0], "DotCraft", StringComparison.Ordinal))
            segments = segments.Skip(1).ToArray();
        if (segments.Length == 0)
            segments = ["Assembly"];
        return $"DotCraft.GeneratedTools.{string.Join(".", segments.Select(SanitizeIdentifier))}";
    }

    private static string SanitizeIdentifier(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        if (sb.Length == 0 || !SyntaxFacts.IsIdentifierStartCharacter(sb[0]))
            sb.Insert(0, '_');
        return sb.ToString();
    }

    private static string Quote(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private static string EscapeForString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void AppendHeader(StringBuilder sb)
    {
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("//     This code was generated by DotCraft.Generators.");
        sb.AppendLine("//     Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
    }

    private sealed class ToolInfo
    {
        public ToolInfo(
            string Identity,
            string Namespace,
            string ContainingTypeName,
            string ContainingTypeFullName,
            string MethodName,
            string FactoryName,
            string WrapperTypeName,
            bool IsStatic,
            string ReturnType,
            string FunctionName,
            string Description,
            string Icon,
            string? DisplayType,
            string? DisplayMethod,
            int? MaxResultChars,
            bool StreamArgumentsEnabled,
            bool CatalogVisible,
            IReadOnlyList<ParameterInfo> Parameters,
            Location? Location)
        {
            this.Identity = Identity;
            this.Namespace = Namespace;
            this.ContainingTypeName = ContainingTypeName;
            this.ContainingTypeFullName = ContainingTypeFullName;
            this.MethodName = MethodName;
            this.FactoryName = FactoryName;
            this.WrapperTypeName = WrapperTypeName;
            this.DescriptorFieldName = SanitizeIdentifier($"{FactoryName}_Descriptor");
            this.IsStatic = IsStatic;
            this.ReturnType = ReturnType;
            this.FunctionName = FunctionName;
            this.Description = Description;
            this.Icon = Icon;
            this.DisplayType = DisplayType;
            this.DisplayMethod = DisplayMethod;
            this.MaxResultChars = MaxResultChars;
            this.StreamArgumentsEnabled = StreamArgumentsEnabled;
            this.CatalogVisible = CatalogVisible;
            this.Parameters = Parameters;
            this.Location = Location;
        }

        public string Identity { get; }
        public string Namespace { get; }
        public string ContainingTypeName { get; }
        public string ContainingTypeFullName { get; }
        public string MethodName { get; }
        public string FactoryName { get; }
        public string WrapperTypeName { get; }
        public string DescriptorFieldName { get; }
        public bool IsStatic { get; }
        public string ReturnType { get; }
        public string FunctionName { get; }
        public string Description { get; }
        public string Icon { get; }
        public string? DisplayType { get; }
        public string? DisplayMethod { get; }
        public int? MaxResultChars { get; }
        public bool StreamArgumentsEnabled { get; }
        public bool CatalogVisible { get; }
        public IReadOnlyList<ParameterInfo> Parameters { get; }
        public Location? Location { get; }
    }

    private sealed class ParameterInfo
    {
        public ParameterInfo(
            string Name,
            string TypeName,
            ITypeSymbol TypeSymbol,
            bool HasDefaultValue,
            object? DefaultValue,
            string Description,
            bool IsCancellationToken,
            Location? Location)
        {
            this.Name = Name;
            this.TypeName = TypeName;
            this.TypeSymbol = TypeSymbol;
            this.HasDefaultValue = HasDefaultValue;
            this.DefaultValue = DefaultValue;
            this.Description = Description;
            this.IsCancellationToken = IsCancellationToken;
            this.Location = Location;
        }

        public string Name { get; }
        public string TypeName { get; }
        public ITypeSymbol TypeSymbol { get; }
        public bool HasDefaultValue { get; }
        public object? DefaultValue { get; }
        public string Description { get; }
        public bool IsCancellationToken { get; }
        public Location? Location { get; }

        public static ParameterInfo From(IParameterSymbol symbol)
        {
            return new ParameterInfo(
                symbol.Name,
                symbol.Type.ToDisplayString(TypeFormat),
                symbol.Type,
                symbol.HasExplicitDefaultValue,
                symbol.HasExplicitDefaultValue ? symbol.ExplicitDefaultValue : null,
                GetDescription(symbol),
                IsCancellationToken(symbol.Type),
                symbol.Locations.FirstOrDefault());
        }
    }
}
