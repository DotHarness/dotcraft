using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Skills;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Handles the <c>skills/*</c> wire methods (spec Section 18): list, read, view, restore-original,
/// set-enabled, and uninstall. Skill-variant resolution is delegated to the shared
/// <see cref="SkillVariantContext"/>; the skills context page is invalidated via
/// <see cref="AppServerContextInvalidation"/>.
/// </summary>
internal sealed class SkillsRequestHandler(
    SkillsLoader? skillsLoader,
    IContextPageManager? contextPageManager,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    SkillVariantContext variantContext) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.SkillsList, HandleSkillsListAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.SkillsRead, HandleSkillsReadAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.SkillsView, HandleSkillsViewAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.SkillsRestoreOriginal, HandleSkillsRestoreOriginalAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.SkillsSetEnabled, HandleSkillsSetEnabledAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.SkillsUninstall, HandleSkillsUninstallAsync);
    }

    private Task<object?> HandleSkillsListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SkillsList);
        var p = AppServerParams.Get<SkillsListParams>(msg);
        var includeUnavailable = p.IncludeUnavailable ?? true;
        var list = skillsLoader.ListSkills(filterUnavailable: !includeUnavailable);
        var wires = list.Select(MapSkillToWire).ToList();
        return Task.FromResult<object?>(new SkillsListResult { Skills = wires });
    }

    private Task<object?> HandleSkillsReadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SkillsRead);
        var p = AppServerParams.Get<SkillsReadParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");
        var content = skillsLoader.LoadSkill(p.Name);
        if (content == null)
            throw AppServerErrors.SkillNotFound(p.Name);
        var metadata = skillsLoader.GetSkillMetadata(p.Name);
        return Task.FromResult<object?>(new SkillsReadResult
        {
            Name = p.Name,
            Content = content,
            Metadata = metadata
        });
    }

    private Task<object?> HandleSkillsViewAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SkillsView);
        var p = AppServerParams.Get<SkillsViewParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var target = variantContext.BuildTarget();
        var effective = skillsLoader.LoadEffectiveSkill(
            p.Name,
            variantContext.IsVariantModeEnabled(),
            target);
        if (effective == null)
            throw AppServerErrors.SkillNotFound(p.Name);

        return Task.FromResult<object?>(new SkillsViewResult
        {
            Name = p.Name,
            Content = effective.Content
        });
    }

    private Task<object?> HandleSkillsRestoreOriginalAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SkillsRestoreOriginal);
        var p = AppServerParams.Get<SkillsRestoreOriginalParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        if (skillsLoader.ResolveSkillInfo(p.Name) == null)
            throw AppServerErrors.SkillNotFound(p.Name);

        var restored = variantContext.IsVariantModeEnabled()
            && skillsLoader.RestoreOriginalSkill(p.Name, variantContext.BuildTarget());
        if (restored)
            AppServerContextInvalidation.MarkSkills(contextPageManager);
        return Task.FromResult<object?>(new SkillsRestoreOriginalResult
        {
            Name = p.Name,
            Restored = restored
        });
    }

    private Task<object?> HandleSkillsSetEnabledAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (skillsLoader == null || string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SkillsSetEnabled);
        var p = AppServerParams.Get<SkillsSetEnabledParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var all = skillsLoader.ListSkills(filterUnavailable: false);
        if (all.All(s => !string.Equals(s.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
            throw AppServerErrors.SkillNotFound(p.Name);

        var disabled = all.Where(s => !s.Enabled).Select(s => s.Name).ToList();
        if (p.Enabled)
            disabled.RemoveAll(n => string.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase));
        else if (!disabled.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            disabled.Add(p.Name);

        SkillsConfigPersistence.WriteWorkspaceDisabledSkills(workspaceCraftPath, disabled);
        skillsLoader.SetDisabledSkills(disabled);
        AppServerContextInvalidation.MarkSkills(contextPageManager);
        appConfigMonitor?.NotifyChanged(
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SkillsSetEnabled,
            [ConfigChangeRegions.Skills]);

        var updated = skillsLoader.ListSkills(filterUnavailable: false)
            .First(s => string.Equals(s.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<object?>(new SkillsSetEnabledResult { Skill = MapSkillToWire(updated) });
    }

    private Task<object?> HandleSkillsUninstallAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (skillsLoader == null || string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SkillsUninstall);

        var p = AppServerParams.Get<SkillsUninstallParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var source = skillsLoader.ResolveSkillInfo(p.Name);
        if (source == null)
            throw AppServerErrors.SkillNotFound(p.Name);

        if (!string.Equals(source.Source, "workspace", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(source.Source, "user", StringComparison.OrdinalIgnoreCase))
        {
            throw AppServerErrors.InvalidParams(
                $"Skill '{source.Name}' is {source.Source} and cannot be uninstalled directly.");
        }

        var skillDir = Path.GetDirectoryName(source.Path);
        if (string.IsNullOrWhiteSpace(skillDir))
            throw AppServerErrors.InvalidParams($"Skill '{source.Name}' has an invalid path.");

        var allowedRoot = string.Equals(source.Source, "workspace", StringComparison.OrdinalIgnoreCase)
            ? skillsLoader.WorkspaceSkillsPath
            : skillsLoader.UserSkillsPath;
        if (!IsStrictChildPathOf(skillDir, allowedRoot))
            throw AppServerErrors.InvalidParams($"Skill '{source.Name}' is outside the allowed {source.Source} skill root.");

        var disabled = skillsLoader.ListSkills(filterUnavailable: false)
            .Where(s => !s.Enabled)
            .Select(s => s.Name)
            .ToList();
        disabled.RemoveAll(n => string.Equals(n, source.Name, StringComparison.OrdinalIgnoreCase));

        var removedVariantCount = skillsLoader.VariantStore.DeleteVariantsForSource(source);
        Directory.Delete(skillDir, recursive: true);

        SkillsConfigPersistence.WriteWorkspaceDisabledSkills(workspaceCraftPath, disabled);
        skillsLoader.SetDisabledSkills(disabled);
        skillsLoader.RefreshDescriptors();
        AppServerContextInvalidation.MarkSkills(contextPageManager);
        appConfigMonitor?.NotifyChanged(
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SkillsUninstall,
            [ConfigChangeRegions.Skills]);

        return Task.FromResult<object?>(new SkillsUninstallResult
        {
            Name = source.Name,
            Uninstalled = true,
            Source = source.Source,
            RemovedSourcePath = skillDir,
            RemovedVariantCount = removedVariantCount
        });
    }

    private SkillInfoWire MapSkillToWire(SkillsLoader.SkillInfo s)
    {
        var metadata = skillsLoader!.GetSkillMetadata(s.Name);
        var interfaceInfo = skillsLoader.GetSkillInterface(s.Name);
        return new SkillInfoWire
        {
            Name = s.Name,
            Description = skillsLoader.GetSkillDescription(s.Name),
            DisplayName = interfaceInfo?.DisplayName,
            ShortDescription = interfaceInfo?.ShortDescription,
            Source = s.Source,
            PluginId = s.PluginId,
            PluginDisplayName = s.PluginDisplayName,
            Available = s.Available,
            UnavailableReason = s.UnavailableReason,
            Enabled = s.Enabled,
            Path = s.Path,
            HasVariant = HasCurrentSkillVariant(s),
            IconSmallDataUrl = interfaceInfo?.IconSmallDataUrl,
            IconLargeDataUrl = interfaceInfo?.IconLargeDataUrl,
            DefaultPrompt = interfaceInfo?.DefaultPrompt,
            Metadata = metadata
        };
    }

    private bool HasCurrentSkillVariant(SkillsLoader.SkillInfo source)
    {
        if (skillsLoader == null || !variantContext.IsVariantModeEnabled())
            return false;

        var effectivePath = skillsLoader.ResolveEffectiveSkillFile(source, true, variantContext.BuildTarget());
        return effectivePath != null
               && !string.Equals(effectivePath, source.Path, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStrictChildPathOf(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
