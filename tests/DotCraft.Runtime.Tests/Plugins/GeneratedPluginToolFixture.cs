using DotCraft.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

internal static class GeneratedPluginToolFixture
{
    public const string PluginId = "generated.tools";

    public static Compilation GenerateTools(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ToolFunctionGenerator().AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.Empty(diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return output;
    }

    public const string Source = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.ComponentModel;
        using System.ComponentModel.DataAnnotations;
        using System.IO;
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Plugins;
        using DotCraft.Tools;
        using Microsoft.Extensions.AI;
        using Factories = DotCraft.GeneratedTools.Plugin.GeneratedToolFunctions;

        namespace GeneratedPlugin;

        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
            {
                Directory.CreateDirectory(context.DataRoot);
                var service = new ProbeService(context.DataRoot);
                context.Lifetime.Own(service);
                context.Contributions.Add<IToolSource>(new ProbeTools(service));
                return ValueTask.CompletedTask;
            }
        }

        internal enum ExecutionMode { Inline, Background }

        internal sealed class Detail
        {
            [Description("Nested label.")]
            public string Label { get; init; } = "";
        }

        internal sealed class Payload
        {
            [Description("Request title.")]
            public string Title { get; init; } = "";

            [Description("Nested request detail.")]
            public Detail Detail { get; init; } = new();
        }

        internal sealed class ProbeService(string dataRoot) : IDisposable
        {
            private int _calls;
            public int NextCall() => Interlocked.Increment(ref _calls);
            public void Started(string callId) => File.WriteAllText(Path.Combine(dataRoot, "started-" + callId), "yes");
            public bool Released => File.Exists(Path.Combine(dataRoot, "release"));
            public void Dispose() => File.WriteAllText(Path.Combine(dataRoot, "disposed"), "yes");
        }

        internal sealed class ProbeTools(ProbeService service) : AIFunctionToolSource
        {
            public override string SourceId => "plugin-supplied-source";

            protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
            {
                yield return Factories.ProbeTools_Exercise(this);
                yield return Factories.ProbeTools_DomainError(this);
                yield return Factories.ProbeTools_Wait(this);
                yield return Factories.ProbeTools_Describe(this);
                yield return Factories.ProbeTools_Hold(this);
            }

            protected override string? GetNamespace(AIFunction function, ToolPlanningContext context) => "probe";
            protected override ToolPresentationDescriptor? GetPresentation(AIFunction function, ToolPlanningContext context) => null;
            protected override ToolPolicyHints GetPolicyHints(AIFunction function, ToolPlanningContext context) => new(ReadOnly: true);

            [GeneratedTool(Name = "exercise")]
            [Description("Returns typed arguments and the live invocation identity.")]
            public Task<ToolExecutionResult> Exercise(
                ToolInvocationContext context,
                [Description("Typed request payload.")] Payload input,
                [Description("Execution mode.")] ExecutionMode mode = ExecutionMode.Inline,
                [Description("Open application JSON.")] JsonObject? extra = null,
                [Range(0, 30000)] [Description("Polling interval.")] int yieldTimeMs = 1000)
            {
                var output = new JsonObject
                {
                    ["threadId"] = context.ThreadId,
                    ["turnId"] = context.TurnId,
                    ["callId"] = context.CallId,
                    ["workspace"] = context.WorkspacePath,
                    ["sourceKind"] = context.DefinitionId.Kind.ToString(),
                    ["sourceId"] = context.DefinitionId.SourceId,
                    ["revision"] = context.SnapshotRevision,
                    ["title"] = input.Title,
                    ["label"] = input.Detail.Label,
                    ["mode"] = mode.ToString(),
                    ["extra"] = extra?.DeepClone(),
                    ["yieldTimeMs"] = yieldTimeMs,
                    ["calls"] = service.NextCall()
                };
                var hidden = JsonSerializer.SerializeToElement(new JsonObject { ["private"] = true });
                return Task.FromResult(ToolExecutionResult.Succeeded(
                    "typed call completed",
                    JsonSerializer.SerializeToElement(output),
                    meta: hidden,
                    rawSourceResult: hidden,
                    providerResult: input,
                    contentItems: [new TextContent("private-copy-boundary-probe")],
                    directive: ToolExecutionDirective.TerminateTurn));
            }

            [GeneratedTool(Name = "domain_error")]
            [Description("Reports a stable domain error.")]
            public ToolExecutionResult DomainError() => ToolExecutionResult.Failed(
                new ToolError("ProbeTargetMissing", "The selected target is missing."),
                "Reconnect the selected target.");

            [GeneratedTool(Name = "wait")]
            [Description("Waits until cancellation and optionally reports an uncertain outcome.")]
            public async ValueTask<ToolExecutionResult> Wait(
                ToolInvocationContext context,
                [Description("Report uncertainty when waiting is interrupted.")] bool uncertain = false,
                CancellationToken cancellationToken = default)
            {
                service.Started(context.CallId);
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return ToolExecutionResult.Succeeded("finished");
                }
                catch (OperationCanceledException) when (uncertain)
                {
                    return ToolExecutionResult.Failed(
                        new ToolError("ProbeOutcomeUnknown", "Execution may have started; do not replay."),
                        "Execution may have started; do not replay.");
                }
            }

            [GeneratedTool(Name = "describe")]
            [Description("Returns a plugin-defined typed result.")]
            public Payload Describe() => new() { Title = "output", Detail = new() { Label = "nested output" } };

            [GeneratedTool(Name = "hold")]
            [Description("Keeps the generation active until its service is released.")]
            public async Task<string> Hold(ToolInvocationContext context, CancellationToken cancellationToken = default)
            {
                service.Started(context.CallId);
                while (!service.Released)
                    await Task.Delay(10, cancellationToken);
                return "released";
            }
        }
        """;
}
