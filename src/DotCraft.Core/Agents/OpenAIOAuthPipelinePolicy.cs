using System.ClientModel.Primitives;
using DotCraft.Auth.OpenAI;
using DotCraft.Tracing;

namespace DotCraft.Agents;

/// <summary>
/// Pipeline policy that authenticates outgoing OpenAI SDK requests with a ChatGPT subscription
/// access token. Each request fetches a fresh token from <see cref="IOpenAIAuthService"/>; a 401
/// response triggers a one-shot refresh + retry.
/// </summary>
internal sealed class OpenAIOAuthPipelinePolicy : PipelinePolicy
{
    private readonly IOpenAIAuthService _authService;
    private readonly string? _accountId;
    private readonly string? _installationId;

    public OpenAIOAuthPipelinePolicy(
        IOpenAIAuthService authService,
        string? accountId,
        string? installationId = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _accountId = string.IsNullOrWhiteSpace(accountId) ? null : accountId.Trim();
        _installationId = string.IsNullOrWhiteSpace(installationId) ? null : installationId.Trim();
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ProcessAsync(message, pipeline, currentIndex).AsTask().GetAwaiter().GetResult();
    }

    public override async ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        var ct = message.CancellationToken;
        var token = await _authService.GetAccessTokenAsync(forceRefresh: false, ct).ConfigureAwait(false);
        ApplyAuthHeaders(message, token);

        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);

        if (message.Response?.Status != 401)
            return;

        // 401 — refresh once and retry.
        try
        {
            token = await _authService.GetAccessTokenAsync(forceRefresh: true, ct).ConfigureAwait(false);
        }
        catch (OpenAIAuthException)
        {
            return; // Surface the original 401 to the caller for richer diagnostics.
        }

        ApplyAuthHeaders(message, token);
        // The transport policy below produces a fresh response on the retry; we do not need to
        // explicitly clear the existing one.
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private void ApplyAuthHeaders(PipelineMessage message, string accessToken)
    {
        message.Request.Headers.Set("Authorization", $"Bearer {accessToken}");
        if (!string.IsNullOrEmpty(_accountId))
            message.Request.Headers.Set(OpenAIAuthConstants.AccountIdHeader, _accountId);
        message.Request.Headers.Set(OpenAIAuthConstants.OriginatorHeader, OpenAIAuthConstants.Originator);
        if (!string.IsNullOrEmpty(_installationId))
            message.Request.Headers.Set(OpenAIAuthConstants.InstallationIdHeader, _installationId);

        var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
        if (!string.IsNullOrWhiteSpace(sessionKey))
        {
            var trimmed = sessionKey.Trim();
            message.Request.Headers.Set(OpenAIAuthConstants.SessionIdHeader, trimmed);
            message.Request.Headers.Set(OpenAIAuthConstants.ThreadIdHeader, trimmed);
        }
    }
}
