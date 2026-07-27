using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;

namespace DotCraft.Channels;

/// <summary>
/// Optional runtime hook for adapter-backed channels whose declared tool descriptors are stored on
/// an AppServer connection and must be validated before runtime exposure.
/// </summary>
public interface IChannelToolRegistrationSource
{
    /// <summary>
    /// Gets the adapter connection that declared channel tools during initialize, when available.
    /// </summary>
    AppServerConnection? ChannelToolRegistrationConnection { get; }
}

/// <summary>
/// Validates and caches channel-native tool descriptors for both legacy origin-channel tools and
/// app-bound social channel tools.
/// </summary>
public sealed class ChannelToolRegistrationService
{
    private readonly Lock _registrationLock = new();

    /// <summary>
    /// Returns the registered channel tools for a runtime, validating adapter-declared descriptors
    /// once when the runtime exposes an AppServer connection.
    /// </summary>
    public IReadOnlyList<ChannelToolDescriptor> GetRegisteredTools(IChannelRuntime runtime) =>
        GetRegisteredTools(runtime, out _);

    /// <summary>
    /// Returns registered channel tools and descriptor diagnostics.
    /// </summary>
    public IReadOnlyList<ChannelToolDescriptor> GetRegisteredTools(
        IChannelRuntime runtime,
        out IReadOnlyList<ChannelToolRegistrationDiagnostic> diagnostics)
    {
        if (runtime is IChannelToolRegistrationSource { ChannelToolRegistrationConnection: { } connection })
        {
            EnsureConnectionRegistration(connection);
            diagnostics = connection.ChannelToolDiagnostics;
            return connection.RegisteredChannelTools;
        }

        return ValidateDeclaredTools(runtime.GetChannelTools(), out diagnostics);
    }

    private void EnsureConnectionRegistration(AppServerConnection connection)
    {
        if (connection.ChannelToolRegistrationFinalized)
            return;

        lock (_registrationLock)
        {
            if (connection.ChannelToolRegistrationFinalized)
                return;

            var registered = ValidateDeclaredTools(
                connection.DeclaredChannelTools,
                out var diagnostics);
            connection.SetChannelToolRegistration(registered, diagnostics);
        }
    }

    private static IReadOnlyList<ChannelToolDescriptor> ValidateDeclaredTools(
        IReadOnlyList<ChannelToolDescriptor> declaredTools,
        out IReadOnlyList<ChannelToolRegistrationDiagnostic> diagnostics)
    {
        var registered = new List<ChannelToolDescriptor>();
        var warnings = new List<ChannelToolRegistrationDiagnostic>();
        var acceptedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var descriptor in declaredTools)
        {
            if (!TryValidateDescriptor(descriptor, out var message))
            {
                warnings.Add(new ChannelToolRegistrationDiagnostic
                {
                    ToolName = descriptor.Name,
                    Code = "InvalidChannelToolDescriptor",
                    Message = message
                });
                continue;
            }

            if (!acceptedNames.Add(descriptor.Name))
            {
                warnings.Add(new ChannelToolRegistrationDiagnostic
                {
                    ToolName = descriptor.Name,
                    Code = "DuplicateChannelToolDescriptor",
                    Message = $"Channel tool '{descriptor.Name}' is declared more than once; only the first declaration is used."
                });
                continue;
            }

            RegisterToolDisplay(descriptor);
            registered.Add(descriptor);
        }

        diagnostics = warnings;
        return registered;
    }

    private static void RegisterToolDisplay(ChannelToolDescriptor descriptor)
    {
        if (descriptor.Display != null)
        {
            ToolRegistry.RegisterDisplay(
                descriptor.Name,
                title: descriptor.Display.Title,
                subtitle: descriptor.Display.Subtitle,
                icon: descriptor.Display.Icon);
        }
    }

    private static bool TryValidateDescriptor(ChannelToolDescriptor descriptor, out string message)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Name))
        {
            message = "Tool name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(descriptor.Description))
        {
            message = $"Tool '{descriptor.Name}' must declare a description.";
            return false;
        }

        if (descriptor.InputSchema == null)
        {
            message = $"Tool '{descriptor.Name}' must declare inputSchema.";
            return false;
        }

        if (!PluginFunctionSchemaValidator.TryValidateSchema(descriptor.InputSchema, out message))
        {
            message = $"Tool '{descriptor.Name}' has an invalid inputSchema: {message}";
            return false;
        }

        if (descriptor.OutputSchema != null
            && !PluginFunctionSchemaValidator.TryValidateSchema(descriptor.OutputSchema, out message))
        {
            message = $"Tool '{descriptor.Name}' has an invalid outputSchema: {message}";
            return false;
        }

        if (descriptor.Approval != null
            && !TryValidateApprovalDescriptor(descriptor, out message))
        {
            message = $"Tool '{descriptor.Name}' has an invalid approval descriptor: {message}";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static bool TryValidateApprovalDescriptor(ChannelToolDescriptor descriptor, out string message)
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
}
