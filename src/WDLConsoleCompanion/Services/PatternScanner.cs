using System.Diagnostics;

namespace WDLConsoleCompanion.Services;

internal static class PatternScanner
{
    internal static ulong FindUnique(RemoteProcess remote, ProcessModule module, string pattern)
    {
        List<ulong> matches = FindAll(remote, module, pattern, 2);
        if (matches.Count == 1)
        {
            var parsed = Parse(pattern);
            byte[] live = remote.ReadBytes(matches[0], parsed.Values.Length);
            if (IsMatch(live, 0, parsed.Values, parsed.Masks)) return matches[0];
            matches.Clear();
        }
        if (matches.Count == 0) throw new InvalidOperationException($"Signature not found in {module.ModuleName}: {pattern}");
        throw new InvalidOperationException($"Signature is not unique in {module.ModuleName}: {pattern}");
    }

    internal static List<ulong> FindAll(RemoteProcess remote, ProcessModule module, string pattern, int maximumMatches = 4096)
    {
        if (maximumMatches < 1) throw new ArgumentOutOfRangeException(nameof(maximumMatches));
        var (values, masks) = Parse(pattern);
        int anchor = Array.FindIndex(masks, value => value);
        if (anchor < 0) throw new InvalidOperationException("A signature must contain at least one fixed byte.");
        ulong start = (ulong)module.BaseAddress;
        ulong end = start + (ulong)module.ModuleMemorySize;
        var matches = new List<ulong>();
        ulong cursor = start;
        int mbiSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MemoryBasicInformation>();
        while (cursor < end)
        {
            if (NativeMethods.VirtualQueryEx(remote.Handle, (nint)cursor, out var info, (nuint)mbiSize) == 0) break;
            ulong regionStart = Math.Max(cursor, (ulong)info.BaseAddress);
            ulong regionEnd = Math.Min(end, (ulong)info.BaseAddress + (ulong)info.RegionSize);
            var p = info.Protect;
            if (info.State == NativeMethods.MemoryState.Commit && (p & NativeMethods.MemoryProtection.Guard) == 0 &&
                (p & NativeMethods.MemoryProtection.NoAccess) == 0 && regionEnd > regionStart)
            {
                const int chunkSize = 1024 * 1024;
                for (ulong chunk = regionStart; chunk < regionEnd;)
                {
                    int length = (int)Math.Min((ulong)chunkSize, regionEnd - chunk);
                    byte[] data;
                    try { data = remote.ReadBytes(chunk, length); }
                    catch { chunk += (ulong)length; continue; }
                    int candidateStart = 0;
                    while (candidateStart <= data.Length - values.Length)
                    {
                        int anchorSearchStart = candidateStart + anchor;
                        int relative = data.AsSpan(anchorSearchStart).IndexOf(values[anchor]);
                        if (relative < 0) break;
                        int i = anchorSearchStart + relative - anchor;
                        if (i > data.Length - values.Length) break;
                        if (IsMatch(data, i, values, masks)) matches.Add(chunk + (ulong)i);
                        candidateStart = i + 1;
                    }
                    if (matches.Count >= maximumMatches) break;
                    ulong advance = (ulong)Math.Max(1, length - values.Length + 1);
                    chunk += advance;
                }
            }
            if (matches.Count >= maximumMatches) break;
            cursor = regionEnd > cursor ? regionEnd : cursor + 0x1000;
        }
        return matches;
    }

    private static bool IsMatch(byte[] data, int offset, byte[] values, bool[] masks)
    {
        for (int j = 0; j < values.Length; j++) if (masks[j] && data[offset + j] != values[j]) return false;
        return true;
    }

    internal static byte[] ParseBytes(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => Convert.ToByte(x, 16)).ToArray();

    private static (byte[] Values, bool[] Masks) Parse(string pattern)
    {
        string[] tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new InvalidOperationException("Empty signature pattern.");
        var values = new byte[tokens.Length];
        var masks = new bool[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            masks[i] = tokens[i] is not "?" and not "??" and not "*";
            if (masks[i]) values[i] = Convert.ToByte(tokens[i], 16);
        }
        return (values, masks);
    }
}
