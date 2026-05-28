using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Protocol;
using DotCraft.Security;
using Microsoft.Extensions.AI;

namespace DotCraft.Plugins;

/// <summary>
/// MEAI function wrapper used for plugin function descriptors.
/// </summary>
public sealed class PluginFunctionRuntimeFunction : AIFunction, IPluginFunctionTool
{
    private readonly PluginFunctionRegistration _registration;
    private readonly JsonElement _jsonSchema;
    private readonly JsonElement? _returnJsonSchema;

    public PluginFunctionRuntimeFunction(PluginFunctionRegistration registration)
    {
        _registration = registration;
        _jsonSchema = ToJsonElement(registration.Descriptor.InputSchema ?? new JsonObject { ["type"] = "object" });
        _returnJsonSchema = registration.Descriptor.OutputSchema != null
            ? ToJsonElement(registration.Descriptor.OutputSchema)
            : null;
    }

    public PluginFunctionDescriptor Descriptor => _registration.Descriptor;

    public PluginFunctionDescriptor? PluginFunctionDescriptor => Descriptor;

    public override string Name => Descriptor.Name;

    public override string Description => Descriptor.Description;

    public override JsonElement JsonSchema => _jsonSchema;

    public override JsonElement? ReturnJsonSchema => _returnJsonSchema;

    public override MethodInfo? UnderlyingMethod => null;

    public override JsonSerializerOptions JsonSerializerOptions => SessionWireJsonOptions.Default;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var scope = PluginFunctionExecutionScope.Current
            ?? throw new InvalidOperationException("Plugin functions require an active turn scope.");

