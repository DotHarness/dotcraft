using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Security;

namespace DotCraft.Tools;

/// <summary>Late-bound common approval evaluator used by every tool source.</summary>
public sealed class CommonToolApprovalEvaluator : IToolApprovalEvaluator
{
    private IApprovalService? _approvalService;

    /// <summary>Binds the workspace/channel approval service used for subsequent invocations.</summary>
    public void Bind(IApprovalService approvalService) =>
        Volatile.Write(ref _approvalService, approvalService ?? throw new ArgumentNullException(nameof(approvalService)));

    /// <inheritdoc />
    public async ValueTask<ToolDispatchDecision> RequestAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        if (!registration.Definition.PolicyHints.RequiresApproval)
            return ToolDispatchDecision.Allow;
        cancellationToken.ThrowIfCancellationRequested();
        var approval = Volatile.Read(ref _approvalService);
        if (approval == null)
        {
            return ToolDispatchDecision.Deny(
                ToolErrorCodes.ApprovalRejected,
                $"Tool '{registration.Definition.Name}' requires an approval service.");
        }

        var kind = "remoteResource";
        string? targetArgument = null;
        string? operation = null;
        string? operationArgument = null;
        var hasDescriptor = registration.Definition.Annotations.TryGetValue(
            "dotcraft/dynamicApproval",
            out var descriptor)
            || registration.Definition.Annotations.TryGetValue(
                "dotcraft/pluginApproval",
                out descriptor)
            || registration.Definition.Annotations.TryGetValue(
                "dotcraft/legacyAppBindingApproval",
                out descriptor)
            || registration.Definition.Annotations.TryGetValue(
                "dotcraft/nativeApproval",
                out descriptor);
        if (!hasDescriptor && registration.Definition.Id.Kind == ToolSourceKind.Mcp)
        {
            kind = "mcp";
            operation = registration.Definition.Name.Name;
            targetArgument = null;
        }
        if (hasDescriptor && descriptor.ValueKind == JsonValueKind.Object)
        {
            kind = ReadString(descriptor, "kind") ?? kind;
            targetArgument = ReadString(descriptor, "targetArgument");
            operation = ReadString(descriptor, "operation");
            operationArgument = ReadString(descriptor, "operationArgument");
        }

        if (hasDescriptor
            && descriptor.ValueKind == JsonValueKind.Object
            && registration.Definition.Id.Kind is ToolSourceKind.PluginNative or ToolSourceKind.RuntimeDynamic
            && PluginFunctionExecutionScope.Current is { } pluginScope)
        {
            return await RequestScopedPluginApprovalAsync(
                registration,
                arguments,
                descriptor,
                pluginScope,
                cancellationToken).ConfigureAwait(false);
        }

