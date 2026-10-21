using System.Text.Json.Nodes;
using DotCraft.Security;
using DotCraft.Tools;

namespace DotCraft.Memory.Extraction;

/// <summary>Lets a memory extraction fork read files and write Markdown files in its store, refusing every other call at dispatch.</summary>
internal sealed class MemoryExtractionToolPolicy(string storeDirectory) : IToolPolicyEvaluator
{
    private static readonly ToolName CodeModeToolName = new(null, "CodeMode");
    private static readonly ToolName CodeModeWaitToolName = new(null, "CodeModeWait");

    public ValueTask<ToolDispatchDecision> EvaluateAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Evaluate(context, registration, arguments));

    private ToolDispatchDecision Evaluate(ToolInvocationContext context, ToolRegistration registration, JsonObject arguments)
    {
        var definition = registration.Definition;
        if (definition.Id.Kind != ToolSourceKind.CoreNative)
            return Refuse();
        // Code mode dispatches each call its program makes through this policy.
        if (definition.Name == CodeModeToolName || definition.Name == CodeModeWaitToolName || EffectiveToolSnapshot.IsDeferredToolSearch(registration))
            return ToolDispatchDecision.Allow;
        if (!FileToolAccess.TryResolveTarget(definition.Id.SourceToolId.Value, arguments, context.WorkspacePath, out var access, out var target))
            return Refuse();
        if (access.Operation == "read"
            || context.ExecutionLocation is not { Target: "remote" }
            && string.Equals(Path.GetExtension(target), ".md", StringComparison.OrdinalIgnoreCase)
            && WorkspaceBoundary.IsWithin(target, storeDirectory))
        {
            return ToolDispatchDecision.Allow;
        }

        return ToolDispatchDecision.Deny(
            ToolErrorCodes.Unauthorized,
            $"Memory extraction can only write Markdown files inside {storeDirectory}.");
    }

    private static ToolDispatchDecision Refuse() =>
        ToolDispatchDecision.Deny(
            ToolErrorCodes.Unauthorized,
            "Memory extraction can only read files and write memory files.");
}
