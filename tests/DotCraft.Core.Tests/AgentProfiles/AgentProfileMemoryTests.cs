using System.Security.Cryptography;
using System.Text;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AgentProfileMemoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"agent_profile_memory_{Guid.NewGuid():N}");
    private readonly string _craft;

    public AgentProfileMemoryTests()
    {
        _craft = Path.Combine(_root, "workspace", ".craft");
        Directory.CreateDirectory(Path.Combine(_craft, "agents"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void OwnProfile_ResolvesThreadIntoItsOwnStore()
    {
        var store = new AgentProfileStore(_craft);
        store.Upsert("Night Shift", AgentProfileSources.Workspace, Profile("Night Shift", "memory: own"));

        Assert.Equal(OwnStore("Night Shift"), StoreOf(store, "Night Shift"));
    }

    [Theory]
    [InlineData("memory: shared")]
    [InlineData("")]
    public void SharedProfile_ResolvesThreadIntoWorkspaceStore(string memory)
    {
        var store = new AgentProfileStore(_craft);
        store.Upsert("helper", AgentProfileSources.Workspace, Profile("helper", memory));

        Assert.Equal(MemoryStore.DirectoryOf(_craft), StoreOf(store, "helper"));
    }

    [Fact]
    public void UnknownMemoryValue_IsRejected()
    {
        var result = new AgentProfileStore(_craft).ValidateRaw(Profile("helper", "memory: private"));

        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "InvalidPolicyValue");
    }

    [Fact]
    public void CreatorScope_TakesPrecedenceOverOwnProfile()
    {
        var store = new AgentProfileStore(_craft);
        store.Upsert("keeper", AgentProfileSources.Workspace, Profile("keeper", "memory: own"));

        var config = store.ResolveThreadStartConfiguration(
            new ThreadConfiguration { AgentProfileId = "keeper", MemoryScope = "nightly" },
            new AppConfig());

        Assert.Equal(Path.Combine(_craft, "scopes", "nightly", "memory"), MemoryScopes.StoreDirectory(config, _craft));
    }

    [Fact]
    public void RenameAndRemove_LeaveExistingStoresInPlace()
    {
        var store = new AgentProfileStore(_craft);
        store.Upsert("Scout", AgentProfileSources.Workspace, Profile("Scout", "memory: own"));
        var scoutFact = WriteFact(StoreOf(store, "Scout"));

        store.Upsert("Ranger", AgentProfileSources.Workspace, Profile("Ranger", "memory: own"), previousName: "Scout");
        var rangerStore = StoreOf(store, "Ranger");
        var rangerFact = WriteFact(rangerStore);
        store.Remove("Ranger", AgentProfileSources.Workspace);

        Assert.Equal(OwnStore("Ranger"), rangerStore);
        Assert.NotEqual(OwnStore("Scout"), rangerStore);
        Assert.True(File.Exists(scoutFact));
        Assert.True(File.Exists(rangerFact));
    }

    private string StoreOf(AgentProfileStore store, string profile) =>
        MemoryScopes.StoreDirectory(
            store.ResolveThreadStartConfiguration(new ThreadConfiguration { AgentProfileId = profile }, new AppConfig()),
            _craft);

    private string OwnStore(string profile) =>
        Path.Combine(
            _craft,
            "scopes",
            "agent-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile))).ToLowerInvariant(),
            "memory");

    private static string WriteFact(string storeDirectory)
    {
        Directory.CreateDirectory(storeDirectory);
        var path = Path.Combine(storeDirectory, "fact.md");
        File.WriteAllText(path, "A fact.");
        return path;
    }

    private static string Profile(string name, string memory) =>
        $"""
---
name: {name}
description: Profile {name}
{memory}
---

Instructions for {name}.
""";
}
