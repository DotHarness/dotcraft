using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Generators;
using DotCraft.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class ToolFunctionGeneratorTests
{
    [Fact]
    public async Task Generator_emits_reusable_declarations_and_extended_schema_metadata()
    {
        const string source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using System.Text.Json;
            using System.Text.Json.Nodes;
            using System.Text.Json.Serialization;
            using DotCraft.Tools;

            namespace GeneratorFixture;

            internal enum FixtureMode
            {
                [JsonStringEnumMemberName("inline")]
                Inline,
                [JsonStringEnumMemberName("saved")]
                Saved
            }

            internal sealed class NestedInput
            {
                [JsonPropertyName("display_name")]
                [Required]
                [MinLength(2)]
                [Description("Display name.")]
                public string? Name { get; init; }

                [JsonIgnore]
                public string? Hidden { get; init; }
            }

            internal interface IFixtureDeclaration
            {
                [ToolDeclaration(Name = "schema_test")]
                [ToolSchema(DisallowAdditionalProperties = true)]
                [Description("Schema-only declaration.")]
                void Run(
                    [ToolParameter(Name = "mode_name")]
                    [Description("Execution mode.")] FixtureMode mode,
                    [Range(0, int.MaxValue)]
                    [Description("Non-negative count.")] int count = 0,
                    [MinLength(2)]
                    [MaxLength(10)]
                    [RegularExpression("^x")]
                    [Description("Constrained text.")] string text = "",
                    [Description("Arbitrary JSON payload.")] JsonNode? payload = null,
                    [Description("Arbitrary JSON element.")] JsonElement? element = null,
                    [Description("Object payload.")] JsonObject? obj = null,
                    [Description("Array payload.")] JsonArray? array = null,
                    [Description("Nested input.")] NestedInput? nested = null);
            }

            internal sealed class FixtureExecutable
            {
                [GeneratedTool(Name = "exec_alias")]
                [ToolRpc]
                [Description("Executable fixture.")]
                public string Go(
                    [ToolParameter(Name = "input_value")]
                    [Description("Input value.")] string value,
                    [Description("Default execution mode.")] FixtureMode mode = FixtureMode.Inline) => value;
            }

            internal static class FixtureToolAttribute
            {
                [Tool(Name = "tool_alias", CatalogVisible = false)]
                [Description("Tool attribute fixture.")]
                public static string Run([Description("Input value.")] string value) => value;
            }
            """;

        var assemblyName = $"GeneratorFixture_{Guid.NewGuid():N}";
        var result = RunGenerator(source, assemblyName, out var outputCompilation);
        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        await using var stream = new MemoryStream();
        var emit = outputCompilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        var generatedNamespace = $"DotCraft.GeneratedTools.{assemblyName}";
        var declarations = assembly.GetType($"{generatedNamespace}.GeneratedToolDeclarations");
        Assert.NotNull(declarations);
        var declaration = Assert.IsType<GeneratedToolDeclaration>(
            declarations.GetProperty("IFixtureDeclaration_Run_Declaration", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));

        Assert.Equal("schema_test", declaration.Name);
        Assert.Equal("Schema-only declaration.", declaration.Description);
        Assert.Null(declaration.OutputSchema);
        Assert.False(declaration.RpcEligible);
        var schema = JsonNode.Parse(declaration.InputSchema.GetRawText())!.AsObject();
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(["mode_name"], schema["required"]!.AsArray().Select(static value => value!.GetValue<string>()));
        var properties = schema["properties"]!.AsObject();
        Assert.Equal(["inline", "saved"], properties["mode_name"]!["enum"]!.AsArray().Select(static value => value!.GetValue<string>()));
        Assert.Equal(0, properties["count"]!["minimum"]!.GetValue<int>());
        Assert.Null(properties["count"]!["maximum"]);
        Assert.Equal(2, properties["text"]!["minLength"]!.GetValue<int>());
        Assert.Equal(10, properties["text"]!["maxLength"]!.GetValue<int>());
        Assert.Equal("^x", properties["text"]!["pattern"]!.GetValue<string>());
        Assert.Null(properties["payload"]!["type"]);
        Assert.Null(properties["element"]!["type"]);
        Assert.Equal("object", properties["obj"]!["type"]!.GetValue<string>());
        Assert.Equal("array", properties["array"]!["type"]!.GetValue<string>());
        var nested = properties["nested"]!["properties"]!.AsObject();
        Assert.True(nested.ContainsKey("display_name"));
        Assert.False(nested.ContainsKey("hidden"));
        Assert.Equal(["display_name"], properties["nested"]!["required"]!.AsArray().Select(static value => value!.GetValue<string>()));

        var functions = assembly.GetType($"{generatedNamespace}.GeneratedToolFunctions");
        Assert.NotNull(functions);
        Assert.Null(functions.GetMethod("IFixtureDeclaration_Run", BindingFlags.Public | BindingFlags.Static));
        var targetType = assembly.GetType("GeneratorFixture.FixtureExecutable");
        Assert.NotNull(targetType);
        var target = Activator.CreateInstance(targetType, nonPublic: true);
        var function = Assert.IsAssignableFrom<AIFunction>(
            functions.GetMethod("FixtureExecutable_Go", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [target]));
        Assert.Equal("exec_alias", function.Name);
        var catalog = assembly.GetType($"{generatedNamespace}.GeneratedToolCatalog");
        Assert.NotNull(catalog);
        var descriptors = Assert.IsAssignableFrom<IReadOnlyList<GeneratedToolDescriptor>>(
            catalog.GetProperty("Descriptors", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        Assert.True(Assert.Single(descriptors, descriptor => descriptor.Name == "exec_alias").RpcEligible);
        var functionProperties = function.JsonSchema.GetProperty("properties");
        Assert.True(functionProperties.TryGetProperty("input_value", out _));
        Assert.Equal("inline", functionProperties.GetProperty("mode").GetProperty("default").GetString());
        var invocation = await function.InvokeAsync(new AIFunctionArguments { ["input_value"] = "ok" });
        Assert.Equal("ok", ((System.Text.Json.JsonElement)invocation!).GetString());

        var toolAttributeFunction = Assert.IsAssignableFrom<AIFunction>(
            functions.GetMethod("FixtureToolAttribute_Run", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null));
        Assert.Equal("tool_alias", toolAttributeFunction.Name);
    }

    [Fact]
    public void Generator_reports_invalid_declarations_names_constraints_and_enum_values()
    {
        const string source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using System.Text.Json.Serialization;
            using DotCraft.Tools;

            namespace InvalidFixture;

            internal enum DuplicateMode
            {
                [JsonStringEnumMemberName("same")] First,
                [JsonStringEnumMemberName("same")] Second
            }

            internal sealed class InvalidDeclarations
            {
                [ToolDeclaration(Name = "")]
                [Description("Invalid declaration.")]
                public void NotAbstract(
                    [ToolParameter(Name = "duplicate")]
                    [Range(0, 1)]
                    [Description("Invalid range target.")] string first,
                    [ToolParameter(Name = "duplicate")]
                    [Description("Duplicate name.")] string second,
                    [Description("Duplicate enum values.")] DuplicateMode mode,
                    [Range(10, 1)]
                    [Description("Conflicting range.")] int range,
                    [MinLength(3)]
                    [MaxLength(2)]
                    [Description("Conflicting length.")] string length,
                    [RegularExpression("[")]
                    [Description("Invalid regular expression.")] string pattern)
                {
                }
            }

            internal interface IDuplicateNames
            {
                [ToolDeclaration(Name = "same_tool")]
                [Description("First declaration.")]
                void First([Description("Input value.")] string value);

                [ToolDeclaration(Name = "same_tool")]
                [Description("Second declaration.")]
                void Second([Description("Input value.")] string value);
            }

            internal sealed class InvalidRpcTool
            {
                [ToolRpc]
                public void Run() { }
            }
            """;

        var result = RunGenerator(source, $"InvalidFixture_{Guid.NewGuid():N}", out _);
        var ids = result.Diagnostics.Select(static diagnostic => diagnostic.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("DCGEN005", ids);
        Assert.Contains("DCGEN006", ids);
        Assert.Contains("DCGEN007", ids);
        Assert.Contains("DCGEN008", ids);
        Assert.Contains("DCGEN009", ids);
        Assert.Contains("DCGEN010", ids);
        Assert.Contains("DCGEN011", ids);
        var constraintMessages = result.Diagnostics
            .Where(static diagnostic => diagnostic.Id == "DCGEN007")
            .Select(static diagnostic => diagnostic.GetMessage())
            .ToArray();
        Assert.Contains(constraintMessages, static message => message.Contains("minimum cannot exceed maximum", StringComparison.Ordinal));
        Assert.Contains(constraintMessages, static message => message.Contains("cannot exceed MaxLength", StringComparison.Ordinal));
        Assert.Contains(constraintMessages, static message => message.Contains("invalid pattern", StringComparison.Ordinal));
    }

    private static GeneratorRunResult RunGenerator(
        string source,
        string assemblyName,
        out Compilation outputCompilation)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Diagnose));
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(ToolAttribute).Assembly.Location)
            .Append(typeof(AIFunction).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ToolFunctionGenerator().AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)syntaxTree.Options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out outputCompilation, out _);
        return Assert.Single(driver.GetRunResult().Results);
    }
}
