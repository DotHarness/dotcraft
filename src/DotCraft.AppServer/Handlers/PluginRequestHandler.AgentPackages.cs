using DotCraft.Agents;
using DotCraft.Agents.Packages;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Plugins.Marketplaces;
using DotCraft.Skills;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class PluginRequestHandler
{
    private const string ImportExpiredCode = "importExpired";
    private const string AgentNameTakenCode = "agentNameTaken";
    private const string PackageNotFoundCode = "packageNotFound";

    private sealed record AgentExportCandidate(
        AgentPackageEntry Entry,
        string? Directory,
        IReadOnlyList<string> Reasons,
        long Bytes,
        string? MarketplaceName);

    private Task<AppServerTypedResult<Contract.AgentExportPlanResult>> HandleAgentExportPlanAsync(
        AppServerTypedRequest<Contract.AgentExportPlanParams> request,
        CancellationToken ct)
    {
        _ = ct;
        var (profile, candidates) = PlanAgentExport(request.Params.Id, request.Params.Source);
        return Task.FromResult(AppServerTypedResult<Contract.AgentExportPlanResult>.FromResult(new()
        {
            FileName = AgentPackageWriter.FileName(profile.Name ?? profile.Id),
            MaximumBytes = AgentPackageLimits.MaximumBytes,
            Packages = candidates.Select(candidate => new Contract.AgentExportPackage
            {
                Kind = candidate.Entry.Kind,
                Name = candidate.Entry.Name,
                DisplayName = candidate.Entry.DisplayName,
                Version = candidate.Entry.Version,
                Dotnet = candidate.Entry.Dotnet,
                Bytes = (int)Math.Min(candidate.Bytes, int.MaxValue),
                MarketplaceName = candidate.MarketplaceName,
                Reasons = candidate.Reasons
            }).ToList()
        }));
    }

    private async Task<AppServerTypedResult<Contract.AgentExportReadResult>> HandleAgentExportReadAsync(
        AppServerTypedRequest<Contract.AgentExportReadParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        if (p.Offset < 0)
        {
            DiscardAgentExport();
            return AppServerTypedResult<Contract.AgentExportReadResult>.FromResult(new() { TotalBytes = 0, DataBase64 = "" });
        }

        if (p.Offset == 0)
        {
            DiscardAgentExport();
            _agentExport = WriteAgentExport(p);
        }

        var export = _agentExport;
        if (export == null || export.Id != p.Id || export.Source != p.Source || export.Offset != p.Offset)
            throw AppServerErrors.InvalidParams("The export transfer is missing or its offset is invalid.");

        var chunk = new byte[Math.Min(AgentPackageChunkBytes, export.TotalBytes - export.Offset)];
        await using (var stream = File.OpenRead(export.Path))
        {
            stream.Position = export.Offset;
            await stream.ReadExactlyAsync(chunk, ct).ConfigureAwait(false);
        }
        export.Offset += chunk.Length;
        if (export.Offset == export.TotalBytes)
            DiscardAgentExport();
        return AppServerTypedResult<Contract.AgentExportReadResult>.FromResult(new()
        {
            TotalBytes = export.TotalBytes,
            DataBase64 = Convert.ToBase64String(chunk)
        });
    }

    private AgentExportTransfer WriteAgentExport(Contract.AgentExportReadParams p)
    {
        var (profile, candidates) = PlanAgentExport(p.Id, p.Source);
        var chosen = p.Packages
            .Select(choice => candidates.FirstOrDefault(candidate => IsPackage(candidate.Entry, choice))
                              ?? throw AgentPackageError(PackageNotFoundCode, $"{choice.Kind} {choice.Name} is not offered for export."))
            .Distinct()
            .Select(candidate => new AgentPackageItem(candidate.Entry, candidate.Directory))
            .ToList();
        SweepAgentPackageStaging();
        var staging = AgentPackageStagingPath();
        var path = Path.Combine(staging, $"{Guid.NewGuid():N}.agent.zip");
        var work = Path.Combine(staging, Guid.NewGuid().ToString("N"));
        try
        {
            AgentPackageWriter.Write(
                path,
                profile.Name ?? profile.Id,
                profile.Description,
                profile.RawContent ?? string.Empty,
                chosen,
                work,
                DateTimeOffset.UtcNow);
        }
        catch (AgentPackageException ex)
        {
            DeleteAgentPackagePath(path);
            throw AgentPackageError(ex.Code, ex.Message);
        }
        catch
        {
            DeleteAgentPackagePath(path);
            throw;
        }
        finally
        {
            DeleteAgentPackagePath(work);
        }

        return new AgentExportTransfer(p.Id, p.Source, path, (int)new FileInfo(path).Length);
    }

    private (AgentProfileEntry Profile, IReadOnlyList<AgentExportCandidate> Candidates) PlanAgentExport(string id, string source)
    {
        RequireAgentPackageSupport();
        if (source is not (AgentProfileSources.User or AgentProfileSources.Workspace))
            throw AppServerErrors.InvalidParams("Only user and workspace profiles can be exported.");

        AgentProfileEntry profile;
        try
        {
            profile = CreateAgentProfileStore().Read(id, source);
        }
        catch (AgentProfileException ex)
        {
            throw AgentProfileRequestHandler.MapError(ex);
        }

        var references = AgentProfileReferences.Read(profile.RawContent ?? string.Empty);
        var candidates = new List<AgentExportCandidate>();
        foreach (var skill in (skillsLoader?.ListSkills() ?? []).Where(skill => skill.Source is "workspace" or "user"))
        {
            var entry = new AgentPackageEntry(
                AgentPackageKinds.Skill,
                skill.Name,
                SkillsLoader.GetSkillInterfaceFromFile(skill.Path)?.DisplayName ?? skill.Name,
                null,
                string.Empty,
                false,
                [skill.Name],
                [],
                null,
                null);
            var directory = Path.GetDirectoryName(skill.Path)!;
            candidates.Add(new AgentExportCandidate(
                entry,
                directory,
                AgentPackageOffer.From(entry).Reasons(references),
                AgentPackageWriter.DirectoryBytes(directory),
                null));
        }

        var catalog = new BuiltInPluginCatalog(builtInPluginSourceRoots, appConfigMonitor?.Current.Plugins, workspaceConfig.UserDataPath)
            .Discover()
            .Plugins;
        var marketplaces = ConfiguredMarketplaces();
        foreach (var plugin in RefreshPluginRuntime().Plugins
                     .Where(plugin => plugin.Installed)
                     .OrderBy(plugin => plugin.Manifest.Id, StringComparer.OrdinalIgnoreCase))
        {
            var offeredBy = catalog.FirstOrDefault(item => PluginIds.EqualsCanonical(item.Manifest.Id, plugin.Manifest.Id));
            var marketplace = marketplaces.FirstOrDefault(item =>
                string.Equals(item.Name, offeredBy?.MarketplaceName, StringComparison.OrdinalIgnoreCase));
            if (offeredBy != null && marketplace == null)
                continue;

            var reference = marketplace is { Kind: MarketplaceSourceKind.Git or MarketplaceSourceKind.Archive } ? marketplace : null;
            var entry = PluginPackageEntry(plugin) with
            {
                Marketplace = reference == null
                    ? null
                    : new AgentPackageMarketplace(
                        reference.Name,
                        MarketplaceKind(reference.Kind),
                        reference.Source,
                        reference.Ref,
                        reference.MarketplacePath,
                        reference.SparsePaths)
            };
            candidates.Add(new AgentExportCandidate(
                entry,
                reference == null ? plugin.Manifest.RootPath : null,
                AgentPackageOffer.From(entry).Reasons(references),
                reference == null ? AgentPackageWriter.DirectoryBytes(plugin.Manifest.RootPath) : 0,
                reference == null ? null : reference.DisplayName ?? reference.Name));
        }

        return (profile, candidates);
    }

    private async Task<AppServerTypedResult<Contract.AgentProfileUpsertResult>> HandleAgentImportCommitAsync(
        AppServerTypedRequest<Contract.AgentImportCommitParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        RequireAgentPackageSupport();
        if (p.Source is not (AgentProfileSources.User or AgentProfileSources.Workspace))
            throw AppServerErrors.InvalidParams("Imported profiles are saved to 'user' or 'workspace'.");

        SweepAgentPackageStaging();
        var import = FindAgentImport(p.ImportId);
        var content = import.Content ?? throw ImportExpired();
        var store = CreateAgentProfileStore();
        string name;
        string document;
        try
        {
            name = AgentProfileName.Normalize(p.Name);
            if (AgentProfileNameTaken(store, name))
                throw AgentPackageError(AgentNameTakenCode, $"Another agent already uses the name '{name}'.");
            var description = string.IsNullOrWhiteSpace(p.Description)
                ? content.Manifest.Description ?? string.Empty
                : p.Description.Trim();
            document = AgentPackageDocument.WithIdentity(content.Document, name, description);
            var validation = store.ValidateRaw(document, p.Source, name);
            if (!validation.Valid)
                throw new AgentProfileException(AgentProfileErrorKind.ValidationFailed, "Agent profile validation failed.", validation.Diagnostics);
        }
        catch (AgentProfileException ex)
        {
            throw AgentProfileRequestHandler.MapError(ex);
        }

        var states = ResolveAgentImportStates(content, RefreshPluginRuntime());
        var chosen = new List<AgentImportState>();
        foreach (var choice in p.Packages)
        {
            var state = states.FirstOrDefault(candidate => IsPackage(candidate.Entry, choice))
                        ?? throw AgentPackageError(PackageNotFoundCode, $"The import does not list {choice.Kind} {choice.Name}.");
            if (state.State == AgentImportStates.Installed || chosen.Contains(state))
                continue;
            if (!state.Installable)
                throw AgentPackageError(PackageNotFoundCode, $"{choice.Kind} {choice.Name} cannot be installed from this import.");
            chosen.Add(state);
        }

        var commitToken = EnterMutationCommit(ct);
        var installedPlugins = new List<string>();
        var installedAny = false;
        AgentProfileEntry profile;
        try
        {
            foreach (var state in chosen)
            {
                await InstallAgentImportPackageAsync(import, state, p.Source, installedPlugins, commitToken).ConfigureAwait(false);
                installedAny = true;
            }

            store.Upsert(name, p.Source, document);
            profile = store.Read(name, p.Source);
        }
        catch (Exception exception) when (installedAny)
        {
            await FinalizeAgentImportInstallsAsync(installedPlugins, commitToken).ConfigureAwait(false);
            if (exception is AgentProfileException profileError)
                throw AgentProfileRequestHandler.MapError(profileError);
            throw;
        }
        catch (AgentProfileException ex)
        {
            throw AgentProfileRequestHandler.MapError(ex);
        }

        DiscardAgentImport(import);
        IReadOnlyList<Contract.AppListUpdatedNotification?> appListUpdates = [];
        IReadOnlyList<string> affected = [];
        if (installedAny)
        {
            var finalized = await FinalizeAgentImportInstallsAsync(installedPlugins, commitToken).ConfigureAwait(false);
            affected = finalized.AffectedPluginIds;
            appListUpdates = installedPlugins
                .Select(pluginId => TryBuildAppListUpdatedNotification(
                    finalized.Discovery,
                    pluginId,
                    Protocol.AppServer.AppServerMethodNames.AgentProfileImportCommit))
                .ToList();
        }

        var result = new Contract.AgentProfileUpsertResult
        {
            Profile = AgentProfileRequestHandler.ToContract(
                profile,
                appConfigMonitor?.Current,
                includeRawContent: true,
                includeCompiledConfig: true)
        };
        return await WriteWithLifecycleNotificationsAsync(
            request.Message,
            result,
            appListUpdates,
            [],
            [.. installedPlugins, .. affected],
            ct).ConfigureAwait(false);
    }

    private async Task InstallAgentImportPackageAsync(
        AgentImport import,
        AgentImportState state,
        string scope,
        List<string> installedPlugins,
        CancellationToken commitToken)
    {
        var entry = state.Entry;
        if (state.State == AgentImportStates.Bundled)
        {
            var staging = Path.Combine(AgentPackageStagingPath(), Guid.NewGuid().ToString("N"));
            try
            {
                try
                {
                    AgentPackageReader.ExtractPackage(import.Path, entry, staging);
                }
                catch (AgentPackageException ex)
                {
                    throw AgentPackageError(ex.Code, ex.Message);
                }

                if (entry.Kind == AgentPackageKinds.Skill)
                    await InstallAgentImportSkillAsync(import, entry, staging, scope, commitToken).ConfigureAwait(false);
                else
                    installedPlugins.Add(InstallAgentImportPlugin(staging, scope));
            }
            finally
            {
                DeleteAgentPackagePath(staging);
            }
            return;
        }

        if (state.State == AgentImportStates.AddMarketplace)
        {
            var source = entry.Marketplace!;
            await RunMarketplaceOperationAsync(() => CreateMarketplaceManager().AddAsync(
                new MarketplaceAddRequest(source.Source, source.Ref, source.SparsePaths, source.MarketplacePath),
                commitToken)).ConfigureAwait(false);
            SyncConfiguredMarketplaces();
        }

        var pluginId = PluginIds.Canonicalize(entry.Name);
        DeployCatalogPlugin(pluginId);
        installedPlugins.Add(pluginId);
    }

    private async Task InstallAgentImportSkillAsync(
        AgentImport import,
        AgentPackageEntry entry,
        string directory,
        string scope,
        CancellationToken commitToken)
    {
        var loader = skillsLoader ?? throw AppServerErrors.InvalidParams("Skills are unavailable in this workspace.");
        var root = scope == AgentProfileSources.User
            ? loader.UserSkillsPath ?? Path.Combine(PluginDataPath("user"), "skills")
            : loader.WorkspaceSkillsPath;
        var result = await new SkillInstallService(loader)
            .InstallAsync(new SkillInstallRequest(directory, entry.Name, Source: import.FileName, TargetRoot: root), commitToken)
            .ConfigureAwait(false);
        if (!result.Success)
            throw AgentPackageError(AgentPackageException.InvalidCode, string.Join(" ", result.Errors));
    }

    private string InstallAgentImportPlugin(string directory, string scope)
    {
        var install = new LocalPluginInstaller(Path.Combine(PluginDataPath(scope), "plugins")).Install(directory);
        PluginDiagnosticsLogger.Write(install.Diagnostics, logger);
        return install.PluginId
               ?? throw AppServerErrors.InvalidParams(
                   install.Diagnostics.FirstOrDefault(d => d.Severity == PluginDiagnosticSeverity.Error)?.Message
                   ?? "The plugin could not be installed.");
    }

    private async Task<(PluginDiscoveryResult Discovery, IReadOnlyList<string> AffectedPluginIds)> FinalizeAgentImportInstallsAsync(
        IReadOnlyList<string> pluginIds,
        CancellationToken commitToken)
    {
        var current = appConfigMonitor?.Current ?? new AppConfig();
        var installed = RefreshPluginRuntime();
        var affected = new List<string>();
        foreach (var pluginId in pluginIds)
        {
            if (installed.Plugins.FirstOrDefault(plugin => plugin.Installed && PluginIds.EqualsCanonical(plugin.Manifest.Id, pluginId)) is { } plugin)
                SetScopedPluginEnabled(plugin, true);
            current.Plugins.EnabledPlugins.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));
            if (dotnetRuntime != null)
                affected.AddRange((await dotnetRuntime.ReconcileAfterMutationAsync(pluginId, commitToken).ConfigureAwait(false)).AffectedPluginIds);
        }

        var discovery = await FinalizePluginContributionRefreshAsync(
            Protocol.AppServer.AppServerMethodNames.AgentProfileImportCommit,
            commitToken).ConfigureAwait(false);
        if (pluginIds.Count > 0)
            AdvancePluginSnapshotRevision();
        return (discovery, affected);
    }

    private AgentProfileStore CreateAgentProfileStore() => new(workspaceCraftPath, workspaceConfig.UserDataPath);

    private static bool AgentProfileNameTaken(AgentProfileStore store, string name) =>
        store.List(AgentProfileSources.User)
            .Concat(store.List(AgentProfileSources.Workspace))
            .Any(profile => string.Equals(profile.Id, name, StringComparison.Ordinal));

    private static bool IsPackage(AgentPackageEntry entry, Contract.AgentPackageRef choice) =>
        string.Equals(entry.Kind, choice.Kind, StringComparison.Ordinal)
        && string.Equals(entry.Name, choice.Name, StringComparison.OrdinalIgnoreCase);

    private static AgentPackageEntry PluginPackageEntry(DiscoveredPlugin plugin)
    {
        var manifest = plugin.Manifest;
        var skills = string.IsNullOrWhiteSpace(manifest.SkillsPath) || !Directory.Exists(manifest.SkillsPath)
            ? []
            : Directory.GetDirectories(manifest.SkillsPath)
                .Where(directory => File.Exists(Path.Combine(directory, "SKILL.md")))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
                .ToArray();
        var servers = PluginMcpServerLoader.LoadPluginServers(plugin, [])
            .Select(server => server.Origin.DeclaredName ?? server.Name)
            .ToArray();
        return new AgentPackageEntry(
            AgentPackageKinds.Plugin,
            manifest.Id,
            manifest.Interface?.DisplayName ?? manifest.DisplayName,
            manifest.Version,
            string.Empty,
            manifest.Dotnet != null,
            skills,
            servers,
            null,
            null);
    }

    private IReadOnlyList<MarketplaceEntry> ConfiguredMarketplaces()
    {
        try
        {
            return CreateMarketplaceManager().List();
        }
        catch (MarketplaceException)
        {
            return [];
        }
    }

    private static string MarketplaceKind(MarketplaceSourceKind kind) => kind.ToString().ToLowerInvariant();

    private static AppServerException AgentPackageError(string code, string message) =>
        new(AppServerErrors.InvalidParamsCode, message, new AppServerErrorData
        {
            Code = code, MessageKey = code, FallbackText = message
        });

    private static AppServerException ImportExpired() =>
        AgentPackageError(ImportExpiredCode, "The import is no longer held. Choose the file again.");
}
