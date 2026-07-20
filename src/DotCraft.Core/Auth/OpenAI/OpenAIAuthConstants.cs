namespace DotCraft.Auth.OpenAI;

/// <summary>
/// Constants for the OpenAI / ChatGPT subscription OAuth flow.
/// </summary>
public static class OpenAIAuthConstants
{
    public const string Issuer = "https://auth.openai.com";
    public const string AuthorizeUrl = "https://auth.openai.com/oauth/authorize";
    public const string TokenUrl = "https://auth.openai.com/oauth/token";
    public const string RevokeUrl = "https://auth.openai.com/oauth/revoke";

    /// <summary>
    /// Public OAuth client_id accepted by OpenAI's ChatGPT subscription endpoints. This identifies
    /// DotCraft as a third-party client during the authorization-code flow.
    /// </summary>
    public const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";

    /// <summary>
    /// Value sent in the <see cref="OriginatorHeader"/> on subscription backend requests. The
    /// backend rejects requests with unknown originators on this path.
    /// </summary>
    public const string Originator = "codex_cli_rs";

    public const int RedirectPortPrimary = 1455;
    public const int RedirectPortFallback = 1457;
    public const string RedirectCallbackPath = "/auth/callback";
    public const string RedirectSuccessPath = "/success";

    /// <summary>ChatGPT backend base URL for OAuth Responses requests.</summary>
    public const string ChatGptBackendBaseUrl = "https://chatgpt.com/backend-api/codex";

    /// <summary>
    /// ChatGPT backend usage / rate-limit endpoint. Returns <see cref="OpenAIUsageSnapshot"/>-shaped
    /// JSON.
    /// </summary>
    public const string ChatGptUsageUrl = "https://chatgpt.com/backend-api/wham/usage";

    /// <summary>Header used by the ChatGPT backend to scope requests to a workspace/account.</summary>
    public const string AccountIdHeader = "chatgpt-account-id";

    /// <summary>Header used to identify the originating CLI client.</summary>
    public const string OriginatorHeader = "originator";

    /// <summary>
    /// Header carrying the per-DotCraft-installation UUID v4. ChatGPT's backend uses this value
    /// as a sticky-routing hint, materially improving <c>prompt_cache_key</c> hit rates. The
    /// value is persisted at <c>~/.craft/installation_id</c>; see
    /// <see cref="OpenAIInstallationIdProvider"/>.
    /// </summary>
    public const string InstallationIdHeader = "x-codex-installation-id";

    /// <summary>
    /// Header carrying a per-thread session identifier. Populated at request time from the active
    /// <see cref="DotCraft.Tracing.TracingChatClient.CurrentSessionKey"/> (the DotCraft thread id)
    /// as a secondary sticky-routing hint for the ChatGPT backend's prompt-cache shards.
    /// </summary>
    public const string SessionIdHeader = "session-id";

    /// <summary>
    /// Header carrying the conversation thread identifier. Sent alongside
    /// <see cref="SessionIdHeader"/> on every request so the ChatGPT backend's load balancer can
    /// stick thread-scoped traffic to the cache shard that already holds the prefix.
    /// </summary>
    public const string ThreadIdHeader = "thread-id";

    /// <summary>
    /// Session identifier key used inside the ChatGPT Responses <c>client_metadata</c> object.
    /// It is not emitted as a direct HTTP header.
    /// </summary>
    public const string SessionIdCompatHeader = "session_id";

    /// <summary>
    /// Legacy compatibility header name retained for source compatibility. DotCraft no longer
    /// emits it on ChatGPT OAuth requests.
    /// </summary>
    public const string ConversationIdHeader = "conversation_id";

    /// <summary>Responses context-window identifier for the active provider-visible history lineage.</summary>
    public const string WindowIdHeader = "x-codex-window-id";

    /// <summary>Canonical Responses metadata envelope.</summary>
    public const string TurnMetadataHeader = "x-codex-turn-metadata";

    /// <summary>Provider-returned state replayed only within the same logical turn.</summary>
    public const string TurnStateHeader = "x-codex-turn-state";

    /// <summary>Provider compatibility header for child/fork lineage when known.</summary>
    public const string ParentThreadIdHeader = "x-codex-parent-thread-id";

    /// <summary>Provider compatibility metadata key for subagent kind when known.</summary>
    public const string SubAgentHeader = "x-openai-subagent";

    /// <summary>Background refresh cadence for OAuth access tokens.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(8);

    /// <summary>
    /// OAuth scopes requested at authorization time. The chatgpt.com backend keys subscription
    /// access on these connector scopes.
    /// </summary>
    public const string Scopes = "openid profile email offline_access api.connectors.read api.connectors.invoke";

    /// <summary>JWT claim path containing ChatGPT-specific account metadata.</summary>
    public const string ChatGptAuthClaim = "https://api.openai.com/auth";
}
