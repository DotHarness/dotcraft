using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.AppBinding;

/// <summary>
/// Test-only projection for legacy assertions that predate M1. Production no longer exposes
/// App Binding tools as executable <see cref="AIFunction"/> instances.
/// </summary>
internal static class LegacyAppBindingTestToolExtensions
{
    public static IReadOnlyList<AITool> CreateRuntimeToolsForThread(
        this AppBindingService service,
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames)
    {
        var registrations = new LegacyAppBindingToolSource(service)
            .GetRegistrationsAsync(new ToolPlanningContext(
                thread.Id,
                null,
                thread.WorkspacePath,
                "agent",
                null,
                null,
                1))
            .AsTask().GetAwaiter().GetResult();
        return registrations
            .Where(registration => !reservedToolNames.Contains(registration.Definition.Name.Name))
            .Select(static registration => (AITool)new LegacyRegistrationFunction(registration))
            .ToArray();
    }

    private sealed class LegacyRegistrationFunction(ToolRegistration registration) : AIFunction, IDeferredToolMetadata
    {
        public override string Name => registration.Definition.Name.Name;
        public override string Description => registration.Definition.Description;
        public override JsonElement JsonSchema => registration.Definition.InputSchema;
        public override JsonElement? ReturnJsonSchema => registration.Definition.OutputSchema;
        public override MethodInfo? UnderlyingMethod => null;
        public override JsonSerializerOptions JsonSerializerOptions => SessionWireJsonOptions.Default;
        public bool DeferLoading => registration.Exposure == ToolExposure.Deferred;
        public string? DeferredToolSource => registration.Definition.Provenance.SourceId;
        public string? DeferredToolNamespace => registration.Definition.Name.Namespace;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            var scope = PluginFunctionExecutionScope.Current
                        ?? throw new InvalidOperationException("A test execution scope is required.");
            var callId = $"legacy_test_{Guid.NewGuid():N}";
            var invocation = new ToolInvocationContext(
                scope.ThreadId,
                scope.TurnId,
                callId,
                ToolInvocationAudience.Model,
                registration.Definition.Name,
                registration.Definition.Id,
                registration.Binding.Id,
                1,
                DateTimeOffset.UtcNow);
            var args = new JsonObject(arguments.Select(pair =>
                KeyValuePair.Create(pair.Key, pair.Value is JsonNode node
                    ? node.DeepClone()
                    : JsonSerializer.SerializeToNode(pair.Value, SessionWireJsonOptions.Default))));
            var item = new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(scope.NextItemSequence()),
                TurnId = scope.TurnId,
                Type = ItemType.DynamicToolCall,
                Status = ItemStatus.Started,
                CreatedAt = DateTimeOffset.UtcNow
            };
            scope.Turn.Items.Add(item);
            scope.EmitItemStarted(item);

            var lease = await registration.Binding.Lease.CheckAsync(invocation, cancellationToken);
            ToolExecutionResult result;
            if (!lease.IsAvailable)
            {
                result = new ToolExecutionResult(
                    false,
                    lease.Error?.Message ?? "The App Binding tool is unavailable.",
                    error: lease.Error);
            }
            else
            {
                result = await registration.Binding.Runtime.InvokeAsync(invocation, args, cancellationToken);
            }

            var sourceResult = result.RawSourceResult is JsonElement raw
                ? raw.Deserialize<AppBoundToolCallResult>(SessionWireJsonOptions.Default)
                : null;
            var errorCode = sourceResult?.ErrorCode ?? (lease.IsAvailable ? result.Error?.Code : AppBindingErrorCodes.Offline);
            var errorMessage = sourceResult?.ErrorMessage ?? result.Error?.Message;
            item.Status = ItemStatus.Completed;
            item.CompletedAt = DateTimeOffset.UtcNow;
            item.Payload = new DynamicToolCallPayload
            {
                Namespace = registration.Definition.Name.Namespace,
                ToolName = registration.Definition.Name.Name,
                CallId = callId,
                Arguments = args,
                Status = result.Success ? "completed" : "failed",
                Success = result.Success,
                ContentItems = sourceResult?.ContentItems?.Select(static content => new PluginFunctionContentItem
                {
                    Type = content.Type,
                    Text = content.Text,
                    DataBase64 = content.DataBase64,
                    MediaType = content.MediaType
                }).ToArray(),
                StructuredContent = sourceResult?.StructuredResult?.DeepClone(),
                ErrorCode = errorCode,
                ErrorMessage = errorMessage
            };
            scope.EmitItemCompleted(item);
            return result.Content;
        }
    }
}
