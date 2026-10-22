using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Tools;
using Microsoft.Extensions.Logging;

namespace DotCraft.CodeMode;

internal interface ICodeModeToolDeclaration
{
    [ToolDeclaration(Name = "CodeMode")]
    [Description("Run a JavaScript program that calls tools.")]
    void Exec(
        [Description("The JavaScript program. An optional first line `// @exec: {...}` sets this call's limits.")] string code);

    [ToolDeclaration(Name = "CodeModeWait")]
    [Description(CodeModeDescription.Wait)]
    void Wait(
        [ToolParameter(Name = "cell_id")]
        [Description("The id from `Script running with cell ID <id>`.")] string cellId,
        [ToolParameter(Name = "yield_time_ms")]
        [Description("How long to wait before yielding again. Defaults to 10000 ms.")] int? yieldTimeMs = null,
        [ToolParameter(Name = "max_tokens")]
        [Description("Output token budget for this result. Defaults to 10000 tokens.")] int? maxTokens = null,
        [Description("True stops the cell and returns its final result; false or omitted waits for output.")] bool terminate = false);
}

internal sealed class CodeModeToolFinalizer(
    Func<AppConfig> config,
    CodeModeWorkerHost host,
    CodeModeStore store,
    IToolDispatcher dispatcher,
    ILogger? logger = null) : IToolSnapshotFinalizer
{
    private const string SourceId = "code-mode";
    public const string PresentationId = "core.code-mode";

    private static readonly Dictionary<string, JsonElement> OwnResultLimit = new()
    {
        ["dotcraft/maxResultChars"] = JsonSerializer.SerializeToElement(0)
    };

    private static readonly ToolFreeformInput ExecInput = new(
        "code",
        "lark",
        string.Join(
            "\n",
            "",
            "start: pragma_source | plain_source",
            "pragma_source: PRAGMA_LINE NEWLINE SOURCE",
            "plain_source: SOURCE",
            "",
            @"PRAGMA_LINE: /[ \t]*\/\/ @exec:[^\r\n]*/",
            @"NEWLINE: /\r?\n/",
            @"SOURCE: /[\s\S]+/",
            ""));

    private readonly ConcurrentDictionary<string, byte> _warnedThreads = new(StringComparer.Ordinal);

    public async ValueTask<ToolSnapshotFinalization> FinalizeAsync(
        EffectiveToolSnapshot snapshot,
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        var current = config();
        var mode = current.Tools.CodeMode.Mode;
        if (mode == AppConfig.CodeModeSetting.Off)
            return ToolSnapshotFinalization.None;

        if (host.UnavailableReason is not null && !await host.TryStartAsync(cancellationToken).ConfigureAwait(false))
        {
            var reason = host.UnavailableReason ?? "The code mode worker is unavailable.";
            if (mode == AppConfig.CodeModeSetting.Only)
                throw new CodeModeUnavailableException($"{CodeModeExecRuntime.UnavailableErrorCode}: {reason}");
            if (_warnedThreads.TryAdd(context.ThreadId, 0))
                logger?.LogWarning("Code mode is off for thread {ThreadId}: {Reason}", context.ThreadId, reason);
            return ToolSnapshotFinalization.None;
        }

        var surface = CodeModeSurface.Build(snapshot, logger);
        var declaration = DotCraft.GeneratedTools.CodeMode.GeneratedToolDeclarations
            .ICodeModeToolDeclaration_Exec_Declaration;
        var definitionId = new ToolDefinitionId(ToolSourceKind.CoreNative, SourceId, new SourceToolId(declaration.Name));
        var definition = new ToolDefinition(
            definitionId,
            CodeModeSurface.CodeModeToolName,
            CodeModeDescription.Build(surface, host.Limits, mode),
            declaration.InputSchema,
            annotations: OwnResultLimit,
            provenance: new ToolProvenance(ToolSourceKind.CoreNative, SourceId),
            presentation: new ToolPresentationDescriptor(new DotCraft.Tools.PresentationId(PresentationId), null),
            policyScope: ToolPolicyScope.RuntimeManaged,
            freeformInput: ExecInput);
        var dependencies = new CodeModeExecDependencies(
            host,
            store,
            dispatcher,
            snapshot,
            surface,
            context.WorkspacePath,
            context.DataPath,
            current.Tools.ResultLimits.SpillPreviewLines);
        var exec = Register(definition, new CodeModeExecRuntime(dependencies), context.Revision);

        var waitDeclaration = DotCraft.GeneratedTools.CodeMode.GeneratedToolDeclarations
            .ICodeModeToolDeclaration_Wait_Declaration;
        var waitId = new ToolDefinitionId(ToolSourceKind.CoreNative, SourceId, new SourceToolId(waitDeclaration.Name));
        var wait = Register(
            new ToolDefinition(
                waitId,
                new ToolName(null, waitDeclaration.Name),
                waitDeclaration.Description,
                waitDeclaration.InputSchema,
                annotations: OwnResultLimit,
                provenance: new ToolProvenance(ToolSourceKind.CoreNative, SourceId),
                presentation: new ToolPresentationDescriptor(new DotCraft.Tools.PresentationId(PresentationId), null),
                policyScope: ToolPolicyScope.RuntimeManaged),
            new CodeModeWaitRuntime(dependencies),
            context.Revision);
        if (mode == AppConfig.CodeModeSetting.Only)
        {
            return new ToolSnapshotFinalization(
                [exec, wait],
                surface.Tools.Select(static tool => tool.Registration.Definition.Name).ToHashSet());
        }

        var annotated = surface.Tools
            .Where(static tool => tool.Listed)
            .Select(WithCallLine);
        return new ToolSnapshotFinalization([exec, wait, .. annotated], new HashSet<ToolName>());
    }

    private static ToolRegistration Register(ToolDefinition definition, IToolRuntime runtime, long revision) =>
        new(
            definition,
            new ToolRuntimeBinding(
                new RuntimeBindingId($"{SourceId}:{definition.Name.Name}"),
                definition.Id,
                runtime,
                ToolBindingLeases.AlwaysAvailable,
                SourceId,
                revision),
            ToolProjectionShape.StandardPair,
            ToolExposure.DirectModelOnly,
            ToolInvocationAudience.Model);

    private static ToolRegistration WithCallLine(CodeModeNestedTool tool)
    {
        var registration = tool.Registration;
        var definition = registration.Definition;
        return new ToolRegistration(
            definition.WithDescription(
                definition.Description.TrimEnd() + "\n\n" + CodeModeDeclarations.CallLine(tool.JsName, definition)),
            registration.Binding,
            registration.ProjectionShape,
            registration.Exposure,
            registration.InvocationAudiences,
            registration.Deferred,
            registration.ProviderFlatNameOverride);
    }
}
