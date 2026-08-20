using System.Windows;
using WDLConsoleCompanion.Models;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class AccentWindow : Window
{
    private readonly TrainerSession _session;
    private readonly OperativeRecord _operative;
    private List<AdvancedOperativeField> _sources = [];
    private AdvancedOperativeField? _selected;

    internal AccentWindow(TrainerSession session, OperativeRecord operative)
    {
        InitializeComponent();
        _session = session; _operative = operative;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        SetBusy(true, "Reading the validated voice-profile chain…");
        try
        {
            var fields = await Task.Run(() => _session.ReadAdvancedFields(_operative));
            _sources = fields.Where(f => (f.Key is "playerpersona" or "voiceactor" or "voiceprofile" or "characterdeck") && f.IsAvailable).ToList();
            if (_sources.Count == 0) throw new InvalidOperationException("No validated voice/persona fields are available for this operative.");
            SourceBox.ItemsSource = _sources; SourceBox.SelectedIndex = 0;
            Footer.Text = "Choose a source first. Player Voice Actor / Persona is the closest mapped accent candidate; Voice Profile changes pitch/modulation only.";
        }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-ACCENT-001", "Accent data could not be read", ex); }
        finally { SetBusy(false); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Source_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selected = SourceBox.SelectedItem as AdvancedOperativeField;
        if (_selected is null) return;
        AccentBox.ItemsSource = _selected.Options; AccentBox.SelectedValue = _selected.Value;
        CurrentText.Text = $"{_selected.DisplayName}: {_selected.ResolvedName} — {_selected.Value}";
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || AccentBox.SelectedItem is not MetadataOption option) { MessageBox.Show("Select a voice/persona source and value first.", "Accent not selected", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (MessageBox.Show($"Set {_operative.FirstName} {_operative.Surname}'s profile to '{option.Label}'?\n\nThis is a high-risk live memory edit. Back up your save first.", "Confirm accent edit", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _selected.Value = option.Value;
        SetBusy(true, "Validating and saving accent/profile…");
        try { Footer.Text = await Task.Run(() => _session.SaveAdvancedField(_operative, _selected)); CurrentText.Text = $"{_selected.DisplayName}: {_selected.ResolvedName} — {_selected.Value}"; MessageBox.Show(Footer.Text, "Voice/accent value saved", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-ACCENT-002", "Accent was not saved", ex); MessageBox.Show(Footer.Text, "Accent not saved", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private void SetBusy(bool busy, string? message = null) { AccentBox.IsEnabled = !busy; if (message is not null) Footer.Text = message; }
}
