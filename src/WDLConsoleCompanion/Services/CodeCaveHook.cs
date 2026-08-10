using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

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
    internal bool WasAdopted { get; }

    private CodeCaveHook(RemoteProcess remote, ulong patchAddress, byte[] original, byte[] patch, ulong allocation, ulong dataAddress, bool wasAdopted = false)
    {
        _remote = remote; _patchAddress = patchAddress; _original = original; _patch = patch;
        _allocation = allocation; DataAddress = dataAddress; _installed = true; WasAdopted = wasAdopted;
    }

    internal static CodeCaveHook InstallOrAdopt(RemoteProcess remote, ProcessModule module, string originalPattern, string patchedPattern, byte[] expected,
        Func<ulong, ulong, ulong, byte[]> buildCode, ReadOnlySpan<byte> initialData = default, IReadOnlyList<byte[]>? adoptionFragments = null)
    {
        try
        {
            ulong patchAddress = PatternScanner.FindUnique(remote, module, originalPattern);
            try { return Install(remote, patchAddress, expected, (codeBase, dataAddress) => buildCode(codeBase, dataAddress, patchAddress), initialData); }
            catch (InvalidOperationException precondition) when (precondition.Message.StartsWith("Hook precondition failed", StringComparison.Ordinal))
            {
                byte[] actual = remote.ReadBytes(patchAddress, expected.Length);
                if (TryAdopt(remote, patchAddress, expected, actual, adoptionFragments, out CodeCaveHook? direct)) return direct!;
                throw;
            }
        }
        catch (InvalidOperationException originalError) when (originalError.Message.StartsWith("Signature not found", StringComparison.Ordinal))
        {
            var adopted = new List<CodeCaveHook>();
            foreach (ulong candidate in PatternScanner.FindAll(remote, module, patchedPattern))
            {
                byte[] actual;
                try { actual = remote.ReadBytes(candidate, expected.Length); } catch { continue; }
                if (TryAdopt(remote, candidate, expected, actual, adoptionFragments, out CodeCaveHook? hook)) adopted.Add(hook!);
            }
            if (adopted.Count == 1) return adopted[0];
            if (adopted.Count > 1) throw new InvalidOperationException($"Found multiple recognized previous hooks for {originalPattern}; refusing ambiguous recovery.");
            throw new InvalidOperationException($"{originalError.Message} No recognized previous companion hook could be recovered. Restart the game once to restore original engine code.", originalError);
        }
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

    private static bool TryAdopt(RemoteProcess remote, ulong patchAddress, byte[] expected, byte[] actual, IReadOnlyList<byte[]>? adoptionFragments, out CodeCaveHook? hook)
    {
        hook = null;
        if (actual.Length != expected.Length || actual.Length < 5 || actual[0] != 0xE9 || actual.Skip(5).Any(value => value != 0x90)) return false;
        try
        {
            int displacement = BinaryPrimitives.ReadInt32LittleEndian(actual.AsSpan(1, 4));
            ulong allocation = checked((ulong)((long)patchAddress + 5 + displacement));
            ulong dataAddress = allocation + 0x1000;
            int mbiSize = Marshal.SizeOf<NativeMethods.MemoryBasicInformation>();
            if (NativeMethods.VirtualQueryEx(remote.Handle, (nint)allocation, out var codeInfo, (nuint)mbiSize) == 0 || (ulong)codeInfo.AllocationBase != allocation) return false;
            if ((codeInfo.Protect & (NativeMethods.MemoryProtection.Execute | NativeMethods.MemoryProtection.ExecuteRead | NativeMethods.MemoryProtection.ExecuteReadWrite | NativeMethods.MemoryProtection.ExecuteWriteCopy)) == 0) return false;
            if (NativeMethods.VirtualQueryEx(remote.Handle, (nint)dataAddress, out var dataInfo, (nuint)mbiSize) == 0 || dataInfo.AllocationBase != codeInfo.AllocationBase) return false;
            if ((dataInfo.Protect & (NativeMethods.MemoryProtection.ReadWrite | NativeMethods.MemoryProtection.ExecuteReadWrite)) == 0) return false;

            byte[] code = remote.ReadBytes(allocation, 0x400);
            IReadOnlyList<byte[]> requiredFragments = adoptionFragments ?? [expected];
            bool containsOriginal = true; int fragmentSearchStart = 0;
            foreach (byte[] fragment in requiredFragments)
            {
                bool foundFragment = false;
                for (int i = fragmentSearchStart; i <= code.Length - fragment.Length; i++)
                    if (code.AsSpan(i, fragment.Length).SequenceEqual(fragment)) { foundFragment = true; fragmentSearchStart = i + fragment.Length; break; }
                if (!foundFragment) { containsOriginal = false; break; }
            }
            bool returnsCorrectly = false;
            for (int i = 0; i <= code.Length - 5; i++)
            {
                if (code[i] != 0xE9) continue;
                int returnDisplacement = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(i + 1, 4));
                ulong target = checked((ulong)((long)allocation + i + 5 + returnDisplacement));
                if (target == patchAddress + (ulong)expected.Length) { returnsCorrectly = true; break; }
            }
            if (!containsOriginal || !returnsCorrectly || !remote.IsRangeReadable(dataAddress, 8)) return false;
            hook = new CodeCaveHook(remote, patchAddress, expected, actual, allocation, dataAddress, wasAdopted: true);
            return true;
        }
        catch { return false; }
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
