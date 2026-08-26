using System.Reflection;
using System.Text.Json;
using DotCraft.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Compiles real plugin bundles so the runtime tests exercise assembly loading rather than a
/// mock.</summary>
internal static class DotNetPluginTestBundle
{
    /// <summary>A plugin that appends one line per lifecycle step, so a test can assert teardown order.</summary>
    public const string LifecyclePluginSource = """
        using System;
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Plugins;
        namespace Lifecycle;
        public sealed class Plugin : IDotCraftPlugin, IDisposable
        {
            private string _log = "";
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
            {
                _log = Path.Combine(context.DataRoot, "lifecycle.log");
                Directory.CreateDirectory(context.DataRoot);
                File.AppendAllText(_log, "activate\n");
                context.Lifetime.Own(new SyncResource(_log));
                context.Lifetime.OwnAsync(new AsyncResource(_log));
                context.Lifetime.Run(async stopping =>
                {
                    File.AppendAllText(_log, "work-start\n");
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, stopping); }
                    finally { File.AppendAllText(_log, "work-stop\n"); }
                });
                return ValueTask.CompletedTask;
            }
            public void Dispose() => File.AppendAllText(_log, "entry\n");
            private sealed class SyncResource(string log) : IDisposable
            {
                public void Dispose() => File.AppendAllText(log, "sync\n");
            }
            private sealed class AsyncResource(string log) : IAsyncDisposable
            {
                public ValueTask DisposeAsync()
                {
                    File.AppendAllText(log, "async\n");
                    throw new InvalidOperationException("async cleanup exploded");
                }
            }
        }
        """;

    /// <summary>Compiled into every bundle so a test Tool is one <c>override</c> rather than a whole source.</summary>
    private const string ToolHarnessSource = """
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Tools;

        namespace DotCraft.Tests.Bundle
        {
            internal abstract class TestTool(
                string toolId,
                string? toolNamespace,
                string name,
                string description,
                string schema = "{\"type\":\"object\"}",
                ToolPolicyHints? policyHints = null) : IToolSource, IToolRuntime
            {
                public string SourceId { get; } = $"test:{toolId}:{name}";

                public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
                    ToolPlanningContext context,
                    CancellationToken cancellationToken = default)
                {
                    var id = new ToolDefinitionId(ToolSourceKind.PluginNative, SourceId, new SourceToolId(toolId));
                    var definition = new ToolDefinition(
                        id,
                        new ToolName(toolNamespace, name),
                        description,
                        JsonDocument.Parse(schema).RootElement,
                        policyHints: policyHints ?? new ToolPolicyHints());
                    var binding = new ToolRuntimeBinding(
                        new RuntimeBindingId($"{SourceId}:{context.Revision}"),
                        id,
                        this,
                        ToolBindingLeases.AlwaysAvailable,
                        SourceId,
                        context.Revision);
                    return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
                        [new ToolRegistration(definition, binding, ToolProjectionShape.StandardPair)]);
                }

                public abstract ValueTask<ToolExecutionResult> InvokeAsync(
                    ToolInvocationContext context,
                    JsonObject arguments,
                    CancellationToken cancellationToken = default);
            }
        }
        """;

    public static IServiceProvider EmptyServices { get; } = CreateServiceProvider();

    public static IServiceProvider CreateServiceProvider(
        params (Type ServiceType, object Service)[] services) =>
        new TestServiceProvider(services.ToDictionary(
            static entry => entry.ServiceType,
            static entry => entry.Service));

    public static void WritePlugin(
        string pluginRoot,
        string id,
        string entryType,
        string source,
        string? privateReference = null)
    {
        WritePluginBundle(
            pluginRoot,
            id,
            entryType,
            source,
            runtimeReferences: privateReference == null ? [] : [privateReference]);
    }

