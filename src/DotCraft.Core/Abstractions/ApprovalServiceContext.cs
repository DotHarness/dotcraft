using DotCraft.Configuration;
using DotCraft.Security;

namespace DotCraft.Abstractions;

/// <summary>
/// Provides context information for approval service factory to create approval services.
/// </summary>
public sealed class ApprovalServiceContext
{
    /// <summary>
    /// The application configuration.
    /// </summary>
    public required AppConfig Config { get; init; }

    /// <summary>
    /// The workspace path for approval store persistence.
    /// </summary>
    public required string WorkspacePath { get; init; }

    /// <summary>
    /// Optional permission service for authorization checks.
    /// </summary>
    public object? PermissionService { get; init; }

    /// <summary>
    /// Optional approval store for console approval persistence.
    /// </summary>
    public ApprovalStore? ApprovalStore { get; init; }

    /// <summary>
    /// Optional admin users list for approval authorization (as string IDs).
    /// </summary>
    public IEnumerable<string>? AdminUsers { get; init; }

    /// <summary>
    /// Optional whitelisted users list (as string IDs).
    /// </summary>
    public IEnumerable<string>? WhitelistedUsers { get; init; }

    /// <summary>
    /// Optional whitelisted groups/chats list (as string IDs).
    /// </summary>
    public IEnumerable<string>? WhitelistedGroups { get; init; }

    /// <summary>
    /// Optional approval timeout in seconds.
    /// </summary>
    public int? ApprovalTimeoutSeconds { get; init; }

    /// <summary>
    /// Optional approval mode for API mode.
    /// </summary>
    public string? ApprovalMode { get; init; }

    /// <summary>
    /// Optional auto-approve fallback for API mode.
    /// </summary>
    public bool? AutoApprove { get; init; }
}
