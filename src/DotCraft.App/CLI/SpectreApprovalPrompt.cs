using DotCraft.Security;
using DotCraft.Text;
using Spectre.Console;

namespace DotCraft.CLI;

internal sealed class SpectreApprovalPrompt : IInteractiveApprovalPrompt
{
    public InteractiveApprovalDecision RequestFileApproval(string operation, string path)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
            $"[yellow]{FallbackText.Format("approval.file.operation")}[/] {Markup.Escape(operation)}\n" +
            $"[yellow]{FallbackText.Format("approval.file.path")}[/] {Markup.Escape(path)}")
        {
            Header = new PanelHeader($"[yellow]{FallbackText.Format("approval.file.title")}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        });

        return Prompt($"[green]{FallbackText.Format("approval.file.approve_question")}[/]");
    }

    public InteractiveApprovalDecision RequestShellApproval(string command, string? workingDirectory)
    {
        AnsiConsole.WriteLine();
        var message = $"[yellow]{FallbackText.Format("approval.shell.command")}[/] {Markup.Escape(command)}";
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            message += $"\n[yellow]{FallbackText.Format("approval.shell.working_dir")}[/] " +
                       Markup.Escape(workingDirectory);
        }

        AnsiConsole.Write(new Panel(message)
        {
            Header = new PanelHeader($"[yellow]{FallbackText.Format("approval.shell.title")}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        });

        return Prompt($"[green]{FallbackText.Format("approval.shell.approve_question")}[/]");
    }

    private static InteractiveApprovalDecision Prompt(string title)
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<InteractiveApprovalDecision>()
                .Title(title)
                .AddChoices(
                    InteractiveApprovalDecision.Once,
                    InteractiveApprovalDecision.Session,
                    InteractiveApprovalDecision.Always,
                    InteractiveApprovalDecision.Reject)
                .UseConverter(ToDisplayText)
                .PageSize(4));

        var color = choice == InteractiveApprovalDecision.Reject ? "red" : "green";
        AnsiConsole.MarkupLine($"[{color}]{FallbackText.Format(ResultKey(choice))}[/]");
        AnsiConsole.WriteLine();
        return choice;
    }

    private static string ToDisplayText(InteractiveApprovalDecision decision) => decision switch
    {
        InteractiveApprovalDecision.Once => FallbackText.Format("approval.option.once"),
        InteractiveApprovalDecision.Session => FallbackText.Format("approval.option.session"),
        InteractiveApprovalDecision.Always => FallbackText.Format("approval.option.always"),
        InteractiveApprovalDecision.Reject => FallbackText.Format("approval.option.reject"),
        _ => decision.ToString()
    };

    private static string ResultKey(InteractiveApprovalDecision decision) => decision switch
    {
        InteractiveApprovalDecision.Once => "approval.result.once",
        InteractiveApprovalDecision.Session => "approval.result.session",
        InteractiveApprovalDecision.Always => "approval.result.always",
        _ => "approval.result.reject"
    };
}
