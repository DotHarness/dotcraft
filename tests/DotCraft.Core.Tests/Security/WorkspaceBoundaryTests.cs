using DotCraft.Security;
using Xunit;

namespace DotCraft.Tests.Security;

public sealed class WorkspaceBoundaryTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), $"workspace_boundary_{Guid.NewGuid():N}");
    private readonly string _root;

    public WorkspaceBoundaryTests()
    {
        _root = Path.Combine(_base, "ws");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Contains_PathInsideRoot_IsTrue()
    {
        var boundary = new WorkspaceBoundary([_root]);

        Assert.True(boundary.Contains(Path.Combine(_root, "nested", "file.txt")));
    }

    [Fact]
    public void Contains_RootItself_IsTrue()
    {
        var boundary = new WorkspaceBoundary([_root]);

        Assert.True(boundary.Contains(_root));
        Assert.True(boundary.Contains(_root + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Contains_SiblingSharingTheRootPrefix_IsFalse()
    {
        var sibling = Path.Combine(_base, "ws2");
        Directory.CreateDirectory(sibling);
        var boundary = new WorkspaceBoundary([_root]);

        Assert.False(boundary.Contains(sibling));
        Assert.False(boundary.Contains(Path.Combine(sibling, "file.txt")));
    }

    [Fact]
    public void Contains_LinkInsideRootPointingOutside_IsFalse()
    {
        var target = Path.Combine(_base, "target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(_root, "link");

        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Creating a link needs privileges this machine may not grant.
            return;
        }

        var boundary = new WorkspaceBoundary([_root]);

        Assert.False(boundary.Contains(Path.Combine(link, "file.txt")));
        Assert.False(boundary.Contains(link));
    }

    [Fact]
    public void Contains_InvalidPath_IsFalse()
    {
        var boundary = new WorkspaceBoundary([_root]);

        Assert.False(boundary.Contains("\0invalid"));
        Assert.False(boundary.Contains(string.Empty));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_base))
                Directory.Delete(_base, recursive: true);
        }
        catch
        {
        }
    }
}
