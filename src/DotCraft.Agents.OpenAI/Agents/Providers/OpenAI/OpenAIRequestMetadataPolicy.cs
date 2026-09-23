using System.ClientModel.Primitives;
using System.Runtime.InteropServices;
using System.Text;
using DotCraft.Auth.OpenAI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Agents;

internal class OpenAIRequestMetadataPolicy : PipelinePolicy
{
    internal const string UserAgentProfileEnvironmentVariable = "DOTCRAFT_CHATGPT_OAUTH_UA_PROFILE";
    internal const string OpenAIBetaEnvironmentVariable = "DOTCRAFT_CHATGPT_OAUTH_OPENAI_BETA";

    private const string CodexUserAgentProfile = "codex";
    private const string OpenAIBetaHeader = "OpenAI-Beta";
    private const string UserAgentHeader = "User-Agent";
    internal const string BetaFeaturesHeader = "x-codex-beta-features";
    internal const string BetaFeaturesValue = "remote_compaction_v2";
    private const string ResponsesPathSuffix = "/responses";
    private static readonly string[] SdkPlatformMetadataHeaders =
    [
        "X-Stainless-Lang",
        "X-Stainless-Package-Version",
        "X-Stainless-Runtime",
        "X-Stainless-Runtime-Version",
        "X-Stainless-OS",
        "X-Stainless-Arch"
    ];

    private readonly string? _installationId;
    private readonly ILogger? _logger;
    private int _missingSessionWarningLogged;

    public OpenAIRequestMetadataPolicy(string? installationId = null, ILogger? logger = null)
    {
        _installationId = installationId;
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
        var isResponsesRequest = IsResponsesRequest(message);
        var codexMetadata = isResponsesRequest && !string.IsNullOrEmpty(_installationId)
            ? OpenAIResponsesCodexMetadata.GetOrCreateSnapshot(message, _installationId)
            : null;
        var routingIdentity = isResponsesRequest
            ? codexMetadata == null
                ? OpenAIResponsesCodexMetadata.ResolveRoutingIdentity()
                : new OpenAIResponsesRoutingIdentity(
                    codexMetadata.SessionId,
                    codexMetadata.ThreadId,
                    codexMetadata.DefaultPromptCacheKey,
                    codexMetadata.ClientRequestId)
            : null;
        ApplyMetadata(message, isResponsesRequest, routingIdentity, codexMetadata);
        await SendAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        CaptureTurnState(message);
    }

    protected virtual ValueTask SendAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex) =>
        ProcessNextAsync(message, pipeline, currentIndex);

    private void ApplyMetadata(
        PipelineMessage message,
        bool isResponsesRequest,
        OpenAIResponsesRoutingIdentity? routingIdentity,
        OpenAIResponsesCodexMetadataSnapshot? codexMetadata)
    {
        RemoveSdkPlatformMetadataHeaders(message);
        message.Request.Headers.Set(OpenAIAuthConstants.OriginatorHeader, OpenAIAuthConstants.Originator);
        if (!string.IsNullOrEmpty(_installationId))
            message.Request.Headers.Set(OpenAIAuthConstants.InstallationIdHeader, _installationId);

        if (isResponsesRequest)
        {
            message.Request.Headers.Set(BetaFeaturesHeader, BetaFeaturesValue);
            SetIfPresent(message, OpenAIAuthConstants.SessionIdHeader, routingIdentity?.SessionId);
            SetIfPresent(message, OpenAIAuthConstants.ThreadIdHeader, routingIdentity?.ThreadId);
            SetIfPresent(
                message,
                OpenAIAuthConstants.ClientRequestIdHeader,
                routingIdentity?.ClientRequestId);
        }
        else
        {
            var sessionKey = ProviderRequestContextScope.Current?.ConversationIdentity.CurrentThreadId;
            if (!string.IsNullOrWhiteSpace(sessionKey))
            {
                var trimmed = sessionKey.Trim();
                message.Request.Headers.Set(OpenAIAuthConstants.SessionIdHeader, trimmed);
                message.Request.Headers.Set(OpenAIAuthConstants.ThreadIdHeader, trimmed);
            }
        }

        if (isResponsesRequest
            && string.IsNullOrWhiteSpace(routingIdentity?.ThreadId)
            && Interlocked.Exchange(ref _missingSessionWarningLogged, 1) == 0)
        {
            _logger?.LogWarning(
                "ChatGPT OAuth Responses request has no active DotCraft thread id; sending without thread-scoped sticky-routing headers.");
        }

        if (codexMetadata != null)
        {
            SetIfPresent(message, OpenAIAuthConstants.WindowIdHeader, codexMetadata.WindowId);
            SetIfPresent(message, OpenAIAuthConstants.TurnMetadataHeader, codexMetadata.TurnMetadataJson);
            SetIfPresent(message, OpenAIAuthConstants.ParentThreadIdHeader, codexMetadata.ParentThreadId);
            SetIfPresent(message, OpenAIAuthConstants.SubAgentHeader, codexMetadata.SubagentHeader);
            SetIfPresent(
                message,
                OpenAIAuthConstants.TurnStateHeader,
                ProviderRequestContextScope.Current is { } requestContext
                    ? requestContext.ConversationState?.ContinuationState
                    : OpenAIResponsesCodexRuntimeScope.Current?.TurnState ?? codexMetadata.TurnState);
        }

        ApplyExperimentalHeaders(message, isResponsesRequest);
    }

    private static void RemoveSdkPlatformMetadataHeaders(PipelineMessage message)
    {
        foreach (var headerName in SdkPlatformMetadataHeaders)
            message.Request.Headers.Remove(headerName);
    }

    private static void CaptureTurnState(PipelineMessage message)
    {
        if (!IsResponsesRequest(message))
            return;

        var state = ProviderRequestContextScope.Current?.ConversationState;
        var context = OpenAIResponsesCodexRuntimeScope.Current;
        if (state == null && context == null || message.Response == null)
            return;

        if (message.Response.Headers.TryGetValue(OpenAIAuthConstants.TurnStateHeader, out var value))
        {
            if (state != null)
                state.TryCaptureContinuationState(value);
            else
                context!.TryCaptureTurnState(value);
        }
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
        return uri is not null
               && uri.AbsolutePath.EndsWith(ResponsesPathSuffix, StringComparison.Ordinal);
    }

    private static void SetIfPresent(PipelineMessage message, string headerName, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            message.Request.Headers.Set(headerName, value.Trim());
    }

    private static string BuildCodexCompatibleUserAgent()
    {
        var version = typeof(OpenAIRequestMetadataPolicy).Assembly.GetName().Version?.ToString();
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
