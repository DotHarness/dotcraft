using DotCraft.Agents.Remote;

namespace DotCraft.CLI;

internal static class StackModelService
{
    public static void ValidateOptions(StackCommandOptions options)
    {
        if (options.ModelServiceUrl is null && options.ModelServiceTokenFile is null)
            return;
        if (!Uri.TryCreate(options.ModelServiceUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            throw new ArgumentException("--model-service-url must be an HTTP URL.");
        if (string.IsNullOrWhiteSpace(options.ModelServiceTokenFile))
            throw new ArgumentException("--model-service-token-file is required.");
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("Configure a model service client credential when using a remote service.");
        if (string.IsNullOrWhiteSpace(options.Provider) || string.IsNullOrWhiteSpace(options.Model))
            throw new ArgumentException("--provider and --model are required for a remote model service.");
    }

    public static string EnvironmentValues(StackCommandOptions options)
    {
        if (options.ModelServiceUrl is null)
            return "DOTCRAFT_MODEL_MODE=direct\n";
        var token = File.ReadAllText(options.ModelServiceTokenFile!).Trim();
        if (token.Length == 0 || token.IndexOfAny(['\r', '\n']) >= 0)
            throw new InvalidDataException("The model service token file must contain one non-empty line.");
        return $"DOTCRAFT_MODEL_MODE=remote\nDOTCRAFT_MODEL_SERVICE_URL={options.ModelServiceUrl}\nDOTCRAFT_MODEL_SERVICE_TOKEN={token}\n";
    }

    public static async Task CheckAsync(string? url, string? token, string? providerId, string? model, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Configure DOTCRAFT_MODEL_SERVICE_URL and DOTCRAFT_MODEL_SERVICE_TOKEN.");
        using var transport = new RemoteProviderTransport(new ModelServiceConnection(endpoint, token));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var catalog = await transport.GetCatalogAsync(timeout.Token).ConfigureAwait(false);
        var provider = catalog.Providers.SingleOrDefault(item => item.Id == providerId)
            ?? throw new InvalidOperationException("The selected provider is not available to this model service client.");
        if (!provider.Authentication.IsAuthenticated)
            throw new InvalidOperationException("The selected provider requires authentication at the model service.");
        if (model is null || provider.Models?.ContainsKey(model) != true)
            throw new InvalidOperationException("The selected model is not available from the model service.");
    }
}
