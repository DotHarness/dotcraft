using System.Text;

namespace DotCraft.Security.ShellCommands;

public sealed class PosixScriptLowerer : IShellScriptLowerer
{
    public ShellFamily Family => ShellFamily.Posix;

    public LoweredScript Lower(string script)
    {
        var commands = TryLowerPlain(script, out var reason);
        return commands is null
            ? LoweredScript.Opaque(ShellFamily.Posix, reason, PosixLiteralCommandReader.Read(script))
            : LoweredScript.Plain(ShellFamily.Posix, commands);
    }

    private static List<IReadOnlyList<string>>? TryLowerPlain(string script, out string reason)
    {
        reason = string.Empty;
        var tokens = new List<PlainToken>();
        var index = 0;
        var atCommandStart = true;

        while (index < script.Length)
        {
            var c = script[index];
            if (c is '\n' or '\r')
            {
                index += c == '\r' && index + 1 < script.Length && script[index + 1] == '\n' ? 2 : 1;
                tokens.Add(PlainToken.ForSeparator(SeparatorKind.Newline));
                atCommandStart = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                index++;
                continue;
            }

            if (c is ';' or '|' or '&')
            {
                var separator = ReadSeparator(script, ref index, out reason);
                if (separator is null)
                    return null;

                tokens.Add(PlainToken.ForSeparator(separator.Value));
                atCommandStart = true;
                continue;
            }

            var word = ReadWord(script, ref index, atCommandStart, out reason);
            if (word is null)
                return null;

            tokens.Add(PlainToken.ForWord(word));
            atCommandStart = false;
        }

        return BuildCommands(tokens, out reason);
    }

    private static SeparatorKind? ReadSeparator(string script, ref int index, out string reason)
    {
        reason = string.Empty;
        var c = script[index];
        var doubled = index + 1 < script.Length && script[index + 1] == c;
        switch (c)
        {
            case ';':
                index++;
                return SeparatorKind.Semicolon;
            case '|':
                index += doubled ? 2 : 1;
                return SeparatorKind.Operator;
            case '&' when doubled:
                index += 2;
                return SeparatorKind.Operator;
            default:
                reason = PosixRejectReasons.Background;
                return null;
        }
    }

