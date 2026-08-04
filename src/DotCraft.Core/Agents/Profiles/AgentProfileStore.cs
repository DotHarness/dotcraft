using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotCraft.Configuration;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using YamlDotNet.Serialization;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using ModelPreferenceContextWindow = DotCraft.Configuration.ModelPreferenceContextWindow;

namespace DotCraft.Agents;

public static class AgentProfileSources
{
    public const string BuiltIn = "builtIn";
    public const string Plugin = "plugin";
    public const string User = "user";
    public const string Workspace = "workspace";
    public const string Managed = "managed";

    public static readonly IReadOnlyList<string> PriorityOrder =
    [
        BuiltIn,
        Plugin,
        User,
        Workspace,
        Managed
    ];
}

public sealed record AgentProfileDiagnostic(
    string Severity,
    string Code,
    string Message);

/// <summary>Reasoning preset authored by an Agent Profile.</summary>
public sealed class AgentProfileReasoningPreference
{
    /// <summary>Whether reasoning is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Requested reasoning effort.</summary>
    public ReasoningEffort Effort { get; init; } = ReasoningEffort.Medium;
}

/// <summary>A provider-scoped model preset fixed by an Agent Profile.</summary>
public sealed class AgentProfileProviderPreference
{
    /// <summary>Provider selected by the profile.</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>Model selected by the profile.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Reasoning selection authored by the profile.</summary>
    public AgentProfileReasoningPreference Reasoning { get; init; } = new();

    /// <summary>Requested inference speed.</summary>
    public InferenceSpeed Speed { get; init; } = InferenceSpeed.Standard;

    /// <summary>Requested context-window mode.</summary>
    public ModelPreferenceContextWindow ContextWindow { get; init; } = new();
}

public sealed class AgentProfileEntry
{
    public string Id { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Optional packed profile avatar used by clients for visual identity.</summary>
    public int? Avatar { get; init; }

    public string Source { get; init; } = AgentProfileSources.BuiltIn;

    public string? Path { get; init; }

    /// <summary>Last write time of the profile file (UTC), when it exists on disk. Null for in-memory/built-in.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    public string? PluginId { get; init; }

    public string Fingerprint { get; init; } = string.Empty;

    public bool Valid { get; init; }

    public bool IsBuiltIn => string.Equals(Source, AgentProfileSources.BuiltIn, StringComparison.Ordinal);

    public bool ReadOnly { get; init; }

    public bool Shadowed { get; init; }

    public string? ShadowedBy { get; init; }

    public List<string> SourceStack { get; init; } = [];

    public List<string> LockedFields { get; init; } = [];

    public List<string> RestrictedFields { get; init; } = [];

    public bool TrustRestricted => RestrictedFields.Count > 0;

    public string? RawContent { get; init; }

    public List<AgentProfileDiagnostic> Diagnostics { get; init; } = [];

    public ThreadConfiguration? CompiledConfiguration { get; init; }

    public AgentProfileProviderPreference? ProviderPreference { get; init; }
}

public sealed class AgentProfileValidationResult
{
    public string? Id { get; init; }

    public string? Description { get; init; }

    /// <summary>Optional packed profile avatar parsed from frontmatter.</summary>
    public int? Avatar { get; init; }

    public string Body { get; init; } = string.Empty;

    public string Fingerprint { get; init; } = string.Empty;

    public bool Valid => !Diagnostics.Any(d => string.Equals(d.Severity, "error", StringComparison.OrdinalIgnoreCase));

    public List<AgentProfileDiagnostic> Diagnostics { get; init; } = [];

    public List<string> LockedFields { get; init; } = [];

    public List<string> RestrictedFields { get; init; } = [];

    public ThreadConfiguration? CompiledConfiguration { get; init; }

    public AgentProfileProviderPreference? ProviderPreference { get; init; }
}

/// <summary>Packs and validates Agent Profile avatar indices into a single integer frontmatter field.</summary>
public static class AgentProfileAvatarCodec
{
    /// <summary>The number of palette variants currently supported by the desktop renderer.</summary>
    public const int PaletteCount = 12;

    /// <summary>The number of face variants currently supported by the desktop renderer.</summary>
    public const int FaceCount = 5;

    /// <summary>The number of accessory variants currently supported by the desktop renderer.</summary>
    public const int AccessoryCount = 6;

    private const int PaletteMask = 0x0f;
    private const int FaceMask = 0x07;
    private const int AccessoryMask = 0x07;
    private const int FaceShift = 4;
    private const int AccessoryShift = 7;
    private const int AvatarMask = PaletteMask | (FaceMask << FaceShift) | (AccessoryMask << AccessoryShift);

    /// <summary>Packs palette, face, and accessory indices into one non-negative integer.</summary>
    public static int Encode(int palette, int face, int accessory) =>
        (palette & PaletteMask)
        | ((face & FaceMask) << FaceShift)
        | ((accessory & AccessoryMask) << AccessoryShift);

    /// <summary>Attempts to unpack a persisted avatar value into palette, face, and accessory indices.</summary>
    public static bool TryDecode(int value, out int palette, out int face, out int accessory)
    {
        palette = value & PaletteMask;
        face = (value >> FaceShift) & FaceMask;
        accessory = (value >> AccessoryShift) & AccessoryMask;

        return value >= 0
            && (value & ~AvatarMask) == 0
            && palette < PaletteCount
            && face < FaceCount
            && accessory < AccessoryCount;
    }
}

public sealed class AgentProfileAuditRecord
{
    public string Event { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string? ProfileId { get; init; }

    public string? Source { get; init; }

    public string? ThreadId { get; init; }

    public string Status { get; init; } = "success";

    public Dictionary<string, string> Fields { get; init; } = [];
}

internal enum AgentProfileErrorKind
{
    NotFound,
    ValidationFailed,
    Protected,
    SourceUnavailable,
    Conflict
}

internal sealed class AgentProfileException(
    AgentProfileErrorKind kind,
    string message,
    IReadOnlyList<AgentProfileDiagnostic>? diagnostics = null) : Exception(message)
{
    public AgentProfileErrorKind Kind { get; } = kind;

    public IReadOnlyList<AgentProfileDiagnostic> Diagnostics { get; } = diagnostics ?? [];
}

public sealed partial class AgentProfileStore
{
    private const int MaxProfileIdLength = 80;
    private static readonly Regex ProfileIdRegex = BuildProfileIdRegex();
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();

    private static readonly HashSet<string> TopLevelFields = new(StringComparer.Ordinal)
    {
        "name",
        "description",
        "avatar",
        "providerPreference",
        "mode",
        "promptProfile",
        "tools",
        "mcp",
        "plugins",
        "skills",
        "permissions",
        "teams",
        "locked"
    };

    private static readonly HashSet<string> ReasoningFields = new(StringComparer.Ordinal)
    {
        "enabled",
        "effort"
    };

    private static readonly HashSet<string> ProviderPreferenceFields = new(StringComparer.Ordinal)
    {
        "providerId",
        "model",
        "reasoning",
        "speed",
        "contextWindow"
    };

    private static readonly HashSet<string> ContextWindowFields = new(StringComparer.Ordinal)
    {
        "mode"
    };

    private static readonly HashSet<string> ToolsFields = new(StringComparer.Ordinal)
    {
        "allow",
        "deny",
        "agentControl",
        "allowedAgentControlTools"
    };

    private static readonly HashSet<string> McpFields = new(StringComparer.Ordinal)
    {
        "servers",
        "tools"
    };

    private static readonly HashSet<string> NamePolicyFields = new(StringComparer.Ordinal)
    {
        "allow",
        "deny"
    };

    private static readonly HashSet<string> PluginFields = new(StringComparer.Ordinal)
    {
        "allow",
        "deny"
    };

    private static readonly HashSet<string> SkillsFields = new(StringComparer.Ordinal)
    {
        "preload",
        "allow",
        "deny",
        "allowManage"
    };

    private static readonly HashSet<string> PermissionFields = new(StringComparer.Ordinal)
    {
        "approvalPolicy",
        "requireApprovalOutsideWorkspace"
    };

    private static readonly HashSet<string> TeamsFields = new(StringComparer.Ordinal)
    {
        "reservedTools"
    };

    private static readonly HashSet<string> LockedFields = new(StringComparer.Ordinal)
    {
        "tools",
        "mcp",
        "permissions",
        "teams",
        "overrideBasePrompt"
    };

    private static readonly HashSet<string> LockedToolsFields = new(StringComparer.Ordinal)
    {
        "deny"
    };

    private static readonly HashSet<string> LockedMcpFields = new(StringComparer.Ordinal)
    {
        "servers"
    };

    private static readonly HashSet<string> LockedPermissionsFields = new(StringComparer.Ordinal)
    {
        "deniedApprovalPolicies"
    };

