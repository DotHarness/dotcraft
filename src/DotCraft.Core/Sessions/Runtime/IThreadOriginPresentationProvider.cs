using System.Text.Json.Serialization;

namespace DotCraft.Sessions;

/// <summary>Trusted, source-neutral inputs for resolving a thread's origin presentation.</summary>
public sealed record ThreadOriginPresentationContext(
    string ThreadId,
    string WorkspacePath,
    string OriginChannel,
    string? ChannelContext);

/// <summary>Resolves optional presentation metadata for threads owned by a known origin source.</summary>
public interface IThreadOriginPresentationProvider
{
    /// <summary>Returns presentation metadata when this provider owns the supplied origin.</summary>
    ThreadOriginPresentationSnapshot? Resolve(ThreadOriginPresentationContext context);
}

/// <summary>Source-neutral presentation metadata for a thread origin badge.</summary>
public sealed class ThreadOriginPresentationSnapshot
{
    /// <summary>Stable identifier for the source contributing the presentation.</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Human-readable origin or subject label.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional data URL or safe URL for the origin icon.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    /// <summary>Optional stable identifier for a source-owned subject, such as a team member.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubjectId { get; set; }

    /// <summary>Optional stable subject kind, such as <c>member</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubjectKind { get; set; }
}
