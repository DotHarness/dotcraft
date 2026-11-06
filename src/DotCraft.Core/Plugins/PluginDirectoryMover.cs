namespace DotCraft.Plugins;

internal static class PluginDirectoryMover
{
    public static void Move(string source, string destination)
    {
        for (var retry = 0; ; retry++)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (IOException exception) when (
                OperatingSystem.IsWindows()
                && retry < 5
                && (exception.HResult & 0xFFFF) is 5 or 32)
            {
                // File-system filters can briefly block renames after executables are copied.
                Thread.Sleep(100 * (retry + 1));
            }
        }
    }
}
