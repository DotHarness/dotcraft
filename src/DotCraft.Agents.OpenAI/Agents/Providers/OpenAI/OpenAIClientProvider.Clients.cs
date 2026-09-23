using System.ClientModel;
using System.ClientModel.Primitives;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using OpenAI;
using OpenAI.Images;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

public sealed partial class OpenAIClientProvider
{
    private OpenAIClient GetOpenAIClient(OpenAIClientKey key)
    {
        return _openAIClients.GetOrAdd(key, static (clientKey, provider) =>
        {
            var options = CreateClientOptions(
                clientKey.Endpoint,
                clientKey.NetworkTimeoutSeconds);
            if (clientKey.IsRemote)
                options.Transport = new HttpClientPipelineTransport(provider.RemoteHttpClient(clientKey.ProviderId, clientKey.Endpoint));
            if (clientKey.AuthMethod == ModelProviderAuthMethods.ChatGptOAuth)
            {
                if (!clientKey.IsRemote && provider._openAIAuthService is null)
                    throw new InvalidOperationException(
                        "ChatGPT OAuth provider requested but no IOpenAIAuthService was registered.");

                var installationId = provider._installationIdProvider?.GetInstallationId();
                options.AddPolicy(
                    clientKey.IsRemote ? new OpenAIRequestMetadataPolicy(installationId, provider._logger) : new OpenAIOAuthPipelinePolicy(
                        provider._openAIAuthService!,
                        clientKey.AccountId,
                        installationId,
                        provider._logger),
                    PipelinePosition.BeforeTransport);
                if (!string.IsNullOrWhiteSpace(installationId))
                {
                    options.AddPolicy(
                        new OpenAIResponsesClientMetadataPipelinePolicy(installationId, provider._logger),
                        PipelinePosition.PerCall);
                }
                options.AddPolicy(
                    new OpenAIResponsesRequestCompressionPipelinePolicy(),
                    PipelinePosition.PerCall);

                // The SDK requires a credential; the selected transport supplies the actual authorization.
                return new OpenAIClient(new ApiKeyCredential("chatgpt-oauth"), options);
            }

            return new OpenAIClient(new ApiKeyCredential(clientKey.IsRemote ? "remote" : clientKey.ApiKey), options);
        }, this);
    }

    private ResponsesClient GetOpenAIResponsesClient(OpenAIClientKey key) =>
        _openAIResponsesClients.GetOrAdd(key, static (clientKey, provider) =>
            provider.GetOpenAIClient(clientKey).GetResponsesClient(), this);

    private OpenAIResponsesLiteChatClient CreateOpenAIResponsesLiteChatClient(OpenAIChatClientKey key)
    {
        var context = GetOpenAIResponsesLiteClient(key.Client);
        return new OpenAIResponsesLiteChatClient(context.Client, key.Model, context.InstallationId);
    }

    private ResponsesLiteClientContext GetOpenAIResponsesLiteClient(OpenAIClientKey key) =>
        _openAIResponsesLiteClients.GetOrAdd(key, static (clientKey, provider) =>
        {
            if (clientKey.AuthMethod != ModelProviderAuthMethods.ChatGptOAuth)
                throw new InvalidOperationException("Responses Lite requires ChatGPT OAuth authentication.");
            if (!clientKey.IsRemote && provider._openAIAuthService is null)
                throw new InvalidOperationException(
                    "ChatGPT OAuth provider requested but no IOpenAIAuthService was registered.");

            var installationId = provider.ResolveInstallationId();
            var options = CreateResponsesLiteClientOptions(
                clientKey.Endpoint,
                clientKey.NetworkTimeoutSeconds);
            if (clientKey.IsRemote)
                options.Transport = new HttpClientPipelineTransport(provider.RemoteHttpClient(clientKey.ProviderId, clientKey.Endpoint));
            options.AddPolicy(
                clientKey.IsRemote ? new OpenAIRequestMetadataPolicy(installationId, provider._logger) : new OpenAIOAuthPipelinePolicy(
                    provider._openAIAuthService!,
                    clientKey.AccountId,
                    installationId,
                    provider._logger),
                PipelinePosition.BeforeTransport);
            // Capture below OAuth so every physical attempt records final headers and wire bytes.
            options.AddPolicy(new LlmHttpCapturePipelinePolicy(), PipelinePosition.BeforeTransport);
            var client = new OpenAIClient(new ApiKeyCredential("chatgpt-oauth"), options).GetResponsesClient();
            return new ResponsesLiteClientContext(client, installationId);
        }, this);

