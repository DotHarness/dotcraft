using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Skills;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

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
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.SkillsList, HandleSkillsListAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.SkillsRead, HandleSkillsReadAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.SkillsView, HandleSkillsViewAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.SkillsRestoreOriginal, HandleSkillsRestoreOriginalAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.SkillsSetEnabled, HandleSkillsSetEnabledAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.SkillsUninstall, HandleSkillsUninstallAsync);
    }

    private Task<AppServerTypedResult<Contract.SkillsListResult>> HandleSkillsListAsync(
        AppServerTypedRequest<Contract.SkillsListParams> request,
        CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsList);
        _ = ct;
        var p = request.Params;
        var includeUnavailable = p.IncludeUnavailable.IsSet ? p.IncludeUnavailable.Value ?? true : true;
        var list = skillsLoader.ListSkills(filterUnavailable: !includeUnavailable);
        var wires = list.Select(MapSkillToWire).ToList();
        return Task.FromResult(AppServerTypedResult<Contract.SkillsListResult>.FromResult(
            new Contract.SkillsListResult { Skills = wires }));
    }

    private Task<AppServerTypedResult<Contract.SkillsReadResult>> HandleSkillsReadAsync(
        AppServerTypedRequest<Contract.SkillsReadParams> request,
        CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsRead);
        _ = ct;
        var name = RequireName(request.Params.Name);
        var content = skillsLoader.LoadSkill(name);
        if (content == null)
            throw AppServerErrors.SkillNotFound(name);
        var metadata = skillsLoader.GetSkillMetadata(name);
        return Task.FromResult(AppServerTypedResult<Contract.SkillsReadResult>.FromResult(new Contract.SkillsReadResult
        {
            Name = name,
            Content = content,
            Metadata = metadata
        }));
    }

    private Task<AppServerTypedResult<Contract.SkillsViewResult>> HandleSkillsViewAsync(
        AppServerTypedRequest<Contract.SkillsViewParams> request,
        CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsView);
        _ = ct;
        var name = RequireName(request.Params.Name);

        var target = variantContext.BuildTarget();
        var effective = skillsLoader.LoadEffectiveSkill(
            name,
            variantContext.IsVariantModeEnabled(),
            target);
        if (effective == null)
            throw AppServerErrors.SkillNotFound(name);

        return Task.FromResult(AppServerTypedResult<Contract.SkillsViewResult>.FromResult(new Contract.SkillsViewResult
        {
            Name = name,
            Content = effective.Content
        }));
    }

    private Task<AppServerTypedResult<Contract.SkillsRestoreOriginalResult>> HandleSkillsRestoreOriginalAsync(
        AppServerTypedRequest<Contract.SkillsRestoreOriginalParams> request,
        CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsRestoreOriginal);
        _ = ct;
        var name = RequireName(request.Params.Name);

        if (skillsLoader.ResolveSkillInfo(name) == null)
            throw AppServerErrors.SkillNotFound(name);

        var restored = variantContext.IsVariantModeEnabled()
            && skillsLoader.RestoreOriginalSkill(name, variantContext.BuildTarget());
        if (restored)
            AppServerContextInvalidation.MarkSkills(contextPageManager);
        return Task.FromResult(AppServerTypedResult<Contract.SkillsRestoreOriginalResult>.FromResult(new Contract.SkillsRestoreOriginalResult
        {
            Name = name,
            Restored = restored
        }));
    }

    private Task<AppServerTypedResult<Contract.SkillsSetEnabledResult>> HandleSkillsSetEnabledAsync(
        AppServerTypedRequest<Contract.SkillsSetEnabledParams> request,
        CancellationToken ct)
    {
        if (skillsLoader == null || string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsSetEnabled);
        _ = ct;
        var p = request.Params;
        var name = RequireName(p.Name);
        var enabled = p.Enabled.IsSet && p.Enabled.Value;

        var all = skillsLoader.ListSkills(filterUnavailable: false);
        if (all.All(s => !string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw AppServerErrors.SkillNotFound(name);

        var disabled = all.Where(s => !s.Enabled).Select(s => s.Name).ToList();
        if (enabled)
            disabled.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        else if (!disabled.Contains(name, StringComparer.OrdinalIgnoreCase))
            disabled.Add(name);

        SkillsConfigPersistence.WriteWorkspaceDisabledSkills(workspaceCraftPath, disabled);
        skillsLoader.SetDisabledSkills(disabled);
        AppServerContextInvalidation.MarkSkills(contextPageManager);
        appConfigMonitor?.NotifyChanged(
            DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsSetEnabled,
            [ConfigChangeRegions.Skills]);

        var updated = skillsLoader.ListSkills(filterUnavailable: false)
            .First(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(AppServerTypedResult<Contract.SkillsSetEnabledResult>.FromResult(
            new Contract.SkillsSetEnabledResult { Skill = MapSkillToWire(updated) }));
    }

    private Task<AppServerTypedResult<Contract.SkillsUninstallResult>> HandleSkillsUninstallAsync(
        AppServerTypedRequest<Contract.SkillsUninstallParams> request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (skillsLoader == null || string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsUninstall);

        var name = RequireName(request.Params.Name);

        var source = skillsLoader.ResolveSkillInfo(name);
        if (source == null)
            throw AppServerErrors.SkillNotFound(name);

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
            DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsUninstall,
            [ConfigChangeRegions.Skills]);

        return Task.FromResult(AppServerTypedResult<Contract.SkillsUninstallResult>.FromResult(new Contract.SkillsUninstallResult
        {
            Name = source.Name,
            Uninstalled = true,
            Source = source.Source,
            RemovedSourcePath = skillDir,
            RemovedVariantCount = removedVariantCount
        }));
    }

    private Contract.SkillInfo MapSkillToWire(SkillsLoader.SkillInfo s)
    {
        var metadata = skillsLoader!.GetSkillMetadata(s.Name);
        var interfaceInfo = skillsLoader.GetSkillInterface(s.Name);
        return new Contract.SkillInfo
        {
            Name = s.Name,
            Description = skillsLoader.GetSkillDescription(s.Name),
            DisplayName = OmitIfNull(interfaceInfo?.DisplayName),
            ShortDescription = OmitIfNull(interfaceInfo?.ShortDescription),
            Source = s.Source,
            PluginId = OmitIfNull(s.PluginId),
            PluginDisplayName = OmitIfNull(s.PluginDisplayName),
            Available = s.Available,
            UnavailableReason = OmitIfNull(s.UnavailableReason),
            Enabled = s.Enabled,
            Path = s.Path,
            HasVariant = HasCurrentSkillVariant(s),
            IconSmallDataUrl = OmitIfNull(interfaceInfo?.IconSmallDataUrl),
            IconLargeDataUrl = OmitIfNull(interfaceInfo?.IconLargeDataUrl),
            DefaultPrompt = OmitIfNull(interfaceInfo?.DefaultPrompt),
            Metadata = OmitIfNull<IReadOnlyDictionary<string, string>>(metadata)
        };
    }

    private static DotCraft.Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : DotCraft.Protocol.Optional<T?>.FromValue(value);

    private static string RequireName(DotCraft.Protocol.Optional<string> value)
    {
        var name = value.IsSet ? value.Value : null;
        if (string.IsNullOrWhiteSpace(name))
            throw AppServerErrors.InvalidParams("'name' is required.");
        return name;
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
