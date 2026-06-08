using System.ClientModel.Primitives;
using System.Runtime.InteropServices;
using System.Text;
using DotCraft.Auth.OpenAI;
using DotCraft.Tracing;
using Microsoft.Extensions.Logging;

namespace DotCraft.Agents;

/// <summary>
/// Pipeline policy that authenticates outgoing OpenAI SDK requests with a ChatGPT subscription
/// access token. Each request fetches a fresh token from <see cref="IOpenAIAuthService"/>; a 401
/// response triggers a one-shot refresh + retry.
/// </summary>
internal sealed class OpenAIOAuthPipelinePolicy : PipelinePolicy
{
    internal const string UserAgentProfileEnvironmentVariable = "DOTCRAFT_CHATGPT_OAUTH_UA_PROFILE";
    internal const string OpenAIBetaEnvironmentVariable = "DOTCRAFT_CHATGPT_OAUTH_OPENAI_BETA";

    private const string CodexUserAgentProfile = "codex";
    private const string OpenAIBetaHeader = "OpenAI-Beta";
    private const string UserAgentHeader = "User-Agent";
    private const string ResponsesPathSuffix = "/responses";

    private readonly IOpenAIAuthService _authService;
    private readonly string? _configuredAccountId;
    private readonly string? _installationId;
    private readonly ILogger? _logger;
    private int _accountMismatchWarningLogged;
    private int _missingSessionWarningLogged;

    public OpenAIOAuthPipelinePolicy(
        IOpenAIAuthService authService,
        string? accountId,
        string? installationId = null,
        ILogger? logger = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configuredAccountId = NormalizeOptional(accountId);
        _installationId = string.IsNullOrWhiteSpace(installationId) ? null : installationId.Trim();
        _logger = logger;
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
        var isResponsesRequest = IsResponsesRequest(message);
        message.Request.Headers.Set("Authorization", $"Bearer {accessToken}");
        var accountId = ResolveAccountId();
        if (!string.IsNullOrEmpty(accountId))
            message.Request.Headers.Set(OpenAIAuthConstants.AccountIdHeader, accountId);
        message.Request.Headers.Set(OpenAIAuthConstants.OriginatorHeader, OpenAIAuthConstants.Originator);
        if (!string.IsNullOrEmpty(_installationId))
            message.Request.Headers.Set(OpenAIAuthConstants.InstallationIdHeader, _installationId);

        var sessionKey = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
        if (!string.IsNullOrWhiteSpace(sessionKey))
        {
            var trimmed = sessionKey.Trim();
            message.Request.Headers.Set(OpenAIAuthConstants.SessionIdHeader, trimmed);
            message.Request.Headers.Set(OpenAIAuthConstants.ThreadIdHeader, trimmed);
            message.Request.Headers.Set(OpenAIAuthConstants.SessionIdCompatHeader, trimmed);
            message.Request.Headers.Set(OpenAIAuthConstants.ConversationIdHeader, trimmed);
        }
        else if (isResponsesRequest &&
                 Interlocked.Exchange(ref _missingSessionWarningLogged, 1) == 0)
        {
            _logger?.LogWarning(
                "ChatGPT OAuth Responses request has no active DotCraft thread id; sending without thread-scoped sticky-routing headers.");
        }

        ApplyExperimentalHeaders(message, isResponsesRequest);
    }

    private string? ResolveAccountId()
    {
        var tokenAccountId = NormalizeOptional(_authService.GetAccountId());
        if (!string.IsNullOrEmpty(tokenAccountId))
        {
            if (!string.IsNullOrEmpty(_configuredAccountId) &&
                !string.Equals(_configuredAccountId, tokenAccountId, StringComparison.Ordinal) &&
                Interlocked.Exchange(ref _accountMismatchWarningLogged, 1) == 0)
            {
                _logger?.LogWarning(
                    "ChatGPT OAuth account id from config ({ConfiguredAccountId}) differs from the signed-in token account ({TokenAccountId}); using the token account for request routing.",
                    _configuredAccountId,
                    tokenAccountId);
            }

            return tokenAccountId;
        }

        return _configuredAccountId;
    }

    private static void ApplyExperimentalHeaders(PipelineMessage message, bool isResponsesRequest)
    {
        if (isResponsesRequest)
        {
            var beta = NormalizeOptional(Environment.GetEnvironmentVariable(OpenAIBetaEnvironmentVariable));
            if (!string.IsNullOrEmpty(beta))
                message.Request.Headers.Set(OpenAIBetaHeader, beta);
        }

        if (string.Equals(
            NormalizeOptional(Environment.GetEnvironmentVariable(UserAgentProfileEnvironmentVariable)),
            CodexUserAgentProfile,
            StringComparison.OrdinalIgnoreCase))
        {
            message.Request.Headers.Set(UserAgentHeader, BuildCodexCompatibleUserAgent());
        }
    }

    private static bool IsResponsesRequest(PipelineMessage message)
    {
        var uri = message.Request.Uri;
        return uri is not null &&
               uri.AbsolutePath.EndsWith(ResponsesPathSuffix, StringComparison.Ordinal);
    }

    private static string BuildCodexCompatibleUserAgent()
    {
        var version = typeof(OpenAIOAuthPipelinePolicy).Assembly.GetName().Version?.ToString();
        if (string.IsNullOrWhiteSpace(version))
            version = "0.0.0";

        var os = SanitizeHeaderSegment(RuntimeInformation.OSDescription);
        var arch = SanitizeHeaderSegment(RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
        return SanitizeHeaderValue($"{OpenAIAuthConstants.Originator}/{version} ({os}; {arch}) dotcraft");
    }

    private static string SanitizeHeaderSegment(string? value)
    {
        var sanitized = SanitizeHeaderValue(value);
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string SanitizeHeaderValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
            builder.Append(ch is >= ' ' and <= '~' ? ch : '_');
        return builder.ToString();
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
