using DotCraft.Configuration;
using DotCraft.Context;

namespace DotCraft.Tests.Context;

public sealed class SubAgentProfilePromptSectionBuilderTests
{
    [Fact]
    public void Build_ConfiguredProfilesOverrideBuiltIns_AndCanRequireWorkingDirectory()
    {
        var section = SubAgentProfilePromptSectionBuilder.Build(
            [
                new SubAgentProfile
                {
                    Name = "cursor-cli",
                    Runtime = "cli-oneshot",
                    Bin = "cursor-agent",
                    WorkingDirectoryMode = "specified",
                    InputMode = "arg",
                    OutputFormat = "json",
                    OutputJsonPath = "result"
                }
            ],
            binaryAvailabilityProbe: _ => true);

        Assert.NotNull(section);
        Assert.Contains("`cursor-cli`", section);
        Assert.Contains("Requires `workingDirectory`.", section);
    }

    [Fact]
    public void Build_ShowsCustomCliTemplateWhenUserOverridesWithValidBin()
    {
        var section = SubAgentProfilePromptSectionBuilder.Build(
            [
                new SubAgentProfile
                {
                    Name = "custom-cli-oneshot",
                    Runtime = "cli-oneshot",
                    Bin = "custom-agent",
                    WorkingDirectoryMode = "workspace",
                    InputMode = "arg",
                    OutputFormat = "text"
                }
            ],
            binaryAvailabilityProbe: _ => true);

        Assert.NotNull(section);
        Assert.Contains("`custom-cli-oneshot`", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OmitsProfilesWithBlockingConfigurationProblems()
    {
        var section = SubAgentProfilePromptSectionBuilder.Build(
            [
                new SubAgentProfile
                {
                    Name = "broken-runtime",
                    Runtime = "missing-runtime"
                },
                new SubAgentProfile
                {
                    Name = "broken-cli",
                    Runtime = "cli-oneshot",
                    WorkingDirectoryMode = "workspace",
                    InputMode = "arg-template"
                }
            ],
            binaryAvailabilityProbe: _ => true);

        Assert.NotNull(section);
        Assert.DoesNotContain("broken-runtime", section, StringComparison.Ordinal);
        Assert.DoesNotContain("broken-cli", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotIncludeDiagnosticNoise()
    {
        var section = SubAgentProfilePromptSectionBuilder.Build(
            [
                new SubAgentProfile
                {
                    Name = "quiet-cli",
                    Runtime = "cli-oneshot",
                    Bin = @"C:\tools\quiet-cli.cmd",
                    WorkingDirectoryMode = "workspace",
                    InputMode = "arg",
                    OutputFormat = "text"
                }
            ],
            binaryAvailabilityProbe: _ => true);

        Assert.NotNull(section);
        Assert.DoesNotContain(@"C:\tools\quiet-cli.cmd", section, StringComparison.Ordinal);
        Assert.DoesNotContain("resolved ->", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("warning", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_HidesCliProfileWhenBinaryProbeFails()
    {
        var section = SubAgentProfilePromptSectionBuilder.Build(
            configuredProfiles: null,
            binaryAvailabilityProbe: bin => !string.Equals(bin, "cursor-agent", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(section);
        Assert.DoesNotContain("`cursor-cli`", section, StringComparison.Ordinal);
        Assert.Contains("`codex-cli`", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HidesDisabledProfiles()
    {
        var section = SubAgentProfilePromptSectionBuilder.Build(
            configuredProfiles: null,
            disabledProfiles: ["cursor-cli"],
            binaryAvailabilityProbe: _ => true);

        Assert.NotNull(section);
        Assert.DoesNotContain("`cursor-cli`", section, StringComparison.Ordinal);
        Assert.Contains("`codex-cli`", section, StringComparison.Ordinal);
        Assert.Contains("`native`", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotDisableProtectedDefaultProfile()
    {
        var section = SubAgentProfilePromptSectionBuilder.Build(
            configuredProfiles: null,
            disabledProfiles: ["native"],
            binaryAvailabilityProbe: _ => true);

        Assert.NotNull(section);
        Assert.Contains("`native`", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ShowsCliProfileWhenBinaryProbeSucceeds()
    {
        var section = SubAgentProfilePromptSectionBuilder.Build(
            [
                new SubAgentProfile
                {
                    Name = "cursor-cli",
                    Runtime = "cli-oneshot",
                    Bin = "cursor-agent",
                    WorkingDirectoryMode = "workspace",
                    InputMode = "arg",
                    OutputFormat = "json",
                    OutputJsonPath = "result"
                }
            ],
            binaryAvailabilityProbe: _ => true);

        Assert.NotNull(section);
        Assert.Contains("`cursor-cli`", section, StringComparison.Ordinal);
    }
}
