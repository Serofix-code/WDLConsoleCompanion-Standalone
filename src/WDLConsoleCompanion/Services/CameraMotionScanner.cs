using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WDLConsoleCompanion.Services;

internal sealed record CameraMotionScanSummary(int Results, ulong BytesScanned, bool Truncated, TimeSpan Elapsed);

internal sealed class CameraMotionScanner
{
    private const int ChunkSize = 64 * 1024 * 1024;
    private const int MaxCandidates = 4_000_000;
    private const uint MemPrivate = 0x00020000;
    private readonly RemoteProcess _remote;
    private readonly List<Candidate> _candidates = [];
    private bool _truncated;
    private readonly record struct Candidate(ulong Address, uint Bits, uint PreviousBits);

    internal CameraMotionScanner(RemoteProcess remote) => _remote = remote;
    internal int Count => _candidates.Count;

    internal IReadOnlyList<MemoryScanResult> Preview(int limit = int.MaxValue) => _candidates.Take(limit)
        .Select(value => new MemoryScanResult(value.Address, Format(value.Bits), Format(value.PreviousBits))).ToArray();

    internal CameraMotionScanSummary DiscoverMotion(CancellationToken token, Action<string>? progress = null)
    {
        _candidates.Clear(); _truncated = false;
        ulong scanned = 0;
        var timer = Stopwatch.StartNew();
        byte[] before = GC.AllocateUninitializedArray<byte>(ChunkSize);
        byte[] after = GC.AllocateUninitializedArray<byte>(ChunkSize);
        foreach ((ulong start, ulong length) in EnumerateWritableRegions())
        {
            ulong cursor = start, end = checked(start + length);
            while (cursor < end)
            {
                token.ThrowIfCancellationRequested();
                int requested = (int)Math.Min((ulong)ChunkSize, end - cursor);
                if (requested < 4) break;
                if (!Read(cursor, before, requested, out int firstRead) || firstRead < 4) { cursor += (ulong)requested; continue; }
                // Cross a render update without retaining a complete multi-gigabyte baseline.
                Thread.Sleep(12);
                if (!Read(cursor, after, requested, out int secondRead) || secondRead < 4) { cursor += (ulong)requested; continue; }
                int available = Math.Min(firstRead, secondRead);
                int first = (int)((4UL - cursor % 4UL) % 4UL);
                unsafe
                {
                    fixed (byte* oldBuffer = before)
                    fixed (byte* newBuffer = after)
                    {
                        for (int offset = first; offset <= available - 4; offset += 4)
                        {
                            uint oldBits = *(uint*)(oldBuffer + offset);
                            uint newBits = *(uint*)(newBuffer + offset);
                            if (oldBits == newBits || !PlausibleChange(oldBits, newBits)) continue;
                            if (_candidates.Count < MaxCandidates) _candidates.Add(new(cursor + (ulong)offset, newBits, oldBits));
                            else _truncated = true;
                        }
                    }
                }
                scanned += (ulong)available;
                cursor += (ulong)requested;
                if ((scanned & 0x1FFFFFFFUL) < (ulong)requested)
                    progress?.Invoke($"Full-memory camera discovery: {scanned / 1024 / 1024:N0} MB, {_candidates.Count:N0} moving floats");
            }
        }
        return new(_candidates.Count, scanned, _truncated, timer.Elapsed);
    }

