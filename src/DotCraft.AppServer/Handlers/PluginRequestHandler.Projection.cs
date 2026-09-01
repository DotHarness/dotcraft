using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Plugins;
using DotCraft.Skills;
using Contract = DotCraft.Protocol.AppServer;
using PluginDiagnostic = DotCraft.Plugins.PluginDiagnostic;

namespace DotCraft.AppServer;

/// <summary>Projects discovered plugins and the .NET runtime snapshot onto the plugin management wire types.</summary>
internal sealed partial class PluginRequestHandler
{
    private Contract.PluginInfo MapPluginToWire(
        DiscoveredPlugin plugin,
        IReadOnlyList<PluginDiagnostic> diagnostics,
        IReadOnlyDictionary<string, IReadOnlyList<PluginHookDeclaration>> hookSummaries,
        IReadOnlyDictionary<string, IReadOnlyList<PluginMcpServerSummary>> mcpSummaries,
        IReadOnlyDictionary<string, IReadOnlyList<PluginLspServerSummary>> lspSummaries)
    {
        var manifest = plugin.Manifest;
        var runtime = dotnetRuntime?.Snapshot.Plugins.FirstOrDefault(candidate =>
            PluginIds.EqualsCanonical(candidate.PluginId, manifest.Id));
        var appDiagnostics = new List<PluginDiagnostic>();
        var apps = MapPluginAppsToWire(plugin, appDiagnostics);
        var pluginDiagnostics = diagnostics
            .Concat(appDiagnostics)
            .Where(d => string.Equals(d.PluginId, manifest.Id, StringComparison.OrdinalIgnoreCase))
            .Select(MapPluginDiagnosticToWire)
            .ToList();
        return new Contract.PluginInfo
        {
            Id = manifest.Id,
            DisplayName = manifest.Interface?.DisplayName ?? manifest.DisplayName,
            Description = OmitIfNull(manifest.Interface?.ShortDescription ?? manifest.Description),
            Version = OmitIfNull(manifest.Version),
            Enabled = plugin.Enabled,
            Installed = plugin.Installed,
            Installable = plugin.Installable,
            Removable = plugin.Removable,
            Source = plugin.SourceKind.ToString().ToLowerInvariant(),
            RootPath = manifest.RootPath,
            MarketplaceName = OmitIfNull(plugin.MarketplaceName),
            Dotnet = manifest.Dotnet is { } dotnet
                ? Protocol.Optional<Contract.PluginDotnetInfo?>.FromValue(new Contract.PluginDotnetInfo
                {
                    EntryAssembly = dotnet.EntryAssembly,
                    EntryType = dotnet.EntryType,
                    ExportedApiAssemblies = dotnet.ExportedApiAssemblies.ToArray(),
                    MinHostVersion = dotnet.MinHostVersion
                })
                : default,
            DotnetRuntime = runtime == null
                ? default
                : Protocol.Optional<Contract.PluginDotnetRuntimeInfo?>.FromValue(MapRuntimeToWire(runtime)),
            Dependencies = manifest.Dotnet != null
                ? (runtime?.DependencyObservations ?? plugin.DependencyObservations)
                    .Select(MapPluginDependencyToWire)
                    .ToArray()
                : default,
            Interface = MapPluginInterfaceToWire(manifest.Interface) is { } pluginInterface
                ? Protocol.Optional<Contract.PluginInterface?>.FromValue(pluginInterface)
                : default,
            Functions = runtime?.Tools?.Select(static tool => new Contract.PluginFunctionInfo
            {
                Namespace = OmitIfNull(tool.Namespace),
                Name = tool.Name,
                Description = tool.Description
            }).ToArray() ?? [],
            Skills = MapPluginSkillsToWire(plugin),
            Apps = apps,
            Desktop = manifest.Desktop is { } desktop
                ? Protocol.Optional<Contract.PluginDesktopInfo?>.FromValue(new Contract.PluginDesktopInfo
                {
                    Description = desktop.Description is { } description
                        ? Protocol.Optional<string>.FromValue(description)
                        : default,
                    Entry = desktop.Entry,
                    Styles = desktop.Styles.ToArray(),
                    Revision = desktop.Revision
                })
                : default,
            Hooks = hookSummaries.TryGetValue(manifest.Id, out var hooks)
                ? hooks.Select(MapPluginHookToWire).ToList()
                : Array.Empty<Contract.PluginHookInfo>(),
            McpServers = mcpSummaries.TryGetValue(manifest.Id, out var servers)
                ? servers.Select(MapPluginMcpServerToWire).ToList()
                : Array.Empty<Contract.PluginMcpServerInfo>(),
            LspServers = lspSummaries.TryGetValue(manifest.Id, out var lspServers)
                ? lspServers.Select(MapPluginLspServerToWire).ToList()
                : Array.Empty<Contract.PluginLspServerInfo>(),
            Workflows = workflowSummaryProvider?.ListForPlugin(manifest.Id)
                .Select(static workflow => new Contract.PluginWorkflowInfo
                {
                    Name = workflow.Name,
                    Command = workflow.Command,
                    Description = workflow.Description,
                    WhenToUse = OmitIfNull(workflow.WhenToUse)
                }).ToArray() ?? [],
            Diagnostics = pluginDiagnostics
        };
    }

