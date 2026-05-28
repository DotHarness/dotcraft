using System.Text;
using Spectre.Console;

namespace DotCraft.CLI;

/// <summary>
/// Prepares the process environment for modes that reserve stdout for a wire protocol
/// (stdio-based JSON-RPC). This involves:
/// <list type="bullet">
/// <item>Redirecting <see cref="AnsiConsole"/> output to stderr so Spectre.Console
/// markup does not pollute the JSON-RPC transport on stdout.</item>
/// <item>Redirecting <see cref="Console.Out"/> to stderr so that any
/// <c>Console.WriteLine</c> calls also go to stderr.</item>
/// <item>Disabling the Ctrl+C / SIGINT handler via <see cref="ConsoleSignalGuard"/>
/// so the subprocess lifecycle is controlled solely by stdin EOF or explicit shutdown.</item>
/// </list>
///
/// <para>
/// Both ACP and AppServer (when running in stdio mode) share this setup.
/// Pure-WebSocket AppServer mode does <b>not</b> need this because it does not
/// use stdout for transport.
/// </para>
/// </summary>
internal static class SubprocessEnvironment
{
    /// <summary>
    /// Redirect all console output to stderr and suppress Ctrl+C.
    /// Call this as early as possible in the process lifetime, before any I/O loop starts.
    /// </summary>
    public static void Prepare()
    {
        // Redirect Spectre.Console output to stderr so that markup/rendered output
        // does not pollute the stdout JSON-RPC transport.
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error)
        });

        // Redirect Console.Out to stderr (catches any raw Console.WriteLine calls).
        Console.SetOut(new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true });

        // Ignore Ctrl+C / SIGINT in the subprocess.
        //
        // On Windows, pressing Ctrl+C sends CTRL_C_EVENT to every process attached to the same
        // console.  The CLI process handles this via Console.CancelKeyPress (setting e.Cancel = true),
        // which prevents the CLI from terminating — but that does NOT protect child processes in
        // the same console process group.  Since .NET's Process.Start lacks a way to specify
        // CREATE_NEW_PROCESS_GROUP, we instead disable the CTRL_C_EVENT handler on the subprocess
        // side.  The AppServer's lifecycle is controlled by stdin EOF / explicit shutdown, so it
        // never needs to respond to Ctrl+C directly.
        //
        // On Unix, Process.Start does not propagate SIGINT to children (the shell does), and this
        // handler provides consistent ignore semantics across platforms.
        ConsoleSignalGuard.IgnoreInterruptSignal();
    }
}