    internal CameraMotionScanSummary Filter(bool changed, CancellationToken token, Action<string>? progress = null)
    {
        if (_candidates.Count == 0) throw new InvalidOperationException("Run full-memory camera discovery first.");
        var timer = Stopwatch.StartNew();
        var kept = new List<Candidate>(Math.Min(_candidates.Count, 250_000));
        int processed = 0, index = 0;
        while (index < _candidates.Count)
        {
            token.ThrowIfCancellationRequested();
            ulong page = _candidates[index].Address & ~0xFFFUL;
            byte[] bytes = new byte[0x1000];
            bool pageRead = Read(page, bytes, out int available);
            while (index < _candidates.Count && (_candidates[index].Address & ~0xFFFUL) == page)
            {
                Candidate candidate = _candidates[index++]; processed++;
                int offset = (int)(candidate.Address - page);
                if (!pageRead || offset + 4 > available) continue;
                uint current = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
                bool matches = changed ? current != candidate.Bits && PlausibleChange(candidate.Bits, current) : Stationary(candidate.Bits, current);
                if (matches) kept.Add(new(candidate.Address, current, candidate.Bits));
            }
            if (processed % 100_000 == 0) progress?.Invoke($"Camera filter: {processed:N0}/{_candidates.Count:N0}, {kept.Count:N0} remain");
        }
        // A single unsuitable axis or a render-jitter sample must not erase the last usable set.
        if (kept.Count > 0) { _candidates.Clear(); _candidates.AddRange(kept); }
        return new(_candidates.Count, (ulong)processed * 4, _truncated, timer.Elapsed);
    }

    private bool Read(ulong address, byte[] buffer, out int read)
        => Read(address, buffer, buffer.Length, out read);

    private bool Read(ulong address, byte[] buffer, int requested, out int read)
    {
        bool ok = NativeMethods.ReadProcessMemory(_remote.Handle, (nint)address, buffer, (nuint)requested, out nuint count);
        read = checked((int)Math.Min(count, int.MaxValue));
        return ok || read > 0;
    }

    private IEnumerable<(ulong Start, ulong Length)> EnumerateWritableRegions()
    {
        ulong cursor = 0x10000, limit = 0x00007FFFFFFF0000;
        int size = Marshal.SizeOf<NativeMethods.MemoryBasicInformation>();
        while (cursor < limit)
        {
            if (NativeMethods.VirtualQueryEx(_remote.Handle, (nint)cursor, out var info, (nuint)size) == 0) yield break;
            ulong start = (ulong)info.BaseAddress, end = start + (ulong)info.RegionSize;
            var protection = info.Protect;
            bool readable = info.State == NativeMethods.MemoryState.Commit && (protection & (NativeMethods.MemoryProtection.NoAccess | NativeMethods.MemoryProtection.Guard)) == 0;
            bool writable = (protection & (NativeMethods.MemoryProtection.ReadWrite | NativeMethods.MemoryProtection.WriteCopy | NativeMethods.MemoryProtection.ExecuteReadWrite | NativeMethods.MemoryProtection.ExecuteWriteCopy)) != 0;
            if (readable && writable && info.Type == MemPrivate && end > start) yield return (start, end - start);
            cursor = end > cursor ? end : cursor + 0x1000;
        }
    }

    private static bool PlausibleChange(uint oldBits, uint newBits)
    {
        float oldValue = BitConverter.UInt32BitsToSingle(oldBits), newValue = BitConverter.UInt32BitsToSingle(newBits);
        if (!float.IsFinite(oldValue) || !float.IsFinite(newValue)) return false;
        if (MathF.Abs(oldValue) > 1_000_000 || MathF.Abs(newValue) > 1_000_000) return false;
        float delta = MathF.Abs(newValue - oldValue);
        return float.IsFinite(delta) && delta is > 0.0000001f and < 100_000f;
    }

    private static bool Stationary(uint oldBits, uint newBits)
    {
        float oldValue = BitConverter.UInt32BitsToSingle(oldBits), newValue = BitConverter.UInt32BitsToSingle(newBits);
        if (!float.IsFinite(oldValue) || !float.IsFinite(newValue)) return false;
        float tolerance = MathF.Max(0.000002f, MathF.Abs(oldValue) * 0.00002f);
        return MathF.Abs(newValue - oldValue) <= tolerance;
    }

    private static string Format(uint bits) => BitConverter.UInt32BitsToSingle(bits).ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
}
