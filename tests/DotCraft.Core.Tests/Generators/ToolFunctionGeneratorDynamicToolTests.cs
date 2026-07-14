using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using DotCraft.AppBinding;
using DotCraft.Gen;
using DotCraft.Protocol.AppServer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotCraft.Tests.Generators;

public sealed class ToolFunctionGeneratorDynamicToolTests
{
    [Fact]
    public void DynamicToolGenerator_SupportsReflectionFallbackParameterMatrix()
    {
        var result = RunGenerator("""
            #nullable enable
            using System.Collections.Generic;
            using System.ComponentModel;
            using System.Text.Json.Nodes;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.AppBinding;
            using DotCraft.Protocol.AppServer;

            namespace GeneratorTests;

            public sealed partial class DynamicHost
            {
                [DynamicTool("AllTypes", Order = 10, DeferLoading = true)]
                [Description("Accept every supported dynamic tool parameter type.")]
                private ValueTask<AppBoundToolCallResult> AllTypes(
                    ManagedAppBindingToolCallContext context,
                    CancellationToken cancellationToken,
                    [Description("Required text.")] string text,
                    [Description("Required flag.")] bool requiredBool,
                    [Description("Required short.")] short requiredShort,
                    [Description("Required int.")] int requiredInt,
                    [Description("Required long.")] long requiredLong,
                    [Description("Required float.")] float requiredFloat,
                    [Description("Required double.")] double requiredDouble,
                    [Description("Required decimal.")] decimal requiredDecimal,
                    [Description("Required metadata.")] JsonObject metadata,
                    [Description("Required array.")] string[] requiredArray,
                    [Description("Required list.")] List<string> requiredList,
                    [Description("Required read-only list.")] IReadOnlyList<string> requiredReadOnly,
                    [Description("Required enumerable.")] IEnumerable<string> requiredEnumerable,
                    [Description("Optional text.")] string? optionalText = null,
                    [Description("Optional flag.")] bool? optionalFlag = null,
                    [Description("Defaulted flag.")] bool defaultedBool = true,
                    [Description("Optional short.")] short? optionalShort = null,
                    [Description("Defaulted short.")] short defaultedShort = 7,
                    [Description("Optional int.")] int? optionalInt = null,
                    [Description("Defaulted int.")] int defaultedInt = 42,
                    [Description("Optional long.")] long? optionalLong = null,
                    [Description("Defaulted long.")] long defaultedLong = 42L,
                    [Description("Optional float.")] float? optionalFloat = null,
                    [Description("Defaulted float.")] float defaultedFloat = 1.5F,
                    [Description("Optional double.")] double? optionalDouble = null,
                    [Description("Defaulted double.")] double defaultedDouble = 2.5D,
                    [Description("Optional decimal.")] decimal? optionalDecimal = null,
                    [Description("Defaulted decimal.")] decimal defaultedDecimal = 3.5M,
                    [Description("Optional metadata.")] JsonObject? optionalMetadata = null,
                    [Description("Optional array.")] string[]? optionalArray = null,
                    [Description("Optional list.")] List<string>? optionalList = null,
                    [Description("Optional read-only list.")] IReadOnlyList<string>? optionalReadOnly = null,
                    [Description("Optional enumerable.")] IEnumerable<string>? optionalEnumerable = null)
                {
                    _ = context;
                    _ = cancellationToken;
                    _ = text;
                    _ = requiredBool;
                    _ = requiredShort;
                    _ = requiredInt;
                    _ = requiredLong;
                    _ = requiredFloat;
                    _ = requiredDouble;
                    _ = requiredDecimal;
                    _ = metadata;
                    _ = requiredArray;
                    _ = requiredList;
                    _ = requiredReadOnly;
                    _ = requiredEnumerable;
                    _ = optionalText;
                    _ = optionalFlag;
                    _ = defaultedBool;
                    _ = optionalShort;
                    _ = defaultedShort;
                    _ = optionalInt;
                    _ = defaultedInt;
                    _ = optionalLong;
                    _ = defaultedLong;
                    _ = optionalFloat;
                    _ = defaultedFloat;
                    _ = optionalDouble;
                    _ = defaultedDouble;
                    _ = optionalDecimal;
                    _ = defaultedDecimal;
                    _ = optionalMetadata;
                    _ = optionalArray;
                    _ = optionalList;
                    _ = optionalReadOnly;
                    _ = optionalEnumerable;
                    return ValueTask.FromResult(new AppBoundToolCallResult { Success = true });
                }
            }
            """);

        Assert.Empty(result.GeneratorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.CompilationDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains("BindRequiredShort", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("BindOptionalDecimal", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("BindRequiredJsonObject", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("BindRequiredStringList", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DynamicToolGenerator_ReportsDuplicateNameAndOrderDiagnostics()
    {
        var result = RunGenerator("""
            using System.ComponentModel;
            using DotCraft.AppBinding;
            using DotCraft.Protocol.AppServer;

            namespace GeneratorTests;

            public sealed partial class DuplicateDynamicHost
            {
                [DynamicTool("Same", Order = 1)]
                [Description("First duplicate.")]
                private AppBoundToolCallResult First() => new() { Success = true };

                [DynamicTool("Same", Order = 2)]
                [Description("Second duplicate.")]
                private AppBoundToolCallResult Second() => new() { Success = true };

                [DynamicTool("Other", Order = 2)]
                [Description("Duplicate order.")]
                private AppBoundToolCallResult Third() => new() { Success = true };
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DCGEN005");
        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DCGEN006");
    }

    [Fact]
    public void DynamicToolGenerator_ReportsUnsupportedParameterAndReturnDiagnostics()
    {
        var result = RunGenerator("""
            using System;
            using System.ComponentModel;
            using DotCraft.AppBinding;

            namespace GeneratorTests;

            public sealed partial class UnsupportedDynamicHost
            {
                [DynamicTool("Bad", Order = 1)]
                [Description("Unsupported dynamic tool.")]
                private string Bad([Description("Unsupported value.")] DateTime value) => "bad";
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DCGEN003");
        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DCGEN007");
    }

    private static GeneratorTestResult RunGenerator(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => assembly.Location)
            .Concat([
                typeof(object).Assembly.Location,
                typeof(DescriptionAttribute).Assembly.Location,
                typeof(JsonObject).Assembly.Location,
                typeof(Task).Assembly.Location,
                typeof(DynamicToolAttribute).Assembly.Location,
                typeof(AppBoundToolCallResult).Assembly.Location
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();
        var compilation = CSharpCompilation.Create(
            "GeneratorDynamicToolTests",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ToolFunctionGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);
        var runResult = driver.GetRunResult();
        var generatedSource = string.Join(
            "\n",
            runResult.GeneratedTrees.Select(tree => tree.GetText().ToString()));
        var compilationDiagnostics = outputCompilation.GetDiagnostics();
        return new GeneratorTestResult(
            generatorDiagnostics,
            compilationDiagnostics,
            generatedSource);
    }

    private sealed record GeneratorTestResult(
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<Diagnostic> CompilationDiagnostics,
        string GeneratedSource);
}
