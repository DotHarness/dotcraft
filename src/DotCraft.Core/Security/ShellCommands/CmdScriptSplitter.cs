using System.Text;

namespace DotCraft.Security.ShellCommands;

public sealed class CmdScriptSplitter : IShellScriptLowerer
{
    private const string NotLoweredReason = "cmd scripts are not lowered.";

    public ShellFamily Family => ShellFamily.Cmd;

    public LoweredScript Lower(string script) =>
        LoweredScript.Opaque(ShellFamily.Cmd, NotLoweredReason, Split(script));

    private static IReadOnlyList<IReadOnlyList<string>> Split(string script)
    {
        var commands = new List<IReadOnlyList<string>>();
        var words = new List<string>();
        var word = new StringBuilder();
        var started = false;
        var quoted = false;

        for (var index = 0; index < script.Length; index++)
        {
            var c = script[index];
            if (quoted)
            {
                if (c == '"')
                    quoted = false;
                else
                    word.Append(c);

                started = true;
                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    started = true;
                    break;
                case '^':
                    if (index + 1 < script.Length)
                    {
                        word.Append(script[++index]);
                        started = true;
                    }

                    break;
                case '&' or '|':
                    FlushWord();
                    EndSegment();
                    if (index + 1 < script.Length && script[index + 1] == c)
                        index++;

                    break;
                case '\n' or '\r':
                    FlushWord();
                    EndSegment();
                    break;
                default:
                    if (char.IsWhiteSpace(c))
                        FlushWord();
                    else
                    {
                        word.Append(c);
                        started = true;
                    }

                    break;
            }
        }

        FlushWord();
        EndSegment();
        return commands;

        void FlushWord()
        {
            if (!started)
                return;

            words.Add(word.ToString());
            word.Clear();
            started = false;
        }

        void EndSegment()
        {
            if (words.Count == 0)
                return;

            commands.Add(words);
            words = [];
        }
    }
}
