using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class PathEvidenceScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"path_evidence_{Guid.NewGuid():N}");
    private readonly PathEvidenceScanner _scanner;

    public PathEvidenceScannerTests()
    {
        Directory.CreateDirectory(_root);
        _scanner = new PathEvidenceScanner(new WorkspaceBoundary([_root]));
    }

    [Fact]
    public void ScanWords_WordsWithoutPaths_FindNothing()
    {
        Assert.Empty(_scanner.ScanWords(["cat", "README.md"]));
    }

    [Fact]
    public void ScanWords_PathInsideWorkspace_IsEvidenceButNotOutside()
    {
        var inside = Path.Combine(_root, "notes.md");

        var evidence = _scanner.ScanWords(["cat", inside]);

        Assert.Equal(inside, Assert.Single(evidence).Original);
        Assert.Empty(_scanner.OutsideWorkspace(evidence));
    }

    [Fact]
    public void ScanWords_PathOutsideWorkspace_IsOutside()
    {
        var outside = OutsidePath();

        var outsideEvidence = _scanner.OutsideWorkspace(_scanner.ScanWords(["cat", outside]));

        Assert.Equal(outside, Assert.Single(outsideEvidence).Original);
    }

    [Fact]
    public void ScanWords_OptionWithAttachedPath_IsOutside()
    {
        var outside = OutsidePath();

        var outsideEvidence = _scanner.OutsideWorkspace(_scanner.ScanWords(["build", $"--output={outside}"]));

        Assert.Equal(outside, Assert.Single(outsideEvidence).Original);
    }

    [Fact]
    public void ScanWords_HomePath_IsOutside()
    {
        if (string.IsNullOrEmpty(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
            return;

        var outsideEvidence = _scanner.OutsideWorkspace(_scanner.ScanWords(["cat", "~/notes"]));

        Assert.Equal("~/notes", Assert.Single(outsideEvidence).Original);
    }

    [Fact]
    public void ScanWords_DeviceName_IsNotEvidence()
    {
        Assert.Empty(_scanner.ScanWords(["echo", "hi", ">", "/dev/null"]));
    }

    [Fact]
    public void ScanWords_UncPath_IsOutside()
    {
        var outsideEvidence = _scanner.OutsideWorkspace(_scanner.ScanWords(["copy", @"\\server\share"]));

        var evidence = Assert.Single(outsideEvidence);
        Assert.Equal(@"\\server\share", evidence.Original);
        Assert.True(evidence.IsUnc);
    }

    [Fact]
    public void ScanWords_WindowsSystemPath_IsOutside()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var outsideEvidence = _scanner.OutsideWorkspace(_scanner.ScanWords(["dir", @"C:\Windows\System32"]));

        Assert.Equal(@"C:\Windows\System32", Assert.Single(outsideEvidence).Original);
    }

    [Fact]
    public void ScanWords_WindowsEnvironmentVariablePath_IsOutside()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var outsideEvidence = _scanner.OutsideWorkspace(_scanner.ScanWords(["dir", @"%SystemRoot%\x"]));

        Assert.Equal(@"%SystemRoot%\x", Assert.Single(outsideEvidence).Original);
    }

    [Fact]
    public void ScanWords_PowerShellEnvironmentVariablePath_IsOutside()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var outsideEvidence = _scanner.OutsideWorkspace(_scanner.ScanWords(["Get-Item", @"$env:SystemRoot\x"]));

        Assert.Equal(@"$env:SystemRoot\x", Assert.Single(outsideEvidence).Original);
    }

    [Fact]
    public void ScanText_FindsPathsInsideALongerScript()
    {
        var evidence = _scanner.ScanText("cat /etc/passwd | head");

        Assert.Equal("/etc/passwd", Assert.Single(evidence).Original);
        Assert.Single(_scanner.OutsideWorkspace(evidence));
    }

    private static string OutsidePath() =>
        Path.Combine(Path.GetTempPath(), $"outside_{Guid.NewGuid():N}", "notes.md");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
