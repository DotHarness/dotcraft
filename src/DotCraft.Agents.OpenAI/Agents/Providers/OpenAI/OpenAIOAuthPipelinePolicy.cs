using System.ClientModel.Primitives;
using DotCraft.Auth.OpenAI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Agents;

internal sealed class OpenAIOAuthPipelinePolicy(
    IOpenAIAuthService authService,
    string? accountId,
    string? installationId = null,
    ILogger? logger = null) : OpenAIRequestMetadataPolicy(installationId, logger)
{
    protected override async ValueTask SendAsync(
        PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        var ct = message.CancellationToken;
        ApplyCredentials(message, await authService.GetAccessTokenAsync(false, ct).ConfigureAwait(false));
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        if (message.Response?.Status != 401)
            return;
        try
        {
            if (authService is OpenAIAuthManager manager
                && await manager.TryReloadAccessTokenAsync(ct).ConfigureAwait(false) is { } token)
            {
                ApplyCredentials(message, token);
                await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
                if (message.Response?.Status != 401)
                    return;
            }
            ApplyCredentials(message, await authService.GetAccessTokenAsync(true, ct).ConfigureAwait(false));
        }
        catch (OpenAIAuthException)
        {
            return;
        }
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private void ApplyCredentials(PipelineMessage message, string token)
    {
        message.Request.Headers.Set("Authorization", $"Bearer {token}");
        var currentAccount = authService.GetAccountId() ?? accountId;
        if (!string.IsNullOrWhiteSpace(currentAccount))
            message.Request.Headers.Set(OpenAIAuthConstants.AccountIdHeader, currentAccount);
    }
}
