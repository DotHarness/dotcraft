using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DotCraft.Unity;

internal static class NativeInjector
{
    public static void Inject(int pid, string nativePath, string payloadPath, string config)
    {
        if (Path.GetFullPath(payloadPath).Length >= 260)
            throw new PathTooLongException("Unity Mono requires a shorter bridge cache path (under 260 characters).");
        using var target = Process.GetProcessById(pid);
        nint handle = OpenProcess(0x043A, false, pid);
        if (handle == 0) throw new System.ComponentModel.Win32Exception();
        nint systemLibrary = 0;
        try
        {
            if (!IsWow64Process2(handle, out var machine, out var nativeMachine)) throw new System.ComponentModel.Win32Exception();
            if (machine != 0 || nativeMachine != 0x8664) throw new PlatformNotSupportedException("Target must be a Windows x64 process.");
            systemLibrary = NativeLibrary.Load("kernel32.dll");
            var load = NativeLibrary.GetExport(systemLibrary, "LoadLibraryW");
            using var self = Process.GetCurrentProcess();
            var owner = self.Modules.Cast<ProcessModule>().Single(m => load >= m.BaseAddress && load < m.BaseAddress + m.ModuleMemorySize);
            var remoteOwner = target.Modules.Cast<ProcessModule>().Single(m => m.ModuleName == owner.ModuleName);
            Call(handle, remoteOwner.BaseAddress + (load - owner.BaseAddress), Path.GetFullPath(nativePath));
            target.Refresh();
            var remote = target.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.FileName, Path.GetFullPath(nativePath), StringComparison.OrdinalIgnoreCase));
            var local = NativeLibrary.Load(Path.GetFullPath(nativePath));
            try
            {
                var entry = remote.BaseAddress + (NativeLibrary.GetExport(local, "Bootstrap") - local);
                uint code = Call(handle, entry, Path.GetFullPath(payloadPath) + "\n" + config);
                if (code != 0) throw new InvalidOperationException($"Native bootstrap failed at stage {code}; payload={Path.GetFullPath(payloadPath)}");
            }
            finally { NativeLibrary.Free(local); }
        }
        finally { if (systemLibrary != 0) NativeLibrary.Free(systemLibrary); CloseHandle(handle); }
    }

    private static uint Call(nint process, nint entry, string argument)
    {
        var bytes = Encoding.Unicode.GetBytes(argument + "\0");
        var memory = VirtualAllocEx(process, 0, (nuint)bytes.Length, 0x3000, 4);
        if (memory == 0) throw new System.ComponentModel.Win32Exception();
        nint thread = 0;
        bool completed = false;
        try
        {
            if (!WriteProcessMemory(process, memory, bytes, (nuint)bytes.Length, out var written) || written != (nuint)bytes.Length)
                throw new System.ComponentModel.Win32Exception();
            thread = CreateRemoteThread(process, 0, 0, entry, memory, 0, out _);
            if (thread == 0) throw new System.ComponentModel.Win32Exception();
            if (WaitForSingleObject(thread, 10000) != 0) throw new TimeoutException("Remote bootstrap outcome unknown; do not replay.");
            completed = true;
            if (!GetExitCodeThread(thread, out uint result)) throw new System.ComponentModel.Win32Exception();
            return result;
        }
        finally
        {
            if (thread != 0) CloseHandle(thread);
            if (completed || thread == 0) VirtualFreeEx(process, memory, 0, 0x8000);
        }
    }

    [DllImport("kernel32", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32", SetLastError = true)] private static extern bool IsWow64Process2(nint process, out ushort machine, out ushort nativeMachine);
    [DllImport("kernel32", SetLastError = true)] private static extern nint VirtualAllocEx(nint process, nint address, nuint size, uint allocation, uint protection);
    [DllImport("kernel32", SetLastError = true)] private static extern bool VirtualFreeEx(nint process, nint address, nuint size, uint type);
    [DllImport("kernel32", SetLastError = true)] private static extern bool WriteProcessMemory(nint process, nint address, byte[] data, nuint size, out nuint written);
    [DllImport("kernel32", SetLastError = true)] private static extern nint CreateRemoteThread(nint process, nint attributes, nuint stackSize, nint start, nint parameter, uint flags, out uint id);
    [DllImport("kernel32", SetLastError = true)] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32", SetLastError = true)] private static extern bool GetExitCodeThread(nint thread, out uint code);
    [DllImport("kernel32")] private static extern bool CloseHandle(nint handle);
}
