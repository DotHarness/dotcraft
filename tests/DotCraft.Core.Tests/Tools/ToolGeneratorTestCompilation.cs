using System.Reflection;
using System.Runtime.Loader;
using DotCraft.Generators;
using DotCraft.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Tools;

internal static class ToolGeneratorTestCompilation
{
    public static GeneratorRunResult Run(string source, string assemblyName, out Compilation outputCompilation)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source, new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Diagnose));
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(ToolAttribute).Assembly.Location)
            .Append(typeof(AIFunction).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            assemblyName, [syntaxTree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ToolFunctionGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)syntaxTree.Options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out outputCompilation, out _);
        return Assert.Single(driver.GetRunResult().Results);
    }

    public static Assembly Compile(string source)
    {
        var generated = Run(source, $"TypedToolFixture_{Guid.NewGuid():N}", out var compilation);
        Assert.Empty(generated.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }

    public static AIFunction Function(Assembly assembly, string factoryName, object? target = null)
    {
        var factories = assembly.GetType($"DotCraft.GeneratedTools.{assembly.GetName().Name}.GeneratedToolFunctions")!;
        return Assert.IsAssignableFrom<AIFunction>(factories.GetMethod(factoryName)!
            .Invoke(null, target is null ? [] : [target]));
    }
}
