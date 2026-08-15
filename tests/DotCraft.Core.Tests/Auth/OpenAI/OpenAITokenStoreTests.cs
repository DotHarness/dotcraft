using DotCraft.Auth.OpenAI;
using Xunit;

namespace DotCraft.Tests.Auth.OpenAI;

public sealed class OpenAITokenStoreTests : IDisposable
{
    private readonly string _tempDir;

    public OpenAITokenStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dotcraft-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public void LoadReturnsNullWhenFileMissing()
    {
        var store = new OpenAITokenStore(_tempDir);
        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveAndLoadRoundTripsAllTokenFields()
    {
        var store = new OpenAITokenStore(_tempDir);
        var auth = new AuthDotJson
        {
            OpenAIApiKey = "sk-test",
            Tokens = new OpenAITokenSet
            {
                IdToken = "id.token.value",
                AccessToken = "access.token.value",
                RefreshToken = "refresh.token.value",
                AccountId = "acct_test"
            },
            LastRefresh = DateTimeOffset.UtcNow
        };
        store.Save(auth);

        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.Tokens);
        Assert.Equal("id.token.value", loaded.Tokens!.IdToken);
        Assert.Equal("access.token.value", loaded.Tokens.AccessToken);
        Assert.Equal("refresh.token.value", loaded.Tokens.RefreshToken);
        Assert.Equal("acct_test", loaded.Tokens.AccountId);
        Assert.Equal("sk-test", loaded.OpenAIApiKey);
    }

    [Fact]
    public void DeleteRemovesFile()
    {
        var store = new OpenAITokenStore(_tempDir);
        store.Save(new AuthDotJson { Tokens = new OpenAITokenSet { AccessToken = "a" } });
        Assert.True(File.Exists(store.FilePath!));
        store.Delete();
        Assert.False(File.Exists(store.FilePath!));
    }

    [Fact]
    public void LoadGracefullyReturnsNullOnCorruptJson()
    {
        var store = new OpenAITokenStore(_tempDir);
        File.WriteAllText(store.FilePath!, "{ not json");
        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveCreatesParentDirectory()
    {
        var nested = Path.Combine(_tempDir, "nested", "dir");
        var store = new OpenAITokenStore(nested);
        store.Save(new AuthDotJson { Tokens = new OpenAITokenSet { AccessToken = "a" } });
        Assert.True(File.Exists(Path.Combine(nested, "auth.json")));
    }

    [Fact]
    public void MissingUserData_DisablesReadsAndRejectsPersistence()
    {
        var store = new OpenAITokenStore();

        Assert.Null(store.FilePath);
        Assert.Null(store.Load());
        Assert.Throws<InvalidOperationException>(() => store.Save(new AuthDotJson()));
        Assert.Throws<InvalidOperationException>(() => store.Delete());
    }
}
