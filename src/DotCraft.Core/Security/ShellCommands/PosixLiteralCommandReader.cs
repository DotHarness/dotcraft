using System.Text;

namespace DotCraft.Security.ShellCommands;

internal static class PosixLiteralCommandReader
{
    private static readonly HashSet<string> SegmentStarters = new(StringComparer.Ordinal)
    {
        "if", "then", "elif", "else", "while", "until", "do", "time", "!"
    };

    private static readonly HashSet<string> ControlSegments = new(StringComparer.Ordinal)
    {
        "fi", "done", "esac", "for", "case", "in", "select"
    };

    public static IReadOnlyList<IReadOnlyList<string>> Read(string script) => new Reader(script).Run();

    internal static bool IsAssignment(string word)
    {
        var separator = word.IndexOf('=');
        if (separator <= 0 || (!char.IsAsciiLetter(word[0]) && word[0] != '_'))
            return false;

        var nameEnd = word[separator - 1] == '+' ? separator - 1 : separator;
        for (var i = 1; i < nameEnd; i++)
        {
            if (!char.IsAsciiLetterOrDigit(word[i]) && word[i] != '_')
                return false;
        }

        return true;
    }

    private enum Quote
    {
        None,
        Single,
        Double
    }

    private readonly record struct Word(string Value, bool IsLiteral);

    private sealed class Reader(string script)
    {
        private readonly List<IReadOnlyList<string>> _commands = [];
        private readonly List<Word> _segment = [];
        private readonly StringBuilder _word = new();
        private readonly Stack<Quote> _substitutions = new();
        private Quote _quote = Quote.None;
        private bool _wordStarted;
        private bool _wordLiteral = true;
        private bool _dropWord;
        private bool _inBacktick;
        private Quote _backtickQuote = Quote.None;
        private int _index;

        public IReadOnlyList<IReadOnlyList<string>> Run()
        {
            while (_index < script.Length)
            {
                switch (_quote)
                {
                    case Quote.Single:
                        ReadSingleQuoted();
                        break;
                    case Quote.Double:
                        ReadDoubleQuoted();
                        break;
                    default:
                        ReadUnquoted();
                        break;
                }
            }

            FlushWord();
            EndSegment();
            return _commands;
        }

        private void ReadSingleQuoted()
        {
            var c = script[_index++];
            if (c == '\'')
                _quote = Quote.None;
            else
                Append(c);
        }

        private void ReadDoubleQuoted()
        {
            var c = script[_index];
            switch (c)
            {
                case '"':
                    _quote = Quote.None;
                    _index++;
                    return;
                case '$' when Next() == '(':
                    OpenSubstitution();
                    return;
                case '`':
                    ToggleBacktick();
                    return;
                case '$' or '\\':
                    MarkNonLiteral();
                    _index++;
                    return;
                default:
                    Append(c);
                    _index++;
                    return;
            }
        }

        private void ReadUnquoted()
        {
            var c = script[_index];
            switch (c)
            {
                case '\'':
                    _quote = Quote.Single;
                    _wordStarted = true;
                    _index++;
                    return;
                case '"':
                    _quote = Quote.Double;
                    _wordStarted = true;
                    _index++;
                    return;
                case '$' when Next() == '(':
                    OpenSubstitution();
                    return;
                case '`':
                    ToggleBacktick();
                    return;
                case '$':
                    MarkNonLiteral();
                    _index++;
                    return;
                case '\\':
                    MarkNonLiteral();
                    _index += _index + 1 < script.Length ? 2 : 1;
                    return;
                case '\n' or '\r' or ';' or '|' or '&' or '(' or '{' or '}':
                    FlushWord();
                    EndSegment();
                    _index++;
                    return;
                case ')':
                    FlushWord();
                    EndSegment();
                    if (_substitutions.Count > 0)
                        _quote = _substitutions.Pop();
                    _index++;
                    return;
                case '<' or '>':
                    SkipRedirection();
                    return;
                default:
                    if (char.IsWhiteSpace(c))
                        FlushWord();
                    else
                        Append(c);
                    _index++;
                    return;
            }
        }

        private void OpenSubstitution()
        {
            MarkNonLiteral();
            FlushWord();
            EndSegment();
            _substitutions.Push(_quote);
            _quote = Quote.None;
            _index += 2;
        }

        private void ToggleBacktick()
        {
            MarkNonLiteral();
            FlushWord();
            EndSegment();
            if (_inBacktick)
            {
                _quote = _backtickQuote;
                _inBacktick = false;
            }
            else
            {
                _backtickQuote = _quote;
                _quote = Quote.None;
                _inBacktick = true;
            }

            _index++;
        }

        private void SkipRedirection()
        {
            if (_wordLiteral && _word.Length > 0 && _word.ToString().All(char.IsAsciiDigit))
            {
                _word.Clear();
                _wordStarted = false;
            }

            FlushWord();
            while (_index < script.Length && script[_index] is '<' or '>' or '&' or '|')
                _index++;
            while (_index < script.Length && script[_index] is ' ' or '\t')
                _index++;
            _dropWord = true;
        }

        private char Next() => _index + 1 < script.Length ? script[_index + 1] : '\0';

        private void Append(char value)
        {
            _word.Append(value);
            _wordStarted = true;
        }

        private void MarkNonLiteral()
        {
            _wordStarted = true;
            _wordLiteral = false;
        }

        private void FlushWord()
        {
            if (_wordStarted && !_dropWord)
                _segment.Add(new Word(_word.ToString(), _wordLiteral));

            _word.Clear();
            _wordStarted = false;
            _wordLiteral = true;
            _dropWord = false;
        }

        private void EndSegment()
        {
            if (_segment.Count == 0)
                return;

            var words = _segment.ToArray();
            _segment.Clear();

            var start = 0;
            while (start < words.Length
                   && words[start].IsLiteral
                   && (SegmentStarters.Contains(words[start].Value) || IsAssignment(words[start].Value)))
                start++;

            if (start == words.Length
                || !words[start].IsLiteral
                || ControlSegments.Contains(words[start].Value))
                return;

            var command = new List<string>();
            for (var i = start; i < words.Length; i++)
            {
                if (words[i].IsLiteral)
                    command.Add(words[i].Value);
            }

            _commands.Add(command);
        }
    }
}
