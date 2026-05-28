using Spectre.Console;

namespace DotCraft.Diagnostics;

/// <summary>
/// 消息格式化工具，提供统一的颜色和样式输出
/// </summary>
public static class MessageFormatter
{
    /// <summary>
    /// 输出错误消息（红色）
    /// </summary>
    public static void Error(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {message}");
    }

    /// <summary>
    /// 输出警告消息（黄色）
    /// </summary>
    public static void Warning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]Warning:[/] {message}");
    }

    /// <summary>
    /// 输出成功消息（绿色）
    /// </summary>
    public static void Success(string message)
    {
        AnsiConsole.MarkupLine($"[green]Success:[/] {message}");
    }

    /// <summary>
    /// 输出信息消息（蓝色）
    /// </summary>
    public static void Info(string message)
    {
        AnsiConsole.MarkupLine($"[blue]Info:[/] {message}");
    }

    /// <summary>
    /// Outputs a tool call line with icon and human-readable description.
    /// </summary>
    public static void ToolCall(string icon, string displayText)
    {
        AnsiConsole.MarkupLine($"[yellow]{EscapeMarkup($"{icon} {displayText}")}[/]");
    }

    /// <summary>
    /// Outputs tool result as an indented sub-line.
    /// </summary>
    public static void ToolResult(string result)
    {
        var display = result.Length > 200 ? result[..200] + "..." : result;
        var normalized = display.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        AnsiConsole.MarkupLine($"  [grey]{EscapeMarkup(normalized)}[/]");
    }

    /// <summary>
    /// 输出子代理信息（紫色）
    /// </summary>
    public static void SubAgent(string taskId, string label)
    {
        AnsiConsole.MarkupLine($"[purple]🐧 SubAgent[[[dim]{taskId}[/]]]:[/] {EscapeMarkup(label)}");
    }

    /// <summary>
    /// 输出子代理完成信息
    /// </summary>
    public static void SubAgentCompleted(string taskId)
    {
        AnsiConsole.MarkupLine($"[green]✓ SubAgent [[[dim]{taskId}[/]]] completed[/]");
    }

    /// <summary>
    /// 输出子代理失败信息
    /// </summary>
    public static void SubAgentFailed(string taskId, string error)
    {
        AnsiConsole.MarkupLine($"[red]✗ SubAgent [[[dim]{taskId}[/]]] failed:[/] {EscapeMarkup(error)}");
    }

    /// <summary>
    /// 转义 Spectre.Console 标记字符
    /// </summary>
    private static string EscapeMarkup(this string text)
    {
        return Markup.Escape(text);
    }
}
