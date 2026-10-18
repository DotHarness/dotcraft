using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DotCraft.Hub;

/// <summary>
/// Starts the Hub through a short-lived launcher so the Hub is not a descendant of its caller and survives
/// the caller's process tree being killed.
/// </summary>
internal static class HubLauncher
{
    public const string LauncherOption = "--detach";

    public static void StartLauncher(string dotcraftBin, HubPaths paths)
    {
        if (OperatingSystem.IsWindows())
            StopInheritingStandardHandles();
        Start(dotcraftBin, paths.HubStatePath, "hub", LauncherOption);
    }

    public static int RunLauncher()
    {
        if (!OperatingSystem.IsWindows())
            DetachFromCallerSession();
        Start(CurrentBinary(), HubPaths.ForCurrentUser().HubStatePath, "hub");
        return 0;
    }

    public static string CurrentBinary()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && Path.GetFileNameWithoutExtension(processPath).Equals("dotcraft", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var assemblyPath = typeof(HubLauncher).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath))
            return assemblyPath;

        return processPath ?? throw new HubClientException("hubUnavailable", "Cannot determine dotcraft binary path.");
    }

    private static void Start(string dotcraftBin, string workingDirectory, params string[] args)
    {
        Directory.CreateDirectory(workingDirectory);
        var isAssembly = dotcraftBin.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var psi = new ProcessStartInfo
        {
            FileName = isAssembly ? "dotnet" : dotcraftBin,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (isAssembly)
            psi.ArgumentList.Add(dotcraftBin);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var _ = Process.Start(psi);
    }

    // The Hub inherits these standard streams; the caller's pipes must not stay open for the Hub's lifetime.
    private static void DetachFromCallerSession()
    {
        _ = setsid();
        using var devNull = File.OpenHandle("/dev/null", FileMode.Open, FileAccess.ReadWrite);
        var devNullFd = (int)devNull.DangerousGetHandle();
        for (var fd = 0; fd <= 2; fd++)
            _ = dup2(devNullFd, fd);
    }

    // Windows children inherit every inheritable handle, so the caller's stdout/stderr pipes would otherwise stay open in the Hub.
    private static void StopInheritingStandardHandles()
    {
        foreach (var stdHandle in new[] { -10, -11, -12 })
            _ = SetHandleInformation(GetStdHandle(stdHandle), HandleFlagInherit, 0);
    }

    private const uint HandleFlagInherit = 1;

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int stdHandle);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int setsid();

    [DllImport("libc", SetLastError = true)]
    private static extern int dup2(int oldFd, int newFd);
}
