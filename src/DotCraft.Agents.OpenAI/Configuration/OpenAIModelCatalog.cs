using System.ClientModel;
using DotCraft.Agents;

namespace DotCraft.Configuration;

public enum OpenAIModelCatalogErrorCode
{
    None = 0,
    MissingApiKey,
    InvalidEndpoint,
    Unauthorized,
    Forbidden,
    EndpointNotSupported,
    Network,
    Timeout,
    Unknown
}

public sealed class OpenAIModelCatalogEntry
{
    public string Id { get; set; } = string.Empty;

    public string OwnedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OpenAIModelCatalogResult
{
    public bool Success { get; set; }

    public List<OpenAIModelCatalogEntry> Models { get; set; } = [];

    public OpenAIModelCatalogErrorCode ErrorCode { get; set; } = OpenAIModelCatalogErrorCode.None;

    public string? ErrorMessage { get; set; }
}

public static class OpenAIModelCatalog
{
    public static async Task<OpenAIModelCatalogResult> FetchAsync(
        EffectiveModelRuntime runtime,
        CancellationToken cancellationToken = default,
        OpenAIClientProvider? openAIClientProvider = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        if (!runtime.IsOpenAICompatible)
        {
            return Failure(
                OpenAIModelCatalogErrorCode.EndpointNotSupported,
                $"Protocol '{runtime.Protocol}' is not OpenAI.");
        }

        if (runtime.IsChatGptOAuth)
            return await ChatGptCodexModelCatalog.FetchAsync(
                runtime,
                cancellationToken,
                openAIClientProvider ?? new OpenAIClientProvider()).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(runtime.ApiKey))
        {
            return Failure(
                OpenAIModelCatalogErrorCode.MissingApiKey,
                "API key is not configured.");
        }

        if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var endpoint))
        {
            return Failure(
                OpenAIModelCatalogErrorCode.InvalidEndpoint,
                "Endpoint is invalid.");
        }

        try
        {
            var client = (openAIClientProvider ?? new OpenAIClientProvider()).GetOpenAIClient(runtime);
            var modelClient = client.GetOpenAIModelClient();
            var models = await modelClient.GetModelsAsync(cancellationToken);

            var entries = models.Value
                .Select(m => new OpenAIModelCatalogEntry
                {
                    Id = m.Id,
                    OwnedBy = m.OwnedBy ?? string.Empty,
                    CreatedAt = m.CreatedAt
                })
                .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new OpenAIModelCatalogResult
            {
                Success = true,
                Models = entries
            };
        }
        catch (ClientResultException ex)
        {
            var status = ResolveStatusCode(ex);
            if (status == 401)
                return Failure(OpenAIModelCatalogErrorCode.Unauthorized, "Request was unauthorized.");
            if (status == 403)
                return Failure(OpenAIModelCatalogErrorCode.Forbidden, "Request was forbidden.");
            if (status == 404 || status == 405)
            {
                return Failure(
                    OpenAIModelCatalogErrorCode.EndpointNotSupported,
                    "Endpoint does not support model listing.");
            }

            return Failure(
                OpenAIModelCatalogErrorCode.Unknown,
                string.IsNullOrWhiteSpace(ex.Message) ? "OpenAI request failed." : ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(OpenAIModelCatalogErrorCode.Timeout, "Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return Failure(
                OpenAIModelCatalogErrorCode.Network,
                string.IsNullOrWhiteSpace(ex.Message) ? "Network request failed." : ex.Message);
        }
        catch (TaskCanceledException)
        {
            return Failure(OpenAIModelCatalogErrorCode.Timeout, "Request timed out.");
        }
        catch (Exception ex)
        {
            return Failure(
                OpenAIModelCatalogErrorCode.Unknown,
                string.IsNullOrWhiteSpace(ex.Message) ? "Unknown error." : ex.Message);
        }
    }

    private static OpenAIModelCatalogResult Failure(OpenAIModelCatalogErrorCode code, string message)
        => new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            Models = []
        };

    private static int ResolveStatusCode(ClientResultException ex)
    {
        var type = ex.GetType();
        var statusProp = type.GetProperty("Status")
            ?? type.GetProperty("StatusCode");

        if (statusProp?.GetValue(ex) is int statusCode)
            return statusCode;

        if (statusProp?.GetValue(ex) is short statusCodeShort)
            return statusCodeShort;

        return 0;
    }
}
