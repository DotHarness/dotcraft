using System.Text.RegularExpressions;
using DotCraft.Auth.OpenAI;
using Xunit;

namespace DotCraft.Tests.Auth.OpenAI;

public sealed class OpenAIInstallationIdProviderTests : IDisposable
{
    private static readonly Regex UuidV4Lowercase = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
        RegexOptions.CultureInvariant);

    private readonly string _tempDir;

    public OpenAIInstallationIdProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dotcraft-install-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public void GeneratesPersistsAndCachesUuidV4OnFirstCall()
    {
        var provider = new OpenAIInstallationIdProvider(_tempDir);
        var id = provider.GetInstallationId();

        Assert.Matches(UuidV4Lowercase, id);
        Assert.Equal(id, File.ReadAllText(provider.FilePath!));

        File.WriteAllText(provider.FilePath!, Guid.NewGuid().ToString("D").ToLowerInvariant());
        Assert.Equal(id, provider.GetInstallationId());
    }

    [Fact]
    public void ExistingFile_IsNormalizedWhenValidAndRewrittenWhenNot()
    {
        var raw = Guid.NewGuid().ToString("D");
        File.WriteAllText(
            Path.Combine(_tempDir, OpenAIInstallationIdProvider.InstallationIdFileName),
            "  " + raw.ToUpperInvariant() + "  \n");

        var provider = new OpenAIInstallationIdProvider(_tempDir);
        var id = provider.GetInstallationId();

        Assert.Equal(raw.ToLowerInvariant(), id);

        var otherDir = Path.Combine(_tempDir, "invalid");
        Directory.CreateDirectory(otherDir);
        File.WriteAllText(Path.Combine(otherDir, OpenAIInstallationIdProvider.InstallationIdFileName), "not-a-uuid");
        var rewritten = new OpenAIInstallationIdProvider(otherDir);
        var rewrittenId = rewritten.GetInstallationId();

        Assert.Matches(UuidV4Lowercase, rewrittenId);
        Assert.Equal(rewrittenId, File.ReadAllText(rewritten.FilePath!));
    }

    [Fact]
    public void MissingUserData_UsesStableEphemeralIdWithoutAFile()
    {
        var provider = new OpenAIInstallationIdProvider();

        var first = provider.GetInstallationId();
        Assert.Null(provider.FilePath);
        Assert.Matches(UuidV4Lowercase, first);
        Assert.Equal(first, provider.GetInstallationId());
    }
}
