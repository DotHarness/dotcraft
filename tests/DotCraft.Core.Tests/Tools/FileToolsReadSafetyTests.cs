using System.Text;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Tools;

public sealed class FileToolsReadSafetyTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-filetools-read-safety-tests",
        Guid.NewGuid().ToString("N"));

    public FileToolsReadSafetyTests()
    {
        Directory.CreateDirectory(_workspace);
    }

    [Fact]
    public async Task ReadFile_Pdf_ReturnsUnsupportedMessageWithoutBinaryPayload()
    {
        var payload = Encoding.ASCII.GetBytes("%PDF-1.7\n")
            .Concat(new byte[] { 0, 1 })
            .Concat(Encoding.ASCII.GetBytes("secret-binary-payload\n%%EOF"))
            .ToArray();
        await File.WriteAllBytesAsync(Path.Combine(_workspace, "report.pdf"), payload);
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false);

        var result = await tools.ReadFile("report.pdf");

        var text = Assert.Single(result.OfType<TextContent>());
        Assert.Contains("PDF files are binary documents", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-binary-payload", text.Text, StringComparison.Ordinal);
        Assert.Empty(result.OfType<DataContent>());
    }

    [Fact]
    public async Task ReadFile_KnownBinaryExtension_ReturnsUnsupportedMessageWithoutPayload()
    {
        var payload = Encoding.ASCII.GetBytes("PK")
            .Concat(new byte[] { 0, 1 })
            .Concat(Encoding.ASCII.GetBytes("secret-zip-payload"))
            .ToArray();
        await File.WriteAllBytesAsync(Path.Combine(_workspace, "archive.zip"), payload);
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false);

        var result = await tools.ReadFile("archive.zip");

        var text = Assert.Single(result.OfType<TextContent>());
        Assert.Contains("Cannot read binary file as text", text.Text, StringComparison.Ordinal);
        Assert.Contains(".zip", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-zip-payload", text.Text, StringComparison.Ordinal);
        Assert.Empty(result.OfType<DataContent>());
    }

    [Fact]
    public async Task ReadFile_ExtensionlessBinary_ReturnsUnsupportedMessageWithoutPayload()
    {
        var payload = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }
            .Concat(Encoding.ASCII.GetBytes("secret-extensionless-payload"))
            .ToArray();
        await File.WriteAllBytesAsync(Path.Combine(_workspace, "blob"), payload);
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false);

        var result = await tools.ReadFile("blob");

        var text = Assert.Single(result.OfType<TextContent>());
        Assert.Contains("appears to contain binary data", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-extensionless-payload", text.Text, StringComparison.Ordinal);
        Assert.Empty(result.OfType<DataContent>());
    }

    [Fact]
    public async Task ReadFile_Utf8BomPrefixedBinary_ReturnsUnsupportedMessageWithoutPayload()
    {
        var payload = new byte[] { 0xEF, 0xBB, 0xBF, 0, 1, 2, 3 }
            .Concat(Encoding.ASCII.GetBytes("secret-bom-bypass-payload"))
            .ToArray();
        await File.WriteAllBytesAsync(Path.Combine(_workspace, "bom-blob"), payload);
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false);

        var result = await tools.ReadFile("bom-blob");

        var text = Assert.Single(result.OfType<TextContent>());
        Assert.Contains("appears to contain binary data", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-bom-bypass-payload", text.Text, StringComparison.Ordinal);
        Assert.Empty(result.OfType<DataContent>());
    }

    [Fact]
    public async Task ReadFile_Utf16BomText_RemainsReadable()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "utf16.txt"), "hello utf16", Encoding.Unicode);
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false);

        var result = await tools.ReadFile("utf16.txt");

        var text = Assert.Single(result.OfType<TextContent>());
        Assert.Equal("hello utf16", text.Text);
    }

    [Fact]
    public void LooksBinary_Utf16BomWithDecodedNull_ReturnsTrue()
    {
        var sample = new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x00, 0x00 };

        Assert.True(FileContentClassifier.LooksBinary(sample));
    }

    [Fact]
    public async Task ReadFile_ImageExtension_StillReturnsVisionInput()
    {
        await File.WriteAllBytesAsync(Path.Combine(_workspace, "pixel.png"), [0x89, 0x50, 0x4E, 0x47]);
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false);

        var result = await tools.ReadFile("pixel.png");

        Assert.Collection(
            result,
            content => Assert.IsType<TextContent>(content),
            content =>
            {
                var data = Assert.IsType<DataContent>(content);
                Assert.Equal("image/png", data.MediaType);
            });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspace))
                Directory.Delete(_workspace, recursive: true);
        }
        catch
        {
            // ignored
        }
    }
}
