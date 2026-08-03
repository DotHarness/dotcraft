using System.Collections.Immutable;
using DotCraft.Protocol.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace DotCraft.Protocol.Contracts.Tests;

public sealed class ContractAnalyzerTests
{
    [Theory]
    [InlineData("public string? Value { get; init; }", "DPC003")]
    [InlineData("public long Value { get; init; }", "DPC005")]
    [InlineData("public DotCraft.Protocol.Optional<long?> Value { get; init; }", "DPC005")]
    [InlineData("[DotCraft.Protocol.JsonSafeInteger] public long[] Value { get; init; } = [];", "DPC005")]
    [InlineData("[DotCraft.Protocol.JsonSafeInteger] public int Value { get; init; }", "DPC005")]
    [InlineData("public External.Domain Value { get; init; } = new();", "DPC007")]
    public async Task Analyzer_Rejects_Unsupported_Property_Shapes(string member, string diagnosticId)
    {
        var source = $$"""
            #nullable enable
            namespace External { public sealed class Domain { } }
            namespace DotCraft.Protocol.AppServer.Testing
            {
                public sealed class InvalidContract
                {
                    {{member}}
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public async Task Analyzer_Allows_Explicitly_Bounded_64Bit_Integers()
    {
        const string source = """
            using DotCraft.Protocol;
            namespace DotCraft.Protocol.AppServer.Testing
            {
                public sealed class SafeIntegerContract
                {
                    [JsonSafeInteger]
                    public long Value { get; init; }

                    [JsonSafeInteger]
                    public Optional<long?> OptionalValue { get; init; }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Id == "DPC005");
    }

    [Fact]
    public async Task Analyzer_Requires_A_Discriminator_For_Abstract_Unions()
    {
        const string source = "namespace DotCraft.Protocol.AppServer.Testing { public abstract class InvalidUnion { } }";
        var diagnostics = await AnalyzeAsync(source);
        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == "DPC006");
    }

    [Fact]
    public async Task Analyzer_Rejects_Unsupported_Converters()
    {
        const string source = """
            using System.Text.Json.Serialization;
            namespace DotCraft.Protocol.AppServer.Testing
            {
                [JsonConverter(typeof(JsonStringEnumConverter))]
                public sealed class InvalidContract { }
            }
            """;
        var diagnostics = await AnalyzeAsync(source);
        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == "DPC004");
    }

    [Fact]
    public async Task Analyzer_Requires_Descriptor_Metadata()
    {
        const string source = """
            using DotCraft.Protocol;
            namespace DotCraft.Protocol.AppServer.Testing
            {
                public static class InvalidCatalog
                {
                    public static readonly RpcRequest<RpcEmpty, RpcEmpty> Missing =
                        new("", RpcDirection.ClientToServer, "", "");
                }
            }
            """;
        var diagnostics = await AnalyzeAsync(source);
        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == "DPC008");
    }

    [Fact]
    public async Task Analyzer_Rejects_Invalid_Descriptor_Directions()
    {
        const string source = """
            using DotCraft.Protocol;
            namespace DotCraft.Protocol.AppServer.Testing
            {
                public static class InvalidCatalog
                {
                    public static readonly RpcRequest<RpcEmpty, RpcEmpty> InvalidDirection =
                        new("thread/read", (RpcDirection)42, "1", "spec.md");
                }
            }
            """;
        var diagnostics = await AnalyzeAsync(source);
        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == "DPC002");
    }

    [Fact]
    public void Generator_Rejects_Duplicate_Descriptor_Identities()
    {
        const string source = """
            namespace DotCraft.Protocol
            {
                public enum RpcDirection { ClientToServer, ServerToClient }
                public interface IRpcMethodDescriptor { }
                public sealed class RpcRequest<TParams, TResult>
                {
                    public RpcRequest(string name, RpcDirection direction, string since, string specRef) { }
                }
                public sealed class RpcNotification<TParams>
                {
                    public RpcNotification(string name, RpcDirection direction, string since, string specRef) { }
                }
            }
            namespace DotCraft.Protocol.AppServer
            {
                public sealed class Params { }
                public sealed class Result { }
                public static class AppServerRpc
                {
                    public static readonly RpcRequest<Params, Result> First =
                        new("thread/read", RpcDirection.ClientToServer, "1", "spec.md");
                    public static readonly RpcRequest<Params, Result> Second =
                        new("thread/read", RpcDirection.ClientToServer, "1", "spec.md");
                }
            }
            """;
        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new AppServerRpcCatalogGenerator().AsSourceGenerator());
        driver = driver.RunGenerators(compilation);

        Assert.Contains(
            driver.GetRunResult().Diagnostics,
            static diagnostic => diagnostic.Id == "DPC001");
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ContractShapeAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        trustedAssemblies.Add(MetadataReference.CreateFromFile(typeof(RpcEmpty).Assembly.Location));

        return CSharpCompilation.Create(
            "ContractAnalyzerFixture",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            trustedAssemblies,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}
