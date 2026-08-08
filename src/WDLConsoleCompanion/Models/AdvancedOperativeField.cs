using System.ComponentModel;

namespace WDLConsoleCompanion.Models;

internal sealed class AdvancedOperativeField : INotifyPropertyChanged
{
    private string _value = "";
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Value { get => _value; set { _value = Normalize(value); OnChanged(nameof(Value)); OnChanged(nameof(ResolvedName)); } }
    public required string Risk { get; init; }
    public required string Description { get; init; }
    public bool IsAvailable { get; init; } = true;
    public IReadOnlyList<MetadataOption> Options { get; init; } = [];
    public string ResolvedName => Options.FirstOrDefault(option => option.Value.Equals(Normalize(Value), StringComparison.OrdinalIgnoreCase))?.Label ?? "Unknown / unlisted hash";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new(name));
    private static string Normalize(string value) => string.Concat(value.Where(Uri.IsHexDigit)).ToUpperInvariant();
}

internal sealed class MetadataOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public string Display => $"{Label} — {Value}";
}
