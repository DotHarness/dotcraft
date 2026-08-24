using DotCraft.Oratorio.Integrations;

namespace DotCraft.Oratorio.Tests;

public sealed class OratorioDotCraftBindingStoreTests
{
    [Fact]
    public void PersistsAuthoritiesByRuntimeIdentityWithoutCredentialReuse()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"oratorio-binding-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new OratorioDotCraftBindingStore(Path.Combine(directory, "bindings.json"));
            var first = Create("local:C:\\one", "C:\\one", "credential-one");
            var second = Create("local:C:\\two", "C:\\two", "credential-two");

            store.Save(first);
            store.Save(second);

            Assert.True(store.TryLoad(first.AppServerIdentity, out var loadedFirst));
            Assert.True(store.TryLoad(second.AppServerIdentity, out var loadedSecond));
            Assert.Equal("credential-one", loadedFirst.ProtectedCredential);
            Assert.Equal("credential-two", loadedSecond.ProtectedCredential);
            Assert.Equal(2, store.LoadAll().Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static OratorioDotCraftBinding Create(string identity, string workspace, string credential) =>
        new(identity, workspace, "ws://127.0.0.1:9100/ws", "com.dotharness.oratorio",
            Guid.NewGuid().ToString("N"), credential, DateTimeOffset.UtcNow.AddDays(30), "Oratorio", []);
}
