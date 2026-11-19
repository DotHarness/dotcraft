using DotCraft.Configuration;
using DotCraft.Memory;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ConfigurationServiceMemoryPolicyTests
{
    [Theory]
    [InlineData("Memory.Types", "[]")]
    [InlineData("Memory.Types", """[{ "Name": "fact", "Description": "One fact." }, { "Name": "fact", "Description": "Another." }]""")]
    [InlineData("Memory.Types", """[{ "Name": "Not A Slug", "Description": "One fact." }]""")]
    [InlineData("Memory.Types", """[{ "Name": "fact", "Description": "Two\nlines." }]""")]
    [InlineData("Memory.SaveGuidance", "\"  \"")]
    [InlineData("Memory.ExtractionInterval", "0")]
    public async Task InvalidPolicyWrites_AreRejectedAndLeaveTheFileUnchanged(string keyPath, string valueJson)
    {
        using var fixture = Create();
        var before = File.ReadAllBytes(fixture.UserPath);

        var error = await Assert.ThrowsAsync<ConfigWriteException>(() => fixture.WriteAsync(keyPath, valueJson));

        Assert.Equal(ConfigWriteErrorCode.ConfigValidationError, error.Code);
        Assert.Equal(before, File.ReadAllBytes(fixture.UserPath));
    }

    [Fact]
    public async Task PolicyWrites_ApplyToTheRuntimeAndRemovingThemRestoresTheDefaults()
    {
        using var fixture = Create();

        await fixture.WriteAsync("Memory.Types", """[{ "Name": "fact", "Description": "One self-contained fact." }]""");
        await fixture.WriteAsync("Memory.SaveGuidance", "\"Save only facts.\"");

        var policy = MemoryPolicy.From(fixture.Monitor.Current.Memory);
        Assert.Equal(["fact"], policy.TypeNames);
        Assert.Equal("Save only facts.", policy.SaveGuidance);

        await fixture.WriteAsync("Memory.Types", "null");
        await fixture.WriteAsync("Memory.SaveGuidance", "null");

        policy = MemoryPolicy.From(fixture.Monitor.Current.Memory);
        Assert.Equal(MemoryPolicy.Default.TypeNames, policy.TypeNames);
        Assert.Equal(MemoryPolicyDefaults.SaveGuidance, policy.SaveGuidance);
    }

    private static ConfigurationServiceFixture Create()
    {
        var fixture = new ConfigurationServiceFixture(user: "{}");
        ConfigurationSubsystems.Register(fixture.Service, fixture.Monitor, null, null, null, null, null);
        return fixture;
    }
}
