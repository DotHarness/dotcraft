using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;

namespace DotCraft.Unity;

internal interface IUnityToolDeclarations
{
    [ToolDeclaration(Name = "list")]
    [Description("List local Unity Editors without attaching.")]
    void List();

    [ToolDeclaration(Name = "connect")]
    [Description("Connect this task to a Unity Editor by PID, or omit PID to reconnect its selected Editor after script reload.")]
    void Connect(
        [Range(1, int.MaxValue)]
        [Description("Unity Editor process ID. Omit it to reconnect the Editor already selected by this task.")] int? pid = null);

    [ToolDeclaration(Name = "status")]
    [Description("Read the selected Editor state without reconnecting.")]
    void Status();

    [ToolDeclaration(Name = "execute")]
    [Description("Execute C# method-body statements on the selected Editor's main thread. Supports cross-frame await and optional background execution.")]
    void Execute(
        [Description("Inline C# method-body statements. Specify exactly one of code or path.")] string? code = null,
        [Description("Host-side C# script path. Specify exactly one of code or path.")] string? path = null,
        [Description("Optional JSON values available to the snippet as Args.")] JsonObject? args = null,
        [Description("Return an execution ID when the code is still running after the initial wait.")] bool runInBackground = false,
        [Range(0, 30000)]
        [Description("Milliseconds to wait for a background execution before returning its current state.")] int yieldTimeMs = 1000);

    [ToolDeclaration(Name = "wait")]
    [Description("Wait for, inspect, or cooperatively cancel a background Unity execution.")]
    void Wait(
        [Required]
        [Description("Execution ID returned by unity.execute.")] string executionId,
        [Range(0, 30000)]
        [Description("Milliseconds to wait for completion before returning the current state.")] int yieldTimeMs = 1000,
        [Description("Request cooperative cancellation before returning the latest state.")] bool terminate = false);

    [ToolDeclaration(Name = "disconnect")]
    [Description("Disconnect this task from its selected Unity Editor.")]
    void Disconnect();
}

internal sealed class UnityToolSource(UnityAttachService service, string workspace) : IToolSource
{
    private static readonly IReadOnlyList<GeneratedToolDeclaration> Declarations =
    [
        DotCraft.GeneratedTools.Unity.GeneratedToolDeclarations.IUnityToolDeclarations_List_Declaration,
        DotCraft.GeneratedTools.Unity.GeneratedToolDeclarations.IUnityToolDeclarations_Connect_Declaration,
        DotCraft.GeneratedTools.Unity.GeneratedToolDeclarations.IUnityToolDeclarations_Status_Declaration,
        DotCraft.GeneratedTools.Unity.GeneratedToolDeclarations.IUnityToolDeclarations_Execute_Declaration,
        DotCraft.GeneratedTools.Unity.GeneratedToolDeclarations.IUnityToolDeclarations_Wait_Declaration,
        DotCraft.GeneratedTools.Unity.GeneratedToolDeclarations.IUnityToolDeclarations_Disconnect_Declaration
    ];

    public string SourceId => "DotCraft.Unity";

    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(ToolPlanningContext context, CancellationToken cancellationToken = default)
    {
        var registrations = new List<ToolRegistration>();
        foreach (var declaration in Declarations)
        {
            var operation = declaration.Name;
            var id = new ToolDefinitionId(ToolSourceKind.PluginNative, SourceId, new SourceToolId(operation));
            var definition = new ToolDefinition(id, new ToolName("unity", operation), declaration.Description, declaration.InputSchema,
                declaration.OutputSchema,
                policyHints: new ToolPolicyHints(
                    RequiresApproval: operation is "connect" or "execute" or "disconnect",
                    ReadOnly: operation is "list" or "status"));
            registrations.Add(new ToolRegistration(definition,
                new ToolRuntimeBinding(new RuntimeBindingId($"{SourceId}:{operation}:{context.Revision}"), id,
                    new Invocation(service, workspace, operation, context.Mode), ToolBindingLeases.AlwaysAvailable, SourceId, context.Revision),
                ToolProjectionShape.StandardPair));
        }
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(registrations);
    }

    private sealed class Invocation(UnityAttachService service, string workspace, string operation, string mode) : IToolRuntime
    {
        public async ValueTask<ToolExecutionResult> InvokeAsync(ToolInvocationContext context, JsonObject arguments, CancellationToken cancellationToken = default)
        {
            if (mode.Equals("plan", StringComparison.OrdinalIgnoreCase)
                && operation is not ("list" or "status")
                && !(operation == "wait" && arguments["terminate"]?.GetValue<bool>() != true))
                return Failure("UnityModeDenied", "Unity mutations are unavailable in Plan mode.");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                JsonObject result;
                if (operation == "list") result = new JsonObject { ["state"] = "completed", ["result"] = JsonSerializer.SerializeToNode(service.List()) };
                else
                {
                    if (operation == "execute")
                    {
                        var code = arguments["code"]?.GetValue<string>();
                        var path = arguments["path"]?.GetValue<string>();
                        if ((code == null) == (path == null)) throw new ArgumentException("Specify exactly one of code or path.");
                        if (path != null) code = await File.ReadAllTextAsync(Path.GetFullPath(path, context.WorkspacePath ?? workspace), cancellationToken);
                        result = await service.Execute(
                            context.ThreadId,
                            code!,
                            arguments["args"]?.AsObject(),
                            arguments["runInBackground"]?.GetValue<bool>() ?? false,
                            arguments["yieldTimeMs"]?.GetValue<int>() ?? 1000,
                            cancellationToken);
                    }
                    else if (operation == "wait")
                        result = await service.Wait(
                            context.ThreadId,
                            arguments["executionId"]!.GetValue<string>(),
                            arguments["yieldTimeMs"]?.GetValue<int>() ?? 1000,
                            arguments["terminate"]?.GetValue<bool>() ?? false,
                            cancellationToken);
                    else result = operation switch
                    {
                        "connect" => await service.Connect(context.ThreadId, arguments["pid"]?.GetValue<int>(), cancellationToken),
                        "status" => await service.Status(context.ThreadId),
                        _ => await service.Disconnect(context.ThreadId)
                    };
                }
                var json = result.ToJsonString();
                return result["state"]?.GetValue<string>() is "completed" or "queued" or "running" or "cancelled" or "lost" or "unknown"
                    ? ToolExecutionResult.Succeeded(json, JsonSerializer.SerializeToElement(result))
                    : Failure("UnityRequestIncomplete", json);
            }
            catch (OperationCanceledException) { return Failure("UnityOutcomeUnknown", "The call stopped waiting. Execution may have started; do not replay automatically."); }
            catch (UnityTargetException e) { return Failure(e.Code, e.Message); }
            catch (Exception e) { return Failure("UnityAttachFailed", e.Message); }
        }

        private static ToolExecutionResult Failure(string code, string text) => ToolExecutionResult.Failed(new ToolError(code, text), text);
    }
}
