namespace DotCraft.Imaging;

public sealed record ImageProcessingOptions(ImageFormat OutputFormat)
{
    public ImageSize? TargetSize { get; init; }
    public bool PreserveSourceBytes { get; init; } = true;
    public int JpegQuality { get; init; } = 85;
    public JpegSubsampling JpegSubsampling { get; init; } = JpegSubsampling.Yuv444;
}

public sealed record JpegEncodingOptions
{
    public ImageSize? TargetSize { get; init; }
    public int Quality { get; init; } = 85;
    public JpegSubsampling Subsampling { get; init; } = JpegSubsampling.Yuv444;
}
