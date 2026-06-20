using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;

namespace DotCraft.ExternalChannel;

internal sealed class ExternalChannelToolProvider(
    IChannelRuntimeRegistry registry,
    AppConfig? config = null,
    ChannelToolRegistrationService? channelToolRegistration = null)
    : IThreadPluginFunctionProvider, IReservedRuntimeToolNameConfigurator
{
    private const string PluginId = "external-channel";
    private const string PluginIdPrefix = "external-channel:";
    private readonly ChannelToolRegistrationService _channelToolRegistration =
        channelToolRegistration ?? new ChannelToolRegistrationService();

    public void ConfigureReservedToolNames(IEnumerable<string> toolNames)
        => _channelToolRegistration.ConfigureReservedToolNames(toolNames);

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

        var descriptors = _channelToolRegistration
            .GetRegisteredTools(runtime)
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
