using System.Collections.ObjectModel;
using System.Windows;
using WDLConsoleCompanion.Models;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;
public partial class AppearanceWindow : Window
{
    private readonly TrainerSession _session; private readonly OperativeRecord _operative; private readonly ObservableCollection<AppearanceFieldValue> _fields = [];
    internal AppearanceWindow(TrainerSession session, OperativeRecord operative) { InitializeComponent(); _session = session; _operative = operative; Title = $"Appearance — {operative.FirstName} {operative.Surname}"; FieldGrid.ItemsSource = _fields; Loaded += async (_, _) => await ReloadAsync(); }
    private async Task ReloadAsync() { bool defaults = DefaultsBox.IsChecked == true; try { AppearanceSnapshot s = await Task.Run(() => _session.ReadAppearance(_operative, defaults)); _fields.Clear(); foreach (var field in s.Fields) _fields.Add(field); FormatText.Text = $"{(s.IsDefault ? "Wardrobe defaults" : "Current appearance")} · format version {s.FormatVersion} · type {s.FormatType}"; Footer.Text = s.FormatVersion == 12 && s.FormatType == 2 ? "Unpacked appearance is editable." : "Unsupported/unpacked format: switch to this operative and away, then reload."; } catch (Exception ex) { Footer.Text = _session.ReportError("WDL-APPEAR-001", "Appearance read stopped", ex); } }
    private async void ModeChanged(object sender, RoutedEventArgs e) { if (IsLoaded) await ReloadAsync(); }
    private async void Save_Click(object sender, RoutedEventArgs e) { if (FieldGrid.SelectedItem is not AppearanceFieldValue field) { MessageBox.Show("Select one component first."); return; } if (MessageBox.Show("Appearance writes can crash or produce invisible models. Continue?", "Confirm appearance write", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; bool defaults = DefaultsBox.IsChecked == true; try { Footer.Text = await Task.Run(() => _session.SaveAppearanceField(_operative, defaults, field)); await ReloadAsync(); } catch (Exception ex) { Footer.Text = _session.ReportError("WDL-APPEAR-002", "Appearance was not saved", ex); MessageBox.Show(Footer.Text, "Appearance not saved", MessageBoxButton.OK, MessageBoxImage.Warning); } }
}
