using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WDLConsoleCompanion.Models;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class EventsWindow : Window, INotifyPropertyChanged
{
    private readonly TrainerSession _session;
    private readonly OperativeRecord _operative;
    private readonly ObservableCollection<OperativeEventRow> _rows = [];
    private readonly IReadOnlyList<EventCatalogItem> _catalog;
    private IReadOnlyList<EventCatalogItem> _filteredCatalog = [];
    private bool _controlsReady;

    public System.Collections.IEnumerable FilteredCatalog => _filteredCatalog;
    public event PropertyChangedEventHandler? PropertyChanged;

    internal EventsWindow(TrainerSession session, OperativeRecord operative)
    {
        _session = session;
        _operative = operative;
        _catalog = session.EventCatalog();
        InitializeComponent();
        DataContext = this;
        _controlsReady = true;
        Title = $"Recent Events — {operative.FirstName} {operative.Surname}";
        EventGrid.ItemsSource = _rows;
        Loaded += async (_, _) => { ApplyFilter(); await ReloadAsync(); };
    }

    private async Task ReloadAsync()
    {
        try
        {
            IReadOnlyList<OperativeEventRow> rows = await Task.Run(() => _session.ReadRecentEvents(_operative));
            _rows.Clear();
            foreach (OperativeEventRow row in rows) _rows.Add(row);
            Footer.Text = $"{rows.Count - 1} recent events plus primary metadata loaded. Search all {_catalog.Count:N0} known events, then choose a replacement on one slot.";
        }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-EVENT-001", "Biography events could not be loaded", ex); }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        OperativeEventRow? row = EventGrid.SelectedItem as OperativeEventRow;
        if (row?.Replacement is null)
        {
            OperativeEventRow[] changed = _rows.Where(candidate => candidate.Replacement is not null).ToArray();
            if (changed.Length == 1) row = changed[0];
            else
            {
                MessageBox.Show(changed.Length == 0
                    ? "Choose a replacement from one row's dropdown first."
                    : "More than one row has a replacement. Click the exact row you want to save.");
                return;
            }
        }
        EventCatalogItem item = row.Replacement!;
        if (MessageBox.Show($"Replace '{row.Label}' with '{item.Label}'?\n\nThis is a HIGH-RISK biography edit.", "Confirm event replacement", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { Footer.Text = await Task.Run(() => _session.SaveRecentEvent(_operative, row, item.Id)); await ReloadAsync(); ApplyFilter(); }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-EVENT-002", "Biography event was not saved", ex); MessageBox.Show(Footer.Text, "Event not saved", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void EventSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void FilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();
    private void ClearSearch_Click(object sender, RoutedEventArgs e) { EventSearch.Clear(); BirthplacesOnly.IsChecked = false; ApplyFilter(); EventSearch.Focus(); }

    private void Replacement_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: OperativeEventRow row, SelectedItem: EventCatalogItem item })
        {
            row.Replacement = item;
            EventGrid.SelectedItem = row;
            Footer.Text = $"Ready to replace '{row.Label}' with '{item.Label}'. Press Save selected row.";
        }
    }

    private void ApplyFilter()
    {
        // TextChanged and Checked fire while InitializeComponent is constructing the XAML tree.
        if (!_controlsReady || EventSearch is null || BirthplacesOnly is null || SearchStatus is null) return;
        string[] terms = EventSearch.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool birthsOnly = BirthplacesOnly.IsChecked == true;
        EventCatalogItem[] allMatches = _catalog
            .Where(item => (!birthsOnly || item.Label.Contains("BIRTH_", StringComparison.OrdinalIgnoreCase) || item.Label.Contains("Born in ", StringComparison.OrdinalIgnoreCase))
                && terms.All(term => item.Display.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        int displayLimit = terms.Length == 0 && !birthsOnly ? 400 : 1000;
        EventCatalogItem[] visibleMatches = allMatches.Take(displayLimit).ToArray();

        // Retain chosen replacements if the search changes, so WPF does not clear their SelectedItem.
        EventCatalogItem[] selected = _rows.Select(row => row.Replacement).OfType<EventCatalogItem>().ToArray();
        _filteredCatalog = selected.Concat(visibleMatches).DistinctBy(item => item.Id).ToArray();
        PropertyChanged?.Invoke(this, new(nameof(FilteredCatalog)));
        SearchStatus.Text = allMatches.Length > visibleMatches.Length
            ? $"{allMatches.Length:N0} matching events; showing the first {visibleMatches.Length:N0}. Type in the search bar to narrow by internal name, description, or ID."
            : $"{allMatches.Length:N0} matching events from {_catalog.Count:N0} total.";
    }
}