    private static readonly HashSet<string> LockedTeamsFields = new(StringComparer.Ordinal)
    {
        "reservedTools"
    };

    private static readonly HashSet<string> HighRiskToolNames = new(StringComparer.Ordinal)
    {
        "WriteFile",
        "EditFile",
        "Exec",
        "WriteStdin"
    };

    private readonly string? _workspaceCraftPath;
    private readonly string _userDotCraftPath;

    public AgentProfileStore(string? workspaceCraftPath = null, string? userDotCraftPath = null)
    {
        _workspaceCraftPath = string.IsNullOrWhiteSpace(workspaceCraftPath) ? null : Path.GetFullPath(workspaceCraftPath);
        _userDotCraftPath = string.IsNullOrWhiteSpace(userDotCraftPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft")
            : Path.GetFullPath(userDotCraftPath);
    }

    public IReadOnlyList<AgentProfileEntry> List(string? source = null, bool includeInvalid = true)
    {
        var sourceFilter = NormalizeSourceOrNull(source);
        var entries = new List<AgentProfileEntry>();
        foreach (var sourceName in AgentProfileSources.PriorityOrder)
        {
            if (sourceFilter != null && !string.Equals(sourceFilter, sourceName, StringComparison.Ordinal))
                continue;

            entries.AddRange(ReadSource(sourceName));
        }

        var sourceStacks = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(entry => SourcePriority(entry.Source))
                    .Select(entry => entry.Source)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var shadowedIds = entries
            .Where(entry => entry.Valid)
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var ordered = group.OrderByDescending(entry => SourcePriority(entry.Source)).ToArray();
                var winner = ordered.FirstOrDefault();
                return winner == null
                    ? []
                    : ordered.Skip(1).Select(entry => (Entry: entry, Winner: winner));
            })
            .ToDictionary(pair => EntryKey(pair.Entry), pair => pair.Winner, StringComparer.Ordinal);

        entries = entries
            .Select(entry => shadowedIds.TryGetValue(EntryKey(entry), out var winner)
                ? CloneWithShadow(entry, winner.Source)
                : entry)
            .Select(entry => sourceStacks.TryGetValue(entry.Id, out var stack)
                ? CloneWithSourceStack(entry, stack)
                : entry)
            .Where(entry => includeInvalid || entry.Valid)
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => SourcePriority(entry.Source))
            .ToList();

        return entries;
    }