        var callId = $"pluginfn_{Guid.NewGuid():N}";
        var argsObject = ToJsonObject(arguments);
        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(scope.NextItemSequence()),
            TurnId = scope.TurnId,
            Type = ItemType.PluginFunctionCall,
            Status = ItemStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = CreatePayload(callId, argsObject)
        };
        scope.Turn.Items.Add(item);
        scope.EmitItemStarted(item);

        var inputSchema = Descriptor.InputSchema ?? new JsonObject { ["type"] = "object" };
        if (!PluginFunctionSchemaValidator.TryValidateArguments(inputSchema, argsObject, out var validationError))
            return FinalizeFailure(item, scope, callId, argsObject, "InvalidArguments", validationError);

        if (Descriptor.RequiresChatContext
            && string.IsNullOrWhiteSpace(scope.ChannelContext)
            && string.IsNullOrWhiteSpace(scope.GroupId))
        {
            return FinalizeFailure(
                item,
                scope,
                callId,
                argsObject,
                "MissingChatContext",
                $"Function '{Descriptor.Name}' requires channel chat context, but this turn does not have one.");
        }

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

        PluginFunctionInvocationResult result;
        try
        {
            result = await _registration.Invoker.InvokeAsync(
                new PluginFunctionInvocationContext
                {
                    Descriptor = Descriptor,
                    Execution = scope,
                    CallId = callId,
                    Arguments = argsObject
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = PluginFunctionInvocationResult.Failed(
                "PluginFunctionTimeout",
                $"Function '{Descriptor.Name}' timed out while waiting for a plugin response.");
        }
        catch (Exception ex)
        {
            result = PluginFunctionInvocationResult.Failed(
                "PluginFunctionFailed",
                ex.Message);
        }

        item.Status = ItemStatus.Completed;
        item.CompletedAt = DateTimeOffset.UtcNow;
        item.Payload = CreatePayload(callId, argsObject, result);
        scope.EmitItemCompleted(item);

        return MapToolResultToModelValue(result);
    }

    private PluginFunctionCallPayload CreatePayload(
        string callId,
        JsonObject argsObject,
        PluginFunctionInvocationResult? result = null)
        => new()
        {
            PluginId = Descriptor.PluginId,
            Namespace = Descriptor.Namespace,
            FunctionName = Descriptor.Name,
            CallId = callId,
            Arguments = argsObject.DeepClone() as JsonObject,
            ContentItems = result?.ContentItems,
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
        var approval = Descriptor.Approval;
        if (approval == null)
            return null;

        if (!TryReadStringArgument(argsObject, approval.TargetArgument, out var approvalTarget))
        {
            return (
                "InvalidArguments",
                $"Function '{Descriptor.Name}' requires string argument '{approval.TargetArgument}' for approval routing.");
        }

        if (!TryResolveApprovalOperation(argsObject, approval, out var approvalOperation, out var operationError))
            return ("InvalidArguments", operationError);

        return approval.Kind.ToLowerInvariant() switch
        {
            "file" => await GuardFileAccessAsync(scope, approvalTarget, approvalOperation, cancellationToken),
            "shell" => await GuardShellAccessAsync(scope, approvalTarget, approvalOperation),
            "remoteresource" => await GuardRemoteResourceAccessAsync(scope, approvalTarget, approvalOperation),
            _ => (
                "InvalidPluginFunctionDescriptor",
                $"Function '{Descriptor.Name}' uses unsupported approval kind '{approval.Kind}'.")
        };
    }

    private bool TryResolveApprovalOperation(
        JsonObject argsObject,
        PluginFunctionApprovalDescriptor approval,
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
        error = $"Function '{Descriptor.Name}' could not resolve approval operation metadata.";
        return false;
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
        {
            return (
                "InvalidArguments",
                "Shell approval routing requires a non-empty command string.");
        }

        if (scope.PathBlacklist != null && scope.PathBlacklist.CommandReferencesBlacklistedPath(normalizedCommand))
        {
            return (
                "AccessDenied",
                "Error: Command references a blacklisted path and cannot be executed.");
        }

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
        return approved
            ? null
            : ("AccessDenied", "Error: Command execution was rejected by user.");
    }

    private static async Task<(string ErrorCode, string ErrorMessage)?> GuardRemoteResourceAccessAsync(
        PluginFunctionExecutionContext scope,
        string target,
        string operation)
    {
        var normalizedTarget = target.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTarget))
        {
            return (
                "InvalidArguments",
                "Remote resource approval routing requires a non-empty target string.");
        }

        var normalizedOperation = operation.Trim();
        if (string.IsNullOrWhiteSpace(normalizedOperation))
        {
            return (
                "InvalidArguments",
                "Remote resource approval routing requires a non-empty operation string.");
        }

        var approved = await scope.ApprovalService.RequestResourceApprovalAsync(
            "remoteResource",
            normalizedOperation,
            normalizedTarget,
            ApprovalContextScope.Current);
        return approved
            ? null
            : ("AccessDenied", "Error: Remote resource operation was rejected by user.");
    }

    private object MapToolResultToModelValue(PluginFunctionInvocationResult result)
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
                    continue;
                }

                if (string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.DataBase64)
                    && !string.IsNullOrWhiteSpace(item.MediaType))
                {
                    try
                    {
                        aiContents.Add(new DataContent(Convert.FromBase64String(item.DataBase64), item.MediaType));
                    }
                    catch (FormatException)
                    {
                        aiContents.Add(new TextContent("[Invalid plugin image payload]"));
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
                success = result.Success,
                contentItems = result.ContentItems,
                structuredResult = result.StructuredResult,
                errorCode = result.ErrorCode,
                errorMessage = result.ErrorMessage
            };
        }

        return FormatResultText(result);
    }

    private static string FormatResultText(PluginFunctionInvocationResult result)
    {
        if (!result.Success)
        {
            var error = result.ErrorMessage ?? "Plugin function call failed.";
            if (!string.IsNullOrWhiteSpace(result.ErrorCode))
                return $"{result.ErrorCode}: {error}";
            return error;
        }

        var lines = new List<string>();
        if (result.ContentItems is { Count: > 0 })
        {
            foreach (var item in result.ContentItems)
            {
                if (string.Equals(item.Type, "text", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.Text))
                {
                    lines.Add(item.Text);
                }
                else if (string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase))
                {
                    lines.Add("[image]");
                }
            }
        }

        if (result.StructuredResult != null)
            lines.Add(result.StructuredResult.ToJsonString(SessionWireJsonOptions.Default));

        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "Plugin function completed.";
    }

    private object FinalizeFailure(
        SessionItem item,
        PluginFunctionExecutionContext scope,
        string callId,
        JsonObject argsObject,
        string errorCode,
        string errorMessage)
    {
        var result = PluginFunctionInvocationResult.Failed(errorCode, errorMessage);
        item.Status = ItemStatus.Completed;
        item.CompletedAt = DateTimeOffset.UtcNow;
        item.Payload = CreatePayload(callId, argsObject, result);
        scope.EmitItemCompleted(item);
        return MapToolResultToModelValue(result);
    }

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

    private static JsonObject ToJsonObject(AIFunctionArguments arguments)
    {
        var root = new JsonObject();
        foreach (var (key, value) in arguments)
            root[key] = value is JsonNode node ? node.DeepClone() : JsonSerializer.SerializeToNode(value, SessionWireJsonOptions.Default);
        return root;
    }

    private static JsonElement ToJsonElement(JsonNode node)
        => JsonSerializer.Deserialize<JsonElement>(node.ToJsonString(SessionWireJsonOptions.Default), SessionWireJsonOptions.Default);
}
