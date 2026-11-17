using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using DotCraft.Hub;
using DotCraft.Processes;

namespace DotCraft.Tests.Hub;

internal sealed class ManagedLocalServiceProcessFixture : IAsyncDisposable
{
    internal const string ReadyRecord = "{\"type\":\"dotcraft.managed-service.ready\",\"serviceId\":\"oratorio\",\"endpoint\":\"http://127.0.0.1:5087\",\"version\":\"test\"}";
    private readonly string _root;
    private readonly Process _process;
    private Process? _descendant;

    private ManagedLocalServiceProcessFixture(string root, ManagedChildProcess child)
    {
        _root = root;
        _process = child.Process;
        Service = new ManagedLocalServiceProcess(child);
    }

    public ManagedLocalServiceProcess Service { get; }

    public static ManagedLocalServiceProcessFixture Start(string windows, string unix, bool inheritedOutput = false)
    {
        var root = Directory.CreateTempSubdirectory("managed-service-process-").FullName;
        var isWindows = OperatingSystem.IsWindows();
        var script = isWindows ? "fixture.cmd" : "fixture.sh";
        try
        {
            File.WriteAllText(Path.Combine(root, script), isWindows ? "@echo off\r\n" + windows.Replace("\n", "\r\n") : unix, Encoding.ASCII);
            if (inheritedOutput)
            {
                File.WriteAllText(Path.Combine(root, "descendant.cmd"),
                    "@echo off\r\necho started>descendant.started\r\nset /p hold=\r\necho exited>descendant.exited\r\n", Encoding.ASCII);
                File.WriteAllText(Path.Combine(root, "descendant.sh"),
                    "printf started > descendant.started\nread hold\nprintf exited > descendant.exited\n", Encoding.ASCII);
            }
            var start = new ProcessStartInfo(isWindows ? "cmd.exe" : "/bin/sh")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            if (isWindows)
            {
                start.ArgumentList.Add("/d");
                start.ArgumentList.Add("/q");
                start.ArgumentList.Add("/c");
            }
            start.ArgumentList.Add(script);
            return new ManagedLocalServiceProcessFixture(root, ManagedChildProcess.Start(start));
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    public async Task SendAsync(string input)
    {
        await _process.StandardInput.WriteLineAsync(input);
        await _process.StandardInput.FlushAsync();
    }

    public bool MarkerExists(string name) => File.Exists(Path.Combine(_root, name));

    public async Task WaitForMarkerAsync(string name, TimeSpan timeout)
    {
        var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(_root, name);
        watcher.Created += (_, _) => created.TrySetResult();
        watcher.EnableRaisingEvents = true;
        if (MarkerExists(name))
            return;
        await created.Task.WaitAsync(timeout);
    }

    public void CaptureDescendant()
    {
        _descendant = OperatingSystem.IsWindows()
            ? GetWindowsDescendant(_process.Id)
            : Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(_root, "descendant.pid"))));
        _ = _descendant.SafeHandle;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_descendant is null && MarkerExists("descendant.started"))
                CaptureDescendant();
        }
        finally
        {
            await DisposeProcessesAsync();
        }
    }

    private async Task DisposeProcessesAsync()
    {
        try
        {
            await Service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch (InvalidOperationException) { }
            if (_descendant is not null)
            {
                using (_descendant)
                {
                    try
                    {
                        if (!_descendant.HasExited)
                            _descendant.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException) { }
                    await _descendant.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            Directory.Delete(_root, recursive: true);
        }
    }

    private static Process GetWindowsDescendant(int parentId)
    {
        const uint snapshotProcesses = 0x00000002;
        using var snapshot = CreateToolhelp32Snapshot(snapshotProcesses, 0);
        if (snapshot.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (Process32First(snapshot, ref entry))
        {
            do
            {
                if (entry.ParentProcessId == (uint)parentId)
                    return Process.GetProcessById((int)entry.ProcessId);
            }
            while (Process32Next(snapshot, ref entry));
        }
        throw new InvalidOperationException("The inherited-pipe descendant was not found.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
}
