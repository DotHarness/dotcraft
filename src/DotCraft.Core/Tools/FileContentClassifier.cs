using System.Text;

namespace DotCraft.Tools;

internal static class FileContentClassifier
{
    internal const int SampleSize = 8192;

    private const double NonPrintableControlByteRatioThreshold = 0.10;

    private static readonly UTF32Encoding Utf32LittleEndian = new(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: false);
    private static readonly UTF32Encoding Utf32BigEndian = new(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: false);

    private static readonly HashSet<string> KnownBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".tar", ".gz", ".7z", ".rar", ".bz2", ".xz",
        ".exe", ".dll", ".so", ".dylib", ".pdb",
        ".class", ".jar", ".war",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff",
        ".mp3", ".mp4", ".avi", ".mkv", ".wav", ".flac", ".ogg", ".webm",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".odt", ".ods", ".odp",
        ".bin", ".dat", ".obj", ".o", ".a", ".lib",
        ".wasm", ".pyc", ".pyo",
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        ".db", ".sqlite", ".mdb", ".ldb"
    };

    internal static bool IsPdf(string filePath)
        => string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase);

    internal static bool IsKnownBinaryExtension(string filePath)
        => KnownBinaryExtensions.Contains(Path.GetExtension(filePath));

    internal static async Task<bool> LooksBinaryFileAsync(string filePath)
    {
        var buffer = new byte[SampleSize];
        int bytesRead;
        await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            bytesRead = await stream.ReadAsync(buffer);
        }

        return LooksBinary(buffer.AsSpan(0, bytesRead));
    }

    internal static bool LooksBinary(ReadOnlySpan<byte> sample)
    {
        if (sample.IsEmpty)
            return false;

        if (TryGetBomEncodedContent(sample, out var encodedContent, out var encoding, out var codeUnitSize))
        {
            if (encodedContent.Length % codeUnitSize != 0)
                return true;

            return LooksBinaryText(encoding.GetString(encodedContent));
        }

        if (sample is [0xEF, 0xBB, 0xBF, ..])
            sample = sample[3..];

        if (sample.IsEmpty)
            return false;

        var controlBytes = 0;
        foreach (var b in sample)
        {
            if (b == 0)
                return true;

            if (IsNonPrintableControlByte(b))
                controlBytes++;
        }

        return controlBytes / (double)sample.Length > NonPrintableControlByteRatioThreshold;
    }

    internal static string FormatPdfUnsupportedMessage(string displayPath, long? byteLength = null)
        => $"Error: PDF files are binary documents and cannot be read as text by ReadFile: {displayPath}{FormatDetails(null, byteLength)}. Convert the PDF to text first or use a dedicated PDF/OCR workflow.";

    internal static string FormatBinaryUnsupportedMessage(
        string displayPath,
        string filePath,
        long? byteLength = null,
        bool detectedFromSample = false)
    {
        var details = detectedFromSample
            ? FormatDetails(null, byteLength)
            : FormatDetails(Path.GetExtension(filePath), byteLength);
        var reason = detectedFromSample
            ? " The file appears to contain binary data."
            : "";

        return $"Error: Cannot read binary file as text: {displayPath}{details}.{reason} Use an appropriate converter or specialized tool for this file type.";
    }

    private static bool TryGetBomEncodedContent(
        ReadOnlySpan<byte> sample,
        out ReadOnlySpan<byte> encodedContent,
        out Encoding encoding,
        out int codeUnitSize)
    {
        if (sample is [0xFF, 0xFE, 0x00, 0x00, ..])
        {
            encodedContent = sample[4..];
            encoding = Utf32LittleEndian;
            codeUnitSize = 4;
            return true;
        }

        if (sample is [0x00, 0x00, 0xFE, 0xFF, ..])
        {
            encodedContent = sample[4..];
            encoding = Utf32BigEndian;
            codeUnitSize = 4;
            return true;
        }

        if (sample is [0xFF, 0xFE, ..])
        {
            encodedContent = sample[2..];
            encoding = Encoding.Unicode;
            codeUnitSize = 2;
            return true;
        }

        if (sample is [0xFE, 0xFF, ..])
        {
            encodedContent = sample[2..];
            encoding = Encoding.BigEndianUnicode;
            codeUnitSize = 2;
            return true;
        }

        encodedContent = default;
        encoding = null!;
        codeUnitSize = 1;
        return false;
    }

    private static bool IsNonPrintableControlByte(byte b)
        => b < 32 && b is not 9 and not 10 and not 12 and not 13;

    private static bool LooksBinaryText(string text)
    {
        if (text.Length == 0)
            return false;

        var controlChars = 0;
        foreach (var c in text)
        {
            if (c == '\0')
                return true;

            if (IsNonPrintableControlChar(c))
                controlChars++;
        }

        return controlChars / (double)text.Length > NonPrintableControlByteRatioThreshold;
    }

    private static bool IsNonPrintableControlChar(char c)
        => char.IsControl(c) && c is not '\t' and not '\n' and not '\f' and not '\r';

    private static string FormatDetails(string? extension, long? byteLength)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(extension))
            parts.Add(extension);
        if (byteLength.HasValue)
            parts.Add($"{byteLength.Value} bytes");

        return parts.Count == 0 ? "" : $" ({string.Join(", ", parts)})";
    }
}