    private static string NormalizeRequiredModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be configured.", nameof(model));

        return model.Trim();
    }

    private string ResolveInstallationId()
    {
        if (_installationIdProvider is null)
        {
            throw new InvalidOperationException(
                "ChatGPT OAuth provider requested but no OpenAIInstallationIdProvider was registered.");
        }

        var installationId = _installationIdProvider.GetInstallationId();
        return string.IsNullOrWhiteSpace(installationId)
            ? throw new InvalidOperationException("OpenAI installation id provider returned an empty value.")
            : installationId;
    }

    private readonly record struct OpenAIClientKey(
        string ProviderId,
        bool IsRemote,
        Uri Endpoint,
        string ApiKey,
        int NetworkTimeoutSeconds,
        string AuthMethod,
        string? AccountId)
    {
        public static OpenAIClientKey From(EffectiveModelRuntime runtime)
        {
            var authMethod = ModelProviderAuthMethods.Normalize(runtime.AuthMethod);
            if (!runtime.IsRemote && authMethod == ModelProviderAuthMethods.ApiKey &&
                string.IsNullOrWhiteSpace(runtime.ApiKey))
            {
                throw new ArgumentException("API key must be configured.", nameof(runtime));
            }

            if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var endpoint))
                throw new ArgumentException("Endpoint must be an absolute URI.", nameof(runtime));

            return new OpenAIClientKey(
                runtime.ProviderId,
                runtime.IsRemote,
                endpoint,
                runtime.ApiKey,
                NormalizeNetworkTimeoutSeconds(runtime.NetworkTimeoutSeconds),
                authMethod,
                runtime.ChatGptAccountId);
        }
    }

    private readonly record struct OpenAIChatClientKey(
        OpenAIClientKey Client,
        string Model,
        bool UseResponsesLite)
    {
        public static OpenAIChatClientKey From(EffectiveModelRuntime runtime) =>
            new(
                OpenAIClientKey.From(runtime),
                NormalizeRequiredModel(runtime.Model),
                runtime.IsChatGptOAuth && runtime.UseResponsesLite);
    }

    private sealed record ResponsesLiteClientContext(
        ResponsesClient Client,
        string InstallationId);

    private readonly record struct OpenAIImageClientKey(OpenAIClientKey Client, string Model)
    {
        public static OpenAIImageClientKey From(EffectiveModelRuntime runtime, string imageModel) =>
            new(OpenAIClientKey.From(runtime), NormalizeRequiredModel(imageModel));
    }

    private static int NormalizeNetworkTimeoutSeconds(int seconds) => Math.Max(1, seconds);

    private HttpClient RemoteHttpClient(string providerId, Uri endpoint) =>
        (_httpTransport ?? throw new InvalidOperationException("Remote model transport is not registered."))
            .CreateClient(providerId, endpoint);

    private HttpClient HttpClientFor(EffectiveModelRuntime runtime) =>
        runtime.IsRemote ? RemoteHttpClient(runtime.ProviderId, new Uri(runtime.EndPoint)) : _chatGptHttpClient;

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static byte[] ExtractImageBytes(GeneratedImage image)
    {
        if (image.ImageBytes == null)
            throw new InvalidOperationException("OpenAI image response did not include image bytes.");

        return image.ImageBytes.ToArray();
    }
}

internal sealed record ChatGptCodexModelsHttpResponse(
    int StatusCode,
    string Content,
    string? ETag);

internal sealed record OpenAIImageEditInput(
    byte[] Bytes,
    string FileName,
    string MediaType);