    public static void WritePluginBundle(
        string pluginRoot,
        string id,
        string entryType,
        string source,
        string version = "1.0.0",
        IReadOnlyDictionary<string, string>? dependencies = null,
        IReadOnlyList<string>? exportedApiAssemblies = null,
        IReadOnlyList<string>? runtimeReferences = null)
    {
        var dotnetRoot = Path.Combine(pluginRoot, "dotnet");
        Directory.CreateDirectory(dotnetRoot);
        var entryPath = Path.Combine(dotnetRoot, "Plugin.dll");
        var runtimeAssemblyPaths = runtimeReferences ?? [];
        Compile(
            entryPath,
            source,
            [typeof(IDotCraftPlugin).Assembly.Location, .. runtimeAssemblyPaths],
            targetFramework: true,
            additionalSource: ToolHarnessSource);
        WriteDependencyManifest(dotnetRoot, runtimeAssemblyPaths);
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        var dependencyJson = dependencies is { Count: > 0 }
            ? ",\n  \"dependencies\": { " + string.Join(", ", dependencies.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => $"\"{pair.Key}\": \"{pair.Value}\"")) + " }"
            : string.Empty;
        var exportsJson = string.Join(", ", (exportedApiAssemblies ?? [])
            .Select(static path => $"\"{path}\""));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "version": "{{version}}",
              "displayName": "{{id}}",
              "capabilities": ["dotnet"],
              "dotnet": {
                "minHostVersion": "0.0.0",
                "entryAssembly": "./dotnet/Plugin.dll",
                "entryType": "{{entryType}}",
                "exportedApiAssemblies": [{{exportsJson}}]
              }{{dependencyJson}}
            }
            """);
    }

    public static void Compile(
        string outputPath,
        string source,
        string[]? references = null,
        bool targetFramework = false,
        string? additionalSource = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var explicitReferences = references ?? [];
        var explicitFileNames = explicitReferences
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var metadataReferences = TrustedPlatformAssemblies()
            .Where(path => !explicitFileNames.Contains(Path.GetFileName(path)))
            .Concat(explicitReferences)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var syntaxTrees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))
        };
        if (additionalSource != null)
        {
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                additionalSource,
                new CSharpParseOptions(LanguageVersion.Preview)));
        }
        if (targetFramework)
        {
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                "[assembly: System.Runtime.Versioning.TargetFramework(\".NETCoreApp,Version=v10.0\")]",
                new CSharpParseOptions(LanguageVersion.Preview)));
        }
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(outputPath),
            syntaxTrees,
            metadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(outputPath);
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    /// <summary>Copies a Host assembly into a bundle so the load context can prefer the Host's copy.</summary>
    public static string CopyHostAssemblyIntoBundle(string pluginRoot, Assembly assembly)
    {
        var dotnetRoot = Path.Combine(pluginRoot, "dotnet");
        Directory.CreateDirectory(dotnetRoot);
        var destination = Path.Combine(dotnetRoot, Path.GetFileName(assembly.Location));
        File.Copy(assembly.Location, destination, overwrite: true);
        return destination;
    }

    private static IEnumerable<string> TrustedPlatformAssemblies() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

    private static void WriteDependencyManifest(
        string dotnetRoot,
        IReadOnlyList<string> runtimeAssemblyPaths)
    {
        var pluginApiAssembly = typeof(IDotCraftPlugin).Assembly.GetName();
        var pluginApiName = pluginApiAssembly.Name!;
        var pluginApiVersion = pluginApiAssembly.Version?.ToString(3) ?? "1.0.0";
        var pluginDependencies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [pluginApiName] = pluginApiVersion
        };
        var targets = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Plugin/1.0.0"] = new
            {
                dependencies = pluginDependencies,
                runtime = new Dictionary<string, object> { ["Plugin.dll"] = new { } }
            },
            [$"{pluginApiName}/{pluginApiVersion}"] = new
            {
                runtime = new Dictionary<string, object> { [$"{pluginApiName}.dll"] = new { } }
            }
        };
        var libraries = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Plugin/1.0.0"] = new { type = "project", serviceable = false, sha512 = "" },
            [$"{pluginApiName}/{pluginApiVersion}"] = new { type = "project", serviceable = false, sha512 = "" }
        };
        foreach (var runtimeAssemblyPath in runtimeAssemblyPaths)
        {
            var identity = AssemblyName.GetAssemblyName(runtimeAssemblyPath);
            var simpleName = identity.Name!;
            var version = identity.Version?.ToString(3) ?? "1.0.0";
            pluginDependencies[simpleName] = version;
            targets[$"{simpleName}/{version}"] = new
            {
                runtime = new Dictionary<string, object> { [Path.GetFileName(runtimeAssemblyPath)] = new { } }
            };
            libraries[$"{simpleName}/{version}"] = new { type = "project", serviceable = false, sha512 = "" };
        }
        var document = new
        {
            runtimeTarget = new { name = ".NETCoreApp,Version=v10.0", signature = "" },
            compilationOptions = new { },
            targets = new Dictionary<string, object>
            {
                [".NETCoreApp,Version=v10.0"] = targets
            },
            libraries
        };
        File.WriteAllText(
            Path.Combine(dotnetRoot, "Plugin.deps.json"),
            JsonSerializer.Serialize(document));
    }

    private sealed class TestServiceProvider(IReadOnlyDictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            services.TryGetValue(serviceType, out var service) ? service : null;
    }
}
