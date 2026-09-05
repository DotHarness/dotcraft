namespace DotCraft.Satellite.Services;

internal enum ProtocolOwnershipAction
{
    Write,
    Leave,
    Rewrite,
    Delegate
}

/// <summary>
/// Another program's registration is never overwritten: on a machine that also runs DotCraft
/// Desktop, Desktop keeps the protocol and forwards invitations to the executable path Satellite
/// publishes.
/// </summary>
internal static class ProtocolOwnershipPolicy
{
    public static ProtocolOwnershipAction Decide(string? existingCommand, string ourExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ourExecutablePath);
        var registered = ExtractExecutable(existingCommand);
        if (registered is null)
            return ProtocolOwnershipAction.Write;
        if (string.Equals(registered, ourExecutablePath, StringComparison.OrdinalIgnoreCase))
            return ProtocolOwnershipAction.Leave;
        return string.Equals(
            Path.GetFileName(registered),
            Path.GetFileName(ourExecutablePath),
            StringComparison.OrdinalIgnoreCase)
            ? ProtocolOwnershipAction.Rewrite
            : ProtocolOwnershipAction.Delegate;
    }

    public static string? ExtractExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        var value = command.Trim();
        if (value[0] == '"')
        {
            var end = value.IndexOf('"', 1);
            return end > 1 ? value[1..end] : null;
        }
        var space = value.IndexOf(' ');
        var path = space < 0 ? value : value[..space];
        return path.Length == 0 ? null : path;
    }
}
