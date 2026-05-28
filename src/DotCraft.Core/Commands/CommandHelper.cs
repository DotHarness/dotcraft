using DotCraft.Localization;

namespace DotCraft.Commands;

public static class CommandHelper
{
    public static string? FindSimilarCommand(string input, string[] candidates)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var distance = LevenshteinDistance(input, candidate);
            if (distance < bestDistance && distance <= 2)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    public static string FormatUnknownCommandMessage(string rawInput, string[] knownCommands)
    {
        var inputCmd = rawInput.Split(' ', 2)[0].ToLowerInvariant();
        var suggestion = FindSimilarCommand(inputCmd, knownCommands);
        return suggestion != null
            ? $"{Strings.UnknownCommand}：{rawInput.Split(' ', 2)[0]}，{Strings.DidYouMean} {suggestion}？{Strings.ViewAllCommands}"
            : $"{Strings.UnknownCommand}：{rawInput.Split(' ', 2)[0]}，{Strings.ViewAllCommands}";
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }
}
