using System.Collections.ObjectModel;
using System.Text.Json;
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
        InitializeComponent(); _session = session; _operative = operative; Title = $"Experimental Perks — {operative.FirstName} {operative.Surname}";
        string catalogPath = Path.Combine(AppContext.BaseDirectory, "config", "perks.json");
        _catalog = JsonSerializer.Deserialize<List<PerkCatalogItem>>(File.ReadAllText(catalogPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        PerkPicker.ItemsSource = _catalog.Where(item => item.Code != "00000000"); PerkGrid.ItemsSource = _perks;
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
                _perks.Add(PerkRow.From(code, item));
            }
            Footer.Text = $"{snapshot.Ids.Count} perks resolved · capacity {snapshot.Capacity} · {(snapshot.Inline ? "inline" : "allocated")} storage";
        }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-PERK-001", "Perks could not be loaded", ex); MessageBox.Show(Footer.Text, "Could not load perks", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { IsEnabled = true; }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await ReloadAsync();
    private void AddPerk_Click(object sender, RoutedEventArgs e)
    {
        if (PerkPicker.SelectedItem is not PerkCatalogItem item) { MessageBox.Show("Choose a perk from the catalog first."); return; }
        if (_perks.Count >= 80) { MessageBox.Show("This editor supports at most 80 perk slots. Remove one before adding another."); return; }
        _perks.Add(PerkRow.From(item.Code, item));
    }
    private void RemovePerk_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button { Tag: PerkRow row }) _perks.Remove(row); }

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
        public string SearchText => $"{DisplayName ?? InternalName ?? "Unknown"} — {Code} — {ShortDescription ?? LongDescription ?? "No description"}";
        public override string ToString() => SearchText;
    }
    private sealed record PerkRow(string Code, string Name, string Description)
    {
        internal static PerkRow From(string code, PerkCatalogItem? item) => new(code, item?.DisplayName ?? item?.InternalName ?? $"Unknown perk ({code})", item?.ShortDescription ?? item?.LongDescription ?? "No catalog description");
    }
}
