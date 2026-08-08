using System.Diagnostics;

namespace WDLConsoleCompanion.Services;

internal static class PatternScanner
{
    internal static ulong FindUnique(RemoteProcess remote, ProcessModule module, string pattern)
    {
        var (values, masks) = Parse(pattern);
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
                    for (int i = 0; i <= data.Length - values.Length; i++)
                    {
                        bool found = true;
                        for (int j = 0; j < values.Length; j++)
                            if (masks[j] && data[i + j] != values[j]) { found = false; break; }
                        if (found) matches.Add(chunk + (ulong)i);
                    }
                    if (matches.Count > 1) break;
                    ulong advance = (ulong)Math.Max(1, length - values.Length + 1);
                    chunk += advance;
                }
            }
            if (matches.Count > 1) break;
            cursor = regionEnd > cursor ? regionEnd : cursor + 0x1000;
        }
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Signature not found in {module.ModuleName}: {pattern}"),
            _ => throw new InvalidOperationException($"Signature is not unique in {module.ModuleName}: {pattern}")
        };
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
