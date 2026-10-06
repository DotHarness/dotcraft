using System.Text.Json;

namespace DotCraft.Agents.Packages;

public sealed record AgentPackageManifest(
    string Format,
    string Name,
    string? Description,
    string Profile,
    DateTimeOffset ExportedAt,
    IReadOnlyList<AgentPackageEntry> Packages)
{
    public const string CurrentFormat = "dotcraft-agent/1";
    public const string FileName = "agent.json";
    public const string ProfileFileName = "profile.md";
}

public sealed record AgentPackageEntry(
    string Kind,
    string Name,
    string DisplayName,
    string? Version,
    string Sha256,
    bool Dotnet,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> McpServers,
    AgentPackageMarketplace? Marketplace,
    string? File);

public sealed record AgentPackageMarketplace(
    string Name,
    string SourceKind,
    string Source,
    string? Ref,
    string MarketplacePath,
    IReadOnlyList<string> SparsePaths);

public static class AgentPackageKinds
{
    public const string Skill = "skill";
    public const string Plugin = "plugin";
}

public static class AgentPackageLimits
{
    public const int MaximumBytes = 64 * 1024 * 1024;
    public const int MaximumManifestBytes = 1024 * 1024;
    public const int MaximumDocumentBytes = 64 * 1024;
    public const int MaximumSkillFiles = 1000;
    public const long MaximumSkillBytes = 100L * 1024 * 1024;
    public const int MaximumPluginFiles = 10_000;
    public const long MaximumPluginBytes = 512L * 1024 * 1024;
}

public static class AgentPackageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

public sealed class AgentPackageException(string code, string message) : Exception(message)
{
    public const string InvalidCode = "agentPackageInvalid";
    public const string TooLargeCode = "agentPackageTooLarge";

    public string Code { get; } = code;

    public static AgentPackageException Invalid(string message) => new(InvalidCode, message);

    public static AgentPackageException TooLarge(string message) => new(TooLargeCode, message);
}
