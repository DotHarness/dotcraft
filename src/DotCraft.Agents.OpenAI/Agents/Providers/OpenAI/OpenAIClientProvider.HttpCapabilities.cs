using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;

namespace DotCraft.Agents;

public sealed partial class OpenAIClientProvider
{
    private async Task<HttpResponseMessage> SendChatGptCodexModelsRequestAsync(
        Uri requestUri,
        EffectiveModelRuntime runtime,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!runtime.IsRemote)
        {
            var token = await _openAIAuthService!.GetAccessTokenAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        var accountId = ResolveChatGptAccountId(runtime);
        if (!string.IsNullOrWhiteSpace(accountId))
            request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.AccountIdHeader, accountId);
        request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.OriginatorHeader, OpenAIAuthConstants.Originator);
        return await HttpClientFor(runtime).SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
    }

    async Task<ProviderImageResult> IProviderImageGeneration.GenerateImageAsync(
        EffectiveModelRuntime runtime,
        ProviderImageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(request);
        if (!runtime.IsOpenAICompatible)
            throw new ArgumentException($"Provider '{runtime.ProviderId}' does not use the OpenAI protocol.", nameof(runtime));
        if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var endpoint))
            throw new ArgumentException("Endpoint must be an absolute URI.", nameof(runtime));

        var model = NormalizeRequiredModel(request.Model);
        using var pipeline = ProviderPipelineOptionsScope.Push(new ProviderPipelineOptions(
            runtime with { Model = model }, null, null, false, "standard", false, null));
        var isEdit = request.ReferenceImageUrls.Count > 0;
        var body = new JsonObject();
        if (isEdit)
        {
            body["images"] = new JsonArray(request.ReferenceImageUrls
                .Select(static url => (JsonNode)new JsonObject { ["image_url"] = url })
                .ToArray());
        }
        body["prompt"] = request.Prompt;
        body["background"] = request.TransparentBackground ? "transparent" : "opaque";
        body["model"] = model;
        body["quality"] = "auto";
        body["size"] = "auto";
        var requestUri = new Uri(
            new Uri(endpoint.ToString().TrimEnd('/') + "/"),
            isEdit ? "images/edits" : "images/generations");
        var json = body.ToJsonString();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(NormalizeNetworkTimeoutSeconds(runtime.NetworkTimeoutSeconds)));

        var response = await SendImageRequestAsync(requestUri, runtime, json, request.TurnId, forceRefresh: false, timeoutCts.Token)
            .ConfigureAwait(false);
        if (runtime.IsChatGptOAuth && !runtime.IsRemote && response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            response = await SendImageRequestAsync(requestUri, runtime, json, request.TurnId, forceRefresh: true, timeoutCts.Token)
                .ConfigureAwait(false);
        }

        return await ReadImageResponseAsync(response, timeoutCts.Token).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendImageRequestAsync(
        Uri requestUri,
        EffectiveModelRuntime runtime,
        string json,
        string turnId,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", DotCraftUserAgentPipelinePolicy.UserAgentValue);
        request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.ImageTurnIdHeader, turnId);
        request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.OriginatorHeader, OpenAIAuthConstants.Originator);
        await ApplyImageAuthHeadersAsync(request, runtime, forceRefresh, cancellationToken).ConfigureAwait(false);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await HttpClientFor(runtime).SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyImageAuthHeadersAsync(
        HttpRequestMessage request,
        EffectiveModelRuntime runtime,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (runtime.IsChatGptOAuth)
        {
            if (!runtime.IsRemote && _openAIAuthService is null)
                throw new InvalidOperationException(
                    "ChatGPT OAuth provider requested but no IOpenAIAuthService was registered.");

            if (!runtime.IsRemote)
            {
                var token = await _openAIAuthService!.GetAccessTokenAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            var accountId = ResolveChatGptAccountId(runtime);
            if (!string.IsNullOrWhiteSpace(accountId))
                request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.AccountIdHeader, accountId);

            var installationId = _installationIdProvider?.GetInstallationId();
            if (!string.IsNullOrWhiteSpace(installationId))
                request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.InstallationIdHeader, installationId);

            var sessionKey = OpenAIResponsesCodexMetadata.ResolveRoutingIdentity().SessionId;
            if (!string.IsNullOrWhiteSpace(sessionKey))
            {
                var trimmed = sessionKey.Trim();
                request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.SessionIdHeader, trimmed);
                request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.ThreadIdHeader, trimmed);
            }

            return;
        }

        if (runtime.IsRemote)
            return;
        if (string.IsNullOrWhiteSpace(runtime.ApiKey))
            throw new ArgumentException("API key must be configured.", nameof(runtime));

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtime.ApiKey);
    }

    private static async Task<ProviderImageResult> ReadImageResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            var requestId = response.Headers.TryGetValues(OpenAIAuthConstants.ImagegenRequestIdHeader, out var values)
                ? values.FirstOrDefault(static value => !string.IsNullOrEmpty(value))
                : null;
            var body = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var snippet = body.Length > 1000 ? body[..1000] : body;
                throw new ProviderImageException(
                    $"Images API request failed with HTTP {(int)response.StatusCode}: {snippet}",
                    requestId);
            }

            string? base64;
            string? generationId;
            try
            {
                using var document = JsonDocument.Parse(body);
                var image = document.RootElement.TryGetProperty("data", out var data)
                            && data.ValueKind == JsonValueKind.Array
                            && data.GetArrayLength() > 0
                    ? data[0]
                    : default;
                base64 = ReadImageString(image, "b64_json");
                generationId = ReadImageString(image, "generation_id");
            }
            catch (JsonException ex)
            {
                throw new ProviderImageException("Images API returned an invalid response.", requestId, ex);
            }

            if (string.IsNullOrWhiteSpace(base64))
                throw new ProviderImageException("Images API returned no image data.", requestId);

            try
            {
                return new ProviderImageResult(Convert.FromBase64String(base64.Trim()), requestId, generationId);
            }
            catch (FormatException ex)
            {
                throw new ProviderImageException("Images API returned invalid image data.", requestId, ex);
            }
        }
    }

    private static string? ReadImageString(JsonElement image, string name) =>
        image.ValueKind == JsonValueKind.Object
        && image.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task<ChatGptCodexModelsHttpResponse> ReadChatGptCodexModelsResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            var body = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new ChatGptCodexModelsHttpResponse(
                StatusCode: (int)response.StatusCode,
                Content: body,
                ETag: response.Headers.ETag?.Tag);
        }
    }

    private static Uri BuildChatGptCodexModelsUri(EffectiveModelRuntime runtime, string clientVersion)
    {
        if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var endpoint))
            throw new ArgumentException("Endpoint must be an absolute URI.", nameof(runtime));

        var baseUri = new Uri(endpoint.ToString().TrimEnd('/') + "/");
        var uriBuilder = new UriBuilder(new Uri(baseUri, "models"))
        {
            Query = $"client_version={Uri.EscapeDataString(clientVersion.Trim())}"
        };
        return uriBuilder.Uri;
    }

}
