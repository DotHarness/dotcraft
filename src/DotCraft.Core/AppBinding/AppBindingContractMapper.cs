using System.Text.Json;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

/// <summary>Explicit boundary between App Binding runtime records and AppServer contracts.</summary>
internal static class AppBindingContractMapper
{
    public static Contract.AppInfo ToContract(AppCatalogProjection value) => new()
    {
        AppId = value.AppId,
        DisplayName = value.DisplayName,
        DeveloperName = value.DeveloperName,
        Description = value.Description,
        Category = OmitIfNull(value.Category),
        Icon = OmitIfNull(value.Icon),
        PluginId = value.PluginId,
        Installed = value.Installed,
        Enabled = value.Enabled,
        CatalogVisible = value.CatalogVisible,
        Managed = value.Managed,
        RequiresExternalConnection = value.RequiresExternalConnection,
        ReleasePage = OmitIfNull(value.ReleasePage),
        DownloadUrl = OmitIfNull(value.DownloadUrl),
        NativeApp = new Contract.AppNativeApplication
        {
            DisplayName = value.NativeApp.DisplayName,
            Protocol = value.NativeApp.Protocol,
            Status = value.NativeApp.Status,
            InstallUrl = OmitIfNull(value.NativeApp.InstallUrl)
        },
        ConnectionState = value.ConnectionState,
        AccountLabel = OmitIfNull(value.AccountLabel),
        HandoffModes = value.HandoffModes.Select(mode => new Contract.AppHandoffModeDescriptor
        {
            Mode = mode.Mode,
            UriTemplate = OmitIfNull(mode.UriTemplate)
        }).ToArray(),
        BindingSummary = value.BindingSummary is null
            ? default
            : Protocol.Optional<Contract.ThreadAppBindingSummary?>.FromValue(
                AppServer.ThreadContractMapper.ToContract(value.BindingSummary)),
        Diagnostics = value.Diagnostics.ToArray()
    };

    public static Contract.AppPrincipal ToContract(AppPrincipalSnapshot value) => new()
    {
        PrincipalId = value.PrincipalId,
        AppId = value.AppId,
        UserId = value.UserId,
        ExpiresAt = value.ExpiresAt
    };

    public static Contract.AppHandoff ToContract(AppHandoffDescriptor value) => new()
    {
        Mode = value.Mode,
        Uri = OmitIfNull(value.Uri),
        BindCode = OmitIfNull(value.BindCode),
        Instructions = OmitIfNull(value.Instructions)
    };

    public static Contract.AppConnectionStartResult ToContract(AppConnectionStartOutcome value) => new()
    {
        ConnectionRequestId = value.ConnectionRequestId,
        RequestToken = value.RequestToken,
        ExpiresAt = value.ExpiresAt,
        Handoff = value.Handoff is null
            ? default
            : Protocol.Optional<Contract.AppHandoff?>.FromValue(ToContract(value.Handoff))
    };

    public static Contract.AppConnectionConnectResult ToContract(AppConnectionConnectOutcome value) => new()
    {
        Principal = ToContract(value.Principal),
        Credential = value.Credential
    };

    public static Contract.AppConnectionRefreshResult ToContract(AppConnectionRefreshOutcome value) => new()
    {
        Principal = ToContract(value.Principal),
        Credential = value.Credential
    };

    public static Contract.AppSurface ToContract(AppSurfaceSnapshot value) => new()
    {
        AppId = value.AppId,
        SurfaceId = value.SurfaceId,
        Endpoint = value.Endpoint,
        Bearer = value.Bearer,
        ExpiresAt = value.ExpiresAt
    };

    public static Contract.ThreadAppBindingEnableResult ToContract(ThreadAppBindingEnableOutcome value) => new()
    {
        BindingRequestId = value.BindingRequestId,
        BindingId = value.BindingId,
        State = value.State,
        ExpiresAt = value.ExpiresAt,
        Handoff = value.Handoff is null
            ? default
            : Protocol.Optional<Contract.AppHandoff?>.FromValue(ToContract(value.Handoff))
    };

    public static Contract.AppBindingRequest ToContract(AppBindingRequestSnapshot value) => new()
    {
        BindingRequestId = value.BindingRequestId,
        BindingId = value.BindingId,
        ThreadId = value.ThreadId,
        AppId = value.AppId,
        State = value.State,
        ExpiresAt = value.ExpiresAt
    };

    public static Contract.AppBinding ToContract(AppBindingSnapshot value) => new()
    {
        BindingId = value.BindingId,
        ThreadId = value.ThreadId,
        AppId = value.AppId,
        State = value.State,
        AuthorityRevision = value.AuthorityRevision,
        ApprovedCapabilityRevision = value.ApprovedCapabilityRevision,
        CandidateCapabilityRevision = OmitIfNull(value.CandidateCapabilityRevision),
        ApprovedTools = value.ApprovedTools.Select(ToContract).ToArray(),
        PendingChanges = value.PendingChanges.Select(ToContract).ToArray(),
        SocialTarget = value.SocialTarget is null
            ? default
            : Protocol.Optional<Contract.SocialChannelTarget?>.FromValue(ToContract(value.SocialTarget)),
        FailureReason = OmitIfNull(value.FailureReason),
        UpdatedAt = value.UpdatedAt
    };

