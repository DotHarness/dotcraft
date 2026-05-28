using System.Text.RegularExpressions;
using DotCraft.Auth.OpenAI;

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
    public void GeneratesAndPersistsUuidV4OnFirstCall()
    {
        var provider = new OpenAIInstallationIdProvider(_tempDir);
        var id = provider.GetInstallationId();

        Assert.Matches(UuidV4Lowercase, id);
        Assert.True(File.Exists(provider.FilePath));
        Assert.Equal(id, File.ReadAllText(provider.FilePath));
    }

    [Fact]
    public void ReusesPersistedUuidAcrossInstances()
    {
        var existing = Guid.NewGuid().ToString("D").ToLowerInvariant();
        File.WriteAllText(Path.Combine(_tempDir, OpenAIInstallationIdProvider.InstallationIdFileName), existing);

        var provider = new OpenAIInstallationIdProvider(_tempDir);
        Assert.Equal(existing, provider.GetInstallationId());
    }

    [Fact]
    public void RewritesFileWhenContentsAreNotAValidUuid()
    {
        File.WriteAllText(Path.Combine(_tempDir, OpenAIInstallationIdProvider.InstallationIdFileName), "not-a-uuid");

        var provider = new OpenAIInstallationIdProvider(_tempDir);
        var id = provider.GetInstallationId();

        Assert.Matches(UuidV4Lowercase, id);
        Assert.Equal(id, File.ReadAllText(provider.FilePath));
    }

    [Fact]
    public void TrimsWhitespaceAndAcceptsUppercaseHexFromExistingFile()
    {
        var raw = Guid.NewGuid().ToString("D"); // uppercase or mixed depending on runtime
        File.WriteAllText(
            Path.Combine(_tempDir, OpenAIInstallationIdProvider.InstallationIdFileName),
            "  " + raw.ToUpperInvariant() + "  \n");

        var provider = new OpenAIInstallationIdProvider(_tempDir);
        var id = provider.GetInstallationId();

        Assert.Equal(raw.ToLowerInvariant(), id);
    }

    [Fact]
    public void CachesResolvedValueWithinAProcess()
    {
        var provider = new OpenAIInstallationIdProvider(_tempDir);
        var first = provider.GetInstallationId();

        // Mutate the file underneath; cached value should win.
        File.WriteAllText(provider.FilePath, Guid.NewGuid().ToString("D").ToLowerInvariant());

        Assert.Equal(first, provider.GetInstallationId());
    }
}