    public AgentProfileEntry Read(string id, string? source = null)
    {
        var normalizedId = NormalizeProfileId(id);
        var sourceFilter = NormalizeSourceOrNull(source);
        var entries = List(sourceFilter, includeInvalid: true)
            .Where(entry => string.Equals(entry.Id, normalizedId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (entries.Count == 0)
            throw new AgentProfileException(AgentProfileErrorKind.NotFound, $"Agent profile not found: {normalizedId}");

        if (sourceFilter != null)
        {
            return entries
                .Where(entry => string.Equals(entry.Source, sourceFilter, StringComparison.Ordinal))
                .OrderByDescending(entry => entry.Valid)
                .FirstOrDefault()
                ?? throw new AgentProfileException(AgentProfileErrorKind.NotFound, $"Agent profile not found: {normalizedId}");
        }

        return entries
            .OrderByDescending(entry => entry.Valid)
            .ThenByDescending(entry => SourcePriority(entry.Source))
            .First();
    }

    public AgentProfileValidationResult ValidateRaw(
        string rawContent,
        string? source = null,
        string? expectedId = null)
    {
        if (rawContent == null)
            throw new ArgumentNullException(nameof(rawContent));

        var sourceName = NormalizeSourceOrNull(source) ?? AgentProfileSources.Workspace;
        var diagnostics = new List<AgentProfileDiagnostic>();
        var fingerprint = ComputeFingerprint(rawContent);
        var extracted = ExtractFrontmatter(rawContent, diagnostics);
        if (extracted == null)
        {
            return new AgentProfileValidationResult
            {
                Fingerprint = fingerprint,
                Diagnostics = diagnostics
            };
        }

        JsonObject? frontmatter = null;
        try
        {
            var yaml = YamlDeserializer.Deserialize(new StringReader(extracted.Value.Frontmatter));
            frontmatter = ConvertYamlToJson(yaml) as JsonObject;
            if (frontmatter == null)
                diagnostics.Add(Error("InvalidYaml", "Agent profile frontmatter must be a YAML object."));
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("InvalidYaml", $"Agent profile frontmatter is not valid YAML: {ex.Message}"));
        }

        if (frontmatter == null)
        {
            return new AgentProfileValidationResult
            {
                Fingerprint = fingerprint,
                Body = extracted.Value.Body,
                Diagnostics = diagnostics
            };
        }

        ValidateAllowedFields(frontmatter, TopLevelFields, string.Empty, diagnostics);

        var id = ReadOptionalString(frontmatter, "name", diagnostics, required: true);
        var description = ReadOptionalString(frontmatter, "description", diagnostics, required: true);
        var avatar = ReadOptionalAvatar(frontmatter, diagnostics);
        if (!string.IsNullOrWhiteSpace(id))
        {
            id = id.Trim();
            if (!IsValidProfileId(id))
                diagnostics.Add(Error("InvalidProfileId", $"Agent profile id '{id}' is invalid."));
        }

        if (!string.IsNullOrWhiteSpace(expectedId)
            && !string.IsNullOrWhiteSpace(id)
            && !string.Equals(expectedId.Trim(), id.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error(
                "ProfileIdMismatch",
                $"Agent profile frontmatter name '{id}' must match requested id '{expectedId}'."));
        }

        var locked = new List<string>();
        var restricted = new List<string>();
        var config = Compile(
            frontmatter,
            id ?? expectedId ?? string.Empty,
            sourceName,
            fingerprint,
            extracted.Value.Body,
            diagnostics,
            locked,
            restricted,
            out var providerPreference);

        return new AgentProfileValidationResult
        {
            Id = id,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Avatar = avatar,
            Body = extracted.Value.Body,
            Fingerprint = fingerprint,
            Diagnostics = diagnostics,
            LockedFields = locked,
            RestrictedFields = restricted,
            CompiledConfiguration = config,
            ProviderPreference = providerPreference
        };
    }

    public AgentProfileEntry Upsert(string id, string source, string rawContent)
    {
        var normalizedId = NormalizeProfileId(id);
        var sourceName = NormalizeSource(source);
        if (IsReadOnlySource(sourceName))
            throw new AgentProfileException(AgentProfileErrorKind.Protected, $"Agent profile source '{sourceName}' is read-only.");

        var path = GetWritableProfilePath(sourceName, normalizedId);
        var validation = ValidateRaw(rawContent, sourceName, normalizedId);
        if (!validation.Valid)
            throw new AgentProfileException(AgentProfileErrorKind.ValidationFailed, "Agent profile validation failed.", validation.Diagnostics);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, rawContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AppendAudit(new AgentProfileAuditRecord
        {
            Event = "agentProfile.upsert",
            Code = "AgentProfileUpserted",
            ProfileId = normalizedId,
            Source = sourceName
        });

        return BuildEntryFromContent(normalizedId, sourceName, path, rawContent);
    }

    public bool Remove(string id, string source)
    {
        var normalizedId = NormalizeProfileId(id);
        var sourceName = NormalizeSource(source);
        if (IsReadOnlySource(sourceName))
            throw new AgentProfileException(AgentProfileErrorKind.Protected, $"Agent profile source '{sourceName}' is read-only.");

        var path = GetWritableProfilePath(sourceName, normalizedId);
        if (!File.Exists(path))
            throw new AgentProfileException(AgentProfileErrorKind.NotFound, $"Agent profile not found: {normalizedId}");

        File.Delete(path);
        AppendAudit(new AgentProfileAuditRecord
        {
            Event = "agentProfile.remove",
            Code = "AgentProfileRemoved",
            ProfileId = normalizedId,
            Source = sourceName
        });
        return true;
    }

    public ThreadConfiguration ResolveProfileConfiguration(string id)
    {
        var profile = Read(id);
        if (!profile.Valid || profile.CompiledConfiguration == null)
            throw new AgentProfileException(AgentProfileErrorKind.ValidationFailed, "Agent profile validation failed.", profile.Diagnostics);

        return CloneThreadConfiguration(profile.CompiledConfiguration);
    }

    /// <summary>
    /// Resolves a profile into a runtime configuration and materializes its fixed model preset.
    /// </summary>
    public ThreadConfiguration ResolveProfileConfiguration(string id, AppConfig appConfig)
    {
        ArgumentNullException.ThrowIfNull(appConfig);
        var profile = Read(id);
        if (!profile.Valid || profile.CompiledConfiguration == null)
            throw new AgentProfileException(AgentProfileErrorKind.ValidationFailed, "Agent profile validation failed.", profile.Diagnostics);

        var resolved = CloneThreadConfiguration(profile.CompiledConfiguration);
        if (profile.ProviderPreference != null)
            ApplyProviderPreference(resolved, profile.ProviderPreference, appConfig);
        return resolved;
    }

    public void AppendAudit(AgentProfileAuditRecord record)
    {
        if (_workspaceCraftPath == null)
            return;

        var directory = Path.Combine(_workspaceCraftPath, "agents");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "audit.jsonl");
        var json = JsonSerializer.Serialize(record, SessionWireJsonOptions.Default);
        File.AppendAllText(path, $"{json}{Environment.NewLine}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public ThreadConfiguration ResolveThreadStartConfiguration(
        ThreadConfiguration requested,
        AppConfig appConfig,
        JsonElement? configElement = null)
    {
        if (string.IsNullOrWhiteSpace(requested.AgentProfileId))
            return requested;

        var profile = Read(requested.AgentProfileId);
        if (!profile.Valid || profile.CompiledConfiguration == null)
            throw new AgentProfileException(AgentProfileErrorKind.ValidationFailed, "Agent profile validation failed.", profile.Diagnostics);

        var unsupported = FindUnsupportedThreadStartOverlayFields(configElement).ToArray();
        if (unsupported.Length > 0)
        {
            throw new AgentProfileException(
                AgentProfileErrorKind.ValidationFailed,
                $"Unsupported agent profile overlay field(s): {string.Join(", ", unsupported)}.",
                unsupported.Select(field => Error(
                    "UnsupportedOverlayField",
                    $"Thread profile overlay field '{field}' is not supported in v1.")).ToArray());
        }

        var resolved = CloneThreadConfiguration(profile.CompiledConfiguration);
        if (profile.ProviderPreference != null)
            ApplyProviderPreference(resolved, profile.ProviderPreference, appConfig);
        var selectsRuntimeModel = HasConfigProperty(configElement, "providerId")
                                  || HasConfigProperty(configElement, "model");
        if (selectsRuntimeModel)
        {
            resolved.Reasoning = null;
            resolved.Speed = null;
            resolved.ContextWindow = null;
            if (HasConfigProperty(configElement, "providerId")
                && !HasConfigProperty(configElement, "model"))
            {
                resolved.Model = null;
            }
        }

        if (HasConfigProperty(configElement, "providerId"))
            resolved.ProviderId = NormalizeNullableString(requested.ProviderId);
        if (HasConfigProperty(configElement, "model"))
            resolved.Model = NormalizeNullableString(requested.Model);
        if (HasConfigProperty(configElement, "reasoning"))
            resolved.Reasoning = CloneReasoning(requested.Reasoning);
        if (HasConfigProperty(configElement, "speed"))
            resolved.Speed = requested.Speed;
        if (HasConfigProperty(configElement, "contextWindow"))
            resolved.ContextWindow = CloneContextWindow(requested.ContextWindow);
        if (HasConfigProperty(configElement, "approvalTimeoutSeconds"))
            resolved.ApprovalTimeoutSeconds = requested.ApprovalTimeoutSeconds;

        return resolved;
    }

    private static void ApplyProviderPreference(
        ThreadConfiguration config,
        AgentProfileProviderPreference profilePreference,
        AppConfig appConfig)
    {
        EffectiveModelRuntime runtime;
        try
        {
            runtime = ModelProviderResolver.ResolveMain(
                appConfig,
                profilePreference.ProviderId,
                profilePreference.Model);
        }
        catch (Exception ex) when (ex is ArgumentException or ModelProviderConfigurationException)
        {
            throw new AgentProfileException(
                AgentProfileErrorKind.ValidationFailed,
                $"Pinned provider '{profilePreference.ProviderId}' is not runnable in the current workspace.",
                [Error("PinnedProviderUnavailable", $"Pinned provider '{profilePreference.ProviderId}' is not runnable in the current workspace.")]);
        }

        var capability = ModelThinkingAdapterCatalog.ResolveReasoningCapability(
            appConfig,
            runtime.Protocol,
            runtime.EndPoint,
            runtime.Model);
        var preference = ModelPreferenceRules.Normalize(
            appConfig,
            runtime.ProviderId,
            new ModelPreference
            {
                Model = runtime.Model,
                Reasoning = new AppConfig.ReasoningConfig
                {
                    Enabled = profilePreference.Reasoning.Enabled,
                    Effort = profilePreference.Reasoning.Effort,
                    Output = capability?.DefaultOutput ?? ReasoningOutput.Full
                },
                Speed = profilePreference.Speed,
                ContextWindow = new ModelPreferenceContextWindow
                {
                    Mode = profilePreference.ContextWindow.Mode
                }
            });

        config.ProviderId = runtime.ProviderId;
        config.Model = preference.Model;
        config.Reasoning = CloneReasoning(preference.Reasoning);
        config.Speed = preference.Speed;
        config.ContextWindow = new ThreadContextWindowConfig
        {
            Mode = preference.ContextWindow.Mode
        };
    }

    private static IEnumerable<string> FindUnsupportedThreadStartOverlayFields(JsonElement? configElement)
    {
        if (!configElement.HasValue || configElement.Value.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var property in configElement.Value.EnumerateObject())
        {
            var name = property.Name;
            if (IsAllowedThreadStartOverlayField(name))
                continue;
            if (IsBenignSerializedDefaultOverlay(property))
                continue;

            yield return name;
        }
    }

    private static bool IsAllowedThreadStartOverlayField(string name) =>
        string.Equals(name, "agentProfileId", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "providerId", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "model", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "reasoning", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "speed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "contextWindow", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "approvalTimeoutSeconds", StringComparison.OrdinalIgnoreCase);

    private static bool IsBenignSerializedDefaultOverlay(JsonProperty property)
    {
        if (string.Equals(property.Name, "mode", StringComparison.OrdinalIgnoreCase))
            return property.Value.ValueKind == JsonValueKind.String
                   && string.Equals(property.Value.GetString(), "agent", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(property.Name, "useToolProfileOnly", StringComparison.OrdinalIgnoreCase)
            || string.Equals(property.Name, "overrideBasePrompt", StringComparison.OrdinalIgnoreCase))
        {
            return property.Value.ValueKind is JsonValueKind.False;
        }

        if (string.Equals(property.Name, "approvalPolicy", StringComparison.OrdinalIgnoreCase))
            return property.Value.ValueKind == JsonValueKind.String
                   && string.Equals(property.Value.GetString(), "default", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private IReadOnlyList<AgentProfileEntry> ReadSource(string source)
    {
        if (string.Equals(source, AgentProfileSources.BuiltIn, StringComparison.Ordinal))
            return BuiltInProfiles()
                .Select(profile => BuildEntryFromContent(profile.Id, source, $"builtin://agent-profiles/{profile.Id}.md", profile.RawContent))
                .ToList();

        if (string.Equals(source, AgentProfileSources.Plugin, StringComparison.Ordinal))
            return ReadPluginProfiles();

        var directory = GetSourceDirectory(source);
        if (directory == null || !Directory.Exists(directory))
            return [];

        var entries = new List<AgentProfileEntry>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
        {
            var fileId = Path.GetFileNameWithoutExtension(path);
            try
            {
                entries.Add(BuildEntryFromContent(fileId, source, path, File.ReadAllText(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                entries.Add(new AgentProfileEntry
                {
                    Id = fileId,
                    Source = source,
                    Path = path,
                    Valid = false,
                    Diagnostics =
                    [
                        Error("ProfileReadFailed", $"Agent profile file could not be read: {ex.Message}")
                    ]
                });
            }
        }

        return entries;
    }

    private IReadOnlyList<AgentProfileEntry> ReadPluginProfiles()
    {
        if (_workspaceCraftPath == null)
            return [];

        var pluginRoot = Path.Combine(_workspaceCraftPath, "plugins");
        if (!Directory.Exists(pluginRoot))
            return [];

        var entries = new List<AgentProfileEntry>();
        foreach (var profileDirectory in Directory.EnumerateDirectories(pluginRoot, "agent-profiles", SearchOption.AllDirectories))
        {
            var pluginId = ResolvePluginId(pluginRoot, profileDirectory);
            foreach (var path in Directory.EnumerateFiles(profileDirectory, "*.md", SearchOption.TopDirectoryOnly))
            {
                var fileId = Path.GetFileNameWithoutExtension(path);
                try
                {
                    entries.Add(BuildEntryFromContent(fileId, AgentProfileSources.Plugin, path, File.ReadAllText(path), pluginId));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    entries.Add(new AgentProfileEntry
                    {
                        Id = fileId,
                        Source = AgentProfileSources.Plugin,
                        PluginId = pluginId,
                        Path = path,
                        Valid = false,
                        ReadOnly = true,
                        Diagnostics =
                        [
                            Error("ProfileReadFailed", $"Agent profile file could not be read: {ex.Message}")
                        ]
                    });
                }
            }
        }

        return entries;
    }

    private AgentProfileEntry BuildEntryFromContent(string id, string source, string? path, string rawContent, string? pluginId = null)
    {
        var validation = ValidateRaw(rawContent, source, id);
        return new AgentProfileEntry
        {
            Id = id,
            Name = validation.Id,
            Description = validation.Description,
            Avatar = validation.Avatar,
            Source = source,
            Path = path,
            UpdatedAt = TryGetLastWriteTime(path),
            PluginId = pluginId,
            Fingerprint = validation.Fingerprint,
            Valid = validation.Valid,
            ReadOnly = IsReadOnlySource(source),
            RawContent = rawContent,
            Diagnostics = validation.Diagnostics,
            LockedFields = validation.LockedFields,
            RestrictedFields = validation.RestrictedFields,
            CompiledConfiguration = validation.CompiledConfiguration,
            ProviderPreference = validation.ProviderPreference
        };
    }

    private static DateTimeOffset? TryGetLastWriteTime(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private string? GetSourceDirectory(string source)
    {
        if (string.Equals(source, AgentProfileSources.Workspace, StringComparison.Ordinal))
            return _workspaceCraftPath == null ? null : Path.Combine(_workspaceCraftPath, "agents");
        if (string.Equals(source, AgentProfileSources.User, StringComparison.Ordinal))
            return Path.Combine(_userDotCraftPath, "agents");
        if (string.Equals(source, AgentProfileSources.Managed, StringComparison.Ordinal))
            return _workspaceCraftPath == null ? null : Path.Combine(_workspaceCraftPath, "managed", "agent-profiles");
        return null;
    }

    private string GetWritableProfilePath(string source, string id)
    {
        var directory = GetSourceDirectory(source);
        if (string.IsNullOrWhiteSpace(directory))
            throw new AgentProfileException(AgentProfileErrorKind.SourceUnavailable, $"Agent profile source '{source}' is not available.");

        return Path.Combine(directory, $"{id}.md");
    }

    private static AgentProfileEntry CloneWithShadow(AgentProfileEntry entry, string shadowedBy) => new()
    {
        Id = entry.Id,
        Name = entry.Name,
        Description = entry.Description,
        Avatar = entry.Avatar,
        Source = entry.Source,
        Path = entry.Path,
        UpdatedAt = entry.UpdatedAt,
        PluginId = entry.PluginId,
        Fingerprint = entry.Fingerprint,
        Valid = entry.Valid,
        ReadOnly = entry.ReadOnly,
        Shadowed = true,
        ShadowedBy = shadowedBy,
        SourceStack = entry.SourceStack,
        LockedFields = entry.LockedFields,
        RestrictedFields = entry.RestrictedFields,
        RawContent = entry.RawContent,
        CompiledConfiguration = entry.CompiledConfiguration,
        ProviderPreference = entry.ProviderPreference,
        Diagnostics =
        [
            .. entry.Diagnostics,
            new AgentProfileDiagnostic(
                "warning",
                "ProfileShadowed",
                $"This profile is shadowed by a higher-priority {shadowedBy} profile.")
        ]
    };

    private static AgentProfileEntry CloneWithSourceStack(AgentProfileEntry entry, IReadOnlyList<string> sourceStack) => new()
    {
        Id = entry.Id,
        Name = entry.Name,
        Description = entry.Description,
        Avatar = entry.Avatar,
        Source = entry.Source,
        Path = entry.Path,
        UpdatedAt = entry.UpdatedAt,
        PluginId = entry.PluginId,
        Fingerprint = entry.Fingerprint,
        Valid = entry.Valid,
        ReadOnly = entry.ReadOnly,
        Shadowed = entry.Shadowed,
        ShadowedBy = entry.ShadowedBy,
        SourceStack = [.. sourceStack],
        LockedFields = entry.LockedFields,
        RestrictedFields = entry.RestrictedFields,
        RawContent = entry.RawContent,
        CompiledConfiguration = entry.CompiledConfiguration,
        ProviderPreference = entry.ProviderPreference,
        Diagnostics = entry.Diagnostics
    };

    private static string EntryKey(AgentProfileEntry entry) =>
        $"{entry.Source}:{entry.Id}";

    private static int SourcePriority(string source) =>
        string.Equals(source, AgentProfileSources.Managed, StringComparison.Ordinal) ? 4
        : string.Equals(source, AgentProfileSources.Workspace, StringComparison.Ordinal) ? 3
        : string.Equals(source, AgentProfileSources.User, StringComparison.Ordinal) ? 2
        : string.Equals(source, AgentProfileSources.Plugin, StringComparison.Ordinal) ? 1
        : 0;

    private static bool IsReadOnlySource(string source) =>
        string.Equals(source, AgentProfileSources.BuiltIn, StringComparison.Ordinal)
        || string.Equals(source, AgentProfileSources.Plugin, StringComparison.Ordinal)
        || string.Equals(source, AgentProfileSources.Managed, StringComparison.Ordinal);

    private static string? ResolvePluginId(string pluginRoot, string profileDirectory)
    {
        var relative = Path.GetRelativePath(pluginRoot, profileDirectory);
        var firstSegment = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstSegment) ? null : firstSegment;
    }

    private ThreadConfiguration? Compile(
        JsonObject frontmatter,
        string id,
        string source,
        string fingerprint,
        string body,
        List<AgentProfileDiagnostic> diagnostics,
        List<string> lockedFields,
        List<string> restrictedFields,
        out AgentProfileProviderPreference? providerPreference)
    {
        ValidateNestedSections(frontmatter, diagnostics);

        var config = new ThreadConfiguration
        {
            AgentProfileId = id,
            AgentProfileSource = source,
            AgentProfileFingerprint = fingerprint,
            RoleInstructions = string.IsNullOrWhiteSpace(body) ? null : body.Trim(),
            OverrideBasePrompt = false
        };

        providerPreference = CompileProviderPreference(frontmatter, diagnostics);

        config.Mode = NormalizeMode(ReadOptionalString(frontmatter, "mode", diagnostics), diagnostics) ?? "agent";
        config.PromptProfile = NormalizeNullableString(ReadOptionalString(frontmatter, "promptProfile", diagnostics));
        config.ToolPolicy = CompileTools(TryGetObject(frontmatter, "tools", diagnostics), diagnostics);
        config.ToolAllowList = config.ToolPolicy?.Allow == null ? null : [.. config.ToolPolicy.Allow];
        config.ToolDenyList = config.ToolPolicy?.Deny == null ? null : [.. config.ToolPolicy.Deny];
        config.McpPolicy = CompileMcp(TryGetObject(frontmatter, "mcp", diagnostics), diagnostics);
        config.PluginPolicy = CompilePlugin(TryGetObject(frontmatter, "plugins", diagnostics), diagnostics);
        config.SkillsPolicy = CompileSkills(TryGetObject(frontmatter, "skills", diagnostics), diagnostics);
        config.TeamsPolicy = CompileTeams(TryGetObject(frontmatter, "teams", diagnostics), diagnostics);

        var permissions = TryGetObject(frontmatter, "permissions", diagnostics);
        if (permissions != null)
        {
            var approvalPolicy = ReadOptionalString(permissions, "approvalPolicy", diagnostics);
            config.ApprovalPolicy = ParseApprovalPolicy(approvalPolicy, diagnostics);
            config.RequireApprovalOutsideWorkspace = ReadOptionalBool(permissions, "requireApprovalOutsideWorkspace", diagnostics);
        }

        config.AgentControlToolAccess = ParseAgentControlLegacy(config.ToolPolicy?.AgentControl);
        config.AllowedAgentControlTools = config.ToolPolicy?.AllowedAgentControlTools == null
            ? null
            : [.. config.ToolPolicy.AllowedAgentControlTools];

        ApplyPluginTrustRestrictions(source, config, diagnostics, restrictedFields);
        ApplyManagedLocks(source, frontmatter, config, diagnostics, lockedFields);

        return diagnostics.Any(d => string.Equals(d.Severity, "error", StringComparison.OrdinalIgnoreCase))
            ? null
            : config;
    }

    private static void ValidateNestedSections(JsonObject frontmatter, List<AgentProfileDiagnostic> diagnostics)
    {
        var providerPreference = TryGetObject(frontmatter, "providerPreference", diagnostics);
        ValidateAllowedFields(providerPreference, ProviderPreferenceFields, "providerPreference", diagnostics);
        ValidateAllowedFields(TryGetObject(providerPreference, "reasoning", diagnostics), ReasoningFields, "providerPreference.reasoning", diagnostics);
        ValidateAllowedFields(TryGetObject(providerPreference, "contextWindow", diagnostics), ContextWindowFields, "providerPreference.contextWindow", diagnostics);
        ValidateAllowedFields(TryGetObject(frontmatter, "tools", diagnostics), ToolsFields, "tools", diagnostics);
        ValidateAllowedFields(TryGetObject(frontmatter, "mcp", diagnostics), McpFields, "mcp", diagnostics);
        ValidateAllowedFields(TryGetObject(TryGetObject(frontmatter, "mcp", diagnostics), "tools", diagnostics), NamePolicyFields, "mcp.tools", diagnostics);
        ValidateAllowedFields(TryGetObject(frontmatter, "plugins", diagnostics), PluginFields, "plugins", diagnostics);
        ValidateAllowedFields(TryGetObject(frontmatter, "skills", diagnostics), SkillsFields, "skills", diagnostics);
        ValidateAllowedFields(TryGetObject(frontmatter, "permissions", diagnostics), PermissionFields, "permissions", diagnostics);
        ValidateAllowedFields(TryGetObject(frontmatter, "teams", diagnostics), TeamsFields, "teams", diagnostics);
        var locked = TryGetObject(frontmatter, "locked", diagnostics);
        ValidateAllowedFields(locked, LockedFields, "locked", diagnostics);
        ValidateAllowedFields(TryGetObject(locked, "tools", diagnostics), LockedToolsFields, "locked.tools", diagnostics);
        ValidateAllowedFields(TryGetObject(locked, "mcp", diagnostics), LockedMcpFields, "locked.mcp", diagnostics);
        ValidateAllowedFields(TryGetObject(locked, "permissions", diagnostics), LockedPermissionsFields, "locked.permissions", diagnostics);
        ValidateAllowedFields(TryGetObject(locked, "teams", diagnostics), LockedTeamsFields, "locked.teams", diagnostics);
    }

    private static int? ReadOptionalAvatar(
        JsonObject frontmatter,
        List<AgentProfileDiagnostic> diagnostics)
    {
        if (!TryGetProperty(frontmatter, "avatar", out var value) || value == null)
            return null;

        var avatar = ReadPackedAvatarValue(value, diagnostics);
        return avatar.HasValue && ValidateAvatarValue(avatar.Value, diagnostics) ? avatar.Value : null;
    }

    private static int? ReadPackedAvatarValue(JsonNode value, List<AgentProfileDiagnostic> diagnostics)
    {
        int? parsed = null;
        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out var intValue))
                parsed = intValue;
            else if (jsonValue.TryGetValue<long>(out var longValue)
                && longValue >= int.MinValue
                && longValue <= int.MaxValue)
            {
                parsed = (int)longValue;
            }
            else if (jsonValue.TryGetValue<string>(out var raw)
                && raw.All(char.IsDigit)
                && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var stringValue))
            {
                parsed = stringValue;
            }
        }

        if (!parsed.HasValue)
        {
            diagnostics.Add(Error("InvalidFieldType", "Agent profile field 'avatar' must be an integer."));
            return null;
        }

        if (parsed.Value < 0)
        {
            diagnostics.Add(Error("InvalidPolicyValue", "Agent profile field 'avatar' must be a non-negative integer."));
            return null;
        }

        return parsed.Value;
    }

    private static bool ValidateAvatarValue(int avatar, List<AgentProfileDiagnostic> diagnostics)
    {
        if (AgentProfileAvatarCodec.TryDecode(avatar, out _, out _, out _))
            return true;

        diagnostics.Add(Error(
            "InvalidPolicyValue",
            $"Agent profile field 'avatar' must encode palette < {AgentProfileAvatarCodec.PaletteCount}, face < {AgentProfileAvatarCodec.FaceCount}, and accessory < {AgentProfileAvatarCodec.AccessoryCount}."));
        return false;
    }

    private static void ApplyPluginTrustRestrictions(
        string source,
        ThreadConfiguration config,
        List<AgentProfileDiagnostic> diagnostics,
        List<string> restrictedFields)
    {
        if (!string.Equals(source, AgentProfileSources.Plugin, StringComparison.Ordinal))
            return;

        if (string.Equals(config.ToolPolicy?.AgentControl, "full", StringComparison.OrdinalIgnoreCase))
        {
            config.ToolPolicy!.AgentControl = "disabled";
            config.AgentControlToolAccess = AgentControlToolAccess.Disabled;
            AddRestriction(restrictedFields, diagnostics, "tools.agentControl", "Plugin profile tool control was restricted at the plugin trust boundary.");
        }

        config.ToolPolicy ??= new ThreadToolPolicy();
        if (config.ToolPolicy.Allow is { Length: > 0 } allowedTools)
        {
            var filtered = allowedTools
                .Where(tool => !HighRiskToolNames.Contains(tool.Trim()))
                .ToArray();
            if (filtered.Length != allowedTools.Length)
            {
                config.ToolPolicy.Allow = filtered;
                config.ToolAllowList = filtered;
                AddRestriction(restrictedFields, diagnostics, "tools.allow", "Plugin profile high-risk tools were removed at the plugin trust boundary.");
            }
        }
        else if (config.ToolPolicy.Allow == null)
        {
            var highRiskTools = HighRiskToolNames.ToArray();
            config.ToolPolicy.Deny = MergeUnique(config.ToolPolicy.Deny, highRiskTools);
            config.ToolDenyList = MergeUnique(config.ToolDenyList, highRiskTools);
            AddRestriction(restrictedFields, diagnostics, "tools.deny", "Plugin profile high-risk tools were denied at the plugin trust boundary.");
        }

        if (config.McpPolicy?.Servers is { Length: > 0 })
        {
            config.McpPolicy.Servers = [];
            AddRestriction(restrictedFields, diagnostics, "mcp.servers", "Plugin profile MCP servers require explicit trust and were removed.");
        }

        if (config.SkillsPolicy?.AllowManage == true)
        {
            config.SkillsPolicy.AllowManage = false;
            AddRestriction(restrictedFields, diagnostics, "skills.allowManage", "Plugin profile skill management was disabled at the plugin trust boundary.");
        }

        if (config.ApprovalPolicy == ApprovalPolicy.AutoApprove)
        {
            config.ApprovalPolicy = ApprovalPolicy.Default;
            AddRestriction(restrictedFields, diagnostics, "permissions.approvalPolicy", "Plugin profile auto-approval was reset to default at the plugin trust boundary.");
        }
    }

    private static void ApplyManagedLocks(
        string source,
        JsonObject frontmatter,
        ThreadConfiguration config,
        List<AgentProfileDiagnostic> diagnostics,
        List<string> lockedFields)
    {
        var locked = TryGetObject(frontmatter, "locked", diagnostics);
        if (locked == null)
            return;

        if (!string.Equals(source, AgentProfileSources.Managed, StringComparison.Ordinal))
        {
            diagnostics.Add(Error("LockedFieldsRequireManagedSource", "Agent profile locked fields are only supported for managed profiles."));
            return;
        }

        var lockedTools = TryGetObject(locked, "tools", diagnostics);
        var requiredToolDenies = lockedTools == null ? null : ReadOptionalStringArray(lockedTools, "deny", diagnostics);
        if (requiredToolDenies is { Length: > 0 })
        {
            config.ToolPolicy ??= new ThreadToolPolicy();
            config.ToolPolicy.Deny = MergeUnique(config.ToolPolicy.Deny, requiredToolDenies);
            config.ToolDenyList = MergeUnique(config.ToolDenyList, requiredToolDenies);
            AddLockedField(lockedFields, "tools.deny");
        }

        var lockedMcp = TryGetObject(locked, "mcp", diagnostics);
        var allowedMcpServers = lockedMcp == null ? null : ReadOptionalStringArray(lockedMcp, "servers", diagnostics);
        if (allowedMcpServers != null)
        {
            config.McpPolicy ??= new ThreadMcpPolicy();
            var before = config.McpPolicy.Servers;
            config.McpPolicy.Servers = before == null
                ? [.. allowedMcpServers]
                : before.Where(server => ContainsName(allowedMcpServers, server)).ToArray();
            if (before != null && before.Length != config.McpPolicy.Servers.Length)
                diagnostics.Add(Warning("LockedFieldConflict", "Agent profile MCP servers were capped by a managed lock."));
            AddLockedField(lockedFields, "mcp.servers");
        }

        var lockedPermissions = TryGetObject(locked, "permissions", diagnostics);
        var deniedApprovalPolicies = lockedPermissions == null
            ? null
            : ReadOptionalStringArray(lockedPermissions, "deniedApprovalPolicies", diagnostics);
        if (deniedApprovalPolicies is { Length: > 0 })
        {
            if (deniedApprovalPolicies.Any(policy => IsApprovalPolicy(policy, config.ApprovalPolicy)))
            {
                config.ApprovalPolicy = ApprovalPolicy.Default;
                diagnostics.Add(Warning("LockedFieldConflict", "Agent profile approval policy was reset by a managed lock."));
            }

            AddLockedField(lockedFields, "permissions.approvalPolicy");
        }

        var overrideBasePrompt = ReadOptionalBool(locked, "overrideBasePrompt", diagnostics);
        if (overrideBasePrompt == false)
        {
            config.OverrideBasePrompt = false;
            AddLockedField(lockedFields, "overrideBasePrompt");
        }

        var lockedTeams = TryGetObject(locked, "teams", diagnostics);
        var reservedTools = lockedTeams == null ? null : ReadOptionalString(lockedTeams, "reservedTools", diagnostics);
        if (!string.IsNullOrWhiteSpace(reservedTools))
        {
            config.TeamsPolicy ??= new ThreadTeamsPolicy();
            config.TeamsPolicy.ReservedTools = reservedTools.Trim();
            AddLockedField(lockedFields, "teams.reservedTools");
        }
    }

    private static AgentProfileProviderPreference? CompileProviderPreference(
        JsonObject frontmatter,
        List<AgentProfileDiagnostic> diagnostics)
    {
        if (!TryGetProperty(frontmatter, "providerPreference", out _))
            return null;

        var section = TryGetObject(frontmatter, "providerPreference", diagnostics);
        if (section == null)
            return null;

        RequireProperties(
            section,
            "providerPreference",
            diagnostics,
            "providerId",
            "model",
            "reasoning",
            "speed",
            "contextWindow");

        var reasoningSection = TryGetObject(section, "reasoning", diagnostics);
        if (reasoningSection != null)
        {
            RequireProperties(
                reasoningSection,
                "providerPreference.reasoning",
                diagnostics,
                "enabled",
                "effort");
        }

        var contextWindowSection = TryGetObject(section, "contextWindow", diagnostics);
        if (contextWindowSection != null)
            RequireProperties(contextWindowSection, "providerPreference.contextWindow", diagnostics, "mode");

        var providerId = NormalizeNullableString(ReadOptionalString(section, "providerId", diagnostics, required: true));
        var model = NormalizeNullableString(ReadOptionalString(section, "model", diagnostics, required: true));
        var reasoning = CompileProfileReasoning(reasoningSection, diagnostics) ?? new AgentProfileReasoningPreference();

        return new AgentProfileProviderPreference
        {
            ProviderId = providerId ?? string.Empty,
            Model = model ?? string.Empty,
            Reasoning = reasoning,
            Speed = ParseInferenceSpeed(ReadOptionalString(section, "speed", diagnostics), diagnostics),
            ContextWindow = new ModelPreferenceContextWindow
            {
                Mode = ParseContextWindowMode(
                    contextWindowSection == null
                        ? null
                        : ReadOptionalString(contextWindowSection, "mode", diagnostics),
                    diagnostics)
            }
        };
    }

    private static void RequireProperties(
        JsonObject section,
        string path,
        List<AgentProfileDiagnostic> diagnostics,
        params string[] properties)
    {
        foreach (var property in properties)
        {
            if (!TryGetProperty(section, property, out var value) || value == null)
            {
                diagnostics.Add(Error(
                    "MissingRequiredField",
                    $"Agent profile field '{path}.{property}' is required."));
            }
        }
    }

    private static InferenceSpeed ParseInferenceSpeed(
        string? raw,
        List<AgentProfileDiagnostic> diagnostics)
    {
        if (string.Equals(raw, "fast", StringComparison.OrdinalIgnoreCase))
            return InferenceSpeed.Fast;
        if (string.Equals(raw, "standard", StringComparison.OrdinalIgnoreCase))
            return InferenceSpeed.Standard;

        if (!string.IsNullOrWhiteSpace(raw))
            diagnostics.Add(Error("InvalidPolicyValue", "providerPreference.speed must be standard or fast."));
        return InferenceSpeed.Standard;
    }

    private static ContextWindowMode ParseContextWindowMode(
        string? raw,
        List<AgentProfileDiagnostic> diagnostics)
    {
        if (string.Equals(raw, "max", StringComparison.OrdinalIgnoreCase))
            return ContextWindowMode.Max;
        if (string.Equals(raw, "default", StringComparison.OrdinalIgnoreCase))
            return ContextWindowMode.Default;

        if (!string.IsNullOrWhiteSpace(raw))
            diagnostics.Add(Error(
                "InvalidPolicyValue",
                "providerPreference.contextWindow.mode must be default or max."));
        return ContextWindowMode.Default;
    }

    private static AgentProfileReasoningPreference? CompileProfileReasoning(
        JsonObject? reasoning,
        List<AgentProfileDiagnostic> diagnostics)
    {
        if (reasoning == null)
            return null;

        var enabled = ReadOptionalBool(reasoning, "enabled", diagnostics);
        var effort = ReadOptionalString(reasoning, "effort", diagnostics);
        return new AgentProfileReasoningPreference
        {
            Enabled = enabled ?? true,
            Effort = ParseReasoningEffort(effort, diagnostics) ?? ReasoningEffort.Medium
        };
    }

    private static ThreadToolPolicy? CompileTools(JsonObject? tools, List<AgentProfileDiagnostic> diagnostics)
    {
        if (tools == null)
            return null;

        var agentControl = ReadOptionalString(tools, "agentControl", diagnostics);
        if (!string.IsNullOrWhiteSpace(agentControl)
            && ParseAgentControlLegacy(agentControl) == null)
        {
            diagnostics.Add(Error(
                "InvalidPolicyValue",
                "tools.agentControl must be 'full', 'disabled', or 'allowList'."));
        }

        return new ThreadToolPolicy
        {
            Allow = ReadOptionalStringArray(tools, "allow", diagnostics),
            Deny = ReadOptionalStringArray(tools, "deny", diagnostics),
            AgentControl = NormalizeAgentControl(agentControl),
            AllowedAgentControlTools = ReadOptionalStringArray(tools, "allowedAgentControlTools", diagnostics)
        };
    }

    private static ThreadMcpPolicy? CompileMcp(JsonObject? mcp, List<AgentProfileDiagnostic> diagnostics)
    {
        if (mcp == null)
            return null;

        var tools = TryGetObject(mcp, "tools", diagnostics);
        return new ThreadMcpPolicy
        {
            Servers = ReadOptionalStringArray(mcp, "servers", diagnostics),
            Tools = tools == null
                ? null
                : new ThreadNamePolicy
                {
                    Allow = ReadOptionalStringArray(tools, "allow", diagnostics),
                    Deny = ReadOptionalStringArray(tools, "deny", diagnostics)
                }
        };
    }

    private static ThreadPluginPolicy? CompilePlugin(JsonObject? plugins, List<AgentProfileDiagnostic> diagnostics)
    {
        if (plugins == null)
            return null;

        return new ThreadPluginPolicy
        {
            Allow = ReadOptionalStringArray(plugins, "allow", diagnostics),
            Deny = ReadOptionalStringArray(plugins, "deny", diagnostics)
        };
    }

    private static ThreadSkillsPolicy? CompileSkills(JsonObject? skills, List<AgentProfileDiagnostic> diagnostics)
    {
        if (skills == null)
            return null;

        return new ThreadSkillsPolicy
        {
            Preload = ReadOptionalStringArray(skills, "preload", diagnostics),
            Allow = ReadOptionalStringArray(skills, "allow", diagnostics),
            Deny = ReadOptionalStringArray(skills, "deny", diagnostics),
            AllowManage = ReadOptionalBool(skills, "allowManage", diagnostics)
        };
    }

    private static ThreadTeamsPolicy? CompileTeams(JsonObject? teams, List<AgentProfileDiagnostic> diagnostics)
    {
        if (teams == null)
            return null;

        var reservedTools = ReadOptionalString(teams, "reservedTools", diagnostics);
        if (!string.IsNullOrWhiteSpace(reservedTools)
            && !string.Equals(reservedTools, "keep", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error("InvalidPolicyValue", "teams.reservedTools only supports 'keep' in v1."));
        }

        return new ThreadTeamsPolicy
        {
            ReservedTools = NormalizeNullableString(reservedTools)
        };
    }

    private static string? NormalizeMode(string? raw, List<AgentProfileDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = raw.Trim();
        if (string.Equals(normalized, "agent", StringComparison.OrdinalIgnoreCase))
            return "agent";
        if (string.Equals(normalized, "plan", StringComparison.OrdinalIgnoreCase))
            return "plan";

        diagnostics.Add(Error("InvalidPolicyValue", "mode must be 'agent' or 'plan'."));
        return null;
    }

    private static ApprovalPolicy ParseApprovalPolicy(string? raw, List<AgentProfileDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ApprovalPolicy.Default;

        return raw.Trim() switch
        {
            "default" => ApprovalPolicy.Default,
            "prompt" => ApprovalPolicy.Prompt,
            "autoApprove" => ApprovalPolicy.AutoApprove,
            "interrupt" => ApprovalPolicy.Interrupt,
            _ => AddApprovalPolicyError(diagnostics)
        };
    }

    private static ApprovalPolicy AddApprovalPolicyError(List<AgentProfileDiagnostic> diagnostics)
    {
        diagnostics.Add(Error("InvalidPolicyValue", "permissions.approvalPolicy must be 'default', 'prompt', 'autoApprove', or 'interrupt'."));
        return ApprovalPolicy.Default;
    }

    private static ReasoningEffort? ParseReasoningEffort(string? raw, List<AgentProfileDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = NormalizeEnumToken(raw);
        return normalized switch
        {
            "low" => ReasoningEffort.Low,
            "medium" => ReasoningEffort.Medium,
            "high" => ReasoningEffort.High,
            "extrahigh" or "xhigh" => ReasoningEffort.ExtraHigh,
            _ => AddReasoningEffortError(diagnostics)
        };
    }

    private static ReasoningEffort? AddReasoningEffortError(List<AgentProfileDiagnostic> diagnostics)
    {
        diagnostics.Add(Error(
            "InvalidPolicyValue",
            "providerPreference.reasoning.effort must be low, medium, high, or extraHigh; use enabled: false for Off."));
        return null;
    }

    private static AgentControlToolAccess? ParseAgentControlLegacy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return NormalizeEnumToken(value) switch
        {
            "disabled" => AgentControlToolAccess.Disabled,
            "full" => AgentControlToolAccess.Full,
            "allowlist" => AgentControlToolAccess.AllowList,
            _ => null
        };
    }

    private static string? NormalizeAgentControl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return NormalizeEnumToken(value) switch
        {
            "disabled" => "disabled",
            "full" => "full",
            "allowlist" => "allowList",
            _ => value.Trim()
        };
    }

    private static string NormalizeEnumToken(string raw) =>
        string.Concat(raw.Trim().Where(ch => ch is not '-' and not '_' and not ' '))
            .ToLowerInvariant();

    private static ExtractedProfile? ExtractFrontmatter(string rawContent, List<AgentProfileDiagnostic> diagnostics)
    {
        var normalized = rawContent.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Length > 0 && normalized[0] == '\uFEFF')
            normalized = normalized[1..];

        var lines = normalized.Split('\n');
        if (lines.Length == 0 || !string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
        {
            diagnostics.Add(Error("MissingFrontmatter", "Agent profile Markdown must start with YAML frontmatter."));
            return null;
        }

        var end = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.Equals(lines[i].Trim(), "---", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            diagnostics.Add(Error("MissingFrontmatter", "Agent profile YAML frontmatter is missing its closing delimiter."));
            return null;
        }

        var frontmatter = string.Join('\n', lines.Skip(1).Take(end - 1));
        var body = string.Join('\n', lines.Skip(end + 1)).Trim();
        return new ExtractedProfile(frontmatter, body);
    }

    private static void ValidateAllowedFields(
        JsonObject? section,
        HashSet<string> allowed,
        string path,
        List<AgentProfileDiagnostic> diagnostics)
    {
        if (section == null)
            return;

        foreach (var property in section)
        {
            if (allowed.Contains(property.Key))
                continue;

            var field = string.IsNullOrWhiteSpace(path) ? property.Key : $"{path}.{property.Key}";
            diagnostics.Add(Error("UnsupportedField", $"Agent profile field '{field}' is not supported."));
        }
    }

    private static JsonNode? ConvertYamlToJson(object? value)
    {
        if (value == null)
            return null;

        if (value is IDictionary dictionary)
        {
            var obj = new JsonObject();
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                obj[key] = ConvertYamlToJson(entry.Value);
            }

            return obj;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var array = new JsonArray();
            foreach (var item in enumerable)
                array.Add(ConvertYamlToJson(item));
            return array;
        }

        return value switch
        {
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            double d => JsonValue.Create(d),
            float f => JsonValue.Create(f),
            decimal m => JsonValue.Create(m),
            _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture))
        };
    }

    private static string? ReadOptionalString(
        JsonObject section,
        string key,
        List<AgentProfileDiagnostic> diagnostics,
        bool required = false)
    {
        if (!TryGetProperty(section, key, out var value) || value == null)
        {
            if (required)
                diagnostics.Add(Error("MissingRequiredField", $"Agent profile frontmatter must include '{key}'."));
            return null;
        }

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
            return text;

        diagnostics.Add(Error("InvalidFieldType", $"Agent profile field '{key}' must be a string."));
        return null;
    }

    private static bool? ReadOptionalBool(
        JsonObject section,
        string key,
        List<AgentProfileDiagnostic> diagnostics)
    {
        if (!TryGetProperty(section, key, out var value) || value == null)
            return null;

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<bool>(out var boolValue))
                return boolValue;
            if (jsonValue.TryGetValue<string>(out var raw)
                && bool.TryParse(raw, out var parsed))
            {
                return parsed;
            }
        }

        diagnostics.Add(Error("InvalidFieldType", $"Agent profile field '{key}' must be a boolean."));
        return null;
    }

    private static string[]? ReadOptionalStringArray(
        JsonObject section,
        string key,
        List<AgentProfileDiagnostic> diagnostics)
    {
        if (!TryGetProperty(section, key, out var value) || value == null)
            return null;

        if (value is not JsonArray array)
        {
            diagnostics.Add(Error("InvalidFieldType", $"Agent profile field '{key}' must be a string array."));
            return null;
        }

        var values = new List<string>();
        foreach (var item in array)
        {
            if (item is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
            {
                values.Add(text);
                continue;
            }

            diagnostics.Add(Error("InvalidFieldType", $"Agent profile field '{key}' must contain only strings."));
        }

        return values.Select(value => value.Trim()).Where(value => value.Length > 0).ToArray();
    }

    private static JsonObject? TryGetObject(
        JsonObject? section,
        string key,
        List<AgentProfileDiagnostic> diagnostics)
    {
        if (section == null || !TryGetProperty(section, key, out var value) || value == null)
            return null;

        if (value is JsonObject obj)
            return obj;

        diagnostics.Add(Error("InvalidFieldType", $"Agent profile field '{key}' must be an object."));
        return null;
    }

    private static bool TryGetProperty(JsonObject obj, string key, out JsonNode? value)
    {
        if (obj.TryGetPropertyValue(key, out value))
            return true;

        foreach (var property in obj)
        {
            if (string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool HasConfigProperty(JsonElement? configElement, string key)
    {
        if (!configElement.HasValue || configElement.Value.ValueKind != JsonValueKind.Object)
            return false;

        return configElement.Value.EnumerateObject()
            .Any(property => string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeProfileId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new AgentProfileException(AgentProfileErrorKind.ValidationFailed, "Agent profile id is required.");

        var normalized = id.Trim();
        if (!IsValidProfileId(normalized))
            throw new AgentProfileException(AgentProfileErrorKind.ValidationFailed, $"Agent profile id '{normalized}' is invalid.");

        return normalized;
    }

    private static bool IsValidProfileId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxProfileIdLength)
            return false;

        if (value is "." or "..")
            return false;

        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        return ProfileIdRegex.IsMatch(value);
    }

    private static string NormalizeSource(string source) =>
        NormalizeSourceOrNull(source)
        ?? throw new AgentProfileException(AgentProfileErrorKind.SourceUnavailable, $"Agent profile source '{source}' is not supported.");

    private static string? NormalizeSourceOrNull(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var trimmed = source.Trim();
        if (string.Equals(trimmed, AgentProfileSources.BuiltIn, StringComparison.OrdinalIgnoreCase))
            return AgentProfileSources.BuiltIn;
        if (string.Equals(trimmed, AgentProfileSources.Plugin, StringComparison.OrdinalIgnoreCase))
            return AgentProfileSources.Plugin;
        if (string.Equals(trimmed, AgentProfileSources.User, StringComparison.OrdinalIgnoreCase))
            return AgentProfileSources.User;
        if (string.Equals(trimmed, AgentProfileSources.Workspace, StringComparison.OrdinalIgnoreCase))
            return AgentProfileSources.Workspace;
        if (string.Equals(trimmed, AgentProfileSources.Managed, StringComparison.OrdinalIgnoreCase))
            return AgentProfileSources.Managed;

        throw new AgentProfileException(AgentProfileErrorKind.SourceUnavailable, $"Agent profile source '{source}' is not supported.");
    }

    private static string? NormalizeNullableString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AgentProfileDiagnostic Error(string code, string message) =>
        new("error", code, message);

    private static AgentProfileDiagnostic Warning(string code, string message) =>
        new("warning", code, message);

    private static void AddRestriction(
        List<string> restrictedFields,
        List<AgentProfileDiagnostic> diagnostics,
        string field,
        string message)
    {
        if (!restrictedFields.Contains(field, StringComparer.Ordinal))
            restrictedFields.Add(field);
        diagnostics.Add(Warning("TrustBoundaryRestriction", message));
    }

    private static void AddLockedField(List<string> lockedFields, string field)
    {
        if (!lockedFields.Contains(field, StringComparer.Ordinal))
            lockedFields.Add(field);
    }

    private static string[] MergeUnique(string[]? first, string[] second) =>
        (first ?? [])
        .Concat(second)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static bool IsApprovalPolicy(string raw, ApprovalPolicy policy) =>
        policy switch
        {
            ApprovalPolicy.Default => string.Equals(raw, "default", StringComparison.OrdinalIgnoreCase),
            ApprovalPolicy.Prompt => string.Equals(raw, "prompt", StringComparison.OrdinalIgnoreCase),
            ApprovalPolicy.AutoApprove => string.Equals(raw, "autoApprove", StringComparison.OrdinalIgnoreCase),
            ApprovalPolicy.Interrupt => string.Equals(raw, "interrupt", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static bool ContainsName(string[] values, string value) =>
        values.Any(candidate => string.Equals(candidate?.Trim(), value.Trim(), StringComparison.Ordinal));

    private static string ComputeFingerprint(string rawContent)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawContent));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ThreadConfiguration CloneThreadConfiguration(ThreadConfiguration source) => new()
    {
        AgentProfileId = source.AgentProfileId,
        AgentProfileSource = source.AgentProfileSource,
        AgentProfileFingerprint = source.AgentProfileFingerprint,
        AgentBuilderTargetId = source.AgentBuilderTargetId,
        AgentBuilderTargetSource = source.AgentBuilderTargetSource,
        McpServers = source.McpServers == null ? null : [.. source.McpServers],
        Mode = source.Mode,
        Extensions = source.Extensions == null ? null : [.. source.Extensions],
        CustomTools = source.CustomTools == null ? null : [.. source.CustomTools],
        ProviderId = source.ProviderId,
        Model = source.Model,
        Reasoning = CloneReasoning(source.Reasoning),
        Speed = source.Speed,
        ContextWindow = CloneContextWindow(source.ContextWindow),
        WorkspaceOverride = source.WorkspaceOverride,
        Cwd = source.Cwd,
        RuntimeWorkspaceRoots = source.RuntimeWorkspaceRoots == null ? null : [.. source.RuntimeWorkspaceRoots],
        ExecutionWorkspaceOverride = source.ExecutionWorkspaceOverride,
        ToolProfile = source.ToolProfile,
        UseToolProfileOnly = source.UseToolProfileOnly,
        AgentInstructions = source.AgentInstructions,
        ToolAllowList = source.ToolAllowList == null ? null : [.. source.ToolAllowList],
        ToolDenyList = source.ToolDenyList == null ? null : [.. source.ToolDenyList],
        ToolPolicy = source.ToolPolicy == null
            ? null
            : new ThreadToolPolicy
            {
                Allow = source.ToolPolicy.Allow == null ? null : [.. source.ToolPolicy.Allow],
                Deny = source.ToolPolicy.Deny == null ? null : [.. source.ToolPolicy.Deny],
                AgentControl = source.ToolPolicy.AgentControl,
                AllowedAgentControlTools = source.ToolPolicy.AllowedAgentControlTools == null ? null : [.. source.ToolPolicy.AllowedAgentControlTools]
            },
        McpPolicy = source.McpPolicy == null
            ? null
            : new ThreadMcpPolicy
            {
                Servers = source.McpPolicy.Servers == null ? null : [.. source.McpPolicy.Servers],
                Tools = source.McpPolicy.Tools == null
                    ? null
                    : new ThreadNamePolicy
                    {
                        Allow = source.McpPolicy.Tools.Allow == null ? null : [.. source.McpPolicy.Tools.Allow],
                        Deny = source.McpPolicy.Tools.Deny == null ? null : [.. source.McpPolicy.Tools.Deny]
                    }
            },
        PluginPolicy = source.PluginPolicy == null
            ? null
            : new ThreadPluginPolicy
            {
                Allow = source.PluginPolicy.Allow == null ? null : [.. source.PluginPolicy.Allow],
                Deny = source.PluginPolicy.Deny == null ? null : [.. source.PluginPolicy.Deny]
            },
        SkillsPolicy = source.SkillsPolicy == null
            ? null
            : new ThreadSkillsPolicy
            {
                Preload = source.SkillsPolicy.Preload == null ? null : [.. source.SkillsPolicy.Preload],
                Allow = source.SkillsPolicy.Allow == null ? null : [.. source.SkillsPolicy.Allow],
                Deny = source.SkillsPolicy.Deny == null ? null : [.. source.SkillsPolicy.Deny],
                AllowManage = source.SkillsPolicy.AllowManage
            },
        TeamsPolicy = source.TeamsPolicy == null
            ? null
            : new ThreadTeamsPolicy
            {
                ReservedTools = source.TeamsPolicy.ReservedTools
            },
        AgentControlToolAccess = source.AgentControlToolAccess,
        AllowedAgentControlTools = source.AllowedAgentControlTools == null ? null : [.. source.AllowedAgentControlTools],
        PromptProfile = source.PromptProfile,
        RoleInstructions = source.RoleInstructions,
        OverrideBasePrompt = source.OverrideBasePrompt,
        ApprovalPolicy = source.ApprovalPolicy,
        ApprovalTimeoutSeconds = source.ApprovalTimeoutSeconds,
        AutomationTaskDirectory = source.AutomationTaskDirectory,
        RequireApprovalOutsideWorkspace = source.RequireApprovalOutsideWorkspace
    };

    private static AppConfig.ReasoningConfig? CloneReasoning(AppConfig.ReasoningConfig? source) =>
        source == null
            ? null
            : new AppConfig.ReasoningConfig
            {
                Enabled = source.Enabled,
                Effort = source.Effort,
                Output = source.Output
            };

    private static ThreadContextWindowConfig? CloneContextWindow(ThreadContextWindowConfig? source) =>
        source == null
            ? null
            : new ThreadContextWindowConfig
            {
                Mode = source.Mode
            };

    private static IEnumerable<BuiltInAgentProfile> BuiltInProfiles()
    {
        yield return new BuiltInAgentProfile(
            "team-leader",
            """
---
name: team-leader
description: Plan, assign, coordinate, synthesize, and finalize.
tools:
  agentControl: full
skills:
  allowManage: false
teams:
  reservedTools: keep
---

You coordinate the mission, keep the plan current, assign work clearly, and synthesize final results.
""");
        yield return new BuiltInAgentProfile(
            "team-explorer",
            """
---
name: team-explorer
description: Inspect, research, map unknowns, and produce findings.
mode: plan
tools:
  agentControl: disabled
skills:
  allowManage: false
teams:
  reservedTools: keep
---

You explore the problem space, gather evidence, and report concise findings before implementation.
""");
        yield return new BuiltInAgentProfile(
            "team-builder",
            """
---
name: team-builder
description: Edit, test, and produce implementation artifacts.
tools:
  agentControl: disabled
skills:
  allowManage: false
teams:
  reservedTools: keep
---

You implement focused changes, verify them, and keep the work aligned with the assigned task.
""");
        yield return new BuiltInAgentProfile(
            "team-reviewer",
            """
---
name: team-reviewer
description: Review correctness, risks, tests, and maintainability with read-focused defaults.
mode: plan
tools:
  deny: [WriteFile, EditFile, Exec, WriteStdin]
  agentControl: disabled
skills:
  allowManage: false
teams:
  reservedTools: keep
---

You review for correctness, risk, tests, and maintainability. Prefer evidence and file references over broad summary.
""");
        yield return new BuiltInAgentProfile(
            "team-operator",
            """
---
name: team-operator
description: Use app, browser, and workflow capabilities selected for operational tasks.
tools:
  agentControl: disabled
skills:
  allowManage: false
teams:
  reservedTools: keep
---

You operate connected tools carefully, report observable state, and stop before taking irreversible actions.
""");
    }

    private readonly record struct ExtractedProfile(string Frontmatter, string Body);

    private sealed record BuiltInAgentProfile(string Id, string RawContent);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildProfileIdRegex();
}
