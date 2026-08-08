using System.Buffers.Binary;

namespace WDLConsoleCompanion.Services;

internal sealed class CodeCaveHook : IDisposable
{
    private readonly RemoteProcess _remote;
    private readonly ulong _patchAddress;
    private readonly byte[] _original;
    private readonly byte[] _patch;
    private readonly ulong _allocation;
    private bool _installed;
    internal ulong DataAddress { get; }

    private CodeCaveHook(RemoteProcess remote, ulong patchAddress, byte[] original, byte[] patch, ulong allocation, ulong dataAddress)
    {
        _remote = remote; _patchAddress = patchAddress; _original = original; _patch = patch;
        _allocation = allocation; DataAddress = dataAddress; _installed = true;
    }

    internal static CodeCaveHook Install(RemoteProcess remote, ulong patchAddress, byte[] expected,
        Func<ulong, ulong, byte[]> buildCode, ReadOnlySpan<byte> initialData = default)
    {
        if (expected.Length < 5) throw new InvalidOperationException("Code-cave overwrite must be at least five bytes.");
        byte[] actual = remote.ReadBytes(patchAddress, expected.Length);
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException($"Hook precondition failed at 0x{patchAddress:X}. Expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}.");

        const int allocationSize = 0x2000;
        ulong allocation = remote.AllocateNear(patchAddress, allocationSize);
        ulong dataAddress = allocation + 0x1000;
        try
        {
            byte[] code = buildCode(allocation, dataAddress);
            if (code.Length > 0x1000) throw new InvalidOperationException("Generated hook code exceeds one page.");
            remote.WriteBytes(allocation, code);
            if (!initialData.IsEmpty) remote.WriteBytes(dataAddress, initialData);
            if (!NativeMethods.VirtualProtectEx(remote.Handle, (nint)allocation, 0x1000,
                NativeMethods.MemoryProtection.ExecuteRead, out _)) throw RemoteProcess.Win32("VirtualProtectEx(code cave)");
            if (!NativeMethods.VirtualProtectEx(remote.Handle, (nint)dataAddress, 0x1000,
                NativeMethods.MemoryProtection.ReadWrite, out _)) throw RemoteProcess.Win32("VirtualProtectEx(code data)");

            byte[] patch = Enumerable.Repeat((byte)0x90, expected.Length).ToArray();
            patch[0] = 0xE9;
            WriteRel32(patch, 1, patchAddress + 5, allocation);
            WriteProtected(remote, patchAddress, patch);
            return new CodeCaveHook(remote, patchAddress, expected, patch, allocation, dataAddress);
        }
        catch
        {
            NativeMethods.VirtualFreeEx(remote.Handle, (nint)allocation, 0, NativeMethods.FreeType.Release);
            throw;
        }
    }

    public void Dispose()
    {
        if (!_installed) return;
        byte[] current = _remote.ReadBytes(_patchAddress, _patch.Length);
        if (!current.SequenceEqual(_patch)) throw new InvalidOperationException("Code-cave hook changed externally; refusing cleanup overwrite.");
        WriteProtected(_remote, _patchAddress, _original);
        NativeMethods.VirtualFreeEx(_remote.Handle, (nint)_allocation, 0, NativeMethods.FreeType.Release);
        _installed = false;
    }

    internal static void AddRel32(List<byte> code, ulong codeBase, ulong destination)
    {
        int displacementOffset = code.Count;
        code.AddRange([0, 0, 0, 0]);
        WriteRel32(code, displacementOffset, codeBase + (ulong)code.Count, destination);
    }

    internal static void AddJumpBack(List<byte> code, ulong codeBase, ulong destination)
    {
        code.Add(0xE9);
        AddRel32(code, codeBase, destination);
    }

    private static void WriteProtected(RemoteProcess remote, ulong address, byte[] bytes)
    {
        using (remote.SuspendThreads())
        {
            if (!NativeMethods.VirtualProtectEx(remote.Handle, (nint)address, (nuint)bytes.Length,
                NativeMethods.MemoryProtection.ExecuteReadWrite, out var old)) throw RemoteProcess.Win32("VirtualProtectEx(code hook)");
            try { remote.WriteBytes(address, bytes); NativeMethods.FlushInstructionCache(remote.Handle, (nint)address, (nuint)bytes.Length); }
            finally { NativeMethods.VirtualProtectEx(remote.Handle, (nint)address, (nuint)bytes.Length, old, out _); }
        }
    }

    private static void WriteRel32(List<byte> bytes, int offset, ulong nextInstruction, ulong destination)
    {
        long delta = checked((long)destination - (long)nextInstruction);
        if (delta is < int.MinValue or > int.MaxValue) throw new InvalidOperationException("Generated instruction target is outside rel32 range.");
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
