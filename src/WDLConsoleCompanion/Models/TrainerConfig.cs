using System.Text.Json.Serialization;

namespace WDLConsoleCompanion.Models;

public sealed class TrainerConfig
{
    public string ProcessName { get; set; } = "WatchDogsLegion";
    public string[] DuniaModules { get; set; } = [];
    public int MaxRosterCount { get; set; } = 256;
    public SignatureConfig OperativeManagerHook { get; set; } = new();
    public RelativeSignatureConfig CensusManagerGlobal { get; set; } = new();
    public OffsetConfig Offsets { get; set; } = new();
}

public class SignatureConfig
{
    public string Pattern { get; set; } = "";
    public int MatchOffset { get; set; }
    public string ExpectedBytes { get; set; } = "";
}

public sealed class RelativeSignatureConfig : SignatureConfig
{
    public int DisplacementOffset { get; set; }
    public int InstructionLength { get; set; }
}

public sealed class OffsetConfig
{
    public int RosterCount { get; set; }
    public int RosterArray { get; set; }
    public int OperativeId { get; set; }
    public int OperativeAvailability { get; set; }
    public int OperativeOrigin { get; set; }
    public int CurrentAppearance { get; set; }
    public int DefaultAppearance { get; set; }
    public int CensusCount { get; set; }
    public int CensusArray { get; set; }
    public int CensusEntryId { get; set; }
    public int CensusEntryActor { get; set; }
    public int CensusActorDescriptor { get; set; }
    public int CensusDescriptorNameData { get; set; }
    public int FirstNameLocId { get; set; }
    public int SurnameLocId { get; set; }
}
