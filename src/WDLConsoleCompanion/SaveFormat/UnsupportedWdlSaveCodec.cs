using WDLConsoleCompanion.Models;

namespace WDLConsoleCompanion.SaveFormat;

public sealed class UnsupportedWdlSaveCodec : IWdlSaveCodec
{
    public bool IsMapped => false;

    public bool TryRead(ReadOnlyMemory<byte> bytes, out IReadOnlyList<Operative> operatives, out string error)
    {
        operatives = Array.Empty<Operative>();
        error = "Operative records are not mapped for this save version. Backup and restore remain available.";
        return false;
    }

    public bool TryWrite(ReadOnlyMemory<byte> original, IReadOnlyList<Operative> operatives, out byte[] result, out string error)
    {
        // TODO(save-format): Map the file header, compression/encryption, operative table,
        // stable operative IDs, string encoding, checksums, and integrity fields here.
        // Never patch guessed byte offsets: different game/save versions may move records.
        result = Array.Empty<byte>();
        error = "Write blocked: the on-disk operative format has not been safely mapped.";
        return false;
    }
}
