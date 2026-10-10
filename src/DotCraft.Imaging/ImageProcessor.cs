namespace DotCraft.Imaging;

public static class ImageProcessor
{
    /// <summary>Identifies stored image dimensions from headers without validating all pixel data.</summary>
    public static bool TryIdentify(ReadOnlySpan<byte> data, out ImageInfo info)
    {
        info = default;
        try
        {
            info = Identify(data);
            return true;
        }
        catch (ImageCodecException)
        {
            return false;
        }
    }

    /// <summary>Validates and processes encoded content, returning owned bytes independent of the input buffer.</summary>
    public static ImageProcessingResult Process(ReadOnlySpan<byte> data, ImageProcessingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.OutputFormat is not (ImageFormat.Png or ImageFormat.Jpeg or ImageFormat.Webp))
            throw new ArgumentOutOfRangeException(nameof(options), "The output format must have an encoder.");
        ValidateJpeg(options.JpegQuality, options.JpegSubsampling);
        if (options.TargetSize is { } requested)
            ValidateSize(requested, nameof(options));
        try
        {
            var info = Identify(data);
            var target = options.TargetSize ?? info.Size;
            ValidateTarget(target, info.Size, nameof(options));
            var budget = new ImageBudget();
            budget.Reserve(data.Length);
            var decoded = Decode(data, info.Format, budget);
            if (decoded.Width != info.Size.Width || decoded.Height != info.Size.Height)
                throw new ImageCodecException(ImageError.InvalidImage);
            if (options.PreserveSourceBytes && info.Format == options.OutputFormat && target == info.Size)
                return ImageProcessingResult.Completed(info.Size, new(info.Format, info.Size, budget.Copy(data)));

            decoded = ImageResampler.Resize(decoded, target.Width, target.Height, budget);
            decoded = FilterMetadata(decoded);
            var bytes = Encode(decoded, options.OutputFormat, options.JpegQuality, options.JpegSubsampling, budget);
            return ImageProcessingResult.Completed(info.Size, new(options.OutputFormat, target, bytes));
        }
        catch (ImageCodecException exception)
        {
            return ImageProcessingResult.Failed(exception.Error);
        }
    }

    /// <summary>Encodes top-down BGRA pixels with a positive byte stride; input alpha does not affect JPEG colors.</summary>
    public static ImageProcessingResult EncodeBgraJpeg(
        ReadOnlySpan<byte> pixels, ImageSize size, int stride, JpegEncodingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateSize(size, nameof(size));
        ValidateJpeg(options.Quality, options.Subsampling);
        var target = options.TargetSize ?? size;
        ValidateTarget(target, size, nameof(options));
        if (stride < (long)size.Width * 4)
            throw new ArgumentOutOfRangeException(nameof(stride));
        if ((size.Height - 1L) * stride + (long)size.Width * 4 > pixels.Length)
            throw new ArgumentException("The pixel buffer is shorter than the declared image.", nameof(pixels));
        try
        {
            var budget = new ImageBudget();
            var decoded = DecodedImage.Create(size.Width, size.Height, budget);
            for (var y = 0; y < size.Height; y++)
            {
                var source = pixels.Slice(y * stride, size.Width * 4);
                var destination = decoded.Pixels.AsSpan(y * size.Width * 4, size.Width * 4);
                for (var x = 0; x < source.Length; x += 4)
                {
                    destination[x] = source[x + 2];
                    destination[x + 1] = source[x + 1];
                    destination[x + 2] = source[x];
                    destination[x + 3] = 255;
                }
            }
            decoded = ImageResampler.Resize(decoded, target.Width, target.Height, budget);
            var encoded = JpegCodec.Encode(decoded, options.Quality, options.Subsampling, budget);
            return ImageProcessingResult.Completed(size, new(ImageFormat.Jpeg, target, encoded));
        }
        catch (ImageCodecException exception)
        {
            return ImageProcessingResult.Failed(exception.Error);
        }
    }

    private static ImageInfo Identify(ReadOnlySpan<byte> data) => DetectFormat(data) switch
    {
        ImageFormat.Png => PngCodec.Identify(data),
        ImageFormat.Jpeg => JpegCodec.Identify(data),
        ImageFormat.Webp => WebpCodec.Identify(data),
        ImageFormat.Gif => GifCodec.Identify(data),
        ImageFormat.Bmp => BmpCodec.Identify(data),
        _ => throw new ImageCodecException(ImageError.UnsupportedFormat)
    };

    private static ImageFormat DetectFormat(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ImageFormat.Png;
        if (data.StartsWith(new byte[] { 255, 216 })) return ImageFormat.Jpeg;
        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8)) return ImageFormat.Gif;
        if (data.StartsWith("BM"u8)) return ImageFormat.Bmp;
        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) return ImageFormat.Webp;
        return ImageFormat.Unknown;
    }

    private static DecodedImage Decode(ReadOnlySpan<byte> data, ImageFormat format, ImageBudget budget) => format switch
    {
        ImageFormat.Png => PngCodec.Decode(data, budget),
        ImageFormat.Jpeg => JpegCodec.Decode(data, budget),
        ImageFormat.Webp => WebpCodec.Decode(data, budget),
        ImageFormat.Gif => GifCodec.Decode(data, budget),
        ImageFormat.Bmp => BmpCodec.Decode(data, budget),
        _ => throw new ImageCodecException(ImageError.UnsupportedFormat)
    };

    private static byte[] Encode(DecodedImage image, ImageFormat format, int quality, JpegSubsampling subsampling, ImageBudget budget) => format switch
    {
        ImageFormat.Png => PngCodec.Encode(image, budget),
        ImageFormat.Jpeg => JpegCodec.Encode(image, quality, subsampling, budget),
        ImageFormat.Webp => WebpCodec.Encode(image, budget),
        _ => throw new ImageCodecException(ImageError.UnsupportedFormat)
    };

    private static DecodedImage FilterMetadata(DecodedImage image) =>
        image.IccProfile is { Length: >= 20 } profile && profile.AsSpan(16, 4).SequenceEqual("RGB "u8)
            ? image
            : image with { IccProfile = null };

    private static void ValidateSize(ImageSize size, string parameter)
    {
        if (size.Width <= 0 || size.Height <= 0)
            throw new ArgumentOutOfRangeException(parameter, "Image dimensions must be positive.");
    }

    private static void ValidateTarget(ImageSize target, ImageSize source, string parameter)
    {
        ValidateSize(target, parameter);
        if (target.Width > source.Width || target.Height > source.Height)
            throw new ArgumentOutOfRangeException(parameter, "Image processing cannot enlarge an image.");
    }

    private static void ValidateJpeg(int quality, JpegSubsampling subsampling)
    {
        if (quality is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(quality));
        if (subsampling is not (JpegSubsampling.Yuv444 or JpegSubsampling.Yuv420))
            throw new ArgumentOutOfRangeException(nameof(subsampling));
    }
}
