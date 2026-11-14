using System.Text.RegularExpressions;

namespace DotCraft.CodexGateway.Workspace;

internal sealed partial class WorkspaceProjector
{
    private static object[] CommandActions(string command)
    {
        object[] Unknown() => [new { type = "unknown", command }];
        if (command.IndexOfAny([';', '|', '&', '<', '>', '\n', '\r', '`', '$']) >= 0) return Unknown();
        var matches = Regex.Matches(command, "\"[^\"]*\"|'[^']*'|[^\\s\"']+");
        var offset = 0;
        foreach (Match match in matches)
        {
            if (!string.IsNullOrWhiteSpace(command[offset..match.Index])) return Unknown();
            offset = match.Index + match.Length;
        }
        if (!string.IsNullOrWhiteSpace(command[offset..])) return Unknown();
        var words = matches.Select(match => match.Value.Trim('\"', '\'')).ToArray();
        if (words.Length == 0) return Unknown();
        var executable = words[0].ToLowerInvariant();
        var args = words.Skip(1).ToArray();
        if (executable is "cat" or "type" or "get-content")
        {
            if (args.Length == 2 && args[0].Equals("-LiteralPath", StringComparison.OrdinalIgnoreCase)) args = args[1..];
            if (args.Length == 1 && !args[0].StartsWith('-'))
                return [new { type = "read", command, name = Path.GetFileName(args[0]), path = args[0] }];
        }
        if (executable is "ls" or "dir" or "get-childitem")
        {
            if (args.Length == 0 || args.Length == 1 && !args[0].StartsWith('-'))
                return [new { type = "listFiles", command, path = args.FirstOrDefault() }];
        }
        if (executable is "rg" or "grep")
        {
            while (args.Length > 0 && args[0] is "-n" or "--line-number" or "-i" or "--ignore-case") args = args[1..];
            if (args.Length is 1 or 2 && args.All(arg => !arg.StartsWith('-')))
                return [new { type = "search", command, query = args[0], path = args.Length == 2 ? args[1] : null }];
        }
        return Unknown();
    }
}
