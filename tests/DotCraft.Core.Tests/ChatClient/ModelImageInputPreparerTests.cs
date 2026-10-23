using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class ModelImageInputPreparerTests
{
    [Theory]
    [InlineData("image/bmp")]
    [InlineData("image/gif")]
    public void Prepare_UnsupportedFormat_TranscodesToPng(string mediaType)
    {
        var result = ModelImageInputPreparer.Prepare(new DataContent(ImageFixture.Red(mediaType), mediaType));

        Assert.True(result.HasImage);
        Assert.Equal("image/png", result.Content!.MediaType);
        Assert.Equal((1, 1), ImageFixture.PngSize(result.Content.Data.Span));
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public void Prepare_SupportedSmallImage_PreservesSourceBytes(string mediaType)
    {
        var sourceBytes = ImageFixture.Red(mediaType);

        var result = ModelImageInputPreparer.Prepare(new DataContent(sourceBytes, mediaType));

        Assert.True(result.HasImage);
        var prepared = result.Content;
        Assert.NotNull(prepared);
        Assert.Equal(mediaType, prepared.MediaType);
        Assert.Equal(sourceBytes, prepared.Data.ToArray());
    }

    [Fact]
    public void Prepare_UndecodableImage_ReturnsPlaceholder()
    {
        DataContent[] inputs =
        [
            new(new byte[] { 1, 2, 3 }, "image/bmp"),
            new(ImageFixture.Red("image/png")[..33], "image/png")
        ];

        foreach (var input in inputs)
        {
            var result = ModelImageInputPreparer.Prepare(input);
            Assert.False(result.HasImage);
            Assert.Null(result.Content);
            Assert.Equal(ModelImageInputPreparer.CouldNotProcessPlaceholder, result.PlaceholderText);
        }
    }

    [Theory]
    [InlineData(2048, 2048, 1600, 1600)]
    [InlineData(4096, 1024, 2048, 512)]
    [InlineData(1, 4096, 1, 2048)]
    public void Prepare_UsesExactModelDimensions(int width, int height, int expectedWidth, int expectedHeight)
    {
        var result = ModelImageInputPreparer.Prepare(new DataContent(ImageFixture.Red("image/png", width, height), "image/png"));
        Assert.True(result.HasImage);
        Assert.Equal((expectedWidth, expectedHeight), ImageFixture.PngSize(result.Content!.Data.Span));
    }

    [Fact]
    public void Prepare_UsesContentTypeAndCopiesApplicationMetadata()
    {
        var bytes = ImageFixture.Red("image/png");
        var source = new DataContent(bytes, "image/jpeg")
        {
            AdditionalProperties = new() { ["artifact"] = "test", ["ordinal"] = 42 }
        };
        var result = ModelImageInputPreparer.Prepare(source);
        Assert.True(result.HasImage);
        Assert.Equal("image/png", result.Content!.MediaType);
        Assert.Equal(bytes, result.Content.Data.ToArray());
        Assert.Equal(source.AdditionalProperties, result.Content.AdditionalProperties);
        Assert.NotSame(source.AdditionalProperties, result.Content.AdditionalProperties);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(64 * 1024 * 1024, false)]
    [InlineData(64 * 1024 * 1024 + 1, true)]
    public void Prepare_ByteLimitsHaveDistinctFailureClassification(int bytes, bool sizeFailure)
    {
        var result = ModelImageInputPreparer.Prepare(new DataContent(new byte[bytes], "image/png"));
        Assert.False(result.HasImage);
        Assert.Equal(sizeFailure ? ModelImageInputPreparer.TooLargePlaceholder : ModelImageInputPreparer.CouldNotProcessPlaceholder, result.PlaceholderText);
    }
}
