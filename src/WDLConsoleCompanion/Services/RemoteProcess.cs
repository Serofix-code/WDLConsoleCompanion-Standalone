using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WDLConsoleCompanion.Services;

internal sealed class RemoteProcess : IDisposable
{
    private readonly Process _process;
    internal nint Handle { get; }
    internal Process Process => _process;

    internal RemoteProcess(Process process)
    {
        _process = process;
        Handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessAccess.QueryInformation | NativeMethods.ProcessAccess.CreateThread | NativeMethods.ProcessAccess.VmOperation |
            NativeMethods.ProcessAccess.VmRead | NativeMethods.ProcessAccess.VmWrite, false, process.Id);
        if (Handle == 0) throw Win32("OpenProcess");
    }

    internal bool IsRangeReadable(ulong address, int length)
    {
        if (address < 0x10000 || length <= 0 || address + (ulong)length < address) return false;
        if (NativeMethods.VirtualQueryEx(Handle, (nint)address, out var info,
            (nuint)Marshal.SizeOf<NativeMethods.MemoryBasicInformation>()) == 0) return false;
        ulong start = (ulong)info.BaseAddress;
        ulong end = start + (ulong)info.RegionSize;
        var p = info.Protect;
        return info.State == NativeMethods.MemoryState.Commit && address >= start && address + (ulong)length <= end &&
               (p & NativeMethods.MemoryProtection.Guard) == 0 && (p & NativeMethods.MemoryProtection.NoAccess) == 0;
    }

    internal byte[] ReadBytes(ulong address, int length)
    {
        if (!IsRangeReadable(address, length)) throw new InvalidOperationException($"Invalid/unreadable pointer range 0x{address:X} (+{length}).");
        var data = new byte[length];
        if (!NativeMethods.ReadProcessMemory(Handle, (nint)address, data, (nuint)length, out var read) || read != (nuint)length)
            throw Win32($"ReadProcessMemory(0x{address:X})");
        return data;
    }

    internal T Read<T>(ulong address) where T : unmanaged
    {
        var bytes = ReadBytes(address, Marshal.SizeOf<T>());
        return MemoryMarshal.Read<T>(bytes);
    }

    internal ulong ReadPointer(ulong address)
    {
        ulong value = Read<ulong>(address);
        if (value == 0) throw new InvalidOperationException($"Null pointer read at 0x{address:X}.");
        if (!IsRangeReadable(value, 1)) throw new InvalidOperationException($"Pointer at 0x{address:X} targets invalid memory 0x{value:X}.");
        return value;
    }

    internal void WriteBytes(ulong address, ReadOnlySpan<byte> bytes)
    {
        if (!IsRangeReadable(address, bytes.Length)) throw new InvalidOperationException($"Invalid write target 0x{address:X} (+{bytes.Length}).");
        byte[] data = bytes.ToArray();
        if (!NativeMethods.WriteProcessMemory(Handle, (nint)address, data, (nuint)data.Length, out var written) || written != (nuint)data.Length)
            throw Win32($"WriteProcessMemory(0x{address:X})");
    }

    internal void Write<T>(ulong address, T value) where T : unmanaged
    {
        Span<byte> bytes = stackalloc byte[Marshal.SizeOf<T>()];
        MemoryMarshal.Write(bytes, in value);
        WriteBytes(address, bytes);
    }

    internal ulong AllocateNear(ulong target, int size)
    {
        const ulong distance = 0x70000000;
        const ulong granularity = 0x10000;
        ulong low = target > distance ? target - distance : 0x10000;
        ulong high = Math.Min(target + distance, 0x00007FFFFFFF0000);
        ulong cursor = low & ~(granularity - 1);
        int mbiSize = Marshal.SizeOf<NativeMethods.MemoryBasicInformation>();
        while (cursor < high)
        {
            if (NativeMethods.VirtualQueryEx(Handle, (nint)cursor, out var info, (nuint)mbiSize) == 0) break;
            ulong regionBase = (ulong)info.BaseAddress;
            ulong regionEnd = regionBase + (ulong)info.RegionSize;
            if (info.State == NativeMethods.MemoryState.Free)
            {
                ulong candidate = (Math.Max(regionBase, low) + granularity - 1) & ~(granularity - 1);
                if (candidate + (ulong)size <= Math.Min(regionEnd, high))
                {
                    nint allocated = NativeMethods.VirtualAllocEx(Handle, (nint)candidate, (nuint)size,
                        NativeMethods.AllocationType.Reserve | NativeMethods.AllocationType.Commit,
                        NativeMethods.MemoryProtection.ExecuteReadWrite);
                    if (allocated != 0) return (ulong)allocated;
                }
            }
            cursor = regionEnd > cursor ? regionEnd : cursor + granularity;
        }
        throw new InvalidOperationException("Could not allocate executable memory within rel32 range of the hook.");
    }

    internal IDisposable SuspendThreads() => new ThreadSuspension(_process);

    internal ulong ExecuteFunction(ulong function, ulong arg1, ulong arg2)
    {
        ulong allocation = AllocateNear(function, 0x2000);
        ulong resultAddress = allocation + 0x1000;
        try
        {
            var code = new List<byte>();
            code.AddRange([0x48, 0xB9]); code.AddRange(BitConverter.GetBytes(arg1));
            code.AddRange([0x48, 0xBA]); code.AddRange(BitConverter.GetBytes(arg2));
            code.AddRange([0x48, 0xB8]); code.AddRange(BitConverter.GetBytes(function));
            code.AddRange([0x48, 0x83, 0xEC, 0x28, 0xFF, 0xD0]);
            code.AddRange([0x48, 0xA3]); code.AddRange(BitConverter.GetBytes(resultAddress));
            code.AddRange([0x48, 0x83, 0xC4, 0x28, 0xC3]);
            WriteBytes(allocation, CollectionsMarshal.AsSpan(code)); Write<ulong>(resultAddress, 0);
            if (!NativeMethods.VirtualProtectEx(Handle, (nint)allocation, 0x1000, NativeMethods.MemoryProtection.ExecuteRead, out _) ||
                !NativeMethods.VirtualProtectEx(Handle, (nint)resultAddress, 0x1000, NativeMethods.MemoryProtection.ReadWrite, out _))
                throw Win32("VirtualProtectEx(remote call)");
            nint thread = NativeMethods.CreateRemoteThread(Handle, 0, 0, (nint)allocation, 0, 0, out _);
            if (thread == 0) throw Win32("CreateRemoteThread");
            try { if (NativeMethods.WaitForSingleObject(thread, 10_000) != 0) throw new TimeoutException("The in-game function call did not finish within 10 seconds."); }
            finally { NativeMethods.CloseHandle(thread); }
            return Read<ulong>(resultAddress);
        }
        finally { NativeMethods.VirtualFreeEx(Handle, (nint)allocation, 0, NativeMethods.FreeType.Release); }
    }

    internal static Win32Exception Win32(string operation) => new(Marshal.GetLastWin32Error(), operation);

    public void Dispose()
    {
        if (Handle != 0) NativeMethods.CloseHandle(Handle);
        _process.Dispose();
    }

    private sealed class ThreadSuspension : IDisposable
    {
        private readonly List<nint> _threads = [];
        internal ThreadSuspension(Process process)
        {
            process.Refresh();
            foreach (ProcessThread thread in process.Threads)
            {
                nint handle = NativeMethods.OpenThread(NativeMethods.ThreadAccess.SuspendResume, false, (uint)thread.Id);
                if (handle == 0) continue;
                if (NativeMethods.SuspendThread(handle) == uint.MaxValue) NativeMethods.CloseHandle(handle);
                else _threads.Add(handle);
            }
        }
        public void Dispose()
        {
            foreach (nint thread in _threads)
            {
                NativeMethods.ResumeThread(thread);
                NativeMethods.CloseHandle(thread);
            }
            _threads.Clear();
        }
    }
}
