using Spectre.Console;

namespace DotCraft.CLI;

internal static class StringExtensions
{
    public static string EscapeMarkup(this string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Markup.Escape(text);
    }
}