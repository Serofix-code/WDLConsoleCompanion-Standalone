using WDLConsoleCompanion.ViewModels;

namespace WDLConsoleCompanion.Models;

public sealed class Operative : ObservableObject
{
    private int _nameId;
    private int _surnameId;
    private string _displayName;
    public Operative(string id, int nameId, int surnameId, string displayName)
    { Id = id; _nameId = nameId; _surnameId = surnameId; _displayName = displayName; }
    public string Id { get; }
    public int NameId { get => _nameId; set { if (SetProperty(ref _nameId, value)) IsDirty = true; } }
    public int SurnameId { get => _surnameId; set { if (SetProperty(ref _surnameId, value)) IsDirty = true; } }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string Initials => string.Join("", DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));
    public bool IsDirty { get; private set; }
    public void UpdateLive(int nameId, int surnameId, string displayName)
    {
        if (IsDirty) return;
        _nameId = nameId; _surnameId = surnameId; _displayName = displayName;
        OnPropertyChanged(nameof(NameId)); OnPropertyChanged(nameof(SurnameId)); OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(Initials));
    }
    public void MarkClean() => IsDirty = false;
}
