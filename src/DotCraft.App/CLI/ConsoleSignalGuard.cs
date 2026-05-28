using System.Runtime.InteropServices;

namespace DotCraft.CLI;

/// <summary>
/// Platform-aware utility for controlling console interrupt signal (Ctrl+C / SIGINT) handling.
///
/// <para>
/// On Windows, pressing Ctrl+C sends a <c>CTRL_C_EVENT</c> to every process attached to the
/// same console window (i.e., every process in the same console process group). .NET's
/// <see cref="Console.CancelKeyPress"/> with <c>e.Cancel = true</c> only prevents the
/// <em>current</em> process from terminating — it has no effect on other processes in the group.
/// </para>
///
/// <para>
/// Since <see cref="System.Diagnostics.ProcessStartInfo"/> does not expose the Windows
/// <c>CREATE_NEW_PROCESS_GROUP</c> creation flag, a CLI-spawned subprocess (AppServer) shares
/// the parent's console process group by default and is vulnerable to Ctrl+C termination.
/// </para>
///
/// <para>
/// This class solves the problem by calling the Windows native <c>SetConsoleCtrlHandler(null, true)</c>
/// API, which tells the OS to skip the default CTRL_C / CTRL_BREAK handler for the calling process.
/// This is more reliable than <see cref="Console.CancelKeyPress"/> because it operates below the
/// .NET runtime layer and prevents the signal from reaching any handler (managed or native).
/// A managed <see cref="Console.CancelKeyPress"/> handler is also registered as a defense-in-depth
/// measure for edge cases where the native call is not effective (e.g., non-Windows platforms
/// or unusual runtime configurations).
/// </para>
/// </summary>
internal static class ConsoleSignalGuard
{
    /// <summary>
    /// Suppress the console interrupt signal (Ctrl+C) for the current process.
    /// Call this early in the process lifetime, before any I/O loop starts.
    /// This method is safe to call on all platforms (non-Windows is a best-effort .NET-only guard).
    /// </summary>
    public static void IgnoreInterruptSignal()
    {
        // Layer 1 (Windows-only): native API — most reliable.
        // SetConsoleCtrlHandler(null, TRUE) causes the calling process to ignore CTRL_C_EVENT
        // and CTRL_BREAK_EVENT signals.  This is the only way to truly prevent termination
        // from a console-group-wide Ctrl+C on Windows.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                SetConsoleCtrlHandler(IntPtr.Zero, add: true);
            }
            catch
            {
                // P/Invoke may fail in sandboxed environments; fall through to managed layer.
            }
        }

        // Layer 2 (cross-platform): managed handler — defense in depth.
        // On non-Windows platforms this is the primary guard.
        // On Windows this is a backup in case the native call failed.
        Console.CancelKeyPress += static (_, e) => e.Cancel = true;
    }

    // -------------------------------------------------------------------------
    // Windows native interop
    // -------------------------------------------------------------------------

    /// <summary>
    /// <see href="https://learn.microsoft.com/windows/console/setconsolectrlhandler"/>
    /// <para>
    /// When <paramref name="handler"/> is <see cref="IntPtr.Zero"/> (null) and <paramref name="add"/>
    /// is <c>true</c>, the calling process ignores CTRL+C input. This attribute is inherited by
    /// child processes but can be overridden.
    /// </para>
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handler, [MarshalAs(UnmanagedType.Bool)] bool add);
}
