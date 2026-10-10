namespace DotCraft.Imaging;

internal sealed record DecodedImage(int Width, int Height, byte[] Pixels)
{
    public byte[]? Exif { get; init; }
    public byte[]? IccProfile { get; init; }
    public ushort[]? Pixels16 { get; init; }

    public static DecodedImage Create(int width, int height, ImageBudget budget)
    {
        if (width <= 0 || height <= 0)
            throw new ImageCodecException(ImageError.InvalidImage);
        return new(width, height, budget.Allocate<byte>(ImageBudget.BufferLength(width, height, 4)));
    }
}

internal sealed class ImageCodecException(ImageError error) : Exception(error.ToString())
{
    public ImageError Error { get; } = error;
}
