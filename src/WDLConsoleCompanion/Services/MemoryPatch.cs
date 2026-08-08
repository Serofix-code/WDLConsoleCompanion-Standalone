namespace WDLConsoleCompanion.Services;

internal sealed class MemoryPatch : IDisposable
{
    private readonly RemoteProcess _remote;
    private readonly ulong _address;
    private readonly byte[] _original;
    private readonly byte[] _replacement;
    private bool _installed;

    private MemoryPatch(RemoteProcess remote, ulong address, byte[] original, byte[] replacement)
    {
        _remote = remote; _address = address; _original = original; _replacement = replacement; _installed = true;
    }

    internal static MemoryPatch Install(RemoteProcess remote, ulong address, byte[] expected, byte[] replacement)
    {
        if (expected.Length != replacement.Length || expected.Length == 0)
            throw new InvalidOperationException("Patch original/replacement lengths must match and be non-zero.");
        byte[] actual = remote.ReadBytes(address, expected.Length);
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException($"Patch precondition failed at 0x{address:X}. Expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}.");
        WriteProtected(remote, address, replacement);
        return new MemoryPatch(remote, address, expected, replacement);
    }

    public void Dispose()
    {
        if (!_installed) return;
        byte[] current = _remote.ReadBytes(_address, _replacement.Length);
        if (!current.SequenceEqual(_replacement))
            throw new InvalidOperationException($"Patch at 0x{_address:X} changed externally; refusing to overwrite it.");
        WriteProtected(_remote, _address, _original);
        _installed = false;
    }

    private static void WriteProtected(RemoteProcess remote, ulong address, byte[] bytes)
    {
        using (remote.SuspendThreads())
        {
            if (!NativeMethods.VirtualProtectEx(remote.Handle, (nint)address, (nuint)bytes.Length,
                NativeMethods.MemoryProtection.ExecuteReadWrite, out var old)) throw RemoteProcess.Win32("VirtualProtectEx(patch)");
            try
            {
                remote.WriteBytes(address, bytes);
                NativeMethods.FlushInstructionCache(remote.Handle, (nint)address, (nuint)bytes.Length);
            }
            finally { NativeMethods.VirtualProtectEx(remote.Handle, (nint)address, (nuint)bytes.Length, old, out _); }
        }
    }
}
