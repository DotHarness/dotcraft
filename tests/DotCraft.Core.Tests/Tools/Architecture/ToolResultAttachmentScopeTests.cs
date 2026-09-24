using DotCraft.Tools;
using Xunit;

namespace DotCraft.Core.Tests.Tools.Architecture;

public sealed class ToolResultAttachmentScopeTests
{
    [Fact]
    public void Suppress_ClearsCurrentAndRestoresOuterAttachmentsOnDispose()
    {
        var outer = new ToolResultAttachments();
        using var outerScope = ToolResultAttachmentScope.Set(outer);

        using (ToolResultAttachmentScope.Suppress())
            Assert.Null(ToolResultAttachmentScope.Current);

        Assert.Same(outer, ToolResultAttachmentScope.Current);
    }

    [Fact]
    public async Task Suppress_KeepsNestedFileWriteOffTheOuterInvocation()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-attachment-scope-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var outer = new ToolResultAttachments();
            using var outerScope = ToolResultAttachmentScope.Set(outer);

            using (ToolResultAttachmentScope.Suppress())
                await new FileTools(workspace).WriteFile("child.txt", "x");

            Assert.True(File.Exists(Path.Combine(workspace, "child.txt")));
            Assert.Null(outer.StructuredContent);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
