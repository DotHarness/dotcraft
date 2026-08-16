using DotCraft.Runtime;
using DotCraft.Workspaces;
using Xunit;

namespace DotCraft.Tests.Runtime;

public sealed class DotCraftPathResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft-paths-{Guid.NewGuid():N}");

    [Fact]
    public void Resolve_NormalizesRelativeDataPathAndOptionalUserData()
    {
        var userData = Path.Combine(_root, "user-data");
        var paths = Resolve(".agents", userData);

        Assert.Equal(Path.GetFullPath(_root), paths.WorkspacePath);
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), ".agents"), paths.Data.RootPath);
        Assert.Equal(Path.GetFullPath(userData), paths.UserData.RootPath);
        Assert.Equal(Path.Combine(paths.Data.RootPath, "skills"), paths.Data.Resolve("skills"));
    }

    [Fact]
    public void Resolve_AcceptsAbsoluteDirectChild()
    {
        var dataPath = Path.Combine(_root, ".agents");
        Assert.Equal(dataPath, Resolve(dataPath).Data.RootPath);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("nested/data")]
    [InlineData("../outside")]
    public void Resolve_RejectsDataPathThatIsNotDirectChild(string dataPath)
    {
        Assert.Throws<ArgumentException>(() => Resolve(dataPath));
    }

    [Fact]
    public void PathRoot_RejectsRootedAndEscapingParts()
    {
        var paths = Resolve(".agents");

        Assert.Throws<ArgumentException>(() => paths.Data.Resolve("..", "outside"));
        Assert.Throws<ArgumentException>(() => paths.Data.Resolve(Path.GetPathRoot(_root)!));
    }

    [Fact]
    public void OptionalUserData_ExpressesDisabledDiscoveryAndRequiredPersistence()
    {
        var paths = Resolve(".agents");

        Assert.False(paths.UserData.IsConfigured);
        Assert.Null(paths.UserData.ResolveOrNull("skills"));
        var error = Assert.Throws<InvalidOperationException>(() => paths.UserData.Require("OpenAI authentication"));
        Assert.Contains("UserDataPath", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsExistingDataLinkOutsideWorkspace_WhenLinksAreSupported()
    {
        Directory.CreateDirectory(_root);
        var outside = Path.Combine(Path.GetTempPath(), $"dotcraft-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(_root, ".agents");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            Assert.Throws<ArgumentException>(() => Resolve(link));
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(outside, recursive: true);
        }
    }

    private DotCraftPaths Resolve(string dataPath, string? userDataPath = null) =>
        DotCraftPathResolver.Resolve(new DotCraftRuntimeOptions
        {
            Config = new DotCraft.Configuration.AppConfig(),
            WorkspacePath = _root,
            DataPath = dataPath,
            UserDataPath = userDataPath
        });

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
