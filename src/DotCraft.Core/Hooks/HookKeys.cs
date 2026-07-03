using System.Text;

namespace DotCraft.Hooks;

/// <summary>
/// Helpers for stable hook keys and event labels.
/// </summary>
public static class HookKeys
{
    public static readonly IReadOnlyList<string> ValidEventNames =
        Enum.GetNames<HookEvent>();

    public static string ForConfig(string sourcePath, string eventName, int groupIndex, int hookIndex) =>
        $"{sourcePath}:{ToSnakeCase(eventName)}:{groupIndex}:{hookIndex}";

    public static string ForPlugin(string pluginId, string sourceRelativePath, string eventName, int groupIndex, int hookIndex) =>
        $"{pluginId}:{sourceRelativePath}:{ToSnakeCase(eventName)}:{groupIndex}:{hookIndex}";

    public static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch))
            {
                if (i > 0)
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
