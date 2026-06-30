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
        Assert.Contains("1: hello utf16", text.Text, StringComparison.Ordinal);
        Assert.Contains("End of file - total 1 lines", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFile_LimitWithoutOffset_ReadsFirstPageOnly()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_workspace, "paged.txt"),
            string.Join('\n', Enumerable.Range(1, 120).Select(i => $"line-{i:D3}")));
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false);

        var result = await tools.ReadFile("paged.txt", limit: 50);

        var text = Assert.Single(result.OfType<TextContent>()).Text;
        Assert.Contains("1: line-001", text, StringComparison.Ordinal);
        Assert.Contains("50: line-050", text, StringComparison.Ordinal);
        Assert.DoesNotContain("51: line-051", text, StringComparison.Ordinal);
        Assert.Contains("Use offset=51 to read more", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFile_OffsetAndLimit_ReadsRequestedPage()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_workspace, "paged-offset.txt"),
            string.Join('\n', Enumerable.Range(1, 120).Select(i => $"value-{i:D3}")));
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false);

        var result = await tools.ReadFile("paged-offset.txt", offset: 51, limit: 50);

        var text = Assert.Single(result.OfType<TextContent>()).Text;
        Assert.Contains("51: value-051", text, StringComparison.Ordinal);
        Assert.Contains("100: value-100", text, StringComparison.Ordinal);
        Assert.DoesNotContain("50: value-050", text, StringComparison.Ordinal);
        Assert.DoesNotContain("101: value-101", text, StringComparison.Ordinal);
        Assert.Contains("Use offset=101 to read more", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFile_LargeTextWithoutPagination_ReturnsShortErrorWithoutPayload()
    {
        var secret = "secret-large-file-payload";
        var content = new string('x', TextFileReadLimiter.MaxUnpaginatedTextBytes + 1) + secret;
        await File.WriteAllTextAsync(Path.Combine(_workspace, "large.txt"), content);
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false);

        var result = await tools.ReadFile("large.txt");

        var text = Assert.Single(result.OfType<TextContent>()).Text;
        Assert.Contains("too large to read without pagination", text, StringComparison.Ordinal);
        Assert.Contains("offset=1", text, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        Assert.True(text.Length < 400, text);
    }

    [Fact]
    public async Task ReadFile_SmallTextWithoutPagination_ReturnsLineNumberedContent()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "small.txt"), "alpha\nbeta");
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false);

        var result = await tools.ReadFile("small.txt");

        var text = Assert.Single(result.OfType<TextContent>()).Text;
        Assert.Contains("1: alpha", text, StringComparison.Ordinal);
        Assert.Contains("2: beta", text, StringComparison.Ordinal);
        Assert.Contains("End of file - total 2 lines", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LooksBinary_Utf16BomWithDecodedNull_ReturnsTrue()
    {
        var sample = new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x00, 0x00 };

        Assert.True(FileContentClassifier.LooksBinary(sample));
    }

    [Theory]
    [InlineData("pixel.png", "image/png")]
    [InlineData("pixel.bmp", "image/bmp")]
    public async Task ReadFile_ImageExtension_StillReturnsVisionInput(string fileName, string mediaType)
    {
        await File.WriteAllBytesAsync(Path.Combine(_workspace, fileName), [0x89, 0x50, 0x4E, 0x47]);
        var tools = new FileTools(_workspace, requireApprovalOutsideWorkspace: false);

        var result = await tools.ReadFile(fileName);

        Assert.Collection(
            result,
            content => Assert.IsType<TextContent>(content),
            content =>
            {
                var data = Assert.IsType<DataContent>(content);
                Assert.Equal(mediaType, data.MediaType);
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
