using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;

namespace DotCraft.Agents;

internal sealed class OpenRouterHttpTransport(OpenRouterRoutingPreferences routing, EffectiveModelRuntime resolved,
    string apiKey, IProviderHttpTransport? remote) : IProviderHttpTransport
{
    public HttpClient CreateClient(string providerId, Uri endpoint)
    {
        var upstream = resolved.IsRemote
            ? (remote ?? throw new InvalidOperationException("Remote model transport is not configured.")).CreateClient(providerId, endpoint)
            : new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        return new HttpClient(new Handler(upstream, routing, resolved, apiKey)) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class Handler(HttpClient upstream, OpenRouterRoutingPreferences routing,
        EffectiveModelRuntime runtime, string key) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var outgoing = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                if (!header.Key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase)
                    && !header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    outgoing.Headers.TryAddWithoutValidation(header.Key, header.Value);
            outgoing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            outgoing.Headers.TryAddWithoutValidation("X-OpenRouter-Metadata", "enabled");
            if (ProviderRequestContextScope.Current?.CurrentIdentity is { } identity)
                outgoing.Headers.TryAddWithoutValidation("x-session-id", identity.RootThreadId);
            var call = OpenRouterChatClient.Active.Value;
            if (request.Content is not null)
            {
                var json = await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var body = JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("Expected a model request object.");
                Shape(body, call);
                outgoing.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }
            var response = await upstream.SendAsync(outgoing, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (call is not null && response.IsSuccessStatusCode)
                response.Content = new OpenRouterCaptureContent(response.Content, call);
            return response;
        }

        private void Shape(JsonObject body, OpenRouterCall? call)
        {
            if (!routing.IsEmpty) body["provider"] = JsonSerializer.SerializeToNode(routing, OpenRouterJson.Options);
            if (runtime.Protocol != "anthropic" && runtime.Model.StartsWith("anthropic/", StringComparison.Ordinal)
                && ProviderPipelineOptionsScope.Current is { PromptCachingEnabled: true } caching)
            {
                var cache = new JsonObject { ["type"] = "ephemeral" };
                if (caching.PromptCacheTtl is "5m" or "1h") cache["ttl"] = caching.PromptCacheTtl;
                body["cache_control"] = cache;
            }
            if (runtime.Protocol == "openai-responses")
            {
                body["store"] = false;
                body.Remove("previous_response_id");
                var include = body["include"] as JsonArray ?? new JsonArray();
                if (!include.Any(item => item?.GetValue<string>() == "reasoning.encrypted_content")) include.Add("reasoning.encrypted_content");
                if (include.Parent is null) body["include"] = include;
                if (runtime.ModelMetadata?.Reasoning is { SupportedEfforts.Count: 0 } && body["reasoning"] is JsonObject reasoning)
                    reasoning.Remove("effort");
            }
            if (runtime.Protocol == "openai-chat-completions")
            {
                body["usage"] = new JsonObject { ["include"] = true };
                var effort = ProviderReasoningOptions.Resolve(call?.Options);
                body.Remove("reasoning_effort");
                if (effort is { } selected)
                    body["reasoning"] = selected == ProviderReasoningEffort.None
                        ? new JsonObject { ["enabled"] = false }
                        : runtime.ModelMetadata?.Reasoning is { SupportedEfforts.Count: 0 } ? new JsonObject { ["enabled"] = true }
                        : new JsonObject { ["effort"] = selected == ProviderReasoningEffort.ExtraHigh ? "xhigh" : selected.ToString().ToLowerInvariant() };
                if (body["messages"] is JsonArray messages && call is not null)
                {
                    var assistants = call.History.Where(message => message.Role == Microsoft.Extensions.AI.ChatRole.Assistant).ToArray();
                    var index = 0;
                    foreach (var message in messages.OfType<JsonObject>().Where(message => message["role"]?.GetValue<string>() == "assistant"))
                    {
                        var source = index < assistants.Length ? assistants[index] : null;
                        index++;
                        var serialized = source?.Contents.Select(content => content.AdditionalProperties?.GetValueOrDefault(OpenRouterChatClient.ReasoningDetailsKey)).OfType<string>().LastOrDefault();
                        if (serialized is not null) message["reasoning_details"] = JsonNode.Parse(serialized);
                        message.Remove("reasoning_content");
                    }
                }
            }
            if (runtime.Protocol == "anthropic" && ProviderReasoningOptions.Resolve(call?.Options) == ProviderReasoningEffort.None)
                body["thinking"] = new JsonObject { ["type"] = "disabled" };
            if (runtime.Protocol == "anthropic" && runtime.ModelMetadata?.Reasoning is { SupportedEfforts.Count: 0 }
                && body["output_config"] is JsonObject output) output.Remove("effort");
        }

        protected override void Dispose(bool disposing) { if (disposing) upstream.Dispose(); base.Dispose(disposing); }
    }
}
