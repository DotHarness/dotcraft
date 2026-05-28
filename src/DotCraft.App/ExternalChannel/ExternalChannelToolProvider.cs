using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.ExternalChannel;

internal sealed class ExternalChannelToolProvider(IChannelRuntimeRegistry registry, AppConfig? config = null)
    : IThreadPluginFunctionProvider, IReservedRuntimeToolNameConfigurator
{
    private const string PluginId = "external-channel";
    private const string PluginIdPrefix = "external-channel:";
    private readonly Lock _registrationLock = new();
    private HashSet<string> _reservedRuntimeToolNames = new(StringComparer.Ordinal);

    public void ConfigureReservedToolNames(IEnumerable<string> toolNames)
    {
        lock (_registrationLock)
        {
            _reservedRuntimeToolNames = new HashSet<string>(
                toolNames.Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.Ordinal);
        }
    }

    public IReadOnlyList<PluginFunctionRegistration> CreateFunctionsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames)
    {
        if (string.IsNullOrWhiteSpace(thread.OriginChannel))
            return [];

        if (!registry.TryGet(thread.OriginChannel, out var runtime) || runtime == null)
            return [];

        if (config?.Plugins.IsPluginEnabled(PluginId, defaultEnabled: true) == false
            || config?.Plugins.IsPluginEnabled(PluginIdPrefix + runtime.Name, defaultEnabled: true) == false)
        {
            return [];
        }

        EnsureRuntimeRegistration(runtime);
        var descriptors = runtime
            .GetChannelTools()
            .Where(descriptor => !reservedToolNames.Contains(descriptor.Name))
            .ToArray();
        if (descriptors.Length == 0)
            return [];

        return descriptors
            .Select(descriptor => new PluginFunctionRegistration(
                MapDescriptor(runtime, descriptor),
                new ExternalChannelPluginFunctionInvoker(runtime, descriptor)))
            .ToArray();
    }

    public IReadOnlyList<AITool> CreateToolsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames)
        => CreateFunctionsForThread(thread, reservedToolNames)
            .Select(registration => (AITool)new PluginFunctionRuntimeFunction(registration))
            .ToArray();

    private void EnsureRuntimeRegistration(IChannelRuntime runtime)
    {
        if (runtime is not ExternalChannelHost host || host.AdapterConnection == null)
            return;

        var connection = host.AdapterConnection;
        if (connection.ChannelToolRegistrationFinalized)
            return;

        lock (_registrationLock)
        {
            if (connection.ChannelToolRegistrationFinalized)
                return;

            FinalizeRuntimeRegistration(host, connection, _reservedRuntimeToolNames);
        }
    }

    private static void FinalizeRuntimeRegistration(
        IChannelRuntime runtime,
        AppServerConnection connection,
        IReadOnlySet<string> reservedToolNames)
    {
        var diagnostics = new List<ChannelToolRegistrationDiagnostic>();
        var registered = new List<ChannelToolDescriptor>();

        foreach (var descriptor in connection.DeclaredChannelTools)
        {
            if (reservedToolNames.Contains(descriptor.Name))
            {
                diagnostics.Add(new ChannelToolRegistrationDiagnostic
                {
                    ToolName = descriptor.Name,
                    Code = "ChannelToolNameConflict",
                    Message = $"Tool '{descriptor.Name}' conflicts with an existing runtime tool."
                });
                continue;
            }

            if (!TryValidateDescriptor(descriptor, out var message))
            {
                diagnostics.Add(new ChannelToolRegistrationDiagnostic
                {
                    ToolName = descriptor.Name,
                    Code = "InvalidChannelToolDescriptor",
                    Message = message
                });
                continue;
            }

            RegisterToolDisplay(descriptor);
            registered.Add(descriptor);
        }

        connection.SetChannelToolRegistration(registered, diagnostics);
    }

    private static PluginFunctionDescriptor MapDescriptor(
        IChannelRuntime runtime,
        ChannelToolDescriptor descriptor)
        => new()
        {
            PluginId = PluginIdPrefix + runtime.Name,
            Namespace = "external_channel",
            Name = descriptor.Name,
            Description = descriptor.Description,
            InputSchema = descriptor.InputSchema,
            OutputSchema = descriptor.OutputSchema,
            Display = descriptor.Display == null
                ? null
                : new PluginFunctionDisplay
                {
                    Title = descriptor.Display.Title,
                    Subtitle = descriptor.Display.Subtitle,
                    Icon = descriptor.Display.Icon
                },
            Approval = descriptor.Approval == null
                ? null
                : new PluginFunctionApprovalDescriptor
                {
                    Kind = descriptor.Approval.Kind,
                    TargetArgument = descriptor.Approval.TargetArgument,
                    Operation = descriptor.Approval.Operation,
                    OperationArgument = descriptor.Approval.OperationArgument
                },
            RequiresChatContext = descriptor.RequiresChatContext,
            DeferLoading = descriptor.DeferLoading
        };

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

    private sealed class ExternalChannelPluginFunctionInvoker(
        IChannelRuntime runtime,
        ChannelToolDescriptor descriptor) : IPluginFunctionInvoker
    {
        public async ValueTask<PluginFunctionInvocationResult> InvokeAsync(
            PluginFunctionInvocationContext context,
            CancellationToken cancellationToken)
        {
            ExtChannelToolCallResult result;
            try
            {
                result = await runtime.ExecuteToolAsync(
                    new ExtChannelToolCallParams
                    {
                        ThreadId = context.Execution.ThreadId,
                        TurnId = context.Execution.TurnId,
                        CallId = context.CallId,
                        Tool = descriptor.Name,
                        Arguments = context.Arguments,
                        Context = new ExtChannelToolCallContext
                        {
                            ChannelName = context.Execution.OriginChannel,
                            ChannelContext = context.Execution.ChannelContext,
                            SenderId = context.Execution.SenderId,
                            GroupId = context.Execution.GroupId
                        }
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return PluginFunctionInvocationResult.Failed(
                    "ExternalChannelToolTimeout",
                    $"Tool '{descriptor.Name}' timed out while waiting for adapter response.");
            }

            return new PluginFunctionInvocationResult
            {
                Success = result.Success,
                ContentItems = result.ContentItems?.Select(MapContentItem).ToArray(),
                StructuredResult = result.StructuredResult,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }

        private static PluginFunctionContentItem MapContentItem(ExtChannelToolContentItem item)
            => new()
            {
                Type = item.Type,
                Text = item.Text,
                DataBase64 = item.DataBase64,
                MediaType = item.MediaType
            };
    }
}
