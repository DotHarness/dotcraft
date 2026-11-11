using System.Text;

namespace DotCraft.Plugins;

internal static class PluginVariables
{
    public static string Expand(string value, string? pluginRoot, string? pluginDataPath)
    {
        const string rootToken = "${PLUGIN_ROOT}";
        const string dataToken = "${PLUGIN_DATA}";
        var remaining = value.AsSpan();
        var output = new StringBuilder(value.Length);
        while (!remaining.IsEmpty)
        {
            var rootIndex = pluginRoot is null ? -1 : remaining.IndexOf(rootToken, StringComparison.Ordinal);
            var dataIndex = pluginDataPath is null ? -1 : remaining.IndexOf(dataToken, StringComparison.Ordinal);
            if (rootIndex < 0 && dataIndex < 0)
            {
                output.Append(remaining);
                break;
            }

            var isRoot = rootIndex >= 0 && (dataIndex < 0 || rootIndex <= dataIndex);
            var index = isRoot ? rootIndex : dataIndex;
            var token = isRoot ? rootToken : dataToken;
            output.Append(remaining[..index]);
            output.Append(isRoot ? pluginRoot : pluginDataPath);
            remaining = remaining[(index + token.Length)..];
        }

        return output.ToString();
    }
}
