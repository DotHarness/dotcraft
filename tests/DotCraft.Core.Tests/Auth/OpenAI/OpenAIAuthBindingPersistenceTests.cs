using DotCraft.Agents;
using System.Text.Json;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Auth.OpenAI;

public sealed class OpenAIAuthBindingPersistenceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"openai_auth_binding_{Guid.NewGuid():N}");

    public OpenAIAuthBindingPersistenceTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void BindProviderToOAuth_UsesCurrentCodexDefaultModel()
    {
        var configPath = Path.Combine(_tempRoot, ".craft", "config.json");

        OpenAIAuthBindingPersistence.BindProviderToOAuth("openai", Status(), configPath);

        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        Assert.Equal(
            ModelProviderDefaults.DefaultChatGptCodexModel,
            document.RootElement.GetProperty("ProviderPreferences").GetProperty("openai").GetProperty("Model").GetString());
    }

    [Fact]
    public void BindProviderToOAuth_PreservesUnrelatedSettings()
    {
        var configPath = Path.Combine(_tempRoot, ".craft", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, """{"CustomSettings":{"theme":"dark"}}""");

        OpenAIAuthBindingPersistence.BindProviderToOAuth("openai", Status(), configPath);

        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        Assert.Equal(
            ModelProviderDefaults.DefaultChatGptCodexModel,
            document.RootElement.GetProperty("ProviderPreferences").GetProperty("openai").GetProperty("Model").GetString());
        Assert.Equal(
            "dark",
            document.RootElement.GetProperty("CustomSettings").GetProperty("theme").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static ProviderAuthenticationStatus Status() => new(
        IsAuthenticated: true,
        AccountId: "acct_test",
        PlanType: "pro",
        Email: "test@example.com",
        LastRefresh: DateTimeOffset.UtcNow,
        AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddHours(1));
}
