using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Configuration;

public sealed class AppConfigProviderPreferencesTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-provider-preferences-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void LoadWithGlobalFallback_WorkspaceRecordAtomicallyOverridesPersonalRecord()
    {
        Directory.CreateDirectory(tempRoot);
        var globalPath = Path.Combine(tempRoot, "global.json");
        var workspacePath = Path.Combine(tempRoot, "workspace.json");
        File.WriteAllText(globalPath, """
            {
              "ProviderId": "openai",
              "ProviderPreferences": {
                "openai": {
                  "Model": "personal-model",
                  "Reasoning": { "Enabled": true, "Effort": "High", "Output": "Summary" },
                  "Speed": "Fast",
                  "ContextWindow": { "Mode": "Max" }
                },
                "anthropic": {
                  "Model": "claude-model",
                  "Reasoning": { "Enabled": false, "Effort": "Medium", "Output": "Full" },
                  "Speed": "Standard",
                  "ContextWindow": { "Mode": "Default" }
                }
              }
            }
            """);
        File.WriteAllText(workspacePath, """
            {
              "ProviderPreferences": {
                "OPENAI": {
                  "Model": "workspace-model",
                  "Reasoning": { "Enabled": false, "Effort": "Low", "Output": "Full" },
                  "Speed": "Standard",
                  "ContextWindow": { "Mode": "Default" }
                }
              }
            }
            """);

        var config = AppConfig.LoadWithGlobalFallback(workspacePath, globalPath);

        var openAi = ModelPreferenceRules.Find(config.ProviderPreferences, "openai");
        Assert.NotNull(openAi);
        Assert.Equal("workspace-model", openAi.Model);
        Assert.False(openAi.Reasoning.Enabled);
        Assert.Equal(ReasoningEffort.Low, openAi.Reasoning.Effort);
        Assert.Equal(ReasoningOutput.Full, openAi.Reasoning.Output);
        Assert.Equal(InferenceSpeed.Standard, openAi.Speed);
        Assert.Equal(ContextWindowMode.Default, openAi.ContextWindow.Mode);
        Assert.Equal("claude-model", ModelPreferenceRules.Find(config.ProviderPreferences, "ANTHROPIC")?.Model);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
        catch
        {
            // Best effort cleanup for Windows test runners that still hold file handles.
        }
    }
}
