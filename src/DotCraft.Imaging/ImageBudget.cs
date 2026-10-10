using System.Runtime.CompilerServices;

namespace DotCraft.Imaging;

internal sealed class ImageBudget(long maximumBytes = ImageBudget.DefaultMaximumBytes)
{
    public const long DefaultMaximumBytes = 512L * 1024 * 1024;
    private long _reserved;

    public static int BufferLength(int width, int height, int channels)
    {
        if (width <= 0 || height <= 0 || channels <= 0)
            throw new ImageCodecException(ImageError.InvalidImage);
        var length = (long)width * height;
        if (length > int.MaxValue / channels)
            throw new ImageCodecException(ImageError.LimitExceeded);
        return (int)length * channels;
    }

    public void Reserve(long bytes)
    {
        if (bytes < 0 || bytes > maximumBytes - _reserved)
            throw new ImageCodecException(ImageError.LimitExceeded);
        _reserved += bytes;
    }

    public void Release(long bytes)
    {
        if (bytes < 0 || bytes > _reserved)
            throw new InvalidOperationException("Invalid image allocation release.");
        _reserved -= bytes;
    }

    public T[] Allocate<T>(int count) where T : unmanaged
    {
        if (count < 0)
            throw new ImageCodecException(ImageError.LimitExceeded);
        Reserve(checked((long)count * Unsafe.SizeOf<T>()));
        return new T[count];
    }

    public byte[] Copy(ReadOnlySpan<byte> bytes)
    {
        var result = Allocate<byte>(bytes.Length);
        bytes.CopyTo(result);
        return result;
    }
}
