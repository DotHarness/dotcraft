namespace DotCraft.CodexGateway.Workspace;

public static class CodexPermissionProfile
{
    public const string Id = "dotcraft";
    public const string FullAccessId = ":danger-full-access";

    public static object List() => new
    {
        data = new[]
        {
            new { id = Id, description = "Use DotCraft's current approval mode and tool permissions.", allowed = true },
            new { id = FullAccessId, description = "Automatically approve tools under DotCraft's tool permissions.", allowed = true },
            new { id = ":read-only", description = "Use DotCraft's tool permissions without a Codex sandbox.", allowed = true },
            new { id = ":workspace", description = "Use DotCraft's tool permissions without a Codex sandbox.", allowed = true }
        },
        nextCursor = (string?)null
    };

    public static object Requirements() => new
    {
        supportsIndependentSpeedModes = (bool?)null,
        requirements = new
        {
            allowedApprovalPolicies = new[] { "on-request", "never" },
            allowedApprovalsReviewers = new[] { "user" },
            allowedSandboxModes = new[] { "danger-full-access" },
            allowedPermissionProfiles = new Dictionary<string, bool>
            {
                [Id] = true, [FullAccessId] = true, [":read-only"] = true, [":workspace"] = true
            },
            defaultPermissions = Id
        }
    };

    public static object Sandbox() => new { type = "dangerFullAccess" };

    public static object Active(string? policy) => new { id = ForPolicy(policy), extends = (string?)null };

    internal static string ForPolicy(string? policy) => policy == "autoApprove" ? FullAccessId : Id;

    internal static void ValidateSelection(string? profile)
    {
        if (profile is not (null or Id or FullAccessId or ":read-only" or ":workspace"))
            throw new ArgumentException("Select a permission profile returned by permissionProfile/list.");
    }
}
