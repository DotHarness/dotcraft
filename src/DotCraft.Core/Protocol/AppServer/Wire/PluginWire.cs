using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;


// ───── plugin/* ─────

public sealed class PluginListParams
{
    public bool? IncludeDisabled { get; set; }
}

public sealed class PluginListResult
{
    public List<PluginInfoWire> Plugins { get; set; } = [];

    public List<PluginDiagnosticWire> Diagnostics { get; set; } = [];
}

public sealed class PluginViewParams
{
    public string Id { get; set; } = string.Empty;
}

public sealed class PluginViewResult
{
    public PluginInfoWire Plugin { get; set; } = new();
}

public sealed class PluginInstallParams
{
    public string Id { get; set; } = string.Empty;
}

public sealed class PluginInstallResult
{
    public PluginInfoWire Plugin { get; set; } = new();
}

public sealed class PluginRemoveParams
{
    public string Id { get; set; } = string.Empty;
}

public sealed class PluginRemoveResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PluginInfoWire? Plugin { get; set; }
}

public sealed class PluginSetEnabledParams
{
    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

public sealed class PluginSetEnabledResult
{
    public PluginInfoWire Plugin { get; set; } = new();
}

public sealed class PluginInfoWire
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    public bool Enabled { get; set; }

    public bool Installed { get; set; } = true;

    public bool Installable { get; set; }

    public bool Removable { get; set; }

    public string Source { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PluginInterfaceWire? Interface { get; set; }

    public List<PluginFunctionInfoWire> Functions { get; set; } = [];

    public List<PluginSkillInfoWire> Skills { get; set; } = [];

    public List<PluginAppInfoWire> Apps { get; set; } = [];

    public List<PluginDesktopExtensionInfoWire> DesktopExtensions { get; set; } = [];

    public List<PluginMcpServerInfoWire> McpServers { get; set; } = [];

    public List<PluginLspServerInfoWire> LspServers { get; set; } = [];

    public List<PluginDiagnosticWire> Diagnostics { get; set; } = [];
}

public sealed class PluginDesktopExtensionInfoWire
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    public string Entry { get; set; } = string.Empty;

    public List<string> Styles { get; set; } = [];

    public List<PluginDesktopExtensionSurfaceWire> Surfaces { get; set; } = [];

    public List<string> RequiredAppIds { get; set; } = [];

    public List<string> ConnectOrigins { get; set; } = [];

    public List<string> SurfaceWriteScopes { get; set; } = [];
}

public sealed class PluginDesktopExtensionSurfaceWire
{
    public string Type { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ViewId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? LocalizedLabel { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Placement { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Order { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Slot { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RendererId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActionId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SettingsId { get; set; }
}

public sealed class PluginAppInfoWire
{
    public string AppId { get; set; } = string.Empty;

    public string ToolNamespace { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string DeveloperName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReleasePage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PluginAppNativeApplicationWire? NativeApplication { get; set; }

    public List<PluginAppToolInfoWire> ToolCatalog { get; set; } = [];

    public PluginAppDynamicToolCatalogWire DynamicToolCatalog { get; set; } = new();
}

public sealed class PluginAppNativeApplicationWire
{
    public string DisplayName { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstallUrl { get; set; }
}

public sealed class PluginAppToolInfoWire
{
    public string Name { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public string Risk { get; set; } = string.Empty;

    public string DefaultExposure { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

public sealed class PluginAppDynamicToolCatalogWire
{
    public bool Enabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

public sealed class PluginMcpServerInfoWire
{
    public string Name { get; set; } = string.Empty;

    public string RuntimeName { get; set; } = string.Empty;

    public string Transport { get; set; } = "stdio";

    public bool Enabled { get; set; }

    public bool Active { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShadowedBy { get; set; }
}

public sealed class PluginLspServerInfoWire
{
    public string Name { get; set; } = string.Empty;

    public string RuntimeName { get; set; } = string.Empty;

    public string Transport { get; set; } = "stdio";

    public bool Enabled { get; set; }

    public bool Active { get; set; }

    public List<string> Extensions { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShadowedBy { get; set; }
}

public sealed class PluginInterfaceWire
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShortDescription { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LongDescription { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeveloperName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    public List<string> Capabilities { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultPrompt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BrandColor { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ComposerIconDataUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogoDataUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WebsiteUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PrivacyPolicyUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TermsOfServiceUrl { get; set; }
}

public sealed class PluginFunctionInfoWire
{
    public string Name { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; set; }

    public string Description { get; set; } = string.Empty;
}

public sealed class PluginSkillInfoWire
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShortDescription { get; set; }

    public bool Enabled { get; set; }
}

public sealed class PluginDiagnosticWire
{
    public string Severity { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }
}
