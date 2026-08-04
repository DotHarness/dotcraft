using DotCraft.Agents;
using Microsoft.Extensions.AI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class ModelImageInputPreparerTests
{
    [Fact]
    public void Prepare_BmpImage_TranscodesToPng()
    {
        var sourceBytes = CreateImageBytes("image/bmp");

        var result = ModelImageInputPreparer.Prepare(new DataContent(sourceBytes, "image/bmp"));

        Assert.True(result.HasImage);
        var prepared = result.Content;
        Assert.NotNull(prepared);
        Assert.Equal("image/png", prepared.MediaType);
        Assert.Equal("image/png", Image.DetectFormat(prepared.Data.ToArray()).DefaultMimeType);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public void Prepare_SupportedSmallImage_PreservesSourceBytes(string mediaType)
    {
        var sourceBytes = CreateImageBytes(mediaType);

        var result = ModelImageInputPreparer.Prepare(new DataContent(sourceBytes, mediaType));

        Assert.True(result.HasImage);
        var prepared = result.Content;
        Assert.NotNull(prepared);
        Assert.Equal(mediaType, prepared.MediaType);
        Assert.Equal(sourceBytes, prepared.Data.ToArray());
    }

    [Fact]
    public void Prepare_InvalidImage_ReturnsPlaceholder()
    {
        var result = ModelImageInputPreparer.Prepare(new DataContent(new byte[] { 1, 2, 3 }, "image/bmp"));

        Assert.False(result.HasImage);
        Assert.Null(result.Content);
        Assert.Equal(ModelImageInputPreparer.CouldNotProcessPlaceholder, result.PlaceholderText);
    }

    [Fact]
    public void Prepare_LargeImage_ResizesWithinPromptPatchBudget()
    {
        var sourceBytes = CreateImageBytes("image/png", width: 1700, height: 1700);

        var result = ModelImageInputPreparer.Prepare(new DataContent(sourceBytes, "image/png"));

        Assert.True(result.HasImage);
        var prepared = result.Content;
        Assert.NotNull(prepared);
        Assert.Equal("image/png", prepared.MediaType);
        var info = Image.Identify(prepared.Data.ToArray());
        Assert.NotNull(info);
        Assert.True(info.Width < 1700, $"Expected width to shrink, got {info.Width}.");
        Assert.True(info.Height < 1700, $"Expected height to shrink, got {info.Height}.");
        Assert.True(CountPatches(info.Width, info.Height) <= 2500);
    }

    private static byte[] CreateImageBytes(string mediaType, int width = 1, int height = 1)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(0xff, 0, 0));
        using var stream = new MemoryStream();
        switch (mediaType)
        {
            case "image/bmp":
                image.SaveAsBmp(stream);
                break;
            case "image/jpeg":
                image.SaveAsJpeg(stream);
                break;
            case "image/webp":
                image.SaveAsWebp(stream);
                break;
            default:
                image.SaveAsPng(stream);
                break;
        }

        return stream.ToArray();
    }

    private static long CountPatches(int width, int height) =>
        ((long)(width + 31) / 32) * ((height + 31) / 32);
}
