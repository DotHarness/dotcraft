using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

public sealed class WebSearchToolSource(AppConfig config, ChatClientRegistry chatClientRegistry) : IToolSource
{
    public const string ToolNamespace = "web";
    public const string ToolName = "run";

    public string SourceId => "web-search";

    public int Priority => 30;

    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolveRuntime(context, out var runtime) || !IsSupportedRuntime(runtime))
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);

        var definitionId = new ToolDefinitionId(ToolSourceKind.CoreNative, SourceId, new SourceToolId(ToolName));
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(ToolNamespace, ToolName),
            WebSearchToolText.Description,
            WebSearchToolText.InputSchema(),
            annotations: new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["dotcraft/streamArguments"] = JsonSerializer.SerializeToElement(false),
                [ReservedToolSchema.Annotation] = JsonSerializer.SerializeToElement(true)
            },
            provenance: new ToolProvenance(ToolSourceKind.CoreNative, SourceId, "native"));
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"native:{SourceId}:{ToolName}:{context.Revision}"),
            definitionId,
            new WebSearchToolRuntime(runtime, chatClientRegistry),
            ToolBindingLeases.AlwaysAvailable,
            $"native:{SourceId}",
            context.Revision);
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
            [new ToolRegistration(definition, binding, ToolProjectionShape.WebSearch)]);
    }

    internal static bool IsSupportedRuntime(EffectiveModelRuntime runtime) =>
        runtime.IsOpenAICompatible && runtime.IsChatGptOAuth;

    private bool TryResolveRuntime(ToolPlanningContext context, out EffectiveModelRuntime runtime)
    {
        try
        {
            runtime = chatClientRegistry.ResolveMainRuntime(config, context.EffectiveProviderId, context.EffectiveMainModel);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UriFormatException)
        {
            runtime = null!;
            return false;
        }
    }
}

internal sealed class WebSearchToolRuntime(
    EffectiveModelRuntime runtime,
    ChatClientRegistry chatClientRegistry) : IToolRuntime
{
    internal const string FailedErrorCode = "web_search_failed";
    private const int MaxAssistantChars = 4000;
    private const string ReminderTag = "<system-reminder>";

    public async ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arguments);

        var request = new ProviderWebSearchRequest(
            context.ThreadId,
            BuildInput(StreamingFunctionInvokingChatClient.CurrentContext?.Messages ?? []),
            (JsonObject)WithoutNulls(arguments));
        ProviderWebSearchResult searched;
        try
        {
            var provider = chatClientRegistry.GetProviderService<IProviderWebSearch>(runtime)
                           ?? throw new InvalidOperationException(
                               $"Provider '{runtime.ProviderId}' does not support web search.");
            searched = await provider.SearchAsync(runtime, request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or ArgumentException
                                       or IOException or OperationCanceledException)
        {
            var message = ex is OperationCanceledException
                ? "the web search request timed out"
                : TrimError(ex.Message);
            return new ToolExecutionResult(
                false,
                $"web search failed: {message}",
                error: new ToolError(FailedErrorCode, message));
        }

        return ToolExecutionResult.Succeeded(
            searched.Output,
            structuredContent: JsonSerializer.SerializeToElement(searched.Results));
    }

    internal static JsonArray BuildInput(IEnumerable<ChatMessage> messages)
    {
        var tail = new List<(bool IsUser, List<string> Parts)>();
        foreach (var message in messages)
        {
            var isUser = message.Role == ChatRole.User;
            if ((!isUser && message.Role != ChatRole.Assistant) || ThreadContextItems.GetKind(message) is not null)
                continue;

            var parts = message.Contents
                .OfType<TextContent>()
                .Select(static content => content.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)
                               && !(isUser && text.TrimStart().StartsWith(ReminderTag, StringComparison.Ordinal)))
                .ToList();
            if (parts.Count > 0)
                tail.Add((isUser, parts));
        }

        var userIndexes = tail.Select((entry, index) => entry.IsUser ? index : -1).Where(index => index >= 0).ToList();
        var window = tail.Skip(userIndexes.Count switch
        {
            0 => 0,
            1 => userIndexes[0],
            _ => userIndexes[^2]
        }).ToList();
        CapAssistantText(window);

        var input = new JsonArray();
        foreach (var (isUser, parts) in window.Where(static entry => entry.Parts.Count > 0))
        {
            var partType = isUser ? "input_text" : "output_text";
            input.Add(new JsonObject
            {
                ["type"] = "message",
                ["role"] = isUser ? "user" : "assistant",
                ["content"] = new JsonArray(parts
                    .Select(text => (JsonNode)new JsonObject { ["type"] = partType, ["text"] = text })
                    .ToArray())
            });
        }

        return input;
    }

    private static void CapAssistantText(List<(bool IsUser, List<string> Parts)> window)
    {
        var budget = MaxAssistantChars;
        for (var index = window.Count - 1; index >= 0; index--)
        {
            var (isUser, parts) = window[index];
            if (isUser)
                continue;

            for (var part = parts.Count - 1; part >= 0; part--)
            {
                var text = parts[part];
                if (text.Length <= budget)
                {
                    budget -= text.Length;
                    continue;
                }

                if (budget > 0)
                    parts[part] = text[^budget..];
                else
                    parts.RemoveAt(part);
                budget = 0;
            }
        }
    }

    private static JsonNode WithoutNulls(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .Where(static pair => !IsNull(pair.Value))
            .Select(static pair => KeyValuePair.Create(pair.Key, (JsonNode?)WithoutNulls(pair.Value!)))),
        JsonArray array => new JsonArray(array
            .Where(static item => !IsNull(item))
            .Select(static item => (JsonNode?)WithoutNulls(item!))
            .ToArray()),
        _ => node.DeepClone()
    };

    private static bool IsNull(JsonNode? node) => node is null || node.GetValueKind() == JsonValueKind.Null;

    private static string TrimError(string message)
    {
        var trimmed = string.IsNullOrWhiteSpace(message) ? "unknown error" : message.Trim();
        return trimmed.Length <= 1000 ? trimmed : trimmed[..1000];
    }
}
