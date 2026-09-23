namespace DotCraft.Auth.OpenAI;

/// <summary>Stores tokens for one credential identity.</summary>
public interface IOpenAITokenStore
{
    AuthDotJson? Load();
    void Save(AuthDotJson auth);
    /// <summary>Atomically replaces or removes tokens only if the stored tokens still match the expected value.</summary>
    bool TryReplace(AuthDotJson expected, AuthDotJson? replacement);
}

public sealed record OpenAIAuthorization(
    string Url,
    string State,
    string CodeVerifier,
    string RedirectUri);
