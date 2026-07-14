using System.Text.Json;
using System.Text.Json.Nodes;
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

    private static bool RequiresOutsideWorkspaceApproval(
        string kind,
        string target,
        string operation,
        JsonElement descriptor)
    {
        var workspacePath = ReadString(descriptor, "workspacePath");
        if (string.IsNullOrWhiteSpace(workspacePath))
            return true;

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
            var workspace = Path.GetFullPath(workspacePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var outsideWorkingDirectory = !string.Equals(fullWorkingDirectory, workspace, StringComparison.OrdinalIgnoreCase)
                && !fullWorkingDirectory.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !fullWorkingDirectory.StartsWith(workspace + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            return outsideWorkingDirectory
                   || command.Contains("../", StringComparison.Ordinal)
                   || command.Contains("..\\", StringComparison.Ordinal)
                   || new ShellCommandInspector(workspacePath).DetectOutsideWorkspacePaths(command).Count > 0;
        }

        var trustedPaths = descriptor.TryGetProperty("trustedReadPaths", out var trusted)
                           && trusted.ValueKind == JsonValueKind.Array
            ? trusted.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString()!)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .ToArray()
            : [];
        var guard = new FileAccessGuard(workspacePath, trustedReadPaths: trustedPaths);
        var fullPath = guard.ResolvePath(string.IsNullOrWhiteSpace(target) ? "." : target);
        return guard.RequiresOutsideWorkspaceApproval(fullPath, operation);
    }
}
