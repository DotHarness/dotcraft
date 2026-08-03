using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotCraft.Protocol.Generators;

/// <summary>Rejects contract shapes that cannot be mapped safely across supported SDK languages.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShapeAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor InvalidDirection = Rule(
        "DPC002", "Invalid RPC direction", "RPC descriptor '{0}' has an invalid direction");
    public static readonly DiagnosticDescriptor AmbiguousPresence = Rule(
        "DPC003", "Ambiguous optional property", "Nullable contract property '{0}' must declare its JSON missing-value behavior");
    public static readonly DiagnosticDescriptor UnsupportedConverter = Rule(
        "DPC004", "Unsupported JSON converter", "Contract member '{0}' uses a JSON converter that is not supported by protocol generation");
    public static readonly DiagnosticDescriptor UnsafeInteger = Rule(
        "DPC005", "Unsafe wire integer", "Contract property '{0}' uses an integer type that is unsafe for JavaScript clients");
    public static readonly DiagnosticDescriptor MissingDiscriminator = Rule(
        "DPC006", "Missing union discriminator", "Abstract contract type '{0}' must declare a JSON discriminator");
    public static readonly DiagnosticDescriptor ForbiddenDependency = Rule(
        "DPC007", "Forbidden contract dependency", "Contract property '{0}' references type '{1}' outside the contract and BCL boundary");
    public static readonly DiagnosticDescriptor MissingMetadata = Rule(
        "DPC008", "Incomplete RPC metadata", "RPC descriptor '{0}' must provide non-empty method, version, specification, module, and scope metadata");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            InvalidDirection,
            AmbiguousPresence,
            UnsupportedConverter,
            UnsafeInteger,
            MissingDiscriminator,
            ForbiddenDependency,
            MissingMetadata);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterSyntaxNodeAction(AnalyzeField, SyntaxKind.VariableDeclarator);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!IsPublicContractType(type))
            return;

        if (type.IsAbstract && !HasAttribute(type, "System.Text.Json.Serialization.JsonPolymorphicAttribute"))
            context.ReportDiagnostic(Diagnostic.Create(MissingDiscriminator, type.Locations.FirstOrDefault(), type.Name));

        if (HasUnsupportedConverter(type))
            context.ReportDiagnostic(Diagnostic.Create(UnsupportedConverter, type.Locations.FirstOrDefault(), type.Name));

        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.DeclaredAccessibility != Accessibility.Public || property.IsStatic)
                continue;

            if (property.NullableAnnotation == NullableAnnotation.Annotated &&
                !HasAttribute(property, "System.Text.Json.Serialization.JsonIgnoreAttribute") &&
                !HasAttribute(property, "System.Text.Json.Serialization.JsonRequiredAttribute"))
            {
                context.ReportDiagnostic(Diagnostic.Create(AmbiguousPresence, property.Locations.FirstOrDefault(), property.Name));
            }

            var hasSafeIntegerAttribute = HasAttribute(property, "DotCraft.Protocol.JsonSafeIntegerAttribute");
            if ((ContainsUnsafeInteger(property.Type) &&
                 (!hasSafeIntegerAttribute || !IsSafeIntegerProperty(property.Type))) ||
                (hasSafeIntegerAttribute && !IsSafeIntegerProperty(property.Type)))
                context.ReportDiagnostic(Diagnostic.Create(UnsafeInteger, property.Locations.FirstOrDefault(), property.Name));

            if (HasUnsupportedConverter(property))
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedConverter, property.Locations.FirstOrDefault(), property.Name));

            var externalType = FindForbiddenType(property.Type);
            if (externalType is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ForbiddenDependency,
                    property.Locations.FirstOrDefault(),
                    property.Name,
                    externalType.ToDisplayString()));
            }
        }
    }

    private static void AnalyzeField(SyntaxNodeAnalysisContext context)
    {
        var declarator = (VariableDeclaratorSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declarator, context.CancellationToken) is not IFieldSymbol field)
            return;
        if (!field.IsStatic || field.DeclaredAccessibility != Accessibility.Public || !IsDescriptor(field.Type))
            return;

        var arguments = declarator.Initializer?.Value switch
        {
            ObjectCreationExpressionSyntax creation => creation.ArgumentList?.Arguments,
            ImplicitObjectCreationExpressionSyntax creation => creation.ArgumentList.Arguments,
            _ => null
        };
        if (arguments is null || arguments.Value.Count < 4)
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingMetadata, field.Locations.FirstOrDefault(), field.Name));
            return;
        }

        var requiredPositions = new[] { 0, 2, 3 };
        if (requiredPositions.Any(index => IsEmptyString(arguments.Value[index].Expression, context.SemanticModel)))
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingMetadata, field.Locations.FirstOrDefault(), field.Name));
        }

        var direction = context.SemanticModel
            .GetSymbolInfo(arguments.Value[1].Expression, context.CancellationToken).Symbol as IFieldSymbol;
        if (direction?.ContainingType.ToDisplayString() != "DotCraft.Protocol.RpcDirection")
            context.ReportDiagnostic(Diagnostic.Create(InvalidDirection, field.Locations.FirstOrDefault(), field.Name));
    }

    private static DiagnosticDescriptor Rule(string id, string title, string message) =>
        new(id, title, message, "DotCraft.Protocol", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static bool IsPublicContractType(INamedTypeSymbol type) =>
        type.DeclaredAccessibility == Accessibility.Public &&
        type.ContainingNamespace.ToDisplayString().StartsWith("DotCraft.Protocol.AppServer", StringComparison.Ordinal);

    private static bool IsDescriptor(ITypeSymbol type)
    {
        var name = type.OriginalDefinition.ToDisplayString();
        return name == "DotCraft.Protocol.RpcRequest<TParams, TResult>" ||
               name == "DotCraft.Protocol.RpcNotification<TParams>";
    }

    private static bool IsEmptyString(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var value = semanticModel.GetConstantValue(expression);
        return value.HasValue && value.Value is string text && string.IsNullOrWhiteSpace(text);
    }

    private static bool HasUnsupportedConverter(ISymbol symbol) =>
        symbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonConverterAttribute" &&
            attribute.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol converter &&
            converter.ToDisplayString() != "DotCraft.Protocol.OptionalJsonConverterFactory");

    private static bool HasAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == metadataName);

    private static bool ContainsUnsafeInteger(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return ContainsUnsafeInteger(array.ElementType);
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];
        if (type.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64)
            return true;
        return type is INamedTypeSymbol named && named.TypeArguments.Any(ContainsUnsafeInteger);
    }

    private static bool IsSafeIntegerProperty(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol optional &&
            optional.OriginalDefinition.ToDisplayString() == "DotCraft.Protocol.Optional<T>")
        {
            type = optional.TypeArguments[0];
        }
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];
        return type.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64;
    }

    private static ITypeSymbol? FindForbiddenType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return FindForbiddenType(array.ElementType);

        if (type is not INamedTypeSymbol named)
            return null;

        foreach (var argument in named.TypeArguments)
        {
            var forbiddenArgument = FindForbiddenType(argument);
            if (forbiddenArgument is not null)
                return forbiddenArgument;
        }

        var assembly = named.ContainingAssembly?.Name ?? string.Empty;
        var ns = named.ContainingNamespace.ToDisplayString();
        if (assembly is "System.Private.CoreLib" or "mscorlib" or "netstandard" ||
            assembly.StartsWith("System", StringComparison.Ordinal) ||
            ns.StartsWith("System", StringComparison.Ordinal) ||
            ns.StartsWith("DotCraft.Protocol", StringComparison.Ordinal))
        {
            return null;
        }

        return named;
    }
}
