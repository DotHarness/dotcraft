using System.Text.RegularExpressions;

namespace DotCraft.Tools.BackgroundTerminals;

internal static partial class TerminalPromptDetector
{
    public static bool LooksLikePrompt(string tail)
    {
        var trimmed = tail.TrimEnd();
        var lastLine = trimmed[(trimmed.LastIndexOf('\n') + 1)..].Trim();
        return lastLine.Length > 0 && PromptPattern().IsMatch(lastLine);
    }

    [GeneratedRegex(
        @"\((?:y/n|yes/no)\)|\[y/n\]|\b(?:Do you|Would you|Shall I|Are you sure|Ready to)\b.*\?\s*$|Press (?:any key|Enter)|Continue\?|Overwrite\?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PromptPattern();
}
