using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.AppServer;
using DotCraft.Sessions.Wire;

namespace DotCraft.CLI;

/// <summary>
/// Internal JSON model-catalog bridge used by Desktop before a workspace is initialized.
/// </summary>
public static class ModelCatalogCliRunner
{
    private sealed class ProviderDraft
    {
        public string Id { get; set; } = "setup";
        public string DisplayName { get; set; } = string.Empty;
        public string Protocol { get; set; } = ModelProviderProtocols.OpenAIResponses;
        public string ApiKey { get; set; } = string.Empty;
        public string AuthMethod { get; set; } = ModelProviderAuthMethods.ApiKey;
        public string EndPoint { get; set; } = string.Empty;
        public int? NetworkTimeoutSeconds { get; set; }
    }

    public static async Task<int> RunAsync(CommandLineArgs args, CancellationToken cancellationToken)
    {
        try
        {
            var globalPath = InitHelper.GetGlobalConfigPath();
            var config = AppConfig.Load(globalPath);
            string? providerId = args.SetupProviderId;
            if (args.ModelCatalogReadStdin)
            {
                var json = await Console.In.ReadToEndAsync(cancellationToken);
                var draft = JsonSerializer.Deserialize<ProviderDraft>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? throw new InvalidOperationException("Provider draft is required.");
                providerId = string.IsNullOrWhiteSpace(draft.Id) ? "setup" : draft.Id.Trim();
                config.Providers[providerId] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = draft.DisplayName,
                    Protocol = draft.Protocol,
                    ApiKey = draft.ApiKey,
                    AuthMethod = draft.AuthMethod,
                    EndPoint = draft.EndPoint,
                    NetworkTimeoutSeconds = draft.NetworkTimeoutSeconds
                };
            }

            var auth = new OpenAIAuthManager();
            if (config.Providers.TryGetValue(providerId ?? string.Empty, out var provider)
                && string.Equals(provider.AuthMethod, ModelProviderAuthMethods.ChatGptOAuth, StringComparison.OrdinalIgnoreCase)
                && !auth.IsAuthenticated)
            {
                await WriteAsync(new { kind = "auth-required" });
                return 0;
            }

            var result = await ModelProviderCatalog.FetchAsync(
                config,
                new ModelProviderRegistry([
                    new OpenAIClientProvider(auth),
                    new AnthropicClientProvider()
                ]),
                providerId,
                cancellationToken);
            if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                await Console.Error.WriteLineAsync(result.ErrorMessage);
            }
            await WriteAsync(result.Success
                ? new
                {
                    kind = "success",
                    models = result.Models.Select(model =>
                        ProviderContractMapper.BuildModelCatalogItem(
                            config,
                            result.Protocol,
                            result.EndPoint,
                            model,
                            includeUltra: false)).ToArray()
                }
                : new
                {
                    kind = result.ErrorCode switch
                    {
                        ModelCatalogErrorCode.EndpointNotSupported => "unsupported",
                        ModelCatalogErrorCode.MissingApiKey => "missing-key",
                        _ => "error"
                    },
                    retryable = result.ErrorCode is ModelCatalogErrorCode.Network
                        or ModelCatalogErrorCode.Timeout
                        or ModelCatalogErrorCode.Unknown,
                    errorCode = result.ErrorCode.ToString(),
                    errorMessage = result.ErrorMessage
                });
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            await WriteAsync(new { kind = "error", errorMessage = ex.Message });
            return 0;
        }
    }

    private static async Task WriteAsync(object value)
    {
        var payload = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(value, SessionWireJsonOptions.Default) + "\n");
        await using var output = Console.OpenStandardOutput();
        await output.WriteAsync(payload);
        await output.FlushAsync();
    }
}