    private Contract.PluginOperationResult BuildOperationResult(
        PluginDiscoveryResult discovery,
        string selectedPluginId,
        string outcome,
        IEnumerable<string> affectedPluginIds,
        IReadOnlyList<PluginDiagnostic> operationDiagnostics)
    {
        var diagnostics = discovery.Diagnostics.ToList();
        var hookSummaries = BuildPluginHookSummaryIndex(discovery, diagnostics);
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var selected = discovery.Plugins.FirstOrDefault(plugin =>
            PluginIds.EqualsCanonical(plugin.Manifest.Id, selectedPluginId));
        var runtimeSnapshot = dotnetRuntime?.Snapshot;
        var affected = affectedPluginIds
            .Where(id => !PluginIds.EqualsCanonical(id, selectedPluginId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .Select(id =>
            {
                var runtime = runtimeSnapshot?.Plugins.FirstOrDefault(candidate =>
                    PluginIds.EqualsCanonical(candidate.PluginId, id));
                if (runtime == null)
                    return null;
                var discovered = discovery.Plugins.FirstOrDefault(candidate =>
                    PluginIds.EqualsCanonical(candidate.Manifest.Id, id));
                return new Contract.PluginRuntimeProjection
                {
                    Id = id,
                    Installed = discovered?.Installed == true,
                    Enabled = discovered?.Enabled == true,
                    DotnetRuntime = MapRuntimeToWire(runtime)
                };
            })
            .OfType<Contract.PluginRuntimeProjection>()
            .ToArray();
        return new Contract.PluginOperationResult
        {
            Outcome = outcome,
            Plugin = Protocol.Optional<Contract.PluginInfo?>.FromValue(selected == null
                ? null
                : MapPluginToWire(selected, diagnostics, hookSummaries, mcpSummaries, lspSummaries)),
            AffectedPlugins = affected,
            Diagnostics = operationDiagnostics.Select(MapPluginDiagnosticToWire).ToArray(),
            SnapshotRevision = CurrentPluginSnapshotRevision
        };
    }

    private long CurrentPluginSnapshotRevision =>
        managementState.SnapshotClock.Observe(dotnetRuntime?.Snapshot.Revision ?? 0);

    private long AdvancePluginSnapshotRevision() =>
        managementState.SnapshotClock.Advance(dotnetRuntime?.Snapshot.Revision ?? 0);

    private static Contract.PluginDotnetRuntimeInfo MapRuntimeToWire(PluginDotnetRuntimeInfo runtime) =>
        new()
        {
            State = CamelCase(runtime.State.ToString()),
            GenerationId = OmitIfNull(runtime.GenerationId),
            Blockers = runtime.Blockers.Select(static blocker => new Contract.PluginRuntimeBlocker
            {
                Code = blocker.Code,
                Message = blocker.Message,
                Parameters = Protocol.Optional<IReadOnlyDictionary<string, System.Text.Json.JsonElement>>
                    .FromValue(blocker.Parameters)
            }).ToArray(),
            LeakedGenerations = runtime.LeakedGenerations,
            RestartRecommended = runtime.RestartRecommended,
            TrustStatus = CamelCase(runtime.TrustStatus.ToString())
        };

    private static string CamelCase(string value) =>
        char.ToLowerInvariant(value[0]) + value[1..];

    private static List<Contract.PluginAppInfo> MapPluginAppsToWire(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics) =>
        AppBindingCatalog.LoadPluginAppDescriptors(plugin, diagnostics)
            .Select(app => new Contract.PluginAppInfo
            {
                AppId = app.AppId,
                DisplayName = app.DisplayName,
                DeveloperName = app.DeveloperName,
                Description = app.Description,
                Category = OmitIfNull(app.Category),
                Icon = OmitIfNull(TryReadDataUrl(app.Icon) ?? app.Icon),
                ReleasePage = OmitIfNull(app.ReleasePage),
                NativeApplication = app.NativeApplication == null
                    ? default
                    : Protocol.Optional<Contract.PluginAppNativeApplication?>.FromValue(
                      new Contract.PluginAppNativeApplication
                    {
                        DisplayName = app.NativeApplication.DisplayName,
                        Protocol = app.NativeApplication.Protocol,
                        InstallUrl = OmitIfNull(app.NativeApplication.InstallUrl)
                    })
            })
            .ToList();

    private static Contract.PluginMcpServerInfo MapPluginMcpServerToWire(PluginMcpServerSummary server) =>
        new()
        {
            Name = server.Name,
            RuntimeName = server.RuntimeName,
            Transport = server.Transport,
            Enabled = server.Enabled,
            Active = server.Active,
            ShadowedBy = OmitIfNull(server.ShadowedBy)
        };

    private static Contract.PluginHookInfo MapPluginHookToWire(PluginHookDeclaration hook) =>
        new()
        {
            Key = hook.Key,
            EventName = hook.EventName
        };

    private static Contract.PluginLspServerInfo MapPluginLspServerToWire(PluginLspServerSummary server) =>
        new()
        {
            Name = server.Name,
            RuntimeName = server.RuntimeName,
            Transport = server.Transport,
            Enabled = server.Enabled,
            Active = server.Active,
            Extensions = server.Extensions.ToArray(),
            ShadowedBy = OmitIfNull(server.ShadowedBy)
        };

    private Contract.PluginInterface? MapPluginInterfaceToWire(PluginInterfaceMetadata? metadata)
    {
        if (metadata == null)
            return null;

        return new Contract.PluginInterface
        {
            DisplayName = OmitIfNull(metadata.DisplayName),
            ShortDescription = OmitIfNull(metadata.ShortDescription),
            LongDescription = OmitIfNull(metadata.LongDescription),
            DeveloperName = OmitIfNull(metadata.DeveloperName),
            Category = OmitIfNull(metadata.Category),
            Capabilities = metadata.Capabilities.ToList(),
            DefaultPrompt = OmitIfNull(metadata.DefaultPrompt),
            ComposerIconDataUrl = OmitIfNull(TryReadDataUrl(metadata.ComposerIcon)),
            LogoDataUrl = OmitIfNull(TryReadDataUrl(metadata.Logo)),
            WebsiteUrl = OmitIfNull(metadata.WebsiteUrl),
            PrivacyPolicyUrl = OmitIfNull(metadata.PrivacyPolicyUrl),
            TermsOfServiceUrl = OmitIfNull(metadata.TermsOfServiceUrl)
        };
    }

    private List<Contract.PluginSkillInfo> MapPluginSkillsToWire(DiscoveredPlugin plugin)
    {
        var manifest = plugin.Manifest;
        if (string.IsNullOrWhiteSpace(manifest.SkillsPath) || !Directory.Exists(manifest.SkillsPath))
            return [];

        var allSkills = skillsLoader?.ListSkills(filterUnavailable: false) ?? new List<SkillsLoader.SkillInfo>();
        return Directory.GetDirectories(manifest.SkillsPath)
            .Where(dir => File.Exists(Path.Combine(dir, "SKILL.md")))
            .Select(dir =>
            {
                var name = Path.GetFileName(dir);
                var skillFile = Path.Combine(dir, "SKILL.md");
                var skill = allSkills.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                var interfaceInfo = SkillsLoader.GetPluginSkillInterfaceFromFile(skillFile, manifest.RootPath)
                                    ?? skillsLoader?.GetSkillInterface(name);
                return new Contract.PluginSkillInfo
                {
                    Name = name,
                    Description = SkillsLoader.GetSkillDescriptionFromFile(skillFile)
                                  ?? skillsLoader?.GetSkillDescription(name)
                                  ?? name,
                    DisplayName = OmitIfNull(interfaceInfo?.DisplayName),
                    ShortDescription = OmitIfNull(interfaceInfo?.ShortDescription),
                    Enabled = plugin.Installed
                              && plugin.Enabled
                              && (skill?.Enabled
                              ?? (!string.Equals(manifest.Id, PluginIds.Browser, StringComparison.OrdinalIgnoreCase)
                                  || (appConfigMonitor?.Current ?? new AppConfig()).Plugins.IsPluginEnabled(PluginIds.Browser, true)))
                };
            })
            .ToList();
    }

    private static Contract.PluginDiagnostic MapPluginDiagnosticToWire(PluginDiagnostic diagnostic) =>
        new()
        {
            Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
            Code = diagnostic.Code,
            Message = diagnostic.Message,
            PluginId = OmitIfNull(diagnostic.PluginId),
            Path = OmitIfNull(diagnostic.Path),
            Parameters = diagnostic.Parameters.ToDictionary()
        };

    private static Contract.PluginDependencyInfo MapPluginDependencyToWire(
        PluginDependencyObservation dependency) =>
        new()
        {
            Id = dependency.Id,
            RequiredVersion = dependency.RequiredVersion,
            ObservedVersion = OmitIfNull(dependency.ObservedVersion),
            Availability = CamelCase(dependency.Availability.ToString())
        };

    private static string? TryReadDataUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var mimeType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => null
        };
        if (mimeType == null)
            return null;

        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > 512 * 1024)
            return null;

        return $"data:{mimeType};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }
}
