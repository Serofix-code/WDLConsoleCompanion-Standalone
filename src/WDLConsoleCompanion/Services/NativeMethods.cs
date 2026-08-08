using System.Runtime.InteropServices;

namespace WDLConsoleCompanion.Services;

internal static class NativeMethods
{
    [Flags]
    internal enum ProcessAccess : uint
    {
        CreateThread = 0x0002, VmOperation = 0x0008,
        VmRead = 0x0010,
        VmWrite = 0x0020,
        QueryInformation = 0x0400
    }

    [Flags]
    internal enum AllocationType : uint { Commit = 0x1000, Reserve = 0x2000 }
    internal enum FreeType : uint { Release = 0x8000 }
    internal enum MemoryState : uint { Commit = 0x1000, Free = 0x10000 }
    [Flags]
    internal enum ThreadAccess : uint { SuspendResume = 0x0002 }

    [Flags]
    internal enum MemoryProtection : uint
    {
        NoAccess = 0x01, ReadOnly = 0x02, ReadWrite = 0x04, WriteCopy = 0x08,
        Execute = 0x10, ExecuteRead = 0x20, ExecuteReadWrite = 0x40,
        ExecuteWriteCopy = 0x80, Guard = 0x100
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public MemoryProtection AllocationProtect;
        public nuint RegionSize;
        public MemoryState State;
        public MemoryProtection Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(ProcessAccess access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(nint process, nint address, byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteProcessMemory(nint process, nint address, byte[] buffer, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint VirtualAllocEx(nint process, nint address, nuint size, AllocationType type, MemoryProtection protect);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool VirtualFreeEx(nint process, nint address, nuint size, FreeType type);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool VirtualProtectEx(nint process, nint address, nuint size, MemoryProtection newProtect, out MemoryProtection oldProtect);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nuint VirtualQueryEx(nint process, nint address, out MemoryBasicInformation info, nuint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FlushInstructionCache(nint process, nint address, nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenThread(ThreadAccess access, bool inheritHandle, uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint SuspendThread(nint thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint ResumeThread(nint thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint CreateRemoteThread(nint process, nint attributes, nuint stackSize, nint startAddress, nint parameter, uint flags, out uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(nint handle, uint milliseconds);
}
