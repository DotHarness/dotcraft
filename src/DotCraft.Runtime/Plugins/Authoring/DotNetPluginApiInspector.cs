using System.Globalization;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotCraft.Runtime;

/// <summary>Describes one public symbol available to managed plugins.</summary>
internal sealed record DotNetPluginApiSymbol(
    string AssemblyName,
    string Signature,
    string? Summary);

/// <summary>Queries the public API represented by the current Host reference pack.</summary>
internal sealed class DotNetPluginApiInspector
{
    private const int ResultLimit = 25;

    private static readonly SymbolDisplayFormat QualifiedNameDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly SymbolDisplayFormat TypeDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
            | SymbolDisplayGenericsOptions.IncludeTypeConstraints
            | SymbolDisplayGenericsOptions.IncludeVariance,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly SymbolDisplayFormat MemberDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
            | SymbolDisplayGenericsOptions.IncludeTypeConstraints,
        memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility
            | SymbolDisplayMemberOptions.IncludeModifiers
            | SymbolDisplayMemberOptions.IncludeContainingType
            | SymbolDisplayMemberOptions.IncludeExplicitInterface
            | SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeRef
            | SymbolDisplayMemberOptions.IncludeType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeDefaultValue
            | SymbolDisplayParameterOptions.IncludeExtensionThis
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeOptionalBrackets
            | SymbolDisplayParameterOptions.IncludeParamsRefOut
            | SymbolDisplayParameterOptions.IncludeType,
        propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
        localOptions: SymbolDisplayLocalOptions.IncludeConstantValue
            | SymbolDisplayLocalOptions.IncludeRef
            | SymbolDisplayLocalOptions.IncludeType,
        kindOptions: SymbolDisplayKindOptions.IncludeMemberKeyword,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private readonly CSharpCompilation _compilation;

    public DotNetPluginApiInspector(DotNetPluginReferenceSet references)
    {
        ArgumentNullException.ThrowIfNull(references);
        _compilation = CSharpCompilation.Create(
            "DotCraft.PluginAuthoring.ApiInspection",
            references: references.References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>Finds public types and members whose fully-qualified or simple name matches a query.</summary>
    public IReadOnlyList<DotNetPluginApiSymbol> Inspect(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        query = query.Trim();

        return EnumeratePublicSymbols()
            .Select(symbol => (
                Symbol: symbol,
                FullyQualifiedName: GetFullyQualifiedName(symbol)))
            .Select(candidate => (
                candidate.Symbol,
                candidate.FullyQualifiedName,
                Exact: string.Equals(
                    candidate.FullyQualifiedName,
                    query,
                    StringComparison.OrdinalIgnoreCase)))
            .Where(candidate => candidate.Exact
                || candidate.FullyQualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => (candidate.Exact, Result: CreateResult(candidate.Symbol)))
            .OrderByDescending(static candidate => candidate.Exact)
            .ThenBy(static candidate => candidate.Result.Signature, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Result.AssemblyName, StringComparer.Ordinal)
            .Take(ResultLimit)
            .Select(static candidate => candidate.Result)
            .ToArray();
    }

    private IEnumerable<ISymbol> EnumeratePublicSymbols()
    {
        foreach (var assembly in _compilation.SourceModule.ReferencedAssemblySymbols
                     .Where(static assembly => DotNetPluginReferenceSet.IsPluginApiAssembly(assembly.Identity.Name))
                     .OrderBy(static assembly => assembly.Identity.Name, StringComparer.Ordinal))
        {
            foreach (var type in EnumeratePublicTypes(assembly.GlobalNamespace))
            {
                yield return type;

                foreach (var member in type.GetMembers()
                             .Where(static member => member.DeclaredAccessibility == Accessibility.Public
                                 && !member.IsImplicitlyDeclared
                                 && member is not INamedTypeSymbol)
                             .OrderBy(static member => member.MetadataName, StringComparer.Ordinal))
                {
                    yield return member;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumeratePublicTypes(INamespaceSymbol @namespace)
    {
        foreach (var type in @namespace.GetTypeMembers()
                     .Where(IsPublicType)
                     .OrderBy(static type => type.MetadataName, StringComparer.Ordinal))
        {
            yield return type;

            foreach (var nested in EnumeratePublicNestedTypes(type))
                yield return nested;
        }

        foreach (var child in @namespace.GetNamespaceMembers()
                     .OrderBy(static child => child.Name, StringComparer.Ordinal))
        {
            foreach (var type in EnumeratePublicTypes(child))
                yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumeratePublicNestedTypes(INamedTypeSymbol containingType)
    {
        foreach (var nested in containingType.GetTypeMembers()
                     .Where(IsPublicType)
                     .OrderBy(static type => type.MetadataName, StringComparer.Ordinal))
        {
            yield return nested;

            foreach (var descendant in EnumeratePublicNestedTypes(nested))
                yield return descendant;
        }
    }

    private static bool IsPublicType(INamedTypeSymbol type) =>
        type.DeclaredAccessibility == Accessibility.Public
        && (type.ContainingType is null || IsPublicType(type.ContainingType));

    private static string GetFullyQualifiedName(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol type => type.ToDisplayString(QualifiedNameDisplayFormat),
        _ => $"{symbol.ContainingType.ToDisplayString(QualifiedNameDisplayFormat)}.{symbol.Name}"
    };

    private static DotNetPluginApiSymbol CreateResult(ISymbol symbol)
    {
        var signature = symbol switch
        {
            INamedTypeSymbol type => FormatTypeSignature(type),
            _ => symbol.ToDisplayString(MemberDisplayFormat)
        };

        return new DotNetPluginApiSymbol(
            symbol.ContainingAssembly.Identity.Name,
            signature,
            ReadSummary(symbol));
    }

    private static string FormatTypeSignature(INamedTypeSymbol type)
    {
        var kind = type.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Delegate => "delegate",
            TypeKind.Enum => "enum",
            TypeKind.Interface => "interface",
            TypeKind.Struct => "struct",
            _ => type.TypeKind.ToString().ToLowerInvariant()
        };

        return $"public {kind} {type.ToDisplayString(TypeDisplayFormat)}";
    }

    private static string? ReadSummary(ISymbol symbol)
    {
        var documentation = symbol.GetDocumentationCommentXml(
            CultureInfo.InvariantCulture,
            expandIncludes: true);
        if (string.IsNullOrWhiteSpace(documentation))
            return null;

        var summary = XElement.Parse($"<documentation>{documentation}</documentation>")
            .Element("summary")?
            .Value;
        if (string.IsNullOrWhiteSpace(summary))
            return null;

        return string.Join(' ', summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
