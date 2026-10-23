using DotCraft.Configuration;
using DotCraft.Context;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class SubAgentProfilePromptSectionBuilderTests
{
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
    public void Build_HidesDisabledProfilesButNeverTheProtectedDefault()
    {
        var section = SubAgentProfilePromptSectionBuilder.Build(
            configuredProfiles: null,
            disabledProfiles: ["cursor-cli", "native"],
            binaryAvailabilityProbe: _ => true);

        Assert.NotNull(section);
        Assert.DoesNotContain("`cursor-cli`", section, StringComparison.Ordinal);
        Assert.Contains("`codex-cli`", section, StringComparison.Ordinal);
        Assert.Contains("`native`", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NativeOnlyOmitsExternalCliGuidance()
    {
        var section = SubAgentProfilePromptSectionBuilder.Build(
            configuredProfiles: null,
            binaryAvailabilityProbe: _ => false,
            enableExternalCliSessionResume: true);

        Assert.NotNull(section);
        Assert.Contains("`native`", section, StringComparison.Ordinal);
        Assert.DoesNotContain("External CLI", section, StringComparison.Ordinal);
        Assert.DoesNotContain("`agentNickname`", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DescribesResumeOnlyWhenEnabled()
    {
        foreach (var enabled in new[] { true, false })
        {
            var section = SubAgentProfilePromptSectionBuilder.Build(
                configuredProfiles: null,
                binaryAvailabilityProbe: _ => true,
                enableExternalCliSessionResume: enabled);

            Assert.NotNull(section);
            Assert.Equal(enabled, section.Contains("`agentNickname`", StringComparison.Ordinal));
        }
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
