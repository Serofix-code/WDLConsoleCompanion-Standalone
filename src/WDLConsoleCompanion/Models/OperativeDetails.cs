using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WDLConsoleCompanion.Models;

internal sealed class OperativeStatistics
{
    public int Age { get; set; }
    public int Income { get; set; }
    public byte Status { get; set; }
    public string CountryTag { get; init; } = "Unavailable";
    public string DetailedBirthplace { get; init; } = "Unavailable";
    public string PrimaryBiography { get; init; } = "Unavailable";
}

internal sealed class EventCatalogItem
{
    public uint Id { get; init; }
    public string Label { get; init; } = "";
    public string Display => $"{Label} — {Id}";
}

internal sealed class OperativeEventRow
{
    public int Index { get; init; }
    public bool IsPrimary { get; init; }
    public uint Id { get; set; }
    public string Label { get; set; } = "";
    public EventCatalogItem? Replacement { get; set; }
    public string Slot => IsPrimary ? "Primary / birthplace headline" : $"Recent event {Index + 1}";
}

internal sealed class AppearanceOption
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
    public string Display => $"{Label} — {Value}";
}

internal sealed class AppearanceFieldDefinition
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int ByteOffset { get; set; }
    public int BitOffset { get; set; }
    public int BitLength { get; set; }
    public List<AppearanceOption> Options { get; set; } = [];
}

internal sealed class AppearanceFieldValue : INotifyPropertyChanged
{
    private int _value;
    public required AppearanceFieldDefinition Definition { get; init; }
    public string DisplayName => Definition.DisplayName;
    public IReadOnlyList<AppearanceOption> Options => Definition.Options;
    public int Value
    {
        get => _value;
        set { if (_value == value) return; _value = value; OnPropertyChanged(); OnPropertyChanged(nameof(ResolvedName)); }
    }
    public string ResolvedName => Options.FirstOrDefault(option => option.Value == Value)?.Label ?? $"Unknown value {Value}";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class AppearanceSnapshot
{
    public int FormatVersion { get; init; }
    public int FormatType { get; init; }
    public bool IsDefault { get; init; }
    public IReadOnlyList<AppearanceFieldValue> Fields { get; init; } = [];
}

internal sealed class ContractCatalog
{
    public Dictionary<string, string> Types { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Attendance { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class OperativeContract
{
    public string ContractId { get; init; } = "";
    public string Type { get; init; } = "";
    public string ParticipantA { get; init; } = "";
    public string ParticipantB { get; init; } = "";
    public ulong CurrentAttendances { get; init; }
    public ulong PreviousAttendances { get; init; }
}

internal sealed class OperativeAttendance
{
    public string ContractId { get; init; } = "";
    public string ContractType { get; init; } = "";
    public string AttendanceType { get; init; } = "";
    public float StartHour { get; set; }
    public float EndHour { get; set; }
    public byte Priority { get; set; }
    internal ulong AttendanceAddress { get; init; }
    internal ulong ActivityAddress { get; init; }
    internal float OriginalStartHour { get; init; }
    internal float OriginalEndHour { get; init; }
    internal byte OriginalPriority { get; init; }
}
