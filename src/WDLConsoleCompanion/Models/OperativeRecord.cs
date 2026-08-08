namespace WDLConsoleCompanion.Models;

public sealed class OperativeRecord
{
    public int Index { get; init; }
    public ulong OperativeAddress { get; init; }
    public ulong Id { get; init; }
    public string FirstName { get; set; } = "<unknown>";
    public string Surname { get; set; } = "<unknown>";
    public int FirstNameLocId { get; init; }
    public int SurnameLocId { get; init; }
    public int Availability { get; set; }
    public int Origin { get; set; }
    public string CurrentAppearanceCode { get; set; } = "";
    public string DefaultAppearanceCode { get; set; } = "";
    public string IdHex => $"0x{Id:X16}";
}

public sealed record OperativeChoice(int Value, string Label)
{
    public override string ToString() => Label;
}

public static class OperativeChoices
{
    public static IReadOnlyList<OperativeChoice> Availabilities { get; } =
    [
        new(0, "Available"), new(1, "Missing"), new(2, "Arrested"), new(3, "Injured"), new(4, "Dead")
    ];
    public static IReadOnlyList<OperativeChoice> Origins { get; } =
    [
        new(0, "Undefined"), new(1, "Initial"), new(2, "Recruited"), new(4, "Re-recruited"),
        new(5, "Prestige"), new(6, "Prestige (Alt)"), new(7, "Borough Uprising")
    ];
}
