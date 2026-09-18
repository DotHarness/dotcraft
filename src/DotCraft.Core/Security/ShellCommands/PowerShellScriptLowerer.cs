using System.Globalization;
using System.Management.Automation.Language;

namespace DotCraft.Security.ShellCommands;

public sealed class PowerShellScriptLowerer : IShellScriptLowerer
{
    private const string StopParsingToken = "--%";

    private const int ReasonTextLimit = 60;

    public ShellFamily Family => ShellFamily.PowerShell;

    public LoweredScript Lower(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return LoweredScript.Opaque(ShellFamily.PowerShell, "PowerShell script is empty.");
        }

        var root = Parser.ParseInput(script, out _, out var errors);
        var nodes = root.FindAll(_ => true, searchNestedScriptBlocks: true).ToList();

        if (errors.Length > 0)
        {
            return Opaque($"PowerShell script does not parse: {Snippet(errors[0].Message)}", nodes);
        }

        if (RejectedBlock(root) is { } blockReason)
        {
            return Opaque(blockReason, nodes);
        }

        var commands = new List<IReadOnlyList<string>>();
        foreach (var node in nodes)
        {
            if (!IsAllowedNode(node))
            {
                return Opaque(Describe(node), nodes);
            }

            if (node is not CommandAst command)
            {
                continue;
            }

            if (!TryLowerCommand(command, out var words, out var commandReason))
            {
                return Opaque(commandReason, nodes);
            }

            commands.Add(words);
        }

