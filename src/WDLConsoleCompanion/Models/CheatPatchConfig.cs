namespace WDLConsoleCompanion.Models;

internal sealed class CheatPatchConfig
{
    public string[] Modules { get; set; } = [];
    public string Pattern { get; set; } = "";
    public int Offset { get; set; }
    public string ExpectedBytes { get; set; } = "";
    public string ReplacementBytes { get; set; } = "";
}
