using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace WDLConsoleCompanion.Services;

internal enum MemoryScanValueType { Byte, Int32, Float, Int64, Double }
internal enum MemoryScanScope { EngineModule, WritableMemory }
internal enum MemoryScanComparison { Unknown, Exact, Changed, Unchanged, Increased, Decreased }

internal sealed record MemoryScanResult(ulong Address, string CurrentValue, string PreviousValue)
{
    public string AddressText => $"0x{Address:X16}";
}

internal sealed record MemoryScanSummary(int Results, ulong BytesScanned, bool Truncated, TimeSpan Elapsed);

internal sealed class MemoryScanner
{
    private const int MaxCandidates = 2_000_000;
    private const ulong MaxUnknownBytes = 256UL * 1024 * 1024;
    private const int ChunkSize = 1024 * 1024;
    private readonly RemoteProcess _remote;
    private readonly ulong _moduleBase;
    private readonly ulong _moduleEnd;
    private readonly List<Candidate> _candidates = [];
    private MemoryScanValueType _valueType;
    private bool _truncated;

    private readonly record struct Candidate(ulong Address, ulong Bits, ulong PreviousBits);

    internal MemoryScanner(RemoteProcess remote, ProcessModule module)
    {
        _remote = remote;
        _moduleBase = (ulong)module.BaseAddress;
        _moduleEnd = checked(_moduleBase + (ulong)module.ModuleMemorySize);
    }

    internal IReadOnlyList<MemoryScanResult> Preview(int limit = 1000) => _candidates.Take(limit)
        .Select(candidate => new MemoryScanResult(candidate.Address, Format(candidate.Bits, _valueType), Format(candidate.PreviousBits, _valueType))).ToArray();

