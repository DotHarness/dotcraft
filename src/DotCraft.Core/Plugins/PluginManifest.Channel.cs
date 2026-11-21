using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Security;

namespace DotCraft.Plugins;

public static partial class PluginManifestParser
{
    private const int MaxChannelManifestBytes = 256 * 1024;
    private static readonly HashSet<string> ReservedChannelConfigNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "agent.json", "agents.json", "approvals.json", "config.json", "dotnet-extension-trust.json",
        "hooks.json", "mcp-auth.json", "models.json", "plugin-config.json", "plugin.json", "shell-rules.json",
    };

    private static PluginChannelManifest? ParseChannel(
        string pluginRoot, JsonElement channel, string? pluginId, string pluginManifestPath, List<PluginDiagnostic> diagnostics)
    {
        if (channel.ValueKind == JsonValueKind.Undefined)
            return null;

        try
        {
            var descriptorPath = ResolveChannelFile(pluginRoot, ChannelManifestJson.String(channel, "manifest"), ".json");
            var entryPath = ResolveChannelFile(pluginRoot, ChannelManifestJson.String(channel, "entry"), ".mjs", ".js");
            var minHostVersion = ChannelManifestJson.String(channel, "minHostVersion");
            if (!DotNetExtensionManifestAdmission.IsCanonicalVersion(minHostVersion)
                || !Version.TryParse(minHostVersion, out _))
                throw new FormatException("channel.minHostVersion must be canonical MAJOR.MINOR.PATCH.");

            using var stream = File.OpenRead(descriptorPath);
            if (stream.Length > MaxChannelManifestBytes)
                throw new FormatException("Channel descriptor is too large.");
            using var document = JsonDocument.Parse(stream);
            return ParseChannelDescriptor(document.RootElement, descriptorPath, entryPath, minHostVersion);
        }
        catch (FormatException ex)
        {
            diagnostics.Add(PluginDiagnostic.Error("InvalidPluginChannel", ex.Message, pluginId, path: pluginManifestPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginChannel", "Channel descriptor and entry must be readable valid files inside the plugin root.",
                pluginId, path: pluginManifestPath));
        }
        return null;
    }

    private static string ResolveChannelFile(string pluginRoot, string value, params string[] extensions)
    {
        if (!value.StartsWith("./", StringComparison.Ordinal))
            throw new FormatException("Channel paths must start with './' and name files inside the plugin root.");

        var parts = value[2..].Split('/');
        if (parts.Any(segment => segment is "" or "." or ".."
            || segment.EndsWith('.') || segment.EndsWith(' ')
            || segment.IndexOfAny(['<', '>', ':', '"', '\\', '|', '?', '*']) >= 0
            || segment.Any(char.IsControl))
            || !extensions.Any(extension => value.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            throw new FormatException("Channel paths must name valid descriptor and launcher files inside the plugin root.");

        var path = WorkspaceBoundary.ResolveSymbolicLink(pluginRoot);
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new FormatException("Channel descriptor and launcher paths must not contain symbolic links or reparse points.");
        }
        if (!File.Exists(path))
            throw new FormatException("Channel descriptor and launcher must be files.");
        return path;
    }

    private static PluginChannelManifest ParseChannelDescriptor(
        JsonElement descriptor, string descriptorPath, string entryPath, string minHostVersion)
    {
        var moduleId = ChannelManifestJson.String(descriptor, "moduleId");
        var channelName = ChannelManifestJson.String(descriptor, "channelName");
        if (!ChannelIdRegex().IsMatch(moduleId) || !ChannelIdRegex().IsMatch(channelName))
            throw new FormatException("Channel name and module id must use lowercase ASCII letters, numbers, and hyphens.");

        var configFileName = ChannelManifestJson.String(descriptor, "configFileName");
        if (!ChannelConfigFileNameRegex().IsMatch(configFileName) || ReservedChannelConfigNames.Contains(configFileName))
            throw new FormatException("Channel configuration requires a non-reserved JSON file name, not a path.");

        var variant = ChannelManifestJson.String(descriptor, "variant");
        if (variant is not ("standard" or "specialized" or "enterprise" or "other"))
            throw new FormatException("Channel variant is not recognized.");

        var transports = ChannelManifestJson.Strings(descriptor, "supportedTransports");
        var protocolVersions = ChannelManifestJson.Strings(descriptor, "supportedChannelProtocolVersions");
        if (transports.Count == 0 || protocolVersions.Count == 0)
            throw new FormatException("Channel transport and adapter protocol lists must not be empty.");

        var requiresInteractiveSetup = ChannelManifestJson.Boolean(descriptor, "requiresInteractiveSetup");
        var capabilities = ChannelManifestJson.Object(descriptor, "capabilitySummary");
        if (ChannelManifestJson.Boolean(capabilities, "requiresInteractiveSetup") != requiresInteractiveSetup)
            throw new FormatException("Channel interactive setup metadata must agree with its capability summary.");

        var launcher = ChannelManifestJson.Object(descriptor, "launcher");
        if (!ChannelManifestJson.Boolean(launcher, "supportsWorkspaceFlag"))
            throw new FormatException("Channel launcher must support the workspace flag.");

        var groups = ChannelManifestJson.ReadGroups(descriptor);
        return new PluginChannelManifest
        {
            ManifestPath = descriptorPath,
            EntryPath = entryPath,
            MinHostVersion = minHostVersion,
            ModuleId = moduleId,
            ChannelName = channelName,
            DisplayName = ChannelManifestJson.String(descriptor, "displayName"),
            LocalizedDisplayName = ChannelManifestJson.StringMap(descriptor, "localizedDisplayName"),
            Interface = ChannelManifestJson.ReadInterface(descriptor),
            PackageName = ChannelManifestJson.String(descriptor, "packageName"),
            ConfigFileName = configFileName,
            Variant = variant,
            RequiresInteractiveSetup = requiresInteractiveSetup,
            HasChannelTools = ChannelManifestJson.Boolean(capabilities, "hasChannelTools"),
            CapabilitySetMayVaryByEnvironment = ChannelManifestJson.Boolean(capabilities, "capabilitySetMayVaryByEnvironment"),
            ChannelContractVersion = ChannelManifestJson.String(descriptor, "channelContractVersion"),
            SupportedTransports = transports,
            SupportedChannelProtocolVersions = protocolVersions,
            ConfigDescriptors = ChannelManifestJson.ReadDescriptors(descriptor, groups.Keys),
            ConfigGroups = groups.Values.ToArray(),
        };
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,127}\z")]
    private static partial Regex ChannelIdRegex();

    [GeneratedRegex(@"^(?!(?:aux|con|nul|prn|com[1-9]|lpt[1-9])\.json\z)[a-z0-9][a-z0-9_-]{0,127}\.json\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChannelConfigFileNameRegex();
}
