using DotCraft.GeneratedTools.Core;
using DotCraft.Memory;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>Contributes Plan and user-input tools through the unified source/runtime path.</summary>
public sealed class ModeSupplementalToolSource(
    PlanStore? planStore,
    Action<string, StructuredPlan>? onPlanUpdated = null) : AIFunctionToolSource
{
    /// <inheritdoc />
    public override string SourceId => "mode-native";

    /// <inheritdoc />
    public override int Priority => 20;

    /// <inheritdoc />
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {
        if (planStore != null)
        {
            var planTools = new PlanTools(planStore, TracingChatClient.GetActiveSessionKey, onPlanUpdated);
            yield return GeneratedToolFunctions.PlanTools_CreatePlan(planTools);
            yield return GeneratedToolFunctions.PlanTools_UpdateTodos(planTools);
            yield return GeneratedToolFunctions.PlanTools_TodoWrite(planTools);
        }

        if (context.ThreadKind is not (ToolPlanningThreadKind.Internal or ToolPlanningThreadKind.SubAgentChild))
        {
            var userInputTools = new RequestUserInputTools();
            yield return GeneratedToolFunctions.RequestUserInputTools_RequestUserInput(userInputTools);
        }
    }
}
