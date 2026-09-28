using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal sealed class TransferSourceChangedException(string message) : IOException(message);

internal static class RemoteFileErrors
{
    internal static ExtensionError FromException(Exception exception, string operation)
    {
        var code = exception switch
        {
            RemoteToolHostException remote => remote.Code,
            OperationCanceledException => ToolErrorCodes.Cancelled,
            FileNotFoundException or DirectoryNotFoundException => RemoteToolErrorCodes.FileNotFound,
            UnauthorizedAccessException => RemoteToolErrorCodes.FileAccessDenied,
            TransferSourceChangedException => RemoteToolErrorCodes.TransferSourceChanged,
            IOException io when OperatingSystem.IsWindows() && (io.HResult & 0xFFFF) is 32 or 33 => RemoteToolErrorCodes.FileInUse,
            IOException => RemoteToolErrorCodes.FileIoError,
            ArgumentException or FormatException => ToolErrorCodes.InputInvalid,
            _ => ToolErrorCodes.ExecutionFailed
        };
        var reason = exception is IOException or UnauthorizedAccessException or RemoteToolHostException or ArgumentException or FormatException
            && !string.IsNullOrWhiteSpace(exception.Message)
                ? exception.Message
                : exception.GetType().Name;
        return new(code, $"{operation} failed: {reason}");
    }
}
