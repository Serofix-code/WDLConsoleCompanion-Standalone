using WDLConsoleCompanion.Models;

namespace WDLConsoleCompanion.SaveFormat;

public interface IWdlSaveCodec
{
    bool IsMapped { get; }
    bool TryRead(ReadOnlyMemory<byte> bytes, out IReadOnlyList<Operative> operatives, out string error);
    bool TryWrite(ReadOnlyMemory<byte> original, IReadOnlyList<Operative> operatives, out byte[] result, out string error);
}
