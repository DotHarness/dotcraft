namespace DotCraft.AppBinding;

/// <summary>
/// Internal catalog view enriched with runtime-only fields before it is mapped to the AppServer
/// contract. This is intentionally not a JSON DTO.
/// </summary>
internal sealed class AppCatalogProjection
{
    public string AppId { get; set; } = string.Empty;
    public string ToolNamespace { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DeveloperName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Icon { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public bool Installed { get; set; }
    public bool Enabled { get; set; }
    public bool CatalogVisible { get; set; } = true;
    public bool Managed { get; set; }
    public bool RequiresExternalConnection { get; set; } = true;
    public string? ReleasePage { get; set; }
    public string? DownloadUrl { get; set; }
    public AppNativeApplicationProjection NativeApp { get; set; } = new();
    public string ConnectionState { get; set; } = AppConnectionStates.NotConnected;
    public string? AccountLabel { get; set; }
    public List<AppHandoffModeDescriptor> HandoffModes { get; set; } = [];
    public List<AppScopeDescriptor> Scopes { get; set; } = [];
    public List<AppToolCatalogEntry> ToolCatalog { get; set; } = [];
    public AppDynamicToolCatalogDescriptor DynamicToolCatalog { get; set; } = new();
    public ThreadAppBindingSummarySnapshot? BindingSummary { get; set; }
    public List<DotCraft.Protocol.AppServer.PluginDiagnostic> Diagnostics { get; set; } = [];
}

internal sealed class AppNativeApplicationProjection
{
    public string DisplayName { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Status { get; set; } = AppNativeApplicationStates.Unknown;
    public string? InstallUrl { get; set; }
}
