using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Teams;

internal sealed record TeamsToolCallContext(
    string ThreadId,
    string WorkspacePath,
    string WorkspaceCraftPath);

internal sealed record TeamsToolResponse(string Content, JsonElement StructuredContent);

internal sealed class TeamsToolUnauthorizedException(string message) : Exception(message);

internal enum TeamsToolRole
{
    Ordinary,
    Leader,
    Teammate
}

internal sealed record TeamsToolPlanningScope(TeamsToolCallContext CallContext, TeamsToolRole Role);

/// <summary>Contributes the Agent Teams tools available to one frozen thread snapshot.</summary>
public sealed class TeamsToolSource(TeamsService service) : IToolSource
{
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.Ordinal)
    {
        "ListTeamMembers",
        "ReadMissionState",
        "ReadMemberStatus"
    };

    /// <inheritdoc />
    public string SourceId => "agent-teams";

    /// <inheritdoc />
    public int Priority => 54;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scope = await service.ResolveToolPlanningScopeAsync(context, cancellationToken).ConfigureAwait(false);
        if (scope == null)
            return [];

        var methods = new TeamsToolMethods(service, scope.CallContext);
        var functions = CreateFunctions(methods, scope.Role)
            .OrderBy(function => function.Name, StringComparer.Ordinal)
            .ToArray();
        return functions.Select(function => CreateRegistration(function, context, scope.CallContext)).ToArray();
    }

    private ToolRegistration CreateRegistration(
        AIFunction function,
        ToolPlanningContext planning,
        TeamsToolCallContext callContext)
    {
        var sourceToolId = new SourceToolId(function.Name);
        var definitionId = new ToolDefinitionId(ToolSourceKind.PluginNative, SourceId, sourceToolId);
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(TeamsConstants.ToolNamespace, function.Name),
            string.IsNullOrWhiteSpace(function.Description) ? function.Name : function.Description,
            function.JsonSchema,
            function.ReturnJsonSchema,
            policyHints: new ToolPolicyHints(ReadOnly: ReadOnlyTools.Contains(function.Name)),
            provenance: new ToolProvenance(ToolSourceKind.PluginNative, SourceId, "teams"));
        var bindingId = $"teams:{callContext.ThreadId}:{function.Name}";
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId(bindingId),
            definitionId,
            new TeamsToolRuntime(function),
            new TeamsToolBindingLease(service, callContext),
            bindingId,
            planning.Revision);
        return new ToolRegistration(
            definition,
            binding,
            ToolExposure.Direct,
            ToolInvocationAudience.Model);
    }

    private static IEnumerable<AIFunction> CreateFunctions(TeamsToolMethods methods, TeamsToolRole role)
    {
        if (role == TeamsToolRole.Ordinary)
        {
            yield return DotCraft.GeneratedTools.Teams.GeneratedToolFunctions.TeamsToolMethods_CreateTeam(methods);
            yield break;
        }

        if (role == TeamsToolRole.Leader)
        {
            yield return DotCraft.GeneratedTools.Teams.GeneratedToolFunctions.TeamsToolMethods_CreateMissionPlan(methods);
            yield return DotCraft.GeneratedTools.Teams.GeneratedToolFunctions.TeamsToolMethods_AssignTask(methods);
        }

        yield return DotCraft.GeneratedTools.Teams.GeneratedToolFunctions.TeamsToolMethods_ListTeamMembers(methods);
        yield return DotCraft.GeneratedTools.Teams.GeneratedToolFunctions.TeamsToolMethods_ReadMissionState(methods);
        yield return DotCraft.GeneratedTools.Teams.GeneratedToolFunctions.TeamsToolMethods_ReadMemberStatus(methods);
        yield return DotCraft.GeneratedTools.Teams.GeneratedToolFunctions.TeamsToolMethods_SendMessage(methods);

        if (role == TeamsToolRole.Leader)
        {
            yield return DotCraft.GeneratedTools.Teams.GeneratedToolFunctions.TeamsToolMethods_MarkMissionDone(methods);
            yield break;
        }

        yield return DotCraft.GeneratedTools.Teams.GeneratedToolFunctions.TeamsToolMethods_ReportProgress(methods);
        yield return DotCraft.GeneratedTools.Teams.GeneratedToolFunctions.TeamsToolMethods_PublishArtifact(methods);
        yield return DotCraft.GeneratedTools.Teams.GeneratedToolFunctions.TeamsToolMethods_MarkTaskDone(methods);
    }
}

internal sealed class TeamsToolBindingLease(TeamsService service, TeamsToolCallContext plannedContext) : IToolBindingLease
{
    public ValueTask<ToolBindingLeaseResult> CheckAsync(
        ToolInvocationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(context.ThreadId, plannedContext.ThreadId, StringComparison.Ordinal))
            return ValueTask.FromResult(ToolBindingLeaseResult.Unavailable("The Teams tool binding belongs to a different thread."));
        if (!service.IsAgentTeamsPluginEnabled(plannedContext.WorkspacePath, plannedContext.WorkspaceCraftPath))
            return ValueTask.FromResult(ToolBindingLeaseResult.Unavailable("The Agent Teams plugin is not enabled for this workspace."));
        return ValueTask.FromResult(ToolBindingLeaseResult.Available);
    }
}

