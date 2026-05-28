using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Context;

internal static class SubAgentProfilePromptSectionBuilder
{
    public static string? Build(
        IEnumerable<SubAgentProfile>? configuredProfiles,
        IEnumerable<string>? knownRuntimeTypes = null,
        IEnumerable<string>? disabledProfiles = null,
        Func<string, bool>? binaryAvailabilityProbe = null)
    {
        var runtimeTypes = (knownRuntimeTypes ?? SubAgentProfileRegistry.KnownRuntimeTypes).ToArray();
        var runtimeSet = new HashSet<string>(runtimeTypes, StringComparer.OrdinalIgnoreCase);
        var probe = binaryAvailabilityProbe ?? (bin => CliOneshotRuntime.TryResolveExecutablePath(bin, out _));
        var registry = new SubAgentProfileRegistry(
            configuredProfiles,
            SubAgentProfileRegistry.CreateBuiltInProfiles(),
            runtimeTypes,
            disabledProfiles);

        var visibleProfiles = registry.Profiles
            .Where(profile => IsPromptVisible(profile, registry, runtimeSet, probe))
            .OrderBy(profile => IsDefaultProfile(profile) ? 0 : 1)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (visibleProfiles.Length == 0)
            return null;

        var lines = new List<string>
        {
            "## Available SubAgent Profiles",
            "",
            "Do not guess profile names that are not listed here.",
            $"Default profile: `{SubAgentCoordinator.DefaultProfileName}`",
            ""
        };

        foreach (var profile in visibleProfiles)
        {
            var description = DescribeProfile(profile);
            if (string.Equals(profile.WorkingDirectoryMode, "specified", StringComparison.OrdinalIgnoreCase))
                description += " Requires `workingDirectory`.";

            if (IsDefaultProfile(profile))
                description += " This is the default profile.";

            lines.Add($"- `{profile.Name}`: {description}");
        }

        lines.Add("");
        lines.Add("External CLI profiles provide stage-level progress and a final result, not native tool-by-tool execution details.");
        lines.Add("When the workspace enables external CLI session resume, reusing the same profile and label continues the prior external CLI session when the profile supports it.");

        return string.Join(Environment.NewLine, lines);
    }

    private static bool IsPromptVisible(
        SubAgentProfile profile,
        SubAgentProfileRegistry registry,
        IReadOnlySet<string> runtimeTypes,
        Func<string, bool> binaryAvailabilityProbe)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.Runtime))
            return false;

        if (!runtimeTypes.Contains(profile.Runtime))
            return false;

        if (registry.IsTemplateProfile(profile.Name))
            return false;

        if (!registry.IsEnabled(profile.Name))
            return false;

        if (string.Equals(profile.Runtime, CliOneshotRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(profile.Bin))
                return false;

            if (!binaryAvailabilityProbe(profile.Bin))
                return false;
        }

        return registry.GetValidationWarningsForProfile(profile.Name).Count == 0;
    }

    private static bool IsDefaultProfile(SubAgentProfile profile)
        => string.Equals(profile.Name, SubAgentCoordinator.DefaultProfileName, StringComparison.OrdinalIgnoreCase);

    private static string DescribeProfile(SubAgentProfile profile)
    {
        if (string.Equals(profile.Runtime, NativeSubAgentRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
            return "Native DotCraft subagent profile with fine-grained tool execution details.";

        if (string.Equals(profile.Runtime, CliOneshotRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
        {
            if (profile.PermissionModeMapping is { Count: > 0 })
            {
                var modes = string.Join(
                    ", ",
                    profile.PermissionModeMapping.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
                return $"External CLI profile launched as a short-lived subprocess per task. Permission modes: {modes}.";
            }

            return "External CLI profile launched as a short-lived subprocess per task.";
        }

        return $"Subagent profile using runtime `{profile.Runtime}`.";
    }
}
