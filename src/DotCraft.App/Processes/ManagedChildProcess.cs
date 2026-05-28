using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DotCraft.Processes;

/// <summary>
/// Owns a child process and, on Windows, binds it to a Job Object configured with
/// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE so abrupt parent termination also tears down the child tree.
/// </summary>
internal sealed class ManagedChildProcess : IAsyncDisposable
{
    private readonly SafeJobHandle? _jobHandle;
    private bool _disposed;

    private ManagedChildProcess(Process process, SafeJobHandle? jobHandle)
    {
        Process = process;
        _jobHandle = jobHandle;
    }

    public Process Process { get; }

    public bool HasJobObject => _jobHandle is not null;

    public static ManagedChildProcess Start(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Failed to start child process: {startInfo.FileName}");

        SafeJobHandle? jobHandle = null;
        try
        {
            if (OperatingSystem.IsWindows())
                jobHandle = CreateKillOnCloseJobAndAssign(process);

            return new ManagedChildProcess(process, jobHandle);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignored
            }

            try
            {
                process.Dispose();
            }
            catch
            {
                // ignored
            }

            jobHandle?.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (!Process.HasExited)
                Process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignored
        }

        try
        {
            if (!Process.HasExited)
            {
                await Process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            Process.Dispose();
            _jobHandle?.Dispose();
        }
    }

    private static SafeJobHandle CreateKillOnCloseJobAndAssign(Process process)
    {
        var jobHandle = CreateJobObject(IntPtr.Zero, null);
        if (jobHandle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create Windows Job Object.");

        try
        {
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, buffer, fDeleteOld: false);
                if (!SetInformationJobObject(
                        jobHandle,
                        JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                        buffer,
                        (uint)length))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Failed to configure Windows Job Object with kill-on-close.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (!AssignProcessToJobObject(jobHandle, process.Handle))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Failed to assign child process {process.Id} to Windows Job Object.");
            }

            return jobHandle;
        }
        catch
        {
            jobHandle.Dispose();
            throw;
        }
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    private enum JOBOBJECTINFOCLASS
    {
        JobObjectExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private sealed class SafeJobHandle() : SafeHandle(IntPtr.Zero, ownsHandle: true)
    {
        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeJobHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeJobHandle job,
        JOBOBJECTINFOCLASS jobObjectInfoClass,
        IntPtr jobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
