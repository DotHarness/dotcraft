using System.Collections.Immutable;
using DotCraft.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotCraft.Runtime;

internal static class DotNetPluginToolGenerator
{
    // This diagnostic is returned by the runtime authoring compiler, not a shipped analyzer rule.
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor GenerationFailed = new(
        "DotNetPluginToolGenerationFailed",
        "Managed plugin tool generation failed",
        "The Host could not generate the managed plugin's tools",
        "DotCraft.Plugins",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    public static ImmutableArray<Diagnostic> Run(
        CSharpCompilation compilation,
        CSharpParseOptions parseOptions,
        out Compilation outputCompilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ToolFunctionGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out outputCompilation,
            out _);
        return GetDiagnostics(driver.GetRunResult());
    }

    internal static ImmutableArray<Diagnostic> GetDiagnostics(GeneratorDriverRunResult result) =>
        result.Results.Any(static generated => generated.Exception is not null)
            ? [Diagnostic.Create(GenerationFailed, Location.None)]
            : result.Diagnostics;
}
