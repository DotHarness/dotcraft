using DotCraft.GeneratedTools.Core;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>Contributes the default-on asynchronous coordination and clock tools.</summary>
public sealed class UserCoordinationToolSource : AIFunctionToolSource
{
    internal const string ClockNamespace = "clock";

    public override string SourceId => "user-coordination";

    public override int Priority => 19;

    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        if (context.ThreadKind == ToolPlanningThreadKind.Internal)
            yield break;

        var tools = new UserCoordinationTools();
        if (context.ThreadKind != ToolPlanningThreadKind.SubAgentChild)
            yield return GeneratedToolFunctions.UserCoordinationTools_SendUserMessageAsync(tools);

        yield return GeneratedToolFunctions.UserCoordinationTools_Sleep(tools);
        yield return GeneratedToolFunctions.UserCoordinationTools_CurrentTime(tools);
    }

    protected override string? GetNamespace(AIFunction function, ToolPlanningContext context) =>
        function.Name is nameof(UserCoordinationTools.Sleep) or nameof(UserCoordinationTools.CurrentTime)
            ? ClockNamespace
            : null;

    protected override string? GetNamespaceDescription(AIFunction function, ToolPlanningContext context) =>
        function.Name is nameof(UserCoordinationTools.Sleep) or nameof(UserCoordinationTools.CurrentTime)
            ? "Clock utilities for reading UTC time and waiting for user or mailbox activity."
            : null;

    protected override ToolExposure GetExposure(AIFunction function, ToolPlanningContext context) =>
        function.Name == nameof(UserCoordinationTools.SendUserMessageAsync)
            ? ToolExposure.DirectModelOnly
            : ToolExposure.Direct;

    protected override ToolPolicyScope GetPolicyScope(AIFunction function, ToolPlanningContext context) =>
        ToolPolicyScope.RuntimeManaged;
}
