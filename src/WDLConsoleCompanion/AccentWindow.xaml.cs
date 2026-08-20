using System.Windows;
using WDLConsoleCompanion.Models;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class AccentWindow : Window
{
    private readonly TrainerSession _session;
    private readonly OperativeRecord _operative;
    private AdvancedOperativeField? _voiceProfile;

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
            _voiceProfile = fields.FirstOrDefault(f => f.Key == "voiceprofile");
            if (_voiceProfile is null || !_voiceProfile.IsAvailable) throw new InvalidOperationException("Voice-profile data is unavailable for this operative.");
            AccentBox.ItemsSource = _voiceProfile.Options;
            AccentBox.SelectedValue = _voiceProfile.Value;
            CurrentText.Text = $"{_voiceProfile.ResolvedName} — {_voiceProfile.Value}";
            Footer.Text = $"{_voiceProfile.Options.Count:N0} known profiles loaded. Select a profile or enter a raw value in Advanced metadata.";
        }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-ACCENT-001", "Accent data could not be read", ex); }
        finally { SetBusy(false); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceProfile is null || AccentBox.SelectedItem is not MetadataOption option) { MessageBox.Show("Select an accent/profile first.", "Accent not selected", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (MessageBox.Show($"Set {_operative.FirstName} {_operative.Surname}'s profile to '{option.Label}'?\n\nThis is a high-risk live memory edit. Back up your save first.", "Confirm accent edit", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _voiceProfile.Value = option.Value;
        SetBusy(true, "Validating and saving accent/profile…");
        try { Footer.Text = await Task.Run(() => _session.SaveAdvancedField(_operative, _voiceProfile)); CurrentText.Text = $"{_voiceProfile.ResolvedName} — {_voiceProfile.Value}"; MessageBox.Show(Footer.Text, "Accent saved", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-ACCENT-002", "Accent was not saved", ex); MessageBox.Show(Footer.Text, "Accent not saved", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private void SetBusy(bool busy, string? message = null) { AccentBox.IsEnabled = !busy; if (message is not null) Footer.Text = message; }
}
