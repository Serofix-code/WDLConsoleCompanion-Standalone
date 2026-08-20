namespace WDLConsoleCompanion.Models;

internal sealed class ClothingCatalog
{
    public List<ClothingShopDefinition> Shops { get; set; } = [];
    public List<ClothingRewardRecord> RewardRecords { get; set; } = [];
}

internal sealed class ClothingShopDefinition
{
    public string Name { get; set; } = "";
    public string Archetype { get; set; } = "";
}

internal sealed class ClothingRewardRecord
{
    public string Database { get; set; } = "";
    public string Label { get; set; } = "";
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
}
