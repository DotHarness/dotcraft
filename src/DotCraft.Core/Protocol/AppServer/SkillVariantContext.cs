using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Skills;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Shared resolver for skill-variant mode state and the per-connection variant target. Skill
/// variant mode is consulted by several domains (skills/*, turn start, the initialize capability
/// report), so the logic lives here and is shared by the dispatcher and the extracted
/// <c>SkillsRequestHandler</c> rather than duplicated.
/// </summary>
internal sealed class SkillVariantContext(
    IAppConfigMonitor? appConfigMonitor,
    ChatClientRegistry chatClientRegistry,
    string? hostWorkspacePath,
    string? workspaceCraftPath)
{
    public bool IsVariantModeEnabled()
    {
        var config = appConfigMonitor?.Current ?? new AppConfig();
        return string.Equals(
            config.Skills.SelfLearning.VariantMode,
            "enabled",
            StringComparison.OrdinalIgnoreCase);
    }

    public SkillVariantTarget BuildTarget()
    {
        var config = appConfigMonitor?.Current ?? new AppConfig();
        var model = ResolveMainModelOrFallback(config);
        return SkillVariantStore.CreateTarget(
            model,
            hostWorkspacePath ?? workspaceCraftPath ?? string.Empty,
            config.Tools.Sandbox.Enabled,
            config.Permissions.DefaultApprovalPolicy.ToString(),
            toolNames: null);
    }

    private string ResolveMainModelOrFallback(AppConfig config)
    {
        try
        {
            return chatClientRegistry.ResolveMainModel(config);
        }
        catch (Exception ex) when (ex is ArgumentException or ModelProviderConfigurationException)
        {
            return string.Empty;
        }
    }
}
