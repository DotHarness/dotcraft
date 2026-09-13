using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Plugins;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Harness.PluginFixture;

public sealed class Plugin : IDotCraftPlugin
{
    public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
    {
        context.Contributions.Add<IToolSource>(new SmokeSource(context.Plugin.Id));
        return ValueTask.CompletedTask;
    }
}

internal sealed class SmokeSource(string pluginId) : AIFunctionToolSource
{
    private readonly SmokeTools _tools = new();

    public override string SourceId => "package-fixture";

    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context) =>
    [
        GeneratedTools.Harness.PluginFixture.GeneratedToolFunctions.SmokeTools_Echo(_tools),
        GeneratedTools.Harness.PluginFixture.GeneratedToolFunctions.SmokeTools_Envelope(_tools)
    ];

    protected override string? GetNamespace(AIFunction function, ToolPlanningContext context) =>
        pluginId == "package.binary" ? "PackageBinary" : "PackageSource";

    protected override ToolPolicyHints GetPolicyHints(AIFunction function, ToolPlanningContext context) =>
        new(ReadOnly: true);

    protected override ToolPresentationDescriptor? GetPresentation(AIFunction function, ToolPlanningContext context) => null;
}

internal sealed class SmokeTools
{
    [GeneratedTool(Name = "Echo")]
    [Description("Echo typed package-test input with live invocation identity.")]
    public async ValueTask<SmokeResponse> Echo(
        [Description("Input text and transformation.")] SmokeRequest request,
        ToolInvocationContext invocation,
        [Description("Number of repetitions.")] int repeat = 2,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        var value = request.Mode == SmokeMode.UpperCase ? request.Value.ToUpperInvariant() : request.Value;
        return new SmokeResponse(
            string.Concat(Enumerable.Repeat(value, repeat)),
            invocation.ThreadId,
            invocation.TurnId,
            invocation.CallId,
            invocation.WorkspacePath);
    }

    [GeneratedTool(Name = "Envelope")]
    [Description("Return an explicit uncertain outcome for the package smoke test.")]
    public ToolExecutionResult Envelope(ToolInvocationContext invocation) => new(
        success: false,
        content: "package-outcome-unknown:" + invocation.ThreadId,
        structuredContent: JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["state"] = "unknown" }),
        meta: JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["secret"] = "host-private-marker" }),
        error: new ToolError("package_outcome_unknown", "The external outcome is unknown."),
        directive: ToolExecutionDirective.TerminateTurn);
}

internal enum SmokeMode { Unchanged, UpperCase }

internal sealed record SmokeRequest(string Value, SmokeMode Mode);

internal sealed record SmokeResponse(string Value, string ThreadId, string? TurnId, string CallId, string? WorkspacePath);
