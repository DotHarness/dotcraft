using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Core.Tests.Sessions;

public sealed class SessionInputPartResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"input-resolver-{Guid.NewGuid():N}");

    public SessionInputPartResolverTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void RebuiltHistory_ReadsLocalImagesAgainWithTheirPaths()
    {
        var kept = Path.Combine(_root, "kept.png");
        File.WriteAllBytes(kept, [0x89, 0x50, 0x4E, 0x47]);
        var missing = Path.Combine(_root, "missing.png");

        var contents = SessionInputPartResolver.ResolvePersisted(
        [
            new SessionInputPart { Type = "localImage", Path = kept },
            new SessionInputPart { Type = "localImage", Path = missing },
        ]);

        Assert.Equal(4, contents.Count);
        Assert.Equal($"<image name=[Image #1] path=\"{kept}\">", Assert.IsType<TextContent>(contents[0]).Text);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], Assert.IsType<DataContent>(contents[1]).Data.ToArray());
        Assert.Equal("</image>", Assert.IsType<TextContent>(contents[2]).Text);
        Assert.Equal($"[localImage:{missing}]", Assert.IsType<TextContent>(contents[3]).Text);
    }

    [Fact]
    public void FileReference_ShowsTheModelItsCanonicalPath()
    {
        var part = new SessionInputPart { Type = "fileRef", Path = ".craft/attachments/a1/log.txt", DisplayPath = "log.txt" };

        Assert.Equal("@.craft/attachments/a1/log.txt", Assert.IsType<TextContent>(part.ToAIContent()).Text);
    }
}
