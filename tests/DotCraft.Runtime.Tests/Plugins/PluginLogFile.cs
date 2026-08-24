namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Reads a log a plugin is still writing to, without locking the plugin out of it.</summary>
/// <remarks><see cref="File.ReadAllLines(string)"/> opens with <see cref="FileShare.Read"/>, which
/// denies writers: a poll landing mid-append makes the plugin's write throw, not the test's read.</remarks>
internal static class PluginLogFile
{
    /// <summary>Reads every line, or an empty array when the file does not exist yet.</summary>
    public static string[] ReadLines(string path)
    {
        if (!File.Exists(path))
            return [];

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return lines.ToArray();
    }

    public static string ReadText(string path) =>
        string.Join(Environment.NewLine, ReadLines(path));
}
