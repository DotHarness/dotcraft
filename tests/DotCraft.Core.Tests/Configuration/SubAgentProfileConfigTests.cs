using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Tests.Configuration;

public class SubAgentProfileConfigTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void SubAgentProfilesSection_HasRootKey_ItemFields_AndExpectedTypes()
    {
        var schema = ConfigSchemaBuilder.BuildAll([typeof(SubAgentProfile)]);
        var section = Assert.Single(schema);

        Assert.Equal("SubAgentProfiles", section.RootKey);
        Assert.NotNull(section.ItemFields);
        Assert.NotEmpty(section.ItemFields);
        Assert.Empty(section.Fields);

        var byKey = section.ItemFields!.ToDictionary(f => f.Key, f => f);
        Assert.Equal("text", byKey["Name"].Type);
        Assert.Equal("text", byKey["Runtime"].Type);
        Assert.Equal("text", byKey["Bin"].Type);
        Assert.Equal("stringList", byKey["Args"].Type);
        Assert.Equal("keyValueMap", byKey["Env"].Type);
        Assert.Equal("stringList", byKey["EnvPassthrough"].Type);
        Assert.Equal("select", byKey["WorkingDirectoryMode"].Type);
        Assert.Contains("workspace", byKey["WorkingDirectoryMode"].Options!);
        Assert.Contains("specified", byKey["WorkingDirectoryMode"].Options!);
        Assert.Equal("select", byKey["InputMode"].Type);
        Assert.Contains("stdin", byKey["InputMode"].Options!);
        Assert.Equal("text", byKey["InputArgTemplate"].Type);
        Assert.Equal("text", byKey["InputEnvKey"].Type);
        Assert.Equal("text", byKey["ResumeArgTemplate"].Type);
        Assert.Equal("text", byKey["ResumeSessionIdJsonPath"].Type);
        Assert.Equal("text", byKey["ResumeSessionIdRegex"].Type);
        Assert.Equal("text", byKey["OutputJsonPath"].Type);
        Assert.Equal("text", byKey["OutputInputTokensJsonPath"].Type);
        Assert.Equal("text", byKey["OutputOutputTokensJsonPath"].Type);
        Assert.Equal("text", byKey["OutputTotalTokensJsonPath"].Type);
        Assert.Equal("text", byKey["OutputFileArgTemplate"].Type);
        Assert.Equal("json", byKey["SanitizationRules"].Type);
    }

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
        Assert.Equal("codex", codex.Bin);
        Assert.Equal(["exec", "--json"], codex.Args);
        Assert.Equal("test", codex.Env!["CODEX_ENV"]);
        Assert.Equal(["CURSOR_API_KEY", "HTTPS_PROXY"], codex.EnvPassthrough);
        Assert.Equal("specified", codex.WorkingDirectoryMode);
        Assert.True(codex.SupportsStreaming);
        Assert.True(codex.SupportsResume);
        Assert.Equal("resume {sessionId}", codex.ResumeArgTemplate);
        Assert.Equal("thread_id=(.+)", codex.ResumeSessionIdRegex);
        Assert.Equal("stdin", codex.InputMode);
        Assert.Equal("json", codex.OutputFormat);
        Assert.Equal("result.message", codex.OutputJsonPath);
        Assert.Equal("usage.input", codex.OutputInputTokensJsonPath);
        Assert.Equal("usage.output", codex.OutputOutputTokensJsonPath);
        Assert.Equal("usage.total", codex.OutputTotalTokensJsonPath);
        Assert.Equal("--output-file {path}", codex.OutputFileArgTemplate);
        Assert.True(codex.ReadOutputFile);
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
        Assert.Equal("stdin", codex["InputMode"]?.GetValue<string>());
        Assert.True(codex["SupportsResume"]?.GetValue<bool>());
        Assert.Equal("resume {sessionId}", codex["ResumeArgTemplate"]?.GetValue<string>());
        Assert.Equal("session_id", codex["ResumeSessionIdJsonPath"]?.GetValue<string>());
        var envPassthrough = Assert.IsType<JsonArray>(codex["EnvPassthrough"]);
        Assert.Equal("CURSOR_API_KEY", envPassthrough[0]?.GetValue<string>());
        Assert.Equal("HTTPS_PROXY", envPassthrough[1]?.GetValue<string>());
        Assert.Equal("result", codex["OutputJsonPath"]?.GetValue<string>());
        Assert.Equal("usage.input", codex["OutputInputTokensJsonPath"]?.GetValue<string>());
        Assert.Equal("usage.output", codex["OutputOutputTokensJsonPath"]?.GetValue<string>());
        Assert.Equal("usage.total", codex["OutputTotalTokensJsonPath"]?.GetValue<string>());
        Assert.Equal("--output-file {path}", codex["OutputFileArgTemplate"]?.GetValue<string>());
        Assert.True(codex["ReadOutputFile"]?.GetValue<bool>());
        Assert.True(codex["DeleteOutputFileAfterRead"]?.GetValue<bool>());
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
    public void BuiltInProfiles_IncludeCodexCursorAndCustomCliProfiles()
    {
        var profiles = SubAgentProfileRegistry.CreateBuiltInProfiles()
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var codex = profiles["codex-cli"];
        Assert.Equal("cli-oneshot", codex.Runtime);
        Assert.Equal("codex", codex.Bin);
        Assert.Equal("arg", codex.InputMode);
        Assert.Equal("prompt", codex.TrustLevel);
        Assert.True(codex.ReadOutputFile);
        Assert.True(codex.SupportsResume);
        Assert.Equal("resume {sessionId}", codex.ResumeArgTemplate);
        Assert.Equal("\"thread_id\"\\s*:\\s*\"(?<sessionId>[^\"]+)\"", codex.ResumeSessionIdRegex);
        Assert.Equal("--skip-git-repo-check --json --output-last-message {path}", codex.OutputFileArgTemplate);
        Assert.Equal(
            "--sandbox read-only",
            codex.PermissionModeMapping![SubAgentApprovalModeResolver.InteractiveMode]);
        Assert.Equal(
            "--dangerously-bypass-approvals-and-sandbox",
            codex.PermissionModeMapping[SubAgentApprovalModeResolver.AutoApproveMode]);

        var cursor = profiles["cursor-cli"];
        Assert.Equal("cli-oneshot", cursor.Runtime);
        Assert.Equal("cursor-agent", cursor.Bin);
        Assert.Equal("json", cursor.OutputFormat);
        Assert.Equal("result", cursor.OutputJsonPath);
        Assert.True(cursor.SupportsResume);
        Assert.Equal("--resume {sessionId}", cursor.ResumeArgTemplate);
        Assert.Equal("session_id", cursor.ResumeSessionIdJsonPath);
        Assert.Equal("prompt", cursor.TrustLevel);
        Assert.Equal("--mode ask --trust --approve-mcps", cursor.PermissionModeMapping![SubAgentApprovalModeResolver.InteractiveMode]);

        var custom = profiles["custom-cli-oneshot"];
        Assert.Equal("cli-oneshot", custom.Runtime);
        Assert.Equal("arg", custom.InputMode);
        Assert.Equal("text", custom.OutputFormat);
        Assert.Equal(120, custom.Timeout);
        Assert.Equal("restricted", custom.TrustLevel);

        var native = profiles["native"];
        Assert.Equal("trusted", native.TrustLevel);
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
    public void ValidateProfiles_SkipsTemplateWarningsForBuiltInCustomCliTemplate()
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
    }

    [Fact]
    public void ValidateProfiles_ReportsWarningsForConfiguredCustomTemplateWithoutBin()
    {
        var warnings = SubAgentProfileRegistry.ValidateProfiles(
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
            warnings,
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
