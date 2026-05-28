using DotCraft.Localization;
using Spectre.Console;

namespace DotCraft.Security;

/// <summary>
/// 审批选项枚举
/// </summary>
public enum ApprovalOption
{
    Once,      // 仅此一次
    Session,   // 本次会话
    Always,    // 永久
    Reject     // 拒绝
}

/// <summary>
/// 审批提示工具类，封装SelectionPrompt交互逻辑
/// </summary>
public static class ApprovalPrompt
{
    /// <summary>
    /// 请求文件操作审批
    /// </summary>
    /// <param name="operation">操作名称（read, write, edit, list）</param>
    /// <param name="path">文件路径</param>
    /// <returns>审批选项</returns>
    public static ApprovalOption RequestFileApproval(string operation, string path)
    {
        var t = LanguageService.Current;
        AnsiConsole.WriteLine();
        var panel = new Panel($"[yellow]{t.T("approval.file.operation")}[/] {EscapeMarkup(operation)}\n[yellow]{t.T("approval.file.path")}[/] {EscapeMarkup(path)}")
        {
            Header = new PanelHeader($"[yellow]{t.T("approval.file.title")}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };
        AnsiConsole.Write(panel);

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<ApprovalOption>()
                .Title($"[green]{t.T("approval.file.approve_question")}[/]")
                .AddChoices(
                    ApprovalOption.Once,
                    ApprovalOption.Session,
                    ApprovalOption.Always,
                    ApprovalOption.Reject)
                .UseConverter(option => option switch
                {
                    ApprovalOption.Once => t.T("approval.option.once"),
                    ApprovalOption.Session => t.T("approval.option.session"),
                    ApprovalOption.Always => t.T("approval.option.always"),
                    ApprovalOption.Reject => t.T("approval.option.reject"),
                    _ => option.ToString()
                })
                .PageSize(4));

        DisplayResult(choice);
        return choice;
    }

    /// <summary>
    /// 请求Shell命令审批
    /// </summary>
    /// <param name="command">Shell命令</param>
    /// <param name="workingDir">工作目录</param>
    /// <returns>审批选项</returns>
    public static ApprovalOption RequestShellApproval(string command, string? workingDir)
    {
        var t = LanguageService.Current;
        AnsiConsole.WriteLine();
        var message = $"[yellow]{t.T("approval.shell.command")}[/] {EscapeMarkup(command)}";
        if (!string.IsNullOrWhiteSpace(workingDir))
        {
            message += $"\n[yellow]{t.T("approval.shell.working_dir")}[/] {EscapeMarkup(workingDir)}";
        }

        var panel = new Panel(message)
        {
            Header = new PanelHeader($"[yellow]{t.T("approval.shell.title")}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };
        AnsiConsole.Write(panel);

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<ApprovalOption>()
                .Title($"[green]{t.T("approval.shell.approve_question")}[/]")
                .AddChoices(
                    ApprovalOption.Once,
                    ApprovalOption.Session,
                    ApprovalOption.Always,
                    ApprovalOption.Reject)
                .UseConverter(option => option switch
                {
                    ApprovalOption.Once => t.T("approval.option.once"),
                    ApprovalOption.Session => t.T("approval.option.session"),
                    ApprovalOption.Always => t.T("approval.option.always"),
                    ApprovalOption.Reject => t.T("approval.option.reject"),
                    _ => option.ToString()
                })
                .PageSize(4));

        DisplayResult(choice);
        return choice;
    }

    /// <summary>
    /// 显示审批结果
    /// </summary>
    private static void DisplayResult(ApprovalOption option)
    {
        var t = LanguageService.Current;
        switch (option)
        {
            case ApprovalOption.Once:
                AnsiConsole.MarkupLine($"[green]{t.T("approval.result.once")}[/]");
                break;
            case ApprovalOption.Session:
                AnsiConsole.MarkupLine($"[green]{t.T("approval.result.session")}[/]");
                break;
            case ApprovalOption.Always:
                AnsiConsole.MarkupLine($"[green]{t.T("approval.result.always")}[/]");
                break;
            case ApprovalOption.Reject:
                AnsiConsole.MarkupLine($"[red]{t.T("approval.result.reject")}[/]");
                break;
        }
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// 转义 Spectre.Console 标记字符
    /// </summary>
    private static string EscapeMarkup(this string text)
    {
        return Markup.Escape(text);
    }
}