internal sealed class TeamsToolRuntime(AIFunction function) : IToolRuntime
{
    public async ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arguments);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
            values[key] = value?.Deserialize<object>(function.JsonSerializerOptions);

        try
        {
            var raw = await function.InvokeAsync(new AIFunctionArguments(values), cancellationToken).ConfigureAwait(false);
            var response = raw switch
            {
                JsonElement element => element.Deserialize<TeamsToolResponse>(function.JsonSerializerOptions),
                TeamsToolResponse direct => direct,
                _ => null
            };
            if (response == null)
            {
                return ToolExecutionResult.Failed(
                    new ToolError(ToolErrorCodes.ResultInvalid, "The Teams runtime returned an invalid result."));
            }

            return ToolExecutionResult.Succeeded(response.Content, response.StructuredContent);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException ex)
        {
            return ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.InputInvalid, ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.InputInvalid, ex.Message));
        }
        catch (TeamsToolUnauthorizedException ex)
        {
            return ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.Unauthorized, ex.Message));
        }
        catch (AppServerException ex)
        {
            return ToolExecutionResult.Failed(
                new ToolError(ToolErrorCodes.InputInvalid, TeamsService.FormatToolException(ex)));
        }
        catch (Exception ex)
        {
            return ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.ExecutionFailed, ex.Message));
        }
    }
}

internal sealed class TeamsToolMethods(TeamsService service, TeamsToolCallContext context)
{
    [GeneratedTool]
    [Description("Start an asynchronous DotCraft Team mission from the current thread.")]
    public Task<TeamsToolResponse> CreateTeam(
        [Description("Short title for the Team mission.")] string title,
        [Description("Mission prompt for the Team to execute.")] string prompt,
        CancellationToken cancellationToken = default) =>
        service.CreateTeamToolAsync(context, cancellationToken, title, prompt);

    [GeneratedTool]
    [Description("Record a mission plan before assigning work.")]
    public Task<TeamsToolResponse> CreateMissionPlan(
        [Description("Concise plan for the current mission.")] string plan,
        CancellationToken cancellationToken = default) =>
        service.CreateMissionPlanToolAsync(context, cancellationToken, plan);

    [GeneratedTool]
    [Description("Create a Teams task and let the scheduler dispatch it when ready.")]
    public Task<TeamsToolResponse> AssignTask(
        [Description("Assignee member id, role, or display name.")] string assignee,
        [Description("Task title.")] string title,
        [Description("Task prompt for the member.")] string prompt,
        [Description("Upstream task aliases or canonical task ids that must be done before this task can run.")] List<string>? dependsOnTaskIds = null,
        [Description("Task kind, such as work or review.")] string? kind = null,
        [Description("Whether dependencies release this task to Leader synthesis before teammate dispatch.")] bool? requiresLeaderSynthesis = null,
        CancellationToken cancellationToken = default) =>
        service.AssignTaskToolAsync(context, cancellationToken, assignee, title, prompt, dependsOnTaskIds, kind, requiresLeaderSynthesis);

    [GeneratedTool]
    [Description("Read Team roster and teammate availability summaries.")]
    public TeamsToolResponse ListTeamMembers() => service.ListTeamMembersTool(context);

    [GeneratedTool]
    [Description("Read Mission-scoped task, thread, digest, artifact, and message summaries.")]
    public TeamsToolResponse ReadMissionState() => service.ReadMissionStateTool(context);

    [GeneratedTool]
    [Description("Read one teammate's current status and recent progress.")]
    public TeamsToolResponse ReadMemberStatus(
        [Description("Member id, role, or display name.")] string memberId) =>
        service.ReadMemberStatusTool(context, memberId);

    [GeneratedTool]
    [Description("Send a lightweight Mission-scoped message to the Leader or a participating teammate.")]
    public Task<TeamsToolResponse> SendMessage(
        [Description("Target member id, role, display name, or 'leader'.")] string to,
        [Description("Message for the teammate.")] string message,
        [Description("Optional related task alias or canonical task id.")] string? taskId = null,
        CancellationToken cancellationToken = default) =>
        service.SendMessageToolAsync(context, cancellationToken, to, message, taskId);

    [GeneratedTool]
    [Description("Record progress for a Teams task.")]
    public Task<TeamsToolResponse> ReportProgress(
        [Description("Progress summary.")] string summary,
        [Description("Progress status: running or blocked.")] string? status = null,
        [Description("Task aliases or canonical task ids this task is blocked on.")] List<string>? blockedOnTaskIds = null,
        CancellationToken cancellationToken = default) =>
        service.ReportProgressToolAsync(context, cancellationToken, summary, status, blockedOnTaskIds);

    [GeneratedTool]
    [Description("Publish an artifact reference for a Teams task.")]
    public Task<TeamsToolResponse> PublishArtifact(
        [Description("Artifact title.")] string title,
        [Description("Artifact path or URI.")] string pathOrUri,
        [Description("Short reusable artifact summary.")] string? summary = null,
        [Description("Optional related task alias or canonical task id when publishing for a specific assigned task.")] string? taskId = null,
        CancellationToken cancellationToken = default) =>
        service.PublishArtifactToolAsync(context, cancellationToken, title, pathOrUri, summary, taskId);

    [GeneratedTool]
    [Description("Mark a Teams task complete.")]
    public Task<TeamsToolResponse> MarkTaskDone(
        [Description("Completion summary for the current task.")] string summary,
        CancellationToken cancellationToken = default) =>
        service.MarkTaskDoneToolAsync(context, cancellationToken, summary);

    [GeneratedTool]
    [Description("Finalize a Teams mission with the user-facing final response.")]
    public Task<TeamsToolResponse> MarkMissionDone(
        [Description("User-facing final response.")] string finalResponse,
        CancellationToken cancellationToken = default) =>
        service.MarkMissionDoneToolAsync(context, cancellationToken, finalResponse);
}
