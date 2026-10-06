using DotCraft.Agents;
using DotCraft.Agents.Packages;
using DotCraft.Plugins;
using DotCraft.Plugins.Marketplaces;
using DotCraft.Skills;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class PluginRequestHandler
{
    private const int AgentPackageChunkBytes = 1024 * 1024;
    private static readonly TimeSpan AgentPackageLifetime = TimeSpan.FromHours(1);

    private readonly Dictionary<string, AgentImport> _agentImports = new(StringComparer.Ordinal);
    private AgentExportTransfer? _agentExport;

    private sealed class AgentImport(string id, string path, string fileName, int totalBytes)
    {
        public string Id { get; } = id;
        public string Path { get; } = path;
        public string FileName { get; } = fileName;
        public int TotalBytes { get; } = totalBytes;
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public int Received { get; set; }
        public AgentPackageContent? Content { get; set; }
    }

    private sealed class AgentExportTransfer(string id, string source, string path, int totalBytes)
    {
        public string Id { get; } = id;
        public string Source { get; } = source;
        public string Path { get; } = path;
        public int TotalBytes { get; } = totalBytes;
        public int Offset { get; set; }
    }

    private static class AgentImportStates
    {
        public const string Installed = "installed";
        public const string Bundled = "bundled";
        public const string Marketplace = "marketplace";
        public const string AddMarketplace = "addMarketplace";
        public const string Unavailable = "unavailable";
    }

    private sealed record AgentImportState(
        AgentPackageEntry Entry,
        string State,
        string? InstalledVersion = null,
        string? MarketplaceName = null,
        string? Reason = null)
    {
        public bool Installable => State is AgentImportStates.Bundled or AgentImportStates.Marketplace or AgentImportStates.AddMarketplace;
    }

    private async Task<AppServerTypedResult<Contract.AgentImportUploadResult>> HandleAgentImportUploadAsync(
        AppServerTypedRequest<Contract.AgentImportUploadParams> request,
        CancellationToken ct)
    {
        RequireAgentPackageSupport();
        var p = request.Params;
        if (p.TotalBytes > AgentPackageLimits.MaximumBytes)
            throw AgentPackageError(AgentPackageException.TooLargeCode, "An Agent package must be 64 MiB or smaller.");
        byte[] data;
        try
        {
            data = Convert.FromBase64String(p.DataBase64);
        }
        catch (FormatException)
        {
            throw AppServerErrors.InvalidParams("'dataBase64' is not valid base64.");
        }
        if (p.TotalBytes <= 0 || data.Length == 0 || data.Length > AgentPackageChunkBytes)
            throw AppServerErrors.InvalidParams("Upload a non-empty file in chunks of at most 1 MiB.");

        SweepAgentPackageStaging();
        AgentImport import;
        if (p.ImportId == null)
        {
            if (p.Offset != 0)
                throw AppServerErrors.InvalidParams("The first chunk starts at offset 0.");
            var id = Guid.NewGuid().ToString("N");
            import = new AgentImport(id, Path.Combine(AgentPackageStagingPath(), $"{id}.upload"), p.FileName, p.TotalBytes);
            _agentImports[id] = import;
        }
        else
        {
            import = FindAgentImport(p.ImportId);
        }

        if (import.Content != null
            || p.TotalBytes != import.TotalBytes
            || p.Offset != import.Received
            || import.Received + data.Length > import.TotalBytes)
            throw AppServerErrors.InvalidParams("The upload offset is invalid.");

        await using (var stream = new FileStream(import.Path, FileMode.Append, FileAccess.Write))
            await stream.WriteAsync(data, ct).ConfigureAwait(false);
        import.Received += data.Length;
        if (import.Received < import.TotalBytes)
        {
            return AppServerTypedResult<Contract.AgentImportUploadResult>.FromResult(new()
            {
                ImportId = import.Id,
                ReceivedBytes = import.Received
            });
        }

        try
        {
            import.Content = AgentPackageReader.Read(import.Path);
        }
        catch (AgentPackageException ex)
        {
            DiscardAgentImport(import);
            throw AgentPackageError(ex.Code, ex.Message);
        }
        catch
        {
            DiscardAgentImport(import);
            throw;
        }

        return AppServerTypedResult<Contract.AgentImportUploadResult>.FromResult(new()
        {
            ImportId = import.Id,
            ReceivedBytes = import.Received,
            Preview = BuildAgentImportPreview(import.Id, import.Content)
        });
    }

    private Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleAgentImportDiscardAsync(
        AppServerTypedRequest<Contract.AgentImportDiscardParams> request,
        CancellationToken ct)
    {
        _ = ct;
        if (_agentImports.TryGetValue(request.Params.ImportId, out var import))
            DiscardAgentImport(import);
        return Task.FromResult(AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new Protocol.RpcEmpty()));
    }

    private Contract.AgentImportPreview BuildAgentImportPreview(string importId, AgentPackageContent content)
    {
        const string placeholder = "Imported agent";
        var name = content.Manifest.Name;
        var store = CreateAgentProfileStore();
        var probe = AgentPackageDocument.WithIdentity(
            content.Document,
            AgentProfileName.IsValid(name) ? name : placeholder,
            string.IsNullOrWhiteSpace(content.Manifest.Description) ? placeholder : content.Manifest.Description);
        var discovery = RefreshPluginRuntime();
        var skills = skillsLoader?.ListSkills() ?? [];
        var offers = skills
            .Select(skill => new AgentPackageOffer(AgentPackageKinds.Skill, skill.Name, [skill.Name], []))
            .Concat(discovery.Plugins.Where(plugin => plugin.Installed).Select(plugin => AgentPackageOffer.From(PluginPackageEntry(plugin))))
            .Concat(content.Manifest.Packages.Select(entry => AgentPackageOffer.From(entry)))
            .ToList();
        var unresolved = AgentPackageOffer.Unresolved(
            AgentProfileReferences.Read(content.Document),
            offers,
            (appConfigMonitor?.Current.McpServers ?? []).Select(server => server.Name).ToList());

        return new Contract.AgentImportPreview
        {
            ImportId = importId,
            Kind = content.Markdown ? "markdown" : "package",
            Name = name,
            Description = content.Manifest.Description,
            NameTaken = AgentProfileName.IsValid(name) && AgentProfileNameTaken(store, AgentProfileName.Canonicalize(name)),
            Problems = store.ValidateRaw(probe, AgentProfileSources.Workspace).Diagnostics
                .Where(diagnostic => diagnostic.Severity == "error")
                .Select(diagnostic => diagnostic.Message)
                .ToList(),
            Packages = ResolveAgentImportStates(content, discovery).Select(state => new Contract.AgentImportPackage
            {
                Kind = state.Entry.Kind,
                Name = state.Entry.Name,
                DisplayName = state.Entry.DisplayName,
                Version = state.Entry.Version,
                Dotnet = state.Entry.Dotnet,
                State = state.State,
                InstalledVersion = state.InstalledVersion,
                MarketplaceName = state.MarketplaceName,
                Reason = state.Reason
            }).ToList(),
            Unresolved = new Contract.AgentImportUnresolved
            {
                Skills = unresolved.Skills,
                McpServers = unresolved.McpServers,
                Plugins = unresolved.Plugins
            }
        };
    }

    private IReadOnlyList<AgentImportState> ResolveAgentImportStates(AgentPackageContent content, PluginDiscoveryResult discovery)
    {
        var skills = skillsLoader?.ListSkills() ?? [];
        var marketplaces = ConfiguredMarketplaces();
        return content.Manifest.Packages
            .Select(entry => ResolveAgentImportState(entry, discovery, skills, marketplaces))
            .ToList();
    }

    private static AgentImportState ResolveAgentImportState(
        AgentPackageEntry entry,
        PluginDiscoveryResult discovery,
        IReadOnlyList<SkillsLoader.SkillInfo> skills,
        IReadOnlyList<MarketplaceEntry> marketplaces)
    {
        if (entry.Kind == AgentPackageKinds.Skill)
        {
            return skills.Any(skill => string.Equals(skill.Name, entry.Name, StringComparison.OrdinalIgnoreCase))
                ? new AgentImportState(entry, AgentImportStates.Installed)
                : new AgentImportState(entry, AgentImportStates.Bundled);
        }

        if (discovery.Plugins.FirstOrDefault(plugin => plugin.Installed && PluginIds.EqualsCanonical(plugin.Manifest.Id, entry.Name)) is { } installed)
            return new AgentImportState(entry, AgentImportStates.Installed, installed.Manifest.Version);
        if (entry.File != null)
            return new AgentImportState(entry, AgentImportStates.Bundled);

        var source = entry.Marketplace!;
        var marketplace = marketplaces.FirstOrDefault(candidate =>
            string.Equals(MarketplaceKind(candidate.Kind), source.SourceKind, StringComparison.Ordinal)
            && string.Equals(candidate.Source, source.Source, StringComparison.Ordinal)
            && string.Equals(candidate.MarketplacePath, source.MarketplacePath, StringComparison.Ordinal)
            && string.Equals(candidate.Ref ?? string.Empty, source.Ref ?? string.Empty, StringComparison.Ordinal));
        if (marketplace != null)
        {
            var offered = discovery.Plugins.Any(plugin =>
                !plugin.Installed
                && string.Equals(plugin.MarketplaceName, marketplace.Name, StringComparison.OrdinalIgnoreCase)
                && PluginIds.EqualsCanonical(plugin.Manifest.Id, entry.Name));
            return offered
                ? new AgentImportState(entry, AgentImportStates.Marketplace, MarketplaceName: marketplace.DisplayName ?? marketplace.Name)
                : new AgentImportState(entry, AgentImportStates.Unavailable, MarketplaceName: marketplace.DisplayName ?? marketplace.Name, Reason: "notOffered");
        }

        return source.SourceKind switch
        {
            "git" => new AgentImportState(entry, AgentImportStates.AddMarketplace, MarketplaceName: source.Name),
            "local" => new AgentImportState(entry, AgentImportStates.Unavailable, MarketplaceName: source.Name, Reason: "localMarketplace"),
            _ => new AgentImportState(entry, AgentImportStates.Unavailable, MarketplaceName: source.Name, Reason: "notOffered")
        };
    }

    private AgentImport FindAgentImport(string importId)
    {
        if (_agentImports.TryGetValue(importId, out var import))
            return import;
        throw ImportExpired();
    }

    private void DiscardAgentImport(AgentImport import)
    {
        _agentImports.Remove(import.Id);
        DeleteAgentPackagePath(import.Path);
    }

    private void DiscardAgentExport()
    {
        if (_agentExport != null)
            DeleteAgentPackagePath(_agentExport.Path);
        _agentExport = null;
    }

    private async Task CloseAgentPackagesAsync()
    {
        await connection.Closed.ConfigureAwait(false);
        await managementState.RunSnapshotReadAsync(_ =>
        {
            DiscardAgentExport();
            foreach (var import in _agentImports.Values.ToList())
                DiscardAgentImport(import);
            return Task.FromResult(true);
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private void SweepAgentPackageStaging()
    {
        var cutoff = DateTimeOffset.UtcNow - AgentPackageLifetime;
        foreach (var import in _agentImports.Values.Where(import => import.CreatedAt < cutoff).ToList())
            DiscardAgentImport(import);
        foreach (var entry in new DirectoryInfo(AgentPackageStagingPath()).EnumerateFileSystemInfos())
        {
            if (entry.LastWriteTimeUtc < cutoff.UtcDateTime)
                DeleteAgentPackagePath(entry.FullName);
        }
    }

    private string AgentPackageStagingPath() =>
        Directory.CreateDirectory(Path.Combine(workspaceTempPath!, "agent-packages")).FullName;

    private void RequireAgentPackageSupport()
    {
        if (string.IsNullOrEmpty(workspaceCraftPath) || string.IsNullOrEmpty(workspaceTempPath))
            throw AppServerErrors.MethodNotFound("agent/profiles/*");
    }

    private static void DeleteAgentPackagePath(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
