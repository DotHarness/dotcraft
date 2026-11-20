using System.Text.Json.Nodes;
using DotCraft.Security;
using DotCraft.Tools;

namespace DotCraft.Memory.Extraction;

internal sealed class MemoryExtractionToolPolicy(params IReadOnlyList<string> storeDirectories) : IToolPolicyEvaluator
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
            && storeDirectories.Any(directory => WorkspaceBoundary.IsWithin(target, directory)))
        {
            return ToolDispatchDecision.Allow;
        }

        return ToolDispatchDecision.Deny(
            ToolErrorCodes.Unauthorized,
            $"Memory extraction can only write Markdown files inside {string.Join(" or ", storeDirectories)}.");
    }

    private static ToolDispatchDecision Refuse() =>
        ToolDispatchDecision.Deny(
            ToolErrorCodes.Unauthorized,
            "Memory extraction can only read files and write memory files.");
}
