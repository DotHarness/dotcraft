namespace DotCraft.CodexGateway.Workspace;

public static class CodexPermissionProfile
{
    public const string Id = "dotcraft";

    public static object List() => new
    {
        data = new[] { new { id = Id, description = "Use DotCraft's existing tool permissions and approvals.", allowed = true } },
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
            allowedPermissionProfiles = new Dictionary<string, bool> { [Id] = true },
            defaultPermissions = Id
        }
    };

    public static object Sandbox() => new { type = "dangerFullAccess" };

    public static object Active() => new { id = Id, extends = (string?)null };

    internal static void ValidateSelection(string? profile)
    {
        if (profile is not null && profile != Id)
            throw new ArgumentException("Only the dotcraft permission profile is available.");
    }

    internal static void ValidateSandbox(string? sandbox)
    {
        if (sandbox is not null && sandbox != "danger-full-access")
            throw new ArgumentException("Only danger-full-access is available; DotCraft retains tool permission enforcement.");
    }
}
