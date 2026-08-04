using DotCraft.Channels;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Tools;
using DotCraft.AppServer;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.ExternalChannel;

internal sealed class ExternalChannelToolProvider(
    IChannelRuntimeRegistry registry,
    AppConfig? config = null,
    ChannelToolRegistrationService? channelToolRegistration = null)
    : IThreadPluginToolSourceProvider
{
    private const string PluginId = "external-channel";
    private const string PluginIdPrefix = "external-channel:";
    private readonly ChannelToolRegistrationService _channelToolRegistration =
        channelToolRegistration ?? new ChannelToolRegistrationService();

    public IReadOnlyList<IToolSource> CreateToolSourcesForThread(SessionThread thread)
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

        var registrations = _channelToolRegistration
            .GetRegisteredTools(runtime)
            .Select(descriptor => new PluginToolRegistration(
                MapDescriptor(runtime, descriptor),
                new ExternalChannelPluginToolInvoker(runtime, descriptor)))
            .ToArray();
        if (registrations.Length == 0)
            return [];

        return
        [
            new PluginToolSource(
                PluginIdPrefix + runtime.Name,
                registrations,
                new PluginToolInvocationMetadata(
                    thread.OriginChannel,
                    thread.ChannelContext,
                    thread.UserId),
                new ExternalChannelPluginLease(
                    registry,
                    runtime,
                    config,
                    PluginIdPrefix + runtime.Name),
                priority: 100)
        ];
    }

    private static PluginFunctionDescriptor MapDescriptor(
        IChannelRuntime runtime,
        ChannelToolSpec descriptor)
        => new()
        {
            PluginId = PluginIdPrefix + runtime.Name,
            FunctionId = descriptor.Name,
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

    private sealed class ExternalChannelPluginToolInvoker(
        IChannelRuntime runtime,
        ChannelToolSpec descriptor) : IPluginToolInvoker
    {
        public async ValueTask<PluginFunctionInvocationResult> InvokeAsync(
            PluginToolInvocationContext context,
            CancellationToken cancellationToken)
        {
            if (descriptor.RequiresChatContext
                && string.IsNullOrWhiteSpace(context.ChannelContext)
                && string.IsNullOrWhiteSpace(context.GroupId))
            {
                return PluginFunctionInvocationResult.Failed(
                    "MissingChatContext",
                    $"Function '{descriptor.Name}' requires channel chat context, but this thread does not have one.");
            }

            ChannelToolInvocationResult result;
            try
            {
                result = await runtime.ExecuteToolAsync(
                    new ChannelToolInvocationRequest
                    {
                        ThreadId = context.Invocation.ThreadId,
                        TurnId = context.Invocation.TurnId ?? string.Empty,
                        CallId = context.Invocation.CallId,
                        Tool = descriptor.Name,
                        Arguments = context.Arguments,
                        Context = new ChannelToolInvocationContext
                        {
                            ChannelName = context.OriginChannel ?? string.Empty,
                            ChannelContext = context.ChannelContext,
                            SenderId = context.SenderId,
                            GroupId = context.GroupId
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

        private static PluginFunctionContentItem MapContentItem(ChannelToolInvocationContentItem item)
            => new()
            {
                Type = item.Type,
                Text = item.Text,
                DataBase64 = item.DataBase64,
                MediaType = item.MediaType
            };
    }

    private sealed class ExternalChannelPluginLease(
        IChannelRuntimeRegistry registry,
        IChannelRuntime runtime,
        AppConfig? config,
        string pluginId) : IToolBindingLease
    {
        public ValueTask<ToolBindingLeaseResult> CheckAsync(
            ToolInvocationContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var enabled = config?.Plugins.IsPluginEnabled(PluginId, defaultEnabled: true) != false
                && config?.Plugins.IsPluginEnabled(pluginId, defaultEnabled: true) != false;
            var available = enabled
                && registry.TryGet(runtime.Name, out var current)
                && ReferenceEquals(current, runtime)
                && runtime.IsReady;
            return ValueTask.FromResult(available
                ? ToolBindingLeaseResult.Available
                : ToolBindingLeaseResult.Unavailable(
                    $"External channel plugin runtime '{runtime.Name}' is disconnected or disabled."));
        }
    }
}
