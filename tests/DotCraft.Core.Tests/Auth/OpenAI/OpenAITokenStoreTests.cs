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
    public void LoadReturnsNullWhenFileMissingCorruptOrWithoutUserData()
    {
        var store = new OpenAITokenStore(_tempDir);
        Assert.Null(store.Load());

        File.WriteAllText(store.FilePath!, "{ not json");
        Assert.Null(store.Load());

        var withoutUserData = new OpenAITokenStore();
        Assert.Null(withoutUserData.FilePath);
        Assert.Null(withoutUserData.Load());
        Assert.Throws<InvalidOperationException>(() => withoutUserData.Save(new AuthDotJson()));
        Assert.Throws<InvalidOperationException>(() => withoutUserData.Delete());
    }

    [Fact]
    public void SaveLoadAndDeleteRoundTripAllTokenFieldsInANewDirectory()
    {
        var nested = Path.Combine(_tempDir, "nested", "dir");
        var store = new OpenAITokenStore(nested);
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
        Assert.Equal(Path.Combine(nested, "auth.json"), store.FilePath);

        store.Delete();
        Assert.False(File.Exists(store.FilePath!));
    }
}
