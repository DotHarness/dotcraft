using System.Reflection;
using System.Text;

namespace DotCraft.Plugins;

internal static class PluginVariables
{
    public static string Expand(string value, string? pluginRoot, string? pluginDataPath)
    {
        const string rootToken = "${PLUGIN_ROOT}";
        const string dataToken = "${PLUGIN_DATA}";
        const string binToken = "${DOTCRAFT_BIN}";
        var dotcraftBin = ResolveDotCraftBinary();
        var remaining = value.AsSpan();
        var output = new StringBuilder(value.Length);
        while (!remaining.IsEmpty)
        {
            var bestIndex = -1;
            string? bestToken = null;
            string? bestValue = null;

            Consider(remaining, rootToken, pluginRoot, ref bestIndex, ref bestToken, ref bestValue);
            Consider(remaining, dataToken, pluginDataPath, ref bestIndex, ref bestToken, ref bestValue);
            Consider(remaining, binToken, dotcraftBin, ref bestIndex, ref bestToken, ref bestValue);

            if (bestIndex < 0 || bestToken is null)
            {
                output.Append(remaining);
                break;
            }

            output.Append(remaining[..bestIndex]);
            output.Append(bestValue);
            remaining = remaining[(bestIndex + bestToken.Length)..];
        }

        return output.ToString();
    }

    private static void Consider(
        ReadOnlySpan<char> remaining,
        string token,
        string? replacement,
        ref int bestIndex,
        ref string? bestToken,
        ref string? bestValue)
    {
        if (replacement is null)
            return;
        var index = remaining.IndexOf(token, StringComparison.Ordinal);
        if (index >= 0 && (bestIndex < 0 || index < bestIndex))
        {
            bestIndex = index;
            bestToken = token;
            bestValue = replacement;
        }
    }

    internal static string? ResolveDotCraftBinary()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && Path.GetFileNameWithoutExtension(processPath).Equals("dotcraft", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var entryPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(entryPath) && File.Exists(entryPath))
            return entryPath;

        return !string.IsNullOrWhiteSpace(processPath) ? processPath : null;
    }
}
