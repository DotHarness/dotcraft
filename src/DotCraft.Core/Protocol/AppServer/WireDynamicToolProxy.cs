using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Plugins;
using DotCraft.Security;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Routes runtime dynamic tool calls to the AppServer client bound to the current thread.
/// </summary>
public sealed class WireDynamicToolProxy : IThreadRuntimeToolProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, DynamicToolThreadBinding> _byThread = new();

    public int Priority => 90;

    /// <summary>
    /// Binds a thread to dynamic tools declared by the client that created the thread.
    /// </summary>
    public void BindThread(
        string threadId,
        IAppServerTransport transport,
        AppServerConnection connection,
        IReadOnlyList<DynamicToolSpec>? tools)
    {
        if (tools is not { Count: > 0 })
            return;

        _byThread[threadId] = new DynamicToolThreadBinding(
            threadId,
            transport,
            connection,
            tools.Select(CloneSpec).ToArray());
    }

    public static bool TryValidateSpecs(
        IReadOnlyList<DynamicToolSpec>? tools,
        out string message)
    {
        message = string.Empty;
        if (tools is not { Count: > 0 })
            return true;

        var names = new HashSet<string>(StringComparer.Ordinal);
        var qualifiedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (!PluginManifestParser.IsValidFunctionName(tool.Name))
            {
                message = "dynamicTools[].name is required and must be a valid model-visible function name.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(tool.Namespace)
                && !PluginManifestParser.IsValidFunctionName(tool.Namespace))
            {
                message = $"Dynamic tool '{tool.Name}' has an invalid namespace.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(tool.Description))
            {
                message = $"Dynamic tool '{tool.Name}' must declare a description.";
                return false;
            }

            if (tool.InputSchema == null)
            {
                message = $"Dynamic tool '{tool.Name}' must declare inputSchema.";
                return false;
            }

            if (!PluginFunctionSchemaValidator.TryValidateSchema(tool.InputSchema, out var schemaError))
            {
                message = $"Dynamic tool '{tool.Name}' has an invalid inputSchema: {schemaError}";
                return false;
            }

            if (!names.Add(tool.Name))
            {
                message = $"Dynamic tool name '{tool.Name}' is declared more than once.";
                return false;
            }

            var qualifiedName = $"{tool.Namespace ?? string.Empty}\u001f{tool.Name}";
            if (!qualifiedNames.Add(qualifiedName))
            {
                message = $"Dynamic tool '{tool.Name}' is declared more than once in namespace '{tool.Namespace}'.";
                return false;
            }

            if (tool.Approval != null
                && !TryValidateApprovalDescriptor(tool, out message))
            {
                message = $"Dynamic tool '{tool.Name}' has an invalid approval descriptor: {message}";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Removes all thread bindings for a disconnected transport.
    /// </summary>
    public void UnbindTransport(IAppServerTransport transport)
    {
        foreach (var kv in _byThread.ToArray())
        {
            if (ReferenceEquals(kv.Value.Transport, transport))
                _byThread.TryRemove(kv.Key, out _);
        }
    }

    /// <summary>
    /// Removes a single thread binding.
    /// </summary>
    public void UnbindThread(string threadId) => _byThread.TryRemove(threadId, out _);

    public IReadOnlyList<AITool> CreateToolsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames)
    {
        if (!_byThread.TryGetValue(thread.Id, out var binding) || binding.Connection.IsClosed)
            return [];

        return binding.Tools
            .Where(tool => !reservedToolNames.Contains(tool.Name))
            .Select(tool => (AITool)new DynamicToolRuntimeFunction(this, binding, tool))
            .ToArray();
    }

    internal async ValueTask<DynamicToolCallResult> InvokeAsync(
        DynamicToolThreadBinding binding,
        DynamicToolSpec spec,
        PluginFunctionExecutionContext execution,
        string callId,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        if (binding.Connection.IsClosed)
            return Failed("DynamicToolUnavailable", $"Dynamic tool '{spec.Name}' is unavailable because the client connection is closed.");

        try
        {
            var response = await binding.Transport.SendClientRequestAsync(
                AppServerMethods.ItemToolCall,
                new DynamicToolCallParams
                {
                    ThreadId = execution.ThreadId,
                    TurnId = execution.TurnId,
                    CallId = callId,
                    Namespace = spec.Namespace,
                    Tool = spec.Name,
                    Arguments = arguments
                },
                cancellationToken,
                TimeSpan.FromSeconds(120));

            if (response.Error.HasValue)
                return Failed("DynamicToolClientError", response.Error.Value.ToString());

            if (!response.Result.HasValue)
                return Failed("DynamicToolInvalidResponse", $"Dynamic tool '{spec.Name}' returned no result.");

            return response.Result.Value.Deserialize<DynamicToolCallResult>(JsonOptions)
                ?? Failed("DynamicToolInvalidResponse", $"Dynamic tool '{spec.Name}' returned an invalid result.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("DynamicToolTimeout", $"Dynamic tool '{spec.Name}' timed out while waiting for client response.");
        }
        catch (Exception ex)
        {
            return Failed("DynamicToolFailed", ex.Message);
        }
    }

    private static DynamicToolSpec CloneSpec(DynamicToolSpec spec) =>
        new()
        {
            Namespace = spec.Namespace,
            Name = spec.Name,
            Description = spec.Description,
            InputSchema = spec.InputSchema?.DeepClone() as JsonObject,
            DeferLoading = spec.DeferLoading,
            Approval = spec.Approval == null
                ? null
                : new ChannelToolApprovalDescriptor
                {
                    Kind = spec.Approval.Kind,
                    TargetArgument = spec.Approval.TargetArgument,
                    Operation = spec.Approval.Operation,
                    OperationArgument = spec.Approval.OperationArgument
                }
        };

    private static DynamicToolCallResult Failed(string code, string message) =>
        new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = $"{code}: {message}" }]
        };

    private static bool TryValidateApprovalDescriptor(DynamicToolSpec descriptor, out string message)
    {
        var approval = descriptor.Approval;
        if (approval == null)
        {
            message = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(approval.Kind))
        {
            message = "approval.kind is required.";
            return false;
        }

        if (!approval.Kind.Equals("file", StringComparison.OrdinalIgnoreCase)
            && !approval.Kind.Equals("shell", StringComparison.OrdinalIgnoreCase)
            && !approval.Kind.Equals("remoteResource", StringComparison.OrdinalIgnoreCase))
        {
            message = $"approval.kind '{approval.Kind}' is not supported.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(approval.TargetArgument))
        {
            message = "approval.targetArgument is required.";
            return false;
        }

        if (!TryValidateStringProperty(descriptor.InputSchema, approval.TargetArgument, out message))
            return false;

        var hasStaticOperation = !string.IsNullOrWhiteSpace(approval.Operation);
        var hasOperationArgument = !string.IsNullOrWhiteSpace(approval.OperationArgument);
        if (hasStaticOperation == hasOperationArgument)
        {
            message = "exactly one of approval.operation or approval.operationArgument must be set.";
            return false;
        }

        if (hasOperationArgument
            && !TryValidateStringProperty(descriptor.InputSchema, approval.OperationArgument!, out message))
        {
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static bool TryValidateStringProperty(JsonObject? schema, string propertyName, out string message)
    {
        if (schema is not JsonObject schemaObject)
        {
            message = "inputSchema must be an object.";
            return false;
        }

        if (!string.Equals(schemaObject["type"]?.GetValue<string>(), "object", StringComparison.Ordinal))
        {
            message = "inputSchema.type must be 'object' when approval metadata is declared.";
            return false;
        }

        if (schemaObject["properties"] is not JsonObject properties
            || !properties.TryGetPropertyValue(propertyName, out var propertySchema)
            || propertySchema is not JsonObject propertySchemaObject)
        {
            message = $"approval references unknown property '{propertyName}'.";
            return false;
        }

        if (!string.Equals(propertySchemaObject["type"]?.GetValue<string>(), "string", StringComparison.Ordinal))
        {
            message = $"approval property '{propertyName}' must be declared as a string.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    internal sealed record DynamicToolThreadBinding(
        string ThreadId,
        IAppServerTransport Transport,
        AppServerConnection Connection,
        IReadOnlyList<DynamicToolSpec> Tools);

    private sealed class DynamicToolRuntimeFunction(
        WireDynamicToolProxy proxy,
        DynamicToolThreadBinding binding,
        DynamicToolSpec spec) : AIFunction, IDynamicToolRuntimeTool
    {
        private readonly JsonElement _jsonSchema = ToJsonElement(spec.InputSchema ?? new JsonObject { ["type"] = "object" });

        public DynamicToolSpec Spec => spec;

        public override string Name => spec.Name;

        public override string Description => spec.Description;

        public override JsonElement JsonSchema => _jsonSchema;

        public override JsonElement? ReturnJsonSchema => null;

        public override MethodInfo? UnderlyingMethod => null;

        public override JsonSerializerOptions JsonSerializerOptions => SessionWireJsonOptions.Default;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            var scope = PluginFunctionExecutionScope.Current
                ?? throw new InvalidOperationException("Runtime dynamic tools require an active turn scope.");

            var callId = $"dyntool_{Guid.NewGuid():N}";
            var argsObject = ToJsonObject(arguments);
            var item = new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(scope.NextItemSequence()),
                TurnId = scope.TurnId,
                Type = ItemType.DynamicToolCall,
                Status = ItemStatus.Started,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = CreatePayload(callId, argsObject)
            };
            scope.Turn.Items.Add(item);
            scope.EmitItemStarted(item);

            var inputSchema = spec.InputSchema ?? new JsonObject { ["type"] = "object" };
            if (!PluginFunctionSchemaValidator.TryValidateArguments(inputSchema, argsObject, out var validationError))
                return FinalizeFailure(item, scope, callId, argsObject, "InvalidArguments", validationError);

            var approvalFailure = await ApplyServerApprovalAsync(scope, argsObject, cancellationToken);
            if (approvalFailure != null)
            {
                return FinalizeFailure(
                    item,
                    scope,
                    callId,
                    argsObject,
                    approvalFailure.Value.ErrorCode,
                    approvalFailure.Value.ErrorMessage);
            }

            var result = await proxy.InvokeAsync(binding, spec, scope, callId, argsObject, cancellationToken);
            item.Status = ItemStatus.Completed;
            item.CompletedAt = DateTimeOffset.UtcNow;
            item.Payload = CreatePayload(callId, argsObject, result);
            scope.EmitItemCompleted(item);

            return MapToolResultToModelValue(result);
        }

        private DynamicToolCallPayload CreatePayload(
            string callId,
            JsonObject argsObject,
            DynamicToolCallResult? result = null)
            => new()
            {
                Namespace = spec.Namespace,
                ToolName = spec.Name,
                CallId = callId,
                Arguments = argsObject.DeepClone() as JsonObject,
                ContentItems = result?.ContentItems?.Select(MapContentItem).ToArray(),
                StructuredResult = result?.StructuredResult?.DeepClone(),
                Success = result?.Success ?? false,
                ErrorCode = result?.ErrorCode,
                ErrorMessage = result?.ErrorMessage
            };

        private async Task<(string ErrorCode, string ErrorMessage)?> ApplyServerApprovalAsync(
            PluginFunctionExecutionContext scope,
            JsonObject argsObject,
            CancellationToken cancellationToken)
        {
            var approval = spec.Approval;
            if (approval == null)
                return null;

            if (!TryReadStringArgument(argsObject, approval.TargetArgument, out var approvalTarget))
            {
                return (
                    "InvalidArguments",
                    $"Dynamic tool '{spec.Name}' requires string argument '{approval.TargetArgument}' for approval routing.");
            }

            if (!TryResolveApprovalOperation(argsObject, approval, out var approvalOperation, out var operationError))
                return ("InvalidArguments", operationError);

            return approval.Kind.ToLowerInvariant() switch
            {
                "file" => await GuardFileAccessAsync(scope, approvalTarget, approvalOperation, cancellationToken),
                "shell" => await GuardShellAccessAsync(scope, approvalTarget, approvalOperation),
                "remoteresource" => await GuardRemoteResourceAccessAsync(scope, approvalTarget, approvalOperation),
                _ => (
                    "InvalidDynamicToolDescriptor",
                    $"Dynamic tool '{spec.Name}' uses unsupported approval kind '{approval.Kind}'.")
            };
        }

        private bool TryResolveApprovalOperation(
            JsonObject argsObject,
            ChannelToolApprovalDescriptor approval,
            out string operation,
            out string error)
        {
            if (!string.IsNullOrWhiteSpace(approval.Operation))
            {
                operation = approval.Operation!;
                error = string.Empty;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(approval.OperationArgument)
                && TryReadStringArgument(argsObject, approval.OperationArgument!, out var operationArgument))
            {
                operation = operationArgument;
                error = string.Empty;
                return true;
            }

            operation = string.Empty;
            error = $"Dynamic tool '{spec.Name}' could not resolve approval operation metadata.";
            return false;
        }

        private object FinalizeFailure(
            SessionItem item,
            PluginFunctionExecutionContext scope,
            string callId,
            JsonObject argsObject,
            string errorCode,
            string errorMessage)
        {
            var result = Failed(errorCode, errorMessage);
            item.Status = ItemStatus.Completed;
            item.CompletedAt = DateTimeOffset.UtcNow;
            item.Payload = CreatePayload(callId, argsObject, result);
            scope.EmitItemCompleted(item);
            return MapToolResultToModelValue(result);
        }

        private static async Task<(string ErrorCode, string ErrorMessage)?> GuardFileAccessAsync(
            PluginFunctionExecutionContext scope,
            string path,
            string operation,
            CancellationToken cancellationToken)
        {
            var userDotCraftPath = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft"));
            var guard = new FileAccessGuard(
                scope.WorkspacePath,
                requireApprovalOutsideWorkspace: scope.RequireApprovalOutsideWorkspace,
                approvalService: scope.ApprovalService,
                blacklist: scope.PathBlacklist,
                trustedReadPaths: [userDotCraftPath]);
            var resolvedPath = guard.ResolvePath(path);
            var error = await guard.ValidatePathAsync(resolvedPath, operation, path, cancellationToken);
            return error == null ? null : ("AccessDenied", error);
        }

        private static async Task<(string ErrorCode, string ErrorMessage)?> GuardShellAccessAsync(
            PluginFunctionExecutionContext scope,
            string workingDirectory,
            string command)
        {
            var normalizedCommand = command.Trim();
            if (string.IsNullOrWhiteSpace(normalizedCommand))
                return ("InvalidArguments", "Shell approval routing requires a non-empty command string.");

            if (scope.PathBlacklist != null && scope.PathBlacklist.CommandReferencesBlacklistedPath(normalizedCommand))
                return ("AccessDenied", "Error: Command references a blacklisted path and cannot be executed.");

            var resolvedWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? scope.WorkspacePath
                : ResolveAgainstWorkspace(scope.WorkspacePath, workingDirectory);
            var hasPathTraversal = normalizedCommand.Contains("..\\", StringComparison.Ordinal)
                || normalizedCommand.Contains("../", StringComparison.Ordinal);
            var isOutsideWorkspace = !IsWithinBoundary(resolvedWorkingDirectory, scope.WorkspacePath);

            if (!hasPathTraversal && !isOutsideWorkspace)
                return null;

            if (!scope.RequireApprovalOutsideWorkspace)
            {
                if (hasPathTraversal)
                    return ("AccessDenied", "Error: Command blocked by safety guard (path traversal detected).");
                return ("AccessDenied", "Error: Working directory is outside workspace boundary.");
            }

            var approved = await scope.ApprovalService.RequestShellApprovalAsync(
                normalizedCommand,
                resolvedWorkingDirectory,
                ApprovalContextScope.Current);
            return approved ? null : ("AccessDenied", "Error: Command execution was rejected by user.");
        }

        private static async Task<(string ErrorCode, string ErrorMessage)?> GuardRemoteResourceAccessAsync(
            PluginFunctionExecutionContext scope,
            string target,
            string operation)
        {
            var normalizedTarget = target.Trim();
            if (string.IsNullOrWhiteSpace(normalizedTarget))
                return ("InvalidArguments", "Remote resource approval routing requires a non-empty target string.");

            var normalizedOperation = operation.Trim();
            if (string.IsNullOrWhiteSpace(normalizedOperation))
                return ("InvalidArguments", "Remote resource approval routing requires a non-empty operation string.");

            var approved = await scope.ApprovalService.RequestResourceApprovalAsync(
                "remoteResource",
                normalizedOperation,
                normalizedTarget,
                ApprovalContextScope.Current);
            return approved ? null : ("AccessDenied", "Error: Remote resource operation was rejected by user.");
        }

        private static object MapToolResultToModelValue(DynamicToolCallResult result)
        {
            if (result.ContentItems is { Count: > 0 } contentItems)
            {
                var aiContents = new List<AIContent>();
                foreach (var item in contentItems)
                {
                    if (string.Equals(item.Type, "text", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(item.Text))
                    {
                        aiContents.Add(new TextContent(item.Text));
                    }
                    else if (string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(item.DataBase64)
                             && !string.IsNullOrWhiteSpace(item.MediaType))
                    {
                        try
                        {
                            aiContents.Add(new DataContent(Convert.FromBase64String(item.DataBase64), item.MediaType));
                        }
                        catch (FormatException)
                        {
                            aiContents.Add(new TextContent("[Invalid dynamic tool image payload]"));
                        }
                    }
                }

                if (aiContents.Count > 0)
                {
                    if (result.StructuredResult != null)
                        aiContents.Add(new TextContent(result.StructuredResult.ToJsonString(SessionWireJsonOptions.Default)));

                    return aiContents;
                }
            }

            if (result.StructuredResult != null)
            {
                return new
                {
                    result.Success,
                    result.ContentItems,
                    result.StructuredResult,
                    result.ErrorCode,
                    result.ErrorMessage
                };
            }

            if (!result.Success)
            {
                var error = result.ErrorMessage ?? "Dynamic tool call failed.";
                return string.IsNullOrWhiteSpace(result.ErrorCode) ? error : $"{result.ErrorCode}: {error}";
            }

            return "Dynamic tool completed.";
        }

        private static PluginFunctionContentItem MapContentItem(ExtChannelToolContentItem item)
            => new()
            {
                Type = item.Type,
                Text = item.Text,
                DataBase64 = item.DataBase64,
                MediaType = item.MediaType
            };

        private static bool TryReadStringArgument(JsonObject argsObject, string argumentName, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(argumentName)
                || !argsObject.TryGetPropertyValue(argumentName, out var node)
                || node == null
                || node.GetValueKind() != JsonValueKind.String)
            {
                return false;
            }

            value = node.GetValue<string>() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static JsonObject ToJsonObject(AIFunctionArguments arguments)
        {
            var root = new JsonObject();
            foreach (var (key, value) in arguments)
                root[key] = value is JsonNode node ? node.DeepClone() : JsonSerializer.SerializeToNode(value, SessionWireJsonOptions.Default);
            return root;
        }

        private static JsonElement ToJsonElement(JsonNode node)
            => JsonSerializer.Deserialize<JsonElement>(node.ToJsonString(SessionWireJsonOptions.Default), SessionWireJsonOptions.Default);

        private static string ResolveAgainstWorkspace(string workspacePath, string path)
            => Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(workspacePath, path));

        private static bool IsWithinBoundary(string fullPath, string boundaryRoot)
        {
            var resolvedPath = Path.GetFullPath(fullPath);
            var resolvedBoundary = Path.GetFullPath(boundaryRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (resolvedPath.Equals(resolvedBoundary, StringComparison.OrdinalIgnoreCase))
                return true;

            return resolvedPath.StartsWith(resolvedBoundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || resolvedPath.StartsWith(resolvedBoundary + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>
/// Marker for AIFunction wrappers that represent a runtime dynamic tool.
/// </summary>
public interface IDynamicToolRuntimeTool
{
    DynamicToolSpec Spec { get; }
}
