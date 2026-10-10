using System.Diagnostics.CodeAnalysis;

namespace DotCraft.Imaging;

public enum ImageFormat { Unknown, Png, Jpeg, Webp, Gif, Bmp }

public enum ImageError { UnsupportedFormat, InvalidImage, LimitExceeded, EncodeFailed }

public enum JpegSubsampling { Yuv444, Yuv420 }

public readonly record struct ImageSize(int Width, int Height);

public readonly record struct ImageInfo(ImageFormat Format, ImageSize Size)
{
    public string MediaType => ImageMediaTypes.For(Format);
}

public sealed class EncodedImage
{
    internal EncodedImage(ImageFormat format, ImageSize size, byte[] data)
    {
        Format = format;
        Size = size;
        Data = data;
    }

    public ImageFormat Format { get; }
    public ImageSize Size { get; }
    public string MediaType => ImageMediaTypes.For(Format);
    public ReadOnlyMemory<byte> Data { get; }
}

public sealed class ImageProcessingResult
{
    private ImageProcessingResult(ImageSize? sourceSize, EncodedImage? image, ImageError? error)
    {
        SourceSize = sourceSize;
        Image = image;
        Error = error;
    }

    [MemberNotNullWhen(true, nameof(Image))]
    public bool IsSuccess => Image is not null;
    public ImageSize? SourceSize { get; }
    public EncodedImage? Image { get; }
    public ImageError? Error { get; }

    internal static ImageProcessingResult Completed(ImageSize size, EncodedImage image) => new(size, image, null);
    internal static ImageProcessingResult Failed(ImageError error) => new(null, null, error);
}

internal static class ImageMediaTypes
{
    public static string For(ImageFormat format) => format switch
    {
        ImageFormat.Png => "image/png",
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Webp => "image/webp",
        ImageFormat.Gif => "image/gif",
        ImageFormat.Bmp => "image/bmp",
        _ => "application/octet-stream"
    };
}
