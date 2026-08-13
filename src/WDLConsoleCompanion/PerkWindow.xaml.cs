using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using WDLConsoleCompanion.Models;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class PerkWindow : Window
{
    private readonly TrainerSession _session;
    private readonly OperativeRecord _operative;
    private readonly List<PerkCatalogItem> _catalog;
    private readonly ObservableCollection<PerkRow> _perks = [];

    internal PerkWindow(TrainerSession session, OperativeRecord operative)
    {
        InitializeComponent();
        _session = session; _operative = operative; Title = $"Experimental Perks — {operative.FirstName} {operative.Surname}";
        string catalogPath = Path.Combine(AppContext.BaseDirectory, "config", "perks.json");
        _catalog = JsonSerializer.Deserialize<List<PerkCatalogItem>>(File.ReadAllText(catalogPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        ApplyCatalogFilter(); PerkGrid.ItemsSource = _perks;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        IsEnabled = false;
        try
        {
            PerkSnapshot snapshot = await Task.Run(() => _session.ReadPerks(_operative));
            var byCode = _catalog.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);
            _perks.Clear();
            foreach (uint id in snapshot.Ids)
            {
                string code = id.ToString("X8"); byCode.TryGetValue(code, out PerkCatalogItem? item);
                _perks.Add(PerkRow.From(code, item, true));
            }
            Footer.Text = $"Showing all {snapshot.Ids.Count} perks currently stored on this operative · capacity {snapshot.Capacity} · {(snapshot.Inline ? "inline" : "allocated")} storage.";
        }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-PERK-001", "Perks could not be loaded", ex); MessageBox.Show(Footer.Text, "Could not load perks", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { IsEnabled = true; }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await ReloadAsync();
    private void AddPerk_Click(object sender, RoutedEventArgs e)
    {
        if (PerkPicker.SelectedItem is not PerkCatalogItem item) { MessageBox.Show("Choose a perk from the catalog first."); return; }
        if (_perks.Count >= 80) { MessageBox.Show("This editor supports at most 80 perk slots. Remove one before adding another."); return; }
        _perks.Add(PerkRow.From(item.Code, item, false));
    }
    private void RemovePerk_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button { Tag: PerkRow row }) _perks.Remove(row); }
    private void PerkSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyCatalogFilter();

    private void ApplyCatalogFilter()
    {
        if (PerkPicker is null) return;
        string query = PerkSearch?.Text?.Trim() ?? "";
        IEnumerable<PerkCatalogItem> matches = _catalog.Where(item => item.Code != "00000000");
        if (query.Length > 0) matches = matches.Where(item => item.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase));
        PerkCatalogItem[] results = matches.OrderBy(item => item.ReadableName).ThenBy(item => item.Code).ToArray();
        PerkPicker.ItemsSource = results;
        PerkPicker.SelectedIndex = PerkPicker.Items.Count > 0 ? 0 : -1;
        if (PerkMatchCount is not null) PerkMatchCount.Text = $"{results.Length} matches";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("This experimental operation can crash the game or damage the save. Have you backed up the save and do you want to continue?", "High-risk perk write", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            uint[] ids = _perks.Select(row => Convert.ToUInt32(row.Code, 16)).ToArray();
            IsEnabled = false; Footer.Text = "Writing and verifying perk array…";
            string result = await Task.Run(() => _session.SavePerks(_operative, ids)); Footer.Text = result; MessageBox.Show(result, "Perks saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-PERK-002", "Perks were not saved", ex); MessageBox.Show(Footer.Text, "Perks not saved", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { IsEnabled = true; }
    }

    private sealed class PerkCatalogItem
    {
        public string Code { get; set; } = ""; public string? InternalName { get; set; } public string? DisplayName { get; set; }
        public string? ShortDescription { get; set; } public string? LongDescription { get; set; }
        public string ReadableName => PerkText.ReadableName(this);
        public string Description => PerkText.Description(this);
        public string SearchText => $"{ReadableName} | {InternalName} | {Code} | {Description}";
        public override string ToString() => $"{ReadableName} — {Code} — {Description}";
    }

    private sealed record PerkRow(string Status, string Code, string Name, string InternalName, string Description)
    {
        internal static PerkRow From(string code, PerkCatalogItem? item, bool existing) => new(existing ? "CURRENT" : "ADDED", code,
            item is null ? $"Unknown perk ({code})" : item.ReadableName, item?.InternalName ?? "Not present in catalog",
            item?.Description ?? "Unknown hash currently stored on this operative; preserve it unless you know what it controls.");
    }

    private static class PerkText
    {
        internal static string ReadableName(PerkCatalogItem item)
        {
            if (item.InternalName?.Equals("Passive_DiscountClothing", StringComparison.OrdinalIgnoreCase) == true)
                return "Clothing Discount (Loyalty Card)";
            if (!string.IsNullOrWhiteSpace(item.DisplayName)) return item.DisplayName.Trim();
            return Humanize(item.InternalName ?? $"Unknown perk {item.Code}");
        }

        internal static string Description(PerkCatalogItem item)
        {
            if (item.InternalName?.Equals("Passive_DiscountClothing", StringComparison.OrdinalIgnoreCase) == true)
                return "All operatives receive discounts at clothing shops.";
            if (!string.IsNullOrWhiteSpace(item.LongDescription) && IsUseful(item.LongDescription)) return item.LongDescription.Trim();
            if (!string.IsNullOrWhiteSpace(item.ShortDescription) && IsUseful(item.ShortDescription)) return item.ShortDescription.Trim();
            string internalName = item.InternalName ?? "";
            if (internalName.StartsWith("WeaponProgression_", StringComparison.OrdinalIgnoreCase))
                return $"Weapon access/progression entry for {Humanize(internalName["WeaponProgression_".Length..]).ToLowerInvariant()}.";
            if (internalName.StartsWith("Passive_", StringComparison.OrdinalIgnoreCase))
                return $"Passive operative ability: {Humanize(internalName["Passive_".Length..]).ToLowerInvariant()}.";
            if (internalName.StartsWith("Ability_", StringComparison.OrdinalIgnoreCase))
                return $"Active operative ability: {Humanize(internalName["Ability_".Length..]).ToLowerInvariant()}.";
            return $"Game perk entry: {Humanize(internalName.Length > 0 ? internalName : item.Code)}. No localized description is available.";
        }

        private static bool IsUseful(string value) => value.Length <= 220 && !value.Contains("Like I said", StringComparison.OrdinalIgnoreCase);
        private static string Humanize(string value)
        {
            string text = value.Replace('_', ' ').Replace('-', ' ');
            text = Regex.Replace(text, "(?<=[a-z0-9])(?=[A-Z])", " ");
            text = Regex.Replace(text, "\\s+", " ").Trim();
            return text.Replace("LTL", "non-lethal", StringComparison.OrdinalIgnoreCase);
        }
    }
}
