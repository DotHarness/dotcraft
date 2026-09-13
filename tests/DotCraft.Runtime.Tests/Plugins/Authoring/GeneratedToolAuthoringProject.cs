namespace DotCraft.Tests.Runtime.Plugins.Authoring;

internal sealed class GeneratedToolAuthoringProject(string dataRoot)
{
    public const string PluginId = "acme.generated-authoring";

    public string Root { get; } = Path.Combine(dataRoot, "plugin-projects", PluginId);

    public string SourcePath => Path.Combine(Root, "src", "Plugin.cs");

    public string BundleRoot => Path.Combine(Root, "plugin");

    public void Write(string? source = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SourcePath)!);
        Directory.CreateDirectory(Path.Combine(BundleRoot, ".craft-plugin"));
        File.WriteAllText(SourcePath, source ?? Source);
        File.WriteAllText(
            Path.Combine(BundleRoot, ".craft-plugin", "plugin.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{PluginId}}",
              "version": "1.0.0",
              "displayName": "Generated authoring test",
              "capabilities": ["dotnet"],
              "dotnet": {
                "minHostVersion": "0.0.0",
                "entryAssembly": "./lib/Acme.Plugin.dll",
                "entryType": "Acme.Plugin"
              }
            }
            """);
    }

    public const string Source = """
        using System.Collections.Generic;
        using System.ComponentModel;
        using System.ComponentModel.DataAnnotations;
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Plugins;
        using DotCraft.Tools;
        using Microsoft.Extensions.AI;

        namespace Acme;

        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
            {
                context.Contributions.Add<IToolSource>(new Source());
                return ValueTask.CompletedTask;
            }
        }

        internal sealed class Source : AIFunctionToolSource
        {
            private readonly ToolMethods _methods = new();

            public override string SourceId => "authored-tools";

            protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context) =>
            [
                DotCraft.GeneratedTools.Acme.Plugin.GeneratedToolFunctions.ToolMethods_Execute(_methods),
                DotCraft.GeneratedTools.Acme.Plugin.GeneratedToolFunctions.ToolMethods_Describe(_methods)
            ];

            protected override string? GetNamespace(AIFunction function, ToolPlanningContext context) => "authored";

            protected override ToolPresentationDescriptor? GetPresentation(AIFunction function, ToolPlanningContext context) => null;
        }

        internal enum ExecutionMode { Inline, Background }

        internal sealed class ToolInput
        {
            [Required]
            public string Text { get; init; } = "";
            public ExecutionMode Mode { get; init; }
        }

        internal sealed class ToolOutput
        {
            public string Text { get; init; } = "";
        }

        internal sealed class ToolMethods
        {
            [GeneratedTool(Name = "execute")]
            [Description("Execute an authored tool.")]
            public async ValueTask<ToolExecutionResult> Execute(
                ToolInvocationContext context,
                [Description("Typed tool input.")] ToolInput input,
                [Description("Maximum wait interval.")] int yieldTimeMs = 1000,
                CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                if (input.Text == "unknown")
                {
                    return ToolExecutionResult.Failed(
                        new ToolError("authored_outcome_unknown", "Execution may have completed.",
                            new Dictionary<string, JsonElement>
                            {
                                ["executionId"] = JsonSerializer.SerializeToElement(context.CallId)
                            }),
                        "Do not repeat the operation.");
                }

                return ToolExecutionResult.Succeeded(input.Text, JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["threadId"] = context.ThreadId,
                    ["turnId"] = context.TurnId,
                    ["callId"] = context.CallId,
                    ["workspace"] = context.WorkspacePath,
                    ["mode"] = input.Mode.ToString(),
                    ["yieldTimeMs"] = yieldTimeMs,
                    ["tokenCanBeCanceled"] = cancellationToken.CanBeCanceled
                }));
            }

            [GeneratedTool(Name = "describe")]
            [Description("Return a typed tool result.")]
            public ToolOutput Describe([Description("Typed tool input.")] ToolInput input) =>
                new() { Text = input.Text };
        }
        """;
}