    private static string? ReadWord(string script, ref int index, bool atCommandStart, out string reason)
    {
        reason = string.Empty;
        var value = new StringBuilder();
        var start = index;

        while (index < script.Length)
        {
            var c = script[index];
            if (char.IsWhiteSpace(c) || c is ';' or '|' or '&')
                break;

            if (c == '\'')
            {
                var close = script.IndexOf('\'', index + 1);
                if (close < 0)
                {
                    reason = PosixRejectReasons.UnbalancedQuotes;
                    return null;
                }

                value.Append(script, index + 1, close - index - 1);
                index = close + 1;
                continue;
            }

            if (c == '"')
            {
                if (!ReadDoubleQuoted(script, ref index, value, out reason))
                    return null;

                continue;
            }

            if (!ReadBare(script, ref index, value, index == start, out reason))
                return null;
        }

        var word = value.ToString();
        if (!atCommandStart)
            return word;

        if (PosixLiteralCommandReader.IsAssignment(word))
        {
            reason = PosixRejectReasons.AssignmentPrefix;
            return null;
        }

        if (ReservedWords.Contains(word))
        {
            reason = PosixRejectReasons.ReservedWord(word);
            return null;
        }

        return word;
    }

    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        "!", "case", "coproc", "do", "done", "elif", "else", "esac", "fi",
        "for", "function", "if", "in", "select", "then", "time", "until", "while"
    };

    private const string RejectedBareCharacters = "{}*?[]~^#<>()";

    private static bool ReadBare(string script, ref int index, StringBuilder value, bool isWordStart, out string reason)
    {
        reason = string.Empty;
        var first = isWordStart;

        while (index < script.Length)
        {
            var c = script[index];
            if (char.IsWhiteSpace(c) || c is '\'' or '"' or ';' or '|' or '&')
                return true;

            if (c is '$' or '`')
            {
                reason = PosixRejectReasons.Expansion;
                return false;
            }

            if (c == '\\')
            {
                reason = index + 1 < script.Length && script[index + 1] is '"' or '\''
                    ? PosixRejectReasons.EscapedQuote
                    : PosixRejectReasons.DisallowedCharacter('\\');
                return false;
            }

            // zsh expands a leading `=` to a program the script never names.
            if ((first && c == '=') || RejectedBareCharacters.Contains(c, StringComparison.Ordinal))
            {
                reason = PosixRejectReasons.DisallowedCharacter(c);
                return false;
            }

            value.Append(c);
            index++;
            first = false;
        }

        return true;
    }

    private static bool ReadDoubleQuoted(string script, ref int index, StringBuilder value, out string reason)
    {
        reason = string.Empty;

        for (var i = index + 1; i < script.Length; i++)
        {
            var c = script[i];
            if (c == '"')
            {
                index = i + 1;
                return true;
            }

            if (c is '$' or '`')
            {
                reason = PosixRejectReasons.Expansion;
                return false;
            }

            if (c == '\\')
            {
                reason = i + 1 < script.Length && script[i + 1] is '"' or '\''
                    ? PosixRejectReasons.EscapedQuote
                    : PosixRejectReasons.BackslashEscape;
                return false;
            }

            value.Append(c);
        }

        reason = PosixRejectReasons.UnbalancedQuotes;
        return false;
    }

    private static List<IReadOnlyList<string>>? BuildCommands(List<PlainToken> tokens, out string reason)
    {
        reason = string.Empty;
        var normalized = DropTerminators(tokens);
        if (normalized.Count == 0)
        {
            reason = PosixRejectReasons.EmptyScript;
            return null;
        }

        var commands = new List<IReadOnlyList<string>>();
        var current = new List<string>();
        foreach (var token in normalized)
        {
            if (token.Word is not null)
            {
                current.Add(token.Word);
                continue;
            }

            if (current.Count == 0)
            {
                reason = PosixRejectReasons.EmptyCommandPosition;
                return null;
            }

            commands.Add(current);
            current = [];
        }

        if (current.Count == 0)
        {
            reason = PosixRejectReasons.EmptyCommandPosition;
            return null;
        }

        commands.Add(current);
        return commands;
    }

    private static List<PlainToken> DropTerminators(List<PlainToken> tokens)
    {
        var normalized = new List<PlainToken>(tokens.Count);
        foreach (var token in tokens)
        {
            if (token.Word is null
                && token.Separator == SeparatorKind.Newline
                && (normalized.Count == 0 || normalized[^1].Word is null))
                continue;

            normalized.Add(token);
        }

        if (normalized.Count >= 2
            && normalized[^1] is { Word: null, Separator: SeparatorKind.Semicolon }
            && normalized[^2].Word is not null)
            normalized.RemoveAt(normalized.Count - 1);

        if (normalized.Count > 0 && normalized[^1] is { Word: null, Separator: SeparatorKind.Newline })
            normalized.RemoveAt(normalized.Count - 1);

        return normalized;
    }

    private enum SeparatorKind
    {
        None,
        Newline,
        Semicolon,
        Operator
    }

    private readonly record struct PlainToken(string? Word, SeparatorKind Separator)
    {
        public static PlainToken ForWord(string word) => new(word, SeparatorKind.None);

        public static PlainToken ForSeparator(SeparatorKind separator) => new(null, separator);
    }
}

internal static class PosixRejectReasons
{
    public const string EmptyScript = "requires a command that can be classified.";

    public const string Expansion =
        "denies command substitution and expansion because the resulting command cannot be classified.";

    public const string EscapedQuote =
        "denies escaped quotes because they make the command's quoting ambiguous.";

    public const string BackslashEscape =
        "denies a backslash inside double quotes because the escape it introduces cannot be classified. "
        + "Single-quote the argument to use it literally.";

    public const string Background = "denies background execution.";

    public const string UnbalancedQuotes = "denied the command because its quoting is unbalanced.";

    public const string EmptyCommandPosition = "denies an empty command position.";

    public const string AssignmentPrefix =
        "denies a variable assignment prefix because it changes the environment of the command.";

    public static string ReservedWord(string value) =>
        $"denies the shell keyword '{value}' because the command it introduces cannot be classified.";

    public static string DisallowedCharacter(char value) =>
        $"denies '{value}' outside quotes because its shell meaning cannot be classified. "
        + "Single-quote the argument to use it literally.";
}