        return commands.Count == 0
            ? Opaque("PowerShell script contains no commands.", nodes)
            : LoweredScript.Plain(ShellFamily.PowerShell, commands);
    }

    private static string? RejectedBlock(ScriptBlockAst root) => root switch
    {
        { ScriptRequirements: not null } => "PowerShell script declares #requires, which runs before the script body.",
        { UsingStatements.Count: > 0 } => "PowerShell script declares a using statement, which runs before the script body.",
        { ParamBlock: not null } => "PowerShell script declares a param block, which binds values before the script body.",
        { DynamicParamBlock: not null } => "PowerShell script declares a dynamicparam block, which runs before the script body.",
        { BeginBlock: not null } => "PowerShell script declares a begin block, which runs outside the statement list.",
        { ProcessBlock: not null } => "PowerShell script declares a process block, which runs outside the statement list.",
        { CleanBlock: not null } => "PowerShell script declares a clean block, which runs outside the statement list.",
        { EndBlock.Traps.Count: > 0 } => "PowerShell script declares a trap block, which runs outside the statement list.",
        _ => null
    };

    private static bool IsAllowedNode(Ast node) => node switch
    {
        ScriptBlockAst or NamedBlockAst or PipelineChainAst or PipelineAst or CommandAst
            or StringConstantExpressionAst or CommandParameterAst => true,
        ConstantExpressionAst number => TryPlainInteger(number, out _),
        _ => false
    };

    private static bool TryLowerCommand(CommandAst command, out IReadOnlyList<string> words, out string reason)
    {
        words = [];
        reason = string.Empty;

        if (command.InvocationOperator != TokenKind.Unknown)
        {
            var operatorText = command.InvocationOperator == TokenKind.Dot ? "." : "&";
            reason = $"PowerShell script invokes '{Snippet(command.Extent.Text)}' with the '{operatorText}' operator, which changes what runs.";
            return false;
        }

        if (command.Redirections.Count > 0)
        {
            reason = $"PowerShell script redirects with '{Snippet(command.Redirections[0].Extent.Text)}', which writes outside the command.";
            return false;
        }

        var lowered = new List<string>();
        foreach (var element in command.CommandElements)
        {
            if (!TryLowerElement(element, lowered, out reason))
            {
                return false;
            }
        }

        if (lowered.Count == 0)
        {
            reason = $"PowerShell script has an empty command name in '{Snippet(command.Extent.Text)}'.";
            return false;
        }

        words = lowered;
        return true;
    }

    private static bool TryLowerElement(CommandElementAst element, List<string> words, out string reason)
    {
        reason = string.Empty;
        switch (element)
        {
            case StringConstantExpressionAst { Value: StopParsingToken }:
                reason = "PowerShell script uses the stop-parsing token '--%', which passes the rest of the line through unparsed.";
                return false;
            case StringConstantExpressionAst { Value.Length: 0 }:
                reason = $"PowerShell script uses the empty word '{Snippet(element.Extent.Text)}', which stands for no command or argument.";
                return false;
            case StringConstantExpressionAst text:
                words.Add(text.Value);
                return true;
            case CommandParameterAst parameter:
                return TryLowerParameter(parameter, words, out reason);
            case ConstantExpressionAst number when TryPlainInteger(number, out var literal):
                words.Add(literal);
                return true;
            default:
                reason = Describe(element);
                return false;
        }
    }

    private static bool TryLowerParameter(CommandParameterAst parameter, List<string> words, out string reason)
    {
        reason = string.Empty;

        if (string.IsNullOrEmpty(parameter.ParameterName))
        {
            reason = $"PowerShell script uses the empty parameter '{Snippet(parameter.Extent.Text)}'.";
            return false;
        }

        if (parameter.Argument is null)
        {
            words.Add("-" + parameter.ParameterName);
            return true;
        }

        if (parameter.Argument is not StringConstantExpressionAst { Value.Length: > 0 } argument)
        {
            reason = $"PowerShell script attaches '{Snippet(parameter.Argument.Extent.Text)}' to '-{parameter.ParameterName}', which is not a literal string.";
            return false;
        }

        words.Add("-" + parameter.ParameterName + ":");
        words.Add(argument.Value);
        return true;
    }

    private static bool TryPlainInteger(ConstantExpressionAst node, out string literal)
    {
        literal = node.Extent.Text;
        if (node.Value is null || !IsDecimalInteger(literal))
        {
            return false;
        }

        return string.Equals(Convert.ToString(node.Value, CultureInfo.InvariantCulture), literal, StringComparison.Ordinal);
    }

    private static bool IsDecimalInteger(string text)
    {
        var start = text.StartsWith('-') ? 1 : 0;
        if (start >= text.Length)
        {
            return false;
        }

        for (var i = start; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static LoweredScript Opaque(string reason, IReadOnlyList<Ast> nodes) =>
        LoweredScript.Opaque(ShellFamily.PowerShell, reason, ExtractLiterals(nodes));

    private static IReadOnlyList<IReadOnlyList<string>> ExtractLiterals(IReadOnlyList<Ast> nodes)
    {
        var commands = new List<IReadOnlyList<string>>();
        foreach (var node in nodes)
        {
            if (node is not CommandAst command
                || command.CommandElements.Count == 0
                || command.CommandElements[0] is not StringConstantExpressionAst { Value.Length: > 0 } name)
            {
                continue;
            }

            var words = new List<string> { name.Value };
            for (var i = 1; i < command.CommandElements.Count; i++)
            {
                AppendLiteral(command.CommandElements[i], words);
            }

            commands.Add(words);
        }

        return commands;
    }

    private static void AppendLiteral(CommandElementAst element, List<string> words)
    {
        switch (element)
        {
            case StringConstantExpressionAst { Value.Length: > 0 } text:
                words.Add(text.Value);
                break;
            case ConstantExpressionAst number when TryPlainInteger(number, out var literal):
                words.Add(literal);
                break;
            case CommandParameterAst { ParameterName.Length: > 0 } parameter:
                if (parameter.Argument is StringConstantExpressionAst { Value.Length: > 0 } argument)
                {
                    words.Add("-" + parameter.ParameterName + ":");
                    words.Add(argument.Value);
                }
                else
                {
                    words.Add("-" + parameter.ParameterName);
                }

                break;
        }
    }

    private static string Describe(Ast node) =>
        $"PowerShell script uses '{Snippet(node.Extent.Text)}' ({node.GetType().Name}), which cannot be lowered to literal commands.";

    private static string Snippet(string text)
    {
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= ReasonTextLimit ? collapsed : collapsed[..ReasonTextLimit] + "...";
    }
}
