using System.Text;

namespace DotCraft.Tools;

internal static class TextFileEncoding
{
    private static readonly UTF8Encoding StrictUtf8NoBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Encoding Detect(string path)
    {
        Span<byte> bom = stackalloc byte[4];
        int bytesRead;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            bytesRead = fs.Read(bom);
        }

        return Detect(bom[..bytesRead]);
    }

    public static async Task<(string? Text, Encoding Encoding, string? Error)> ReadAsync(
        string fullPath,
        string displayPath,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        var encoding = Detect(bytes);
        var bomLength = encoding.Preamble.Length;
        try
        {
            return (encoding.GetString(bytes, bomLength, bytes.Length - bomLength), encoding, null);
        }
        catch (DecoderFallbackException)
        {
            return (null, encoding, InvalidTextError(displayPath, encoding));
        }
    }

    /// <summary>
    /// Encodes the whole text before opening the file, so text the encoding rejects fails without truncating it.
    /// </summary>
    public static Task WriteAsync(string fullPath, string text, Encoding encoding)
        => File.WriteAllBytesAsync(fullPath, [.. encoding.Preamble, .. encoding.GetBytes(text)]);

    public static string InvalidTextError(string displayPath, Encoding encoding)
        => $"Error: {displayPath} is not valid {encoding.WebName} text. Only UTF-8, UTF-16, and UTF-32 files can be read or edited.";

    private static Encoding Detect(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0x00, 0x00, 0xFE, 0xFF, ..] => new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true),
        [0xFF, 0xFE, 0x00, 0x00, ..] => new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true),
        [0xEF, 0xBB, 0xBF, ..] => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true),
        [0xFF, 0xFE, ..] => new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true),
        [0xFE, 0xFF, ..] => new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true),
        _ => StrictUtf8NoBom,
    };
}