        var target = ReadArgument(arguments, targetArgument) ?? registration.Definition.Name.ToString();
        operation ??= ReadArgument(arguments, operationArgument) ?? registration.Definition.Name.Name;
        if (descriptor.ValueKind == JsonValueKind.Object
            && descriptor.TryGetProperty("whenOperationIn", out var operationSet)
            && operationSet.ValueKind == JsonValueKind.Array
            && !operationSet.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String
                && string.Equals(item.GetString(), operation, StringComparison.OrdinalIgnoreCase)))
        {
            return ToolDispatchDecision.Allow;
        }

        if (descriptor.ValueKind == JsonValueKind.Object
            && descriptor.TryGetProperty("outsideWorkspaceOnly", out var outsideOnly)
            && outsideOnly.ValueKind == JsonValueKind.True
            && !RequiresOutsideWorkspaceApproval(kind, target, operation, descriptor))
        {
            return ToolDispatchDecision.Allow;
        }

        var approved = kind.ToLowerInvariant() switch
        {
            "file" => await approval.RequestFileApprovalAsync(operation, target).ConfigureAwait(false),
            "shell" => await approval.RequestShellApprovalAsync(operation, target).ConfigureAwait(false),
            _ => await approval.RequestResourceApprovalAsync(kind, operation, target).ConfigureAwait(false)
        };
        return approved
            ? ToolDispatchDecision.Allow
            : ToolDispatchDecision.Deny(
                ToolErrorCodes.ApprovalRejected,
                $"Tool '{registration.Definition.Name}' approval was rejected.");
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadArgument(JsonObject arguments, string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && arguments.TryGetPropertyValue(name, out var value)
        && value is JsonValue jsonValue
        && jsonValue.TryGetValue<string>(out var text)
            ? text
            : null;

    private static async ValueTask<ToolDispatchDecision> RequestScopedPluginApprovalAsync(
        ToolRegistration registration,
        JsonObject arguments,
        JsonElement descriptor,
        PluginFunctionExecutionContext scope,
        CancellationToken cancellationToken)
    {
        var kind = ReadString(descriptor, "kind") ?? string.Empty;
        var targetArgument = ReadString(descriptor, "targetArgument") ?? string.Empty;
        var target = ReadArgument(arguments, targetArgument);
        if (string.IsNullOrWhiteSpace(target))
        {
            return IsOptionalProperty(registration.Definition.InputSchema, targetArgument)
                ? ToolDispatchDecision.Allow
                : ToolDispatchDecision.Deny(
                    ToolErrorCodes.InputInvalid,
                    $"Tool '{registration.Definition.Name}' requires string argument '{targetArgument}' for approval routing.");
        }

        var operation = ReadString(descriptor, "operation");
        if (string.IsNullOrWhiteSpace(operation))
            operation = ReadArgument(arguments, ReadString(descriptor, "operationArgument"));
        if (string.IsNullOrWhiteSpace(operation))
        {
            return ToolDispatchDecision.Deny(
                ToolErrorCodes.InputInvalid,
                $"Tool '{registration.Definition.Name}' could not resolve approval operation metadata.");
        }

        return kind.ToLowerInvariant() switch
        {
            "file" => await GuardFileAccessAsync(scope, target, operation, cancellationToken).ConfigureAwait(false),
            "shell" => await GuardShellAccessAsync(scope, target, operation).ConfigureAwait(false),
            "remoteresource" => await GuardRemoteResourceAccessAsync(scope, target, operation).ConfigureAwait(false),
            _ => ToolDispatchDecision.Deny(
                ToolErrorCodes.InputInvalid,
                $"Tool '{registration.Definition.Name}' uses unsupported approval kind '{kind}'.")
        };
    }

    private static bool IsOptionalProperty(JsonElement schema, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName)
            || schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty(propertyName, out _))
        {
            return false;
        }

        return !schema.TryGetProperty("required", out var required)
               || required.ValueKind != JsonValueKind.Array
               || !required.EnumerateArray().Any(item =>
                   item.ValueKind == JsonValueKind.String
                   && string.Equals(item.GetString(), propertyName, StringComparison.Ordinal));
    }

    private static async ValueTask<ToolDispatchDecision> GuardFileAccessAsync(
        PluginFunctionExecutionContext scope,
        string path,
        string operation,
        CancellationToken cancellationToken)
    {
        var userDotCraftPath = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft"));
        var guard = new FileAccessGuard(
            scope.WorkspacePath,
            scope.RequireApprovalOutsideWorkspace,
            scope.ApprovalService,
            scope.PathBlacklist,
            [userDotCraftPath],
            scope.WorkspaceRoots);
        var error = await guard.ValidatePathAsync(
            guard.ResolvePath(path),
            operation,
            path,
            cancellationToken).ConfigureAwait(false);
        return error is null
            ? ToolDispatchDecision.Allow
            : ToolDispatchDecision.Deny(ToolErrorCodes.AccessDenied, error);
    }

    private static async ValueTask<ToolDispatchDecision> GuardShellAccessAsync(
        PluginFunctionExecutionContext scope,
        string workingDirectory,
        string command)
    {
        var normalizedCommand = command.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCommand))
            return ToolDispatchDecision.Deny(ToolErrorCodes.InputInvalid, "Shell approval routing requires a non-empty command string.");

        if (scope.PathBlacklist?.CommandReferencesBlacklistedPath(normalizedCommand) == true)
        {
            return ToolDispatchDecision.Deny(
                ToolErrorCodes.AccessDenied,
                "Error: Command references a blacklisted path and cannot be executed.");
        }

        var resolvedWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? scope.WorkspacePath
            : ResolveAgainstWorkspace(scope.WorkspacePath, workingDirectory);
        var hasPathTraversal = normalizedCommand.Contains("..\\", StringComparison.Ordinal)
                               || normalizedCommand.Contains("../", StringComparison.Ordinal);
        var isOutsideWorkspace = !scope.WorkspaceRoots.Any(
            root => IsWithinBoundary(resolvedWorkingDirectory, root));
        if (!hasPathTraversal && !isOutsideWorkspace)
            return ToolDispatchDecision.Allow;

        if (!scope.RequireApprovalOutsideWorkspace)
        {
            var message = hasPathTraversal
                ? "Error: Command blocked by safety guard (path traversal detected)."
                : "Error: Working directory is outside workspace boundary.";
            return ToolDispatchDecision.Deny(ToolErrorCodes.AccessDenied, message);
        }

        var approved = await scope.ApprovalService.RequestShellApprovalAsync(
            normalizedCommand,
            resolvedWorkingDirectory,
            ApprovalContextScope.Current).ConfigureAwait(false);
        return approved
            ? ToolDispatchDecision.Allow
            : ToolDispatchDecision.Deny(ToolErrorCodes.ApprovalRejected, "Error: Command execution was rejected by user.");
    }

    private static async ValueTask<ToolDispatchDecision> GuardRemoteResourceAccessAsync(
        PluginFunctionExecutionContext scope,
        string target,
        string operation)
    {
        var approved = await scope.ApprovalService.RequestResourceApprovalAsync(
            "remoteResource",
            operation.Trim(),
            target.Trim(),
            ApprovalContextScope.Current).ConfigureAwait(false);
        return approved
            ? ToolDispatchDecision.Allow
            : ToolDispatchDecision.Deny(ToolErrorCodes.ApprovalRejected, "Error: Remote resource operation was rejected by user.");
    }

    private static string ResolveAgainstWorkspace(string workspacePath, string path) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(workspacePath, path));

    private static bool IsWithinBoundary(string path, string boundary)
    {
        var fullPath = Path.GetFullPath(path);
        var fullBoundary = Path.GetFullPath(boundary)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullBoundary, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullBoundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullBoundary + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresOutsideWorkspaceApproval(
        string kind,
        string target,
        string operation,
        JsonElement descriptor)
    {
        var workspacePath = ReadString(descriptor, "workspacePath");
        if (string.IsNullOrWhiteSpace(workspacePath))
            return true;
        var workspaceRoots = ReadWorkspaceRoots(descriptor, workspacePath);

        if (string.Equals(kind, "shell", StringComparison.OrdinalIgnoreCase))
        {
            var command = operation;
            var workingDirectory = string.IsNullOrWhiteSpace(target)
                || string.Equals(target, "Exec", StringComparison.Ordinal)
                    ? workspacePath
                    : target;
            var fullWorkingDirectory = Path.IsPathRooted(workingDirectory)
                ? Path.GetFullPath(workingDirectory)
                : Path.GetFullPath(Path.Combine(workspacePath, workingDirectory));
            var outsideWorkingDirectory = !workspaceRoots.Any(
                root => IsWithinBoundary(fullWorkingDirectory, root));
            return outsideWorkingDirectory
                   || command.Contains("../", StringComparison.Ordinal)
                   || command.Contains("..\\", StringComparison.Ordinal)
                   || new ShellCommandInspector(workspacePath, workspaceRoots)
                       .DetectOutsideWorkspacePaths(command).Count > 0;
        }

        var trustedPaths = descriptor.TryGetProperty("trustedReadPaths", out var trusted)
                           && trusted.ValueKind == JsonValueKind.Array
            ? trusted.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString()!)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .ToArray()
            : [];
        var guard = new FileAccessGuard(
            workspacePath,
            trustedReadPaths: trustedPaths,
            workspaceRoots: workspaceRoots);
        var fullPath = guard.ResolvePath(string.IsNullOrWhiteSpace(target) ? "." : target);
        return guard.RequiresOutsideWorkspaceApproval(fullPath, operation);
    }

    private static IReadOnlyList<string> ReadWorkspaceRoots(
        JsonElement descriptor,
        string workspacePath) =>
        descriptor.TryGetProperty("workspaceRoots", out var roots)
        && roots.ValueKind == JsonValueKind.Array
            ? roots.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString()!)
                .ToArray()
            : [workspacePath];
}
