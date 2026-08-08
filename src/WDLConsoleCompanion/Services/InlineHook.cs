using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace WDLConsoleCompanion.Services;

internal sealed class InlineHook : IDisposable
{
    private readonly RemoteProcess _remote;
    private readonly ulong _patchAddress;
    private readonly byte[] _original;
    private readonly byte[] _patch;
    private readonly ulong _allocation;
    private readonly ulong _captureAddress;
    private bool _installed;
    internal bool WasAdopted { get; }

    private InlineHook(RemoteProcess remote, ulong patchAddress, byte[] original, byte[] patch, ulong allocation, ulong captureAddress, bool wasAdopted = false)
    {
        _remote = remote; _patchAddress = patchAddress; _original = original; _patch = patch;
        _allocation = allocation; _captureAddress = captureAddress; _installed = true; WasAdopted = wasAdopted;
    }

    internal static InlineHook Install(RemoteProcess remote, ulong patchAddress, byte[] expected)
    {
        if (expected.Length < 5) throw new InvalidOperationException("Hook overwrite must be at least 5 bytes.");
        byte[] actual = remote.ReadBytes(patchAddress, expected.Length);
        if (!actual.SequenceEqual(expected))
        {
            if (TryAdopt(remote, patchAddress, expected, actual, out InlineHook? adopted)) return adopted!;
            throw new InvalidOperationException($"Hook precondition failed at 0x{patchAddress:X}. It is neither original code nor a recognized companion hook. Expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}.");
        }

        // Keep executable code and writable capture storage on separate pages.
        // The hook writes RAX on every invocation, so placing capture storage on
        // the RX code page causes an access violation as soon as this routine runs.
        const int allocationSize = 0x2000;
        const ulong dataPageOffset = 0x1000;
        ulong allocation = remote.AllocateNear(patchAddress, allocationSize);
        ulong capture = allocation + dataPageOffset;
        try
        {
            var stub = new List<byte> { 0x48, 0x89, 0x05, 0, 0, 0, 0 }; // mov [rip+disp32],rax
            WriteRel32(stub, 3, allocation + 7, capture);
            stub.AddRange(expected);
            int jumpOffset = stub.Count;
            stub.AddRange([0xE9, 0, 0, 0, 0]);
            WriteRel32(stub, jumpOffset + 1, allocation + (ulong)jumpOffset + 5, patchAddress + (ulong)expected.Length);
            remote.WriteBytes(allocation, CollectionsMarshal.AsSpan(stub));
            remote.Write<ulong>(capture, 0);
            if (!NativeMethods.VirtualProtectEx(remote.Handle, (nint)allocation, 0x1000, NativeMethods.MemoryProtection.ExecuteRead, out _))
                throw RemoteProcess.Win32("VirtualProtectEx(stub)");
            if (!NativeMethods.VirtualProtectEx(remote.Handle, (nint)capture, 0x1000, NativeMethods.MemoryProtection.ReadWrite, out _))
                throw RemoteProcess.Win32("VirtualProtectEx(capture data)");

            var patch = Enumerable.Repeat((byte)0x90, expected.Length).ToArray();
            patch[0] = 0xE9;
            WriteRel32(patch, 1, patchAddress + 5, allocation);
            using (remote.SuspendThreads())
            {
                if (!NativeMethods.VirtualProtectEx(remote.Handle, (nint)patchAddress, (nuint)expected.Length,
                    NativeMethods.MemoryProtection.ExecuteReadWrite, out var old)) throw RemoteProcess.Win32("VirtualProtectEx(hook)");
                try { remote.WriteBytes(patchAddress, patch); NativeMethods.FlushInstructionCache(remote.Handle, (nint)patchAddress, (nuint)patch.Length); }
                finally { NativeMethods.VirtualProtectEx(remote.Handle, (nint)patchAddress, (nuint)expected.Length, old, out _); }
            }
            return new InlineHook(remote, patchAddress, expected, patch, allocation, capture);
        }
        catch
        {
            NativeMethods.VirtualFreeEx(remote.Handle, (nint)allocation, 0, NativeMethods.FreeType.Release);
            throw;
        }
    }

