using System.Text.Json;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;

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
        Assert.False(document.RootElement.TryGetProperty("Model", out _));
    }

    [Theory]
    [InlineData("gpt-5")]
    [InlineData("gpt-5-codex")]
    public void BindProviderToOAuth_IgnoresAndRemovesLegacyRootModel(string legacyModel)
    {
        var configPath = Path.Combine(_tempRoot, ".craft", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, $$"""{"Model":"{{legacyModel}}"}""");

        OpenAIAuthBindingPersistence.BindProviderToOAuth("openai", Status(), configPath);

        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        Assert.Equal(
            ModelProviderDefaults.DefaultChatGptCodexModel,
            document.RootElement.GetProperty("ProviderPreferences").GetProperty("openai").GetProperty("Model").GetString());
        Assert.False(document.RootElement.TryGetProperty("Model", out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static OpenAIAuthStatus Status() => new(
        true,
        "acct_test",
        "pro",
        "test@example.com",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddHours(1));
}