    public static Contract.AppBindingToolCapability ToContract(AppBindingToolCapability value) => new()
    {
        Namespace = value.Namespace,
        Name = value.Name,
        InputSchema = JsonSerializer.SerializeToElement(value.InputSchema),
        Visibility = value.Visibility.ToArray(),
        Annotations = JsonSerializer.SerializeToElement(value.Annotations),
        Ui = value.Ui is null
            ? default
            : Protocol.Optional<Contract.AppBindingUiCapability?>.FromValue(ToContract(value.Ui))
    };

    public static Contract.AppBindingUiCapability ToContract(AppBindingUiCapability value) => new()
    {
        ResourceUri = value.ResourceUri,
        ConnectDomains = value.ConnectDomains.ToArray(),
        ResourceDomains = value.ResourceDomains.ToArray(),
        Permissions = value.Permissions.ToArray(),
        SecurityHash = value.SecurityHash
    };

    public static Contract.AppBindingCapabilityChange ToContract(AppBindingCapabilityChange value) => new()
    {
        Kind = value.Kind,
        Tool = value.Tool,
        Detail = value.Detail
    };

    public static Contract.SocialChannelTarget ToContract(SocialChannelTarget value) => new()
    {
        ChannelName = value.ChannelName,
        AccountId = OmitIfNull(value.AccountId),
        ConversationKind = value.ConversationKind,
        ConversationId = value.ConversationId,
        DeliveryTarget = value.DeliveryTarget,
        DisplayName = OmitIfNull(value.DisplayName),
        BoundBy = value.BoundBy is null
            ? default
            : Protocol.Optional<Contract.SocialChannelBoundBy?>.FromValue(
                new Contract.SocialChannelBoundBy
                {
                    PlatformUserId = value.BoundBy.PlatformUserId,
                    DisplayName = OmitIfNull(value.BoundBy.DisplayName)
                })
    };

    public static SocialChannelTarget FromContract(Contract.SocialChannelTarget value) => new()
    {
        ChannelName = Read(value.ChannelName) ?? string.Empty,
        AccountId = Read(value.AccountId),
        ConversationKind = Read(value.ConversationKind) ?? string.Empty,
        ConversationId = Read(value.ConversationId) ?? string.Empty,
        DeliveryTarget = Read(value.DeliveryTarget) ?? string.Empty,
        DisplayName = Read(value.DisplayName),
        BoundBy = Read(value.BoundBy) is { } boundBy
            ? new SocialChannelBoundBy
            {
                PlatformUserId = Read(boundBy.PlatformUserId) ?? string.Empty,
                DisplayName = Read(boundBy.DisplayName)
            }
            : null
    };

    public static AppConnectionRequestQuery FromContract(Contract.AppConnectionRequestGetParams value) => new()
    {
        ConnectionRequestId = Read(value.ConnectionRequestId) ?? string.Empty,
        RequestToken = Read(value.RequestToken) ?? string.Empty
    };

    public static AppConnectionConnectCommand FromContract(Contract.AppConnectionConnectParams value) => new()
    {
        ConnectionRequestId = Read(value.ConnectionRequestId) ?? string.Empty,
        RequestToken = Read(value.RequestToken) ?? string.Empty,
        AccountLabel = Read(value.AccountLabel)
    };

    public static AppSurfacePublishCommand FromContract(Contract.AppSurfacePublishParams value) => new()
    {
        SurfaceId = Read(value.SurfaceId) ?? string.Empty,
        Endpoint = Read(value.Endpoint) ?? string.Empty,
        Bearer = Read(value.Bearer) ?? string.Empty
    };

    public static AppBindingRequestQuery FromContract(Contract.AppBindingRequestGetParams value) => new()
    {
        BindingRequestId = Read(value.BindingRequestId) ?? string.Empty,
        RequestToken = Read(value.RequestToken)
    };

    public static AppBindingActivateCommand FromContract(Contract.AppBindingActivateParams value) => new()
    {
        BindingRequestId = Read(value.BindingRequestId) ?? string.Empty,
        Endpoint = Read(value.Endpoint) ?? string.Empty,
        Bearer = Read(value.Bearer) ?? string.Empty,
        BearerExpiresAt = Read(value.BearerExpiresAt)
    };

    public static AppBindingRebindCommand FromContract(Contract.AppBindingRebindParams value) => new()
    {
        BindingId = Read(value.BindingId) ?? string.Empty,
        AuthorityRevision = Read(value.AuthorityRevision),
        Endpoint = Read(value.Endpoint) ?? string.Empty,
        Bearer = Read(value.Bearer) ?? string.Empty,
        BearerExpiresAt = Read(value.BearerExpiresAt)
    };

    public static ThreadAppBindingConfirmCapabilitiesCommand FromContract(
        Contract.ThreadAppBindingConfirmCapabilitiesParams value) => new()
    {
        ThreadId = Read(value.ThreadId) ?? string.Empty,
        BindingId = Read(value.BindingId) ?? string.Empty,
        CandidateRevision = Read(value.CandidateRevision),
        Decision = Read(value.Decision) ?? string.Empty
    };

    public static SocialBindingAcceptCommand FromContract(Contract.SocialBindingAcceptParams value) => new()
    {
        Code = Read(value.Code) ?? string.Empty,
        Target = Read(value.Target) is { } target ? FromContract(target) : new SocialChannelTarget()
    };

    public static SocialBindingRebindCommand FromContract(Contract.SocialBindingRebindParams value) => new()
    {
        BindingId = Read(value.BindingId) ?? string.Empty,
        AuthorityRevision = Read(value.AuthorityRevision),
        Target = Read(value.Target) is { } target ? FromContract(target) : new SocialChannelTarget()
    };

    public static T? Read<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : Protocol.Optional<T?>.FromValue(value);
}
