using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Agents;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public class SubAgentProfileConfigTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void AppConfig_Deserializes_SubAgentProfiles_ObjectMap_IntoStrongTypedList()
    {
        const string json = """
        {
          "SubAgentProfiles": {
            "native": {
              "runtime": "native",
              "workingDirectoryMode": "workspace"
            },
            "codex-cli": {
              "runtime": "cli-oneshot",
              "bin": "codex",
              "args": ["exec", "--json"],
              "env": {
                "CODEX_ENV": "test"
              },
              "envPassthrough": ["CURSOR_API_KEY", "HTTPS_PROXY"],
              "workingDirectoryMode": "specified",
              "supportsStreaming": true,
              "inputMode": "stdin",
              "supportsResume": true,
              "resumeArgTemplate": "resume {sessionId}",
              "resumeSessionIdRegex": "thread_id=(.+)",
              "outputFormat": "json",
              "outputJsonPath": "result.message",
              "outputInputTokensJsonPath": "usage.input",
              "outputOutputTokensJsonPath": "usage.output",
              "outputTotalTokensJsonPath": "usage.total",
              "outputFileArgTemplate": "--output-file {path}",
              "readOutputFile": true,
              "deleteOutputFileAfterRead": true,
              "maxOutputBytes": 2048
            }
          }
        }
        """;

        var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);

        Assert.NotNull(config);
        Assert.Equal(2, config!.SubAgentProfiles.Count);

        var native = Assert.Single(config.SubAgentProfiles, p => p.Name == "native");
        Assert.Equal("native", native.Runtime);
        Assert.Equal("workspace", native.WorkingDirectoryMode);

        var codex = Assert.Single(config.SubAgentProfiles, p => p.Name == "codex-cli");
        Assert.Equal("cli-oneshot", codex.Runtime);
        Assert.Equal(["exec", "--json"], codex.Args);
        Assert.Equal("test", codex.Env!["CODEX_ENV"]);
        Assert.Equal(["CURSOR_API_KEY", "HTTPS_PROXY"], codex.EnvPassthrough);
        Assert.Equal("stdin", codex.InputMode);
        Assert.Equal("usage.total", codex.OutputTotalTokensJsonPath);
        Assert.True(codex.DeleteOutputFileAfterRead);
        Assert.Equal(2048, codex.MaxOutputBytes);
    }

    [Fact]
    public void AppConfig_Serializes_SubAgentProfiles_AsObjectMap_KeyedByName()
    {
        var config = new AppConfig
        {
            SubAgentProfiles =
            [
                new SubAgentProfile
                {
                    Name = "codex-cli",
                    Runtime = "cli-oneshot",
                    Bin = "codex",
                    Args = ["exec", "--json"],
                    EnvPassthrough = ["CURSOR_API_KEY", "HTTPS_PROXY"],
                    WorkingDirectoryMode = "workspace",
                    InputMode = "stdin",
                    SupportsResume = true,
                    ResumeArgTemplate = "resume {sessionId}",
                    ResumeSessionIdJsonPath = "session_id",
                    OutputJsonPath = "result",
                    OutputInputTokensJsonPath = "usage.input",
                    OutputOutputTokensJsonPath = "usage.output",
                    OutputTotalTokensJsonPath = "usage.total",
                    OutputFileArgTemplate = "--output-file {path}",
                    ReadOutputFile = true,
                    DeleteOutputFileAfterRead = true,
                    MaxOutputBytes = 4096,
                    SanitizationRules = new JsonObject { ["stripAnsi"] = true }
                },
                new SubAgentProfile
                {
                    Name = "native",
                    Runtime = "native",
                    WorkingDirectoryMode = "workspace"
                }
            ]
        };

        var node = JsonSerializer.SerializeToNode(config, SerializerOptions) as JsonObject;
        Assert.NotNull(node);

        var profiles = Assert.IsType<JsonObject>(node!["SubAgentProfiles"]);
        Assert.NotNull(profiles["codex-cli"]);
        Assert.NotNull(profiles["native"]);

        var codex = Assert.IsType<JsonObject>(profiles["codex-cli"]);
        Assert.False(codex.ContainsKey("Name"));
        Assert.Equal("codex", codex["Bin"]?.GetValue<string>());
        Assert.Equal("session_id", codex["ResumeSessionIdJsonPath"]?.GetValue<string>());
        Assert.Equal(2, Assert.IsType<JsonArray>(codex["EnvPassthrough"]).Count);
        Assert.Equal(4096, codex["MaxOutputBytes"]?.GetValue<int>());
    }

    [Fact]
    public void SubAgentProfiles_CaseInsensitiveDuplicateKeys_LastEntryWinsInMap()
    {
        const string json = """
        {
          "SubAgentProfiles": {
            "Codex-CLI": {
              "runtime": "cli-persistent",
              "workingDirectoryMode": "workspace"
            },
            "codex-cli": {
              "runtime": "cli-oneshot",
              "bin": "codex"
            }
          }
        }
        """;

        var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);

        Assert.NotNull(config);
        Assert.Equal(2, config!.SubAgentProfiles.Count);

        var map = SubAgentProfileMap.ToDictionaryByNameLastWins(config.SubAgentProfiles);
        Assert.Single(map);
        var entry = map["codex-cli"];
        Assert.Equal("cli-oneshot", entry.Runtime);
        Assert.Equal("codex", entry.Bin);
    }

    [Fact]
    public void ValidateProfiles_ReportsCliOneshotFieldWarnings()
    {
        var warnings = SubAgentProfileRegistry.ValidateProfiles(
            [
                new SubAgentProfile
                {
                    Name = "broken-cli",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "workspace",
                    InputMode = "arg-template",
                    OutputFormat = "json",
                    SupportsResume = true,
                    ReadOutputFile = true
                }
            ],
            SubAgentProfileRegistry.KnownRuntimeTypes);

        Assert.Contains(warnings, w => w.Contains("missing required field 'bin'", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("inputArgTemplate", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("outputJsonPath", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("outputFileArgTemplate", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("resumeArgTemplate", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("resumeSessionIdJsonPath", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProfiles_SkipsTemplateWarningsOnlyForTheBuiltInCustomCliTemplate()
    {
        var profiles = SubAgentProfileRegistry.CreateBuiltInProfiles();
        var warnings = SubAgentProfileRegistry.ValidateProfiles(
            profiles,
            SubAgentProfileRegistry.KnownRuntimeTypes,
            SubAgentProfileRegistry.CreateBuiltInTemplateProfileNames());

        Assert.DoesNotContain(
            warnings,
            w => w.Contains("custom-cli-oneshot", StringComparison.Ordinal)
                 && w.Contains("missing required field 'bin'", StringComparison.Ordinal));

        var configuredWarnings = SubAgentProfileRegistry.ValidateProfiles(
            [
                new SubAgentProfile
                {
                    Name = "custom-cli-oneshot",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "workspace",
                    InputMode = "arg"
                }
            ],
            SubAgentProfileRegistry.KnownRuntimeTypes);

        Assert.Contains(
            configuredWarnings,
            w => w.Contains("custom-cli-oneshot", StringComparison.Ordinal)
                 && w.Contains("missing required field 'bin'", StringComparison.Ordinal));
    }

    [Fact]
    public void GetHiddenBuiltInReasons_ReturnsNoteWhenBuiltInBinaryProbeFails()
    {
        var registry = new SubAgentProfileRegistry(
            configuredProfiles: null,
            SubAgentProfileRegistry.CreateBuiltInProfiles(),
            SubAgentProfileRegistry.KnownRuntimeTypes);

        var notes = registry.GetHiddenBuiltInReasons(
            bin => !string.Equals(bin, "cursor-agent", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            notes,
            note => note.Contains("'cursor-cli'", StringComparison.Ordinal)
                    && note.Contains("'cursor-agent'", StringComparison.Ordinal));
    }
}
