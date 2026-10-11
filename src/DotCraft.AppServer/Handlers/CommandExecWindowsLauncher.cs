using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DotCraft.AppServer;

[SupportedOSPlatform("windows")]
internal static class CommandExecWindowsLauncher
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateNoWindow = 0x08000000;
    private const int StartfUseStdHandles = 0x00000100;
    private static readonly IntPtr ProcThreadAttributeHandleList = 0x00020002;

    public static CommandExecProcess Start(
        string program,
        IReadOnlyList<string> arguments,
        string cwd,
        IReadOnlyDictionary<string, string> environment)
    {
        using var stdin = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var (stdoutRead, stdoutWrite) = CreateOutputPipe();
        var (stderrRead, stderrWrite) = CreateOutputPipe();
        var attributeList = IntPtr.Zero;
        var handleList = IntPtr.Zero;
        var environmentBlock = IntPtr.Zero;
        try
        {
            IntPtr[] inherited =
            [
                stdin.ClientSafePipeHandle.DangerousGetHandle(),
                stdoutWrite.SafePipeHandle.DangerousGetHandle(),
                stderrWrite.SafePipeHandle.DangerousGetHandle()
            ];
            handleList = Marshal.AllocHGlobal(IntPtr.Size * inherited.Length);
            Marshal.Copy(inherited, 0, handleList, inherited.Length);

            var size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributeList = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
            {
                Marshal.FreeHGlobal(attributeList);
                attributeList = IntPtr.Zero;
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            if (!UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    ProcThreadAttributeHandleList,
                    handleList,
                    IntPtr.Size * inherited.Length,
                    IntPtr.Zero,
                    IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var startupInfo = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    cb = Marshal.SizeOf<StartupInfoEx>(),
                    dwFlags = StartfUseStdHandles,
                    hStdInput = inherited[0],
                    hStdOutput = inherited[1],
                    hStdError = inherited[2]
                },
                lpAttributeList = attributeList
            };
            environmentBlock = Marshal.StringToHGlobalUni(BuildEnvironmentBlock(environment));
            var batch = IsBatchFile(program);
            var commandLine = ((batch ? BuildBatchCommandLine(program, arguments) : BuildCommandLine(program, arguments)) + '\0')
                .ToCharArray();

            if (!CreateProcessW(
                    batch ? CommandInterpreter() : null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    CreateSuspended | CreateUnicodeEnvironment | ExtendedStartupInfoPresent | CreateNoWindow,
                    environmentBlock,
                    cwd,
                    ref startupInfo,
                    out var processInfo))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var processHandle = new SafeProcessHandle(processInfo.hProcess, ownsHandle: true);
            try
            {
                var process = Process.GetProcessById(processInfo.dwProcessId);
                if (ResumeThread(processInfo.hThread) == uint.MaxValue)
                {
                    var error = Marshal.GetLastWin32Error();
                    process.Kill();
                    process.Dispose();
                    throw new Win32Exception(error);
                }

                return new CommandExecProcess(process, stdoutRead, stderrRead, processHandle);
            }
            catch
            {
                processHandle.Dispose();
                throw;
            }
            finally
            {
                CloseHandle(processInfo.hThread);
            }
        }
        catch
        {
            stdoutRead.Dispose();
            stderrRead.Dispose();
            throw;
        }
        finally
        {
            stdin.DisposeLocalCopyOfClientHandle();
            stdoutWrite.Dispose();
            stderrWrite.Dispose();
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
            Marshal.FreeHGlobal(handleList);
            Marshal.FreeHGlobal(environmentBlock);
        }
    }

    public static bool IsBatchFile(string program) =>
        Path.GetExtension(program.TrimEnd('.', ' ')) is { } extension
        && (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase));

    private static string CommandInterpreter() => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static (NamedPipeServerStream Read, NamedPipeClientStream Write) CreateOutputPipe()
    {
        var name = $"dotcraft-command-{Guid.NewGuid():N}";
        var read = new NamedPipeServerStream(
            name,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            var write = new NamedPipeClientStream(
                ".",
                name,
                PipeDirection.Out,
                PipeOptions.None,
                TokenImpersonationLevel.None,
                HandleInheritability.Inheritable);
            write.Connect();
            read.WaitForConnection();
            return (read, write);
        }
        catch
        {
            read.Dispose();
            throw;
        }
    }

    private static string BuildCommandLine(string program, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder().Append('"').Append(program).Append('"');
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            {
                builder.Append(argument);
                continue;
            }

            builder.Append('"');
            var backslashes = 0;
            foreach (var c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                builder.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
                backslashes = 0;
                builder.Append(c);
            }
            builder.Append('\\', backslashes * 2).Append('"');
        }

        return builder.ToString();
    }

    private static string BuildBatchCommandLine(string script, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder("cmd.exe /e:ON /v:OFF /d /c \"\"")
            .Append(script.Replace("%", "%%cd:~,%", StringComparison.Ordinal))
            .Append('"');
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            var quote = argument.Length == 0 || argument[^1] == '\\' || argument.Any(NeedsBatchQuotes);
            if (quote)
                builder.Append('"');
            var backslashes = 0;
            foreach (var c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    builder.Append(c);
                    continue;
                }

                if (c == '"')
                    builder.Append('\\', backslashes).Append('"');
                else if (c == '%')
                    builder.Append("%%cd:~,");
                backslashes = 0;
                builder.Append(c);
            }
            if (quote)
                builder.Append('\\', backslashes).Append('"');
        }

        return builder.Append('"').ToString();
    }

    private static bool NeedsBatchQuotes(char c) =>
        char.IsAscii(c) ? !char.IsAsciiLetterOrDigit(c) && !"#$*+-./:?@\\_".Contains(c) : char.IsControl(c);

    private static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var builder = new StringBuilder();
        foreach (var (name, value) in environment.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            builder.Append(name).Append('=').Append(value).Append('\0');
        return builder.Append('\0').ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        char[] lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