    private static bool TryAdopt(RemoteProcess remote, ulong patchAddress, byte[] expected, byte[] actual, out InlineHook? hook)
    {
        hook = null;
        if (actual.Length < 5 || actual[0] != 0xE9 || actual.Skip(5).Any(value => value != 0x90)) return false;
        try
        {
            int patchDisplacement = BinaryPrimitives.ReadInt32LittleEndian(actual.AsSpan(1, 4));
            ulong allocation = checked((ulong)((long)patchAddress + 5 + patchDisplacement));
            byte[] stub = remote.ReadBytes(allocation, 19);
            if (!stub.AsSpan(0, 3).SequenceEqual(new byte[] { 0x48, 0x89, 0x05 }) ||
                !stub.AsSpan(7, expected.Length).SequenceEqual(expected) || stub[7 + expected.Length] != 0xE9) return false;

            int captureDisplacement = BinaryPrimitives.ReadInt32LittleEndian(stub.AsSpan(3, 4));
            ulong capture = checked((ulong)((long)allocation + 7 + captureDisplacement));
            int returnDisplacement = BinaryPrimitives.ReadInt32LittleEndian(stub.AsSpan(8 + expected.Length, 4));
            ulong returnTarget = checked((ulong)((long)allocation + 12 + expected.Length + returnDisplacement));
            if (returnTarget != patchAddress + (ulong)expected.Length || !remote.IsRangeReadable(capture, 8)) return false;

            int mbiSize = Marshal.SizeOf<NativeMethods.MemoryBasicInformation>();
            if (NativeMethods.VirtualQueryEx(remote.Handle, (nint)allocation, out var codeInfo, (nuint)mbiSize) == 0 ||
                (ulong)codeInfo.AllocationBase != allocation) return false;
            if (NativeMethods.VirtualQueryEx(remote.Handle, (nint)capture, out var dataInfo, (nuint)mbiSize) == 0 ||
                dataInfo.AllocationBase != codeInfo.AllocationBase ||
                (dataInfo.Protect & (NativeMethods.MemoryProtection.ReadWrite | NativeMethods.MemoryProtection.ExecuteReadWrite)) == 0) return false;

            hook = new InlineHook(remote, patchAddress, expected, actual, allocation, capture, wasAdopted: true);
            return true;
        }
        catch { return false; }
    }

    internal ulong ReadCapturedPointer()
    {
        ulong pointer = _remote.Read<ulong>(_captureAddress);
        if (pointer == 0) throw new InvalidOperationException("The team-manager hook has not captured a pointer yet. Load into the game world and try Refresh.");
        if (!_remote.IsRangeReadable(pointer, 1)) throw new InvalidOperationException($"Captured manager pointer 0x{pointer:X} is invalid.");
        return pointer;
    }

    public void Dispose()
    {
        if (!_installed) return;
        try
        {
            byte[] current = _remote.ReadBytes(_patchAddress, _patch.Length);
            if (!current.SequenceEqual(_patch)) throw new InvalidOperationException("Hook bytes changed externally; refusing to overwrite them during cleanup.");
            using (_remote.SuspendThreads())
            {
                if (!NativeMethods.VirtualProtectEx(_remote.Handle, (nint)_patchAddress, (nuint)_original.Length,
                    NativeMethods.MemoryProtection.ExecuteReadWrite, out var old)) throw RemoteProcess.Win32("VirtualProtectEx(unhook)");
                try { _remote.WriteBytes(_patchAddress, _original); NativeMethods.FlushInstructionCache(_remote.Handle, (nint)_patchAddress, (nuint)_original.Length); }
                finally { NativeMethods.VirtualProtectEx(_remote.Handle, (nint)_patchAddress, (nuint)_original.Length, old, out _); }
            }
            NativeMethods.VirtualFreeEx(_remote.Handle, (nint)_allocation, 0, NativeMethods.FreeType.Release);
            _installed = false;
        }
        catch { throw; }
    }

    private static void WriteRel32(List<byte> bytes, int offset, ulong nextInstruction, ulong destination)
    {
        long delta = checked((long)destination - (long)nextInstruction);
        if (delta is < int.MinValue or > int.MaxValue) throw new InvalidOperationException("Hook target is outside rel32 range.");
        byte[] value = BitConverter.GetBytes((int)delta);
        for (int i = 0; i < 4; i++) bytes[offset + i] = value[i];
    }
    private static void WriteRel32(byte[] bytes, int offset, ulong nextInstruction, ulong destination)
    {
        long delta = checked((long)destination - (long)nextInstruction);
        if (delta is < int.MinValue or > int.MaxValue) throw new InvalidOperationException("Hook target is outside rel32 range.");
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), (int)delta);
    }
}
