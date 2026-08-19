using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace DotCraft.Generators;

internal static class ToolGeneratorValidator
{
    private const string RangeAttributeFqn = "System.ComponentModel.DataAnnotations.RangeAttribute";
    private const string MinLengthAttributeFqn = "System.ComponentModel.DataAnnotations.MinLengthAttribute";
    private const string MaxLengthAttributeFqn = "System.ComponentModel.DataAnnotations.MaxLengthAttribute";
    private const string RegularExpressionAttributeFqn = "System.ComponentModel.DataAnnotations.RegularExpressionAttribute";

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

    private static readonly DiagnosticDescriptor InvalidDeclarationMethod = new(
        "DCGEN005",
        "Declaration-only tool method must be abstract",
        "Tool declaration '{0}' must be declared on an interface or as an abstract method",
        "DotCraft.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateParameterName = new(
        "DCGEN006",
        "Duplicate generated tool parameter name",
        "Generated tool '{0}' contains duplicate JSON parameter name '{1}'",
        "DotCraft.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidSchemaConstraint = new(
        "DCGEN007",
        "Invalid generated tool schema constraint",
        "Generated tool '{0}' parameter '{1}' has invalid schema constraint: {2}",
        "DotCraft.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidGeneratedName = new(
        "DCGEN008",
        "Invalid generated tool name",
        "Generated tool declaration '{0}' has an empty model-visible name",
        "DotCraft.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateEnumJsonName = new(
        "DCGEN009",
        "Duplicate generated enum JSON value",
        "Generated tool '{0}' parameter '{1}' enum contains duplicate JSON value '{2}'",
        "DotCraft.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateToolName = new(
        "DCGEN010",
        "Duplicate generated tool name",
        "Generated tool name '{0}' is ambiguous within declaring type '{1}' for methods: {2}",
        "DotCraft.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static bool ValidateUniqueFactoryNames(
        SourceProductionContext context,
        IReadOnlyList<ToolFunctionGenerator.ToolInfo> tools)
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
        var duplicateToolNames = tools
            .Where(static tool => !string.IsNullOrWhiteSpace(tool.FunctionName))
            .GroupBy(static tool => (tool.ContainingTypeFullName, tool.FunctionName))
            .Where(static group => group.Count() > 1)
            .ToList();
        foreach (var group in duplicateToolNames)
        {
            var methods = string.Join(
                ", ",
                group.Select(static tool => tool.Identity).OrderBy(static value => value, StringComparer.Ordinal));
            foreach (var tool in group)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateToolName,
                    tool.Location,
                    group.Key.FunctionName,
                    group.Key.ContainingTypeFullName,
                    methods));
            }
        }

        return duplicates.Count == 0 && duplicateToolNames.Count == 0;
    }

    public static void ValidateTool(SourceProductionContext context, ToolFunctionGenerator.ToolInfo tool)
    {
        if (string.IsNullOrWhiteSpace(tool.FunctionName))
            context.ReportDiagnostic(Diagnostic.Create(InvalidGeneratedName, tool.Location, tool.Identity));
        if (!tool.GenerateFunction && !tool.IsAbstract)
            context.ReportDiagnostic(Diagnostic.Create(InvalidDeclarationMethod, tool.Location, tool.FunctionName));
        if (string.IsNullOrWhiteSpace(tool.Description))
            context.ReportDiagnostic(Diagnostic.Create(MissingDescription, tool.Location, tool.FunctionName, "method"));

        foreach (var duplicate in tool.Parameters
                     .Where(static parameter => !parameter.IsCancellationToken)
                     .GroupBy(static parameter => parameter.SchemaName, StringComparer.Ordinal)
                     .Where(static group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuplicateParameterName,
                duplicate.First().Location,
                tool.FunctionName,
                duplicate.Key));
        }

        foreach (var parameter in tool.Parameters.Where(static parameter => !parameter.IsCancellationToken))
        {
            if (string.IsNullOrWhiteSpace(parameter.Description))
                context.ReportDiagnostic(Diagnostic.Create(MissingDescription, parameter.Location, tool.FunctionName, $"parameter '{parameter.Name}'"));
            if (!IsSupportedToolParameter(parameter.TypeSymbol))
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedToolParameter, parameter.Location, tool.FunctionName, parameter.Name, parameter.TypeName));

            ValidateConstraints(context, tool, parameter);
            ValidateEnumNames(context, tool, parameter);
        }
    }

    private static void ValidateConstraints(
        SourceProductionContext context,
        ToolFunctionGenerator.ToolInfo tool,
        ToolFunctionGenerator.ParameterInfo parameter)
    {
        var type = ToolSchemaEmitter.UnwrapNullable(parameter.TypeSymbol);
        if (ToolSchemaEmitter.FindAttribute(parameter.Symbol, RangeAttributeFqn) is { } range)
        {
            if (!ToolSchemaEmitter.IsInteger(type) && !ToolSchemaEmitter.IsNumber(type))
            {
                ReportConstraint(context, tool, parameter, "RangeAttribute requires a numeric parameter");
            }
            else if (!ToolSchemaEmitter.TryReadRange(range, out var minimum, out var maximum)
                     || !TryParseFiniteNumber(minimum, out var minimumValue)
                     || !TryParseFiniteNumber(maximum, out var maximumValue))
            {
                ReportConstraint(context, tool, parameter, "RangeAttribute bounds must be finite JSON numbers");
            }
            else if (minimumValue > maximumValue)
            {
                ReportConstraint(context, tool, parameter, "RangeAttribute minimum cannot exceed maximum");
            }
        }

        var minLength = ReadLength(parameter.Symbol, MinLengthAttributeFqn);
        var maxLength = ReadLength(parameter.Symbol, MaxLengthAttributeFqn);
        var hasLength = minLength.HasValue || maxLength.HasValue;
        if (hasLength
            && !ToolSchemaEmitter.IsString(type)
            && !ToolSchemaEmitter.TryGetCollectionElement(type, out _)
            && type.ToDisplayString() != "System.Text.Json.Nodes.JsonArray")
        {
            ReportConstraint(context, tool, parameter, "length constraints require a string or collection parameter");
        }
        else if (minLength < 0 || maxLength < 0)
        {
            ReportConstraint(context, tool, parameter, "length constraints cannot be negative");
        }
        else if (minLength.HasValue && maxLength.HasValue && minLength > maxLength)
        {
            ReportConstraint(context, tool, parameter, "MinLengthAttribute cannot exceed MaxLengthAttribute");
        }

        if (ToolSchemaEmitter.FindAttribute(parameter.Symbol, RegularExpressionAttributeFqn) is { } regex)
        {
            if (!ToolSchemaEmitter.IsString(type))
            {
                ReportConstraint(context, tool, parameter, "RegularExpressionAttribute requires a string parameter");
            }
            else if (regex.ConstructorArguments.FirstOrDefault().Value?.ToString() is not { Length: > 0 } pattern)
            {
                ReportConstraint(context, tool, parameter, "RegularExpressionAttribute requires a non-empty pattern");
            }
            else
            {
                try
                {
                    _ = new Regex(pattern);
                }
                catch (ArgumentException)
                {
                    ReportConstraint(context, tool, parameter, "RegularExpressionAttribute contains an invalid pattern");
                }
            }
        }
    }

    private static int? ReadLength(ISymbol symbol, string attributeName) =>
        ToolSchemaEmitter.FindAttribute(symbol, attributeName)?.ConstructorArguments.FirstOrDefault().Value as int?;

    private static bool TryParseFiniteNumber(string value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && !double.IsNaN(number)
        && !double.IsInfinity(number);

    private static void ReportConstraint(
        SourceProductionContext context,
        ToolFunctionGenerator.ToolInfo tool,
        ToolFunctionGenerator.ParameterInfo parameter,
        string message) =>
        context.ReportDiagnostic(Diagnostic.Create(
            InvalidSchemaConstraint,
            parameter.Location,
            tool.FunctionName,
            parameter.SchemaName,
            message));

    private static void ValidateEnumNames(
        SourceProductionContext context,
        ToolFunctionGenerator.ToolInfo tool,
        ToolFunctionGenerator.ParameterInfo parameter)
    {
        var type = ToolSchemaEmitter.UnwrapNullable(parameter.TypeSymbol);
        if (type.TypeKind != TypeKind.Enum)
            return;

        foreach (var duplicate in type.GetMembers().OfType<IFieldSymbol>()
                     .Where(static field => field.HasConstantValue)
                     .GroupBy(ToolSchemaEmitter.GetEnumJsonName, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuplicateEnumJsonName,
                parameter.Location,
                tool.FunctionName,
                parameter.SchemaName,
                duplicate.Key));
        }
    }

    private static bool IsSupportedToolParameter(ITypeSymbol type)
    {
        if (IsCancellationToken(type))
            return true;
        var nonNullable = ToolSchemaEmitter.UnwrapNullable(type);
        if (IsPrimitiveLike(nonNullable) || nonNullable.TypeKind == TypeKind.Enum)
            return true;
        if (ToolSchemaEmitter.TryGetCollectionElement(nonNullable, out var element))
            return IsSupportedToolParameter(element);
        if (ToolSchemaEmitter.IsSupportedJsonType(nonNullable))
            return true;
        return nonNullable is INamedTypeSymbol objectType && objectType.TypeKind == TypeKind.Class;
    }

    private static bool IsPrimitiveLike(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_String or
        SpecialType.System_Boolean or
        SpecialType.System_Int16 or
        SpecialType.System_Int32 or
        SpecialType.System_Int64 or
        SpecialType.System_Single or
        SpecialType.System_Double or
        SpecialType.System_Decimal;

    private static bool IsCancellationToken(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Threading.CancellationToken";
}
