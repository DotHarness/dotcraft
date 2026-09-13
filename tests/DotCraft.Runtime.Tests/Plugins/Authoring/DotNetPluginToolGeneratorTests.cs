using DotCraft.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins.Authoring;

public sealed class DotNetPluginToolGeneratorTests
{
    [Fact]
    public void GeneratorException_IsAnErrorWithoutRoslynExceptionDetails()
    {
        var compilation = CSharpCompilation.Create("FaultingGeneratorTest");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new FaultingGenerator().AsSourceGenerator());
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(result.Diagnostics).Severity);

        var diagnostic = Assert.Single(DotNetPluginToolGenerator.GetDiagnostics(result));

        Assert.Equal("DotNetPluginToolGenerationFailed", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(Location.None, diagnostic.Location);
        Assert.DoesNotContain("private-path", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private sealed class FaultingGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterSourceOutput(context.CompilationProvider, static (_, _) =>
                throw new InvalidOperationException("private-path and internal failure details"));
    }
}