    internal MemoryScanSummary FirstScan(MemoryScanValueType type, MemoryScanScope scope, MemoryScanComparison comparison, string input, CancellationToken cancellationToken, Action<string>? progress = null)
    {
        if (comparison is not MemoryScanComparison.Exact and not MemoryScanComparison.Unknown) throw new InvalidOperationException("A first scan must use Exact or Unknown initial value.");
        ulong target = comparison == MemoryScanComparison.Exact ? Parse(input, type) : 0;
        int size = SizeOf(type);
        _valueType = type;
        _candidates.Clear();
        _truncated = false;
        ulong scanned = 0;
        var timer = Stopwatch.StartNew();

        foreach ((ulong start, ulong length) in EnumerateRegions(scope))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (comparison == MemoryScanComparison.Unknown && scanned >= MaxUnknownBytes) { _truncated = true; break; }
            ulong regionCursor = start;
            ulong regionEnd = checked(start + length);
            while (regionCursor < regionEnd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int requested = (int)Math.Min((ulong)ChunkSize, regionEnd - regionCursor);
                if (comparison == MemoryScanComparison.Unknown) requested = (int)Math.Min((ulong)requested, MaxUnknownBytes - scanned);
                if (requested < size) break;
                byte[] buffer = new byte[requested];
                if (NativeMethods.ReadProcessMemory(_remote.Handle, (nint)regionCursor, buffer, (nuint)requested, out nuint read) && read >= (nuint)size)
                {
                    int available = (int)read;
                    int first = (int)((ulong)size - regionCursor % (ulong)size) % size;
                    for (int offset = first; offset <= available - size; offset += size)
                    {
                        ulong bits = ReadBits(buffer.AsSpan(offset, size), type);
                        if (comparison == MemoryScanComparison.Unknown || bits == target)
                        {
                            if (_candidates.Count < MaxCandidates) _candidates.Add(new Candidate(regionCursor + (ulong)offset, bits, bits));
                            else _truncated = true;
                        }
                    }
                    scanned += (ulong)available;
                }
                regionCursor += (ulong)requested;
                if ((scanned & 0x3FFFFFF) < (ulong)requested) progress?.Invoke($"Scanned {scanned / 1024 / 1024:N0} MB; {_candidates.Count:N0} candidates");
                if (comparison == MemoryScanComparison.Unknown && (_candidates.Count >= MaxCandidates || scanned >= MaxUnknownBytes)) { _truncated = true; break; }
            }
            if (comparison == MemoryScanComparison.Unknown && _truncated) break;
        }
        return new MemoryScanSummary(_candidates.Count, scanned, _truncated, timer.Elapsed);
    }

    internal MemoryScanSummary NextScan(MemoryScanComparison comparison, string input, CancellationToken cancellationToken, Action<string>? progress = null)
    {
        if (_candidates.Count == 0) throw new InvalidOperationException("Run a first scan before filtering results.");
        if (comparison is MemoryScanComparison.Unknown) throw new InvalidOperationException("Unknown is only valid for a first scan.");
        ulong target = comparison == MemoryScanComparison.Exact ? Parse(input, _valueType) : 0;
        int size = SizeOf(_valueType);
        int processed = 0;
        var kept = new List<Candidate>(Math.Min(_candidates.Count, 250_000));
        var timer = Stopwatch.StartNew();
        int candidateIndex = 0;
        while (candidateIndex < _candidates.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ulong pageKey = _candidates[candidateIndex].Address & ~0xFFFUL;
            byte[] buffer = new byte[0x1000];
            bool pageRead = NativeMethods.ReadProcessMemory(_remote.Handle, (nint)pageKey, buffer, 0x1000, out nuint read);
            int pageStartProcessed = processed;
            while (candidateIndex < _candidates.Count && (_candidates[candidateIndex].Address & ~0xFFFUL) == pageKey)
            {
                Candidate candidate = _candidates[candidateIndex++];
                processed++;
                int offset = (int)(candidate.Address - pageKey);
                ulong current;
                if (pageRead && offset + size <= (int)read) current = ReadBits(buffer.AsSpan(offset, size), _valueType);
                else
                {
                    try { current = ReadBits(_remote.ReadBytes(candidate.Address, size), _valueType); }
                    catch { continue; }
                }
                bool match = comparison switch
                {
                    MemoryScanComparison.Exact => current == target,
                    MemoryScanComparison.Changed => current != candidate.Bits,
                    MemoryScanComparison.Unchanged => current == candidate.Bits,
                    MemoryScanComparison.Increased => Compare(current, candidate.Bits, _valueType) > 0,
                    MemoryScanComparison.Decreased => Compare(current, candidate.Bits, _valueType) < 0,
                    _ => false
                };
                if (match) kept.Add(new Candidate(candidate.Address, current, candidate.Bits));
            }
            if (processed / 100_000 != pageStartProcessed / 100_000) progress?.Invoke($"Filtered {processed:N0}/{_candidates.Count:N0}; {kept.Count:N0} remain");
        }
        _candidates.Clear();
        _candidates.AddRange(kept);
        return new MemoryScanSummary(_candidates.Count, (ulong)processed * (ulong)size, _truncated, timer.Elapsed);
    }

    internal string Write(ulong address, string input)
    {
        if (!_candidates.Any(candidate => candidate.Address == address)) throw new InvalidOperationException("Select an address from the current scan results.");
        ulong bits = Parse(input, _valueType);
        byte[] bytes = Bytes(bits, _valueType);
        _remote.WriteBytes(address, bytes);
        ulong verified = ReadBits(_remote.ReadBytes(address, bytes.Length), _valueType);
        if (verified != bits) throw new InvalidOperationException("Memory write did not pass read-back verification.");
        int index = _candidates.FindIndex(candidate => candidate.Address == address);
        _candidates[index] = new Candidate(address, verified, _candidates[index].Bits);
        return $"Wrote {Format(verified, _valueType)} to 0x{address:X}.";
    }

    private IEnumerable<(ulong Start, ulong Length)> EnumerateRegions(MemoryScanScope scope)
    {
        ulong cursor = scope == MemoryScanScope.EngineModule ? _moduleBase : 0x10000;
        ulong limit = scope == MemoryScanScope.EngineModule ? _moduleEnd : 0x00007FFFFFFF0000;
        int infoSize = Marshal.SizeOf<NativeMethods.MemoryBasicInformation>();
        while (cursor < limit)
        {
            if (NativeMethods.VirtualQueryEx(_remote.Handle, (nint)cursor, out var info, (nuint)infoSize) == 0) yield break;
            ulong baseAddress = (ulong)info.BaseAddress;
            ulong regionEnd = baseAddress + (ulong)info.RegionSize;
            bool readable = info.State == NativeMethods.MemoryState.Commit && (info.Protect & (NativeMethods.MemoryProtection.NoAccess | NativeMethods.MemoryProtection.Guard)) == 0;
            bool writable = (info.Protect & (NativeMethods.MemoryProtection.ReadWrite | NativeMethods.MemoryProtection.WriteCopy | NativeMethods.MemoryProtection.ExecuteReadWrite | NativeMethods.MemoryProtection.ExecuteWriteCopy)) != 0;
            if (readable && (scope == MemoryScanScope.EngineModule || writable))
            {
                ulong start = Math.Max(cursor, baseAddress);
                ulong end = Math.Min(limit, regionEnd);
                if (end > start) yield return (start, end - start);
            }
            cursor = regionEnd > cursor ? regionEnd : cursor + 0x1000;
        }
    }

    private static int SizeOf(MemoryScanValueType type) => type switch { MemoryScanValueType.Byte => 1, MemoryScanValueType.Int32 or MemoryScanValueType.Float => 4, MemoryScanValueType.Int64 or MemoryScanValueType.Double => 8, _ => 4 };
    private static ulong ReadBits(ReadOnlySpan<byte> bytes, MemoryScanValueType type) => SizeOf(type) switch { 1 => bytes[0], 4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes), 8 => BinaryPrimitives.ReadUInt64LittleEndian(bytes), _ => 0 };
    private static byte[] Bytes(ulong bits, MemoryScanValueType type) { byte[] result = new byte[SizeOf(type)]; if (result.Length == 1) result[0] = (byte)bits; else if (result.Length == 4) BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)bits); else BinaryPrimitives.WriteUInt64LittleEndian(result, bits); return result; }
    private static ulong Parse(string text, MemoryScanValueType type)
    {
        text = text.Trim();
        bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        string value = hex ? text[2..] : text;
        return type switch
        {
            MemoryScanValueType.Byte => hex ? byte.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : byte.Parse(value, CultureInfo.InvariantCulture),
            MemoryScanValueType.Int32 => hex ? unchecked((uint)int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)) : unchecked((uint)int.Parse(value, CultureInfo.InvariantCulture)),
            MemoryScanValueType.Int64 => hex ? unchecked((ulong)long.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)) : unchecked((ulong)long.Parse(value, CultureInfo.InvariantCulture)),
            MemoryScanValueType.Float => BitConverter.SingleToUInt32Bits(float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)),
            MemoryScanValueType.Double => unchecked((ulong)BitConverter.DoubleToInt64Bits(double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture))),
            _ => throw new InvalidOperationException("Unsupported scan value type.")
        };
    }
    private static string Format(ulong bits, MemoryScanValueType type) => type switch
    {
        MemoryScanValueType.Byte => ((byte)bits).ToString(CultureInfo.InvariantCulture),
        MemoryScanValueType.Int32 => unchecked((int)bits).ToString(CultureInfo.InvariantCulture),
        MemoryScanValueType.Int64 => unchecked((long)bits).ToString(CultureInfo.InvariantCulture),
        MemoryScanValueType.Float => BitConverter.UInt32BitsToSingle((uint)bits).ToString("G9", CultureInfo.InvariantCulture),
        MemoryScanValueType.Double => BitConverter.Int64BitsToDouble(unchecked((long)bits)).ToString("G17", CultureInfo.InvariantCulture),
        _ => bits.ToString(CultureInfo.InvariantCulture)
    };
    private static int Compare(ulong left, ulong right, MemoryScanValueType type) => type switch
    {
        MemoryScanValueType.Byte => ((byte)left).CompareTo((byte)right),
        MemoryScanValueType.Int32 => (unchecked((int)left)).CompareTo(unchecked((int)right)),
        MemoryScanValueType.Int64 => (unchecked((long)left)).CompareTo(unchecked((long)right)),
        MemoryScanValueType.Float => BitConverter.UInt32BitsToSingle((uint)left).CompareTo(BitConverter.UInt32BitsToSingle((uint)right)),
        MemoryScanValueType.Double => BitConverter.Int64BitsToDouble(unchecked((long)left)).CompareTo(BitConverter.Int64BitsToDouble(unchecked((long)right))),
        _ => 0
    };
}
