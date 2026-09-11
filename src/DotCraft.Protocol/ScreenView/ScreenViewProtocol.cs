using System.Buffers.Binary;

namespace DotCraft.Protocol.ScreenView;

/// <summary>Frame format and budget ceilings of a lossy, non-durable desktop frame stream.</summary>
public static class ScreenViewProtocol
{
    public const int FrameHeaderBytes = 16;
    public const int MaximumFrameBytes = 2 * 1024 * 1024;
    public const int MaximumFramePixels = 8192 * 8192;
    public const int DefaultFps = 8;
    public const int MinimumFps = 1;
    public const int MaximumFps = 15;
    public const int MinimumWidth = 320;
    public const int MaximumWidth = 3840;
    public const int MinimumQuality = 20;
    public const int MaximumQuality = 90;

    public static void WriteFrameHeader(Span<byte> destination, ScreenViewFrameHeader header)
    {
        if (destination.Length < FrameHeaderBytes)
            throw new ArgumentException($"A frame header needs {FrameHeaderBytes} bytes.", nameof(destination));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[..4], header.Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), header.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), header.Height);
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(8, 8), header.CapturedAtUnixMilliseconds);
    }

    public static bool TryReadFrameHeader(ReadOnlySpan<byte> source, out ScreenViewFrameHeader header)
    {
        header = default;
        if (source.Length < FrameHeaderBytes)
            return false;
        header = new ScreenViewFrameHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(source[..4]),
            BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(6, 2)),
            BinaryPrimitives.ReadInt64LittleEndian(source.Slice(8, 8)));
        return header is { Width: > 0, Height: > 0 }
            && (long)header.Width * header.Height <= MaximumFramePixels;
    }

    public static ScreenViewControl Clamp(ScreenViewControl control) => new(
        Math.Max(0, control.Watchers),
        Math.Clamp(control.Fps, MinimumFps, MaximumFps),
        Math.Clamp(control.MaxWidth, MinimumWidth, MaximumWidth),
        Math.Clamp(control.Quality, MinimumQuality, MaximumQuality));
}

public readonly record struct ScreenViewFrameHeader(
    uint Sequence,
    ushort Width,
    ushort Height,
    long CapturedAtUnixMilliseconds);

/// <summary>A viewer's demand; zero <see cref="Watchers"/> stops capture entirely.</summary>
public sealed record ScreenViewControl(int Watchers, int Fps, int MaxWidth, int Quality);

/// <param name="Detail">A short, diagnostic-only account of <paramref name="UnavailableReason"/>; never a path.</param>
public sealed record ScreenViewCapability(bool Enabled, string? UnavailableReason, string? Detail = null);
