using System.Net.Http.Headers;
using System.Text.Json;
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
        var token = await _openAIAuthService!.GetAccessTokenAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var accountId = ResolveChatGptAccountId(runtime);
        if (!string.IsNullOrWhiteSpace(accountId))
            request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.AccountIdHeader, accountId);
        request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.OriginatorHeader, OpenAIAuthConstants.Originator);
        return await _chatGptHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendImageEditRequestAsync(
        Uri endpoint,
        EffectiveModelRuntime runtime,
        string imageModel,
        string prompt,
        IReadOnlyList<OpenAIImageEditInput> images,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var baseUri = new Uri(endpoint.ToString().TrimEnd('/') + "/");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "images/edits"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", DotCraftUserAgentPipelinePolicy.UserAgentValue);
        await ApplyImageEditAuthHeadersAsync(request, runtime, forceRefresh, cancellationToken).ConfigureAwait(false);

        var content = new MultipartFormDataContent
        {
            { new StringContent(imageModel), "model" },
            { new StringContent(prompt), "prompt" },
            { new StringContent("b64_json"), "response_format" },
            { new StringContent("png"), "output_format" }
        };

        foreach (var image in images)
        {
            var imageContent = new ByteArrayContent(image.Bytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(image.MediaType);
            content.Add(imageContent, "image[]", image.FileName);
        }

        request.Content = content;
        return await _chatGptHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyImageEditAuthHeadersAsync(
        HttpRequestMessage request,
        EffectiveModelRuntime runtime,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (runtime.IsChatGptOAuth)
        {
            if (_openAIAuthService is null)
                throw new InvalidOperationException(
                    "ChatGPT OAuth provider requested but no IOpenAIAuthService was registered.");

            var token = await _openAIAuthService.GetAccessTokenAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var accountId = ResolveChatGptAccountId(runtime);
            if (!string.IsNullOrWhiteSpace(accountId))
                request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.AccountIdHeader, accountId);
            request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.OriginatorHeader, OpenAIAuthConstants.Originator);

            var installationId = _installationIdProvider?.GetInstallationId();
            if (!string.IsNullOrWhiteSpace(installationId))
                request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.InstallationIdHeader, installationId);

            var sessionKey = ProviderRequestContextScope.Current?.ConversationIdentity.CurrentThreadId;
            if (!string.IsNullOrWhiteSpace(sessionKey))
            {
                var trimmed = sessionKey.Trim();
                request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.SessionIdHeader, trimmed);
                request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.ThreadIdHeader, trimmed);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(runtime.ApiKey))
            throw new ArgumentException("API key must be configured.", nameof(runtime));

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtime.ApiKey);
    }

    private static async Task<byte[]> ReadImageEditResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            var body = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var snippet = body.Length > 1000 ? body[..1000] : body;
                throw new HttpRequestException(
                    $"Image edit request failed with HTTP {(int)response.StatusCode}: {snippet}");
            }

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array ||
                data.GetArrayLength() == 0 ||
                !data[0].TryGetProperty("b64_json", out var b64Json) ||
                b64Json.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("Image edit response did not include image bytes.");
            }

            var base64 = b64Json.GetString();
            if (string.IsNullOrWhiteSpace(base64))
                throw new InvalidOperationException("Image edit response included empty image bytes.");

            return Convert.FromBase64String(base64);
        }
    }

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
