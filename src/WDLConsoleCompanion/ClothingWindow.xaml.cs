using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WDLConsoleCompanion.Models;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class ClothingWindow : Window
{
    private static readonly Dictionary<TrainerSession, WeakReference<ClothingWindow>> OpenWindows = [];
    private readonly TrainerSession _session;
    private readonly ICollectionView _shopView;
    private CancellationTokenSource? _cancellation;

    private ClothingWindow(TrainerSession session)
    {
        InitializeComponent();
        _session = session;
        _shopView = CollectionViewSource.GetDefaultView(session.ClothingShops.OrderBy(shop => shop.Name).ToList());
        ShopList.ItemsSource = _shopView;
        UpdateResultCount();
        if (!_shopView.IsEmpty) ShopList.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ShopList is null || ResultCount is null || _shopView is null) return;
        string query = SearchBox.Text.Trim();
        _shopView.Filter = item => item is ClothingShopDefinition shop &&
            (query.Length == 0 || shop.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        _shopView.Refresh();
        UpdateResultCount();
        if (!_shopView.IsEmpty) ShopList.SelectedIndex = 0;
    }

    private void UpdateResultCount() => ResultCount.Text = $"{_shopView.Cast<object>().Count()} / {_session.ClothingShops.Count} shops";

    internal static void OpenFor(TrainerSession session, Window owner)
    {
        if (OpenWindows.TryGetValue(session, out WeakReference<ClothingWindow>? reference) && reference.TryGetTarget(out ClothingWindow? existing))
        {
            existing.Show(); existing.Activate(); return;
        }
        var window = new ClothingWindow(session) { Owner = owner };
        OpenWindows[session] = new(window);
        window.Closed += (_, _) => { window._cancellation?.Cancel(); OpenWindows.Remove(session); };
        window.Show();
    }

    private async void Spawn_Click(object sender, RoutedEventArgs e)
    {
        if (ShopList.SelectedItem is not ClothingShopDefinition shop) { StatusText.Text = "Select a clothing shop first."; return; }
        SetBusy(true);
        _cancellation = new();
        StatusText.Text = $"Spawning {shop.Name} at the reticle…";
        try { StatusText.Text = await _session.SpawnClothingShopAsync(shop, _cancellation.Token); }
        catch (OperationCanceledException) { StatusText.Text = "Shop spawn cancelled."; }
        catch (Exception ex)
        {
            StatusText.Text = _session.ReportError("WDL-CLOTHING-001", $"{shop.Name} was not spawned", ex);
            MessageBox.Show(this, StatusText.Text, "Clothing shop not spawned", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _cancellation?.Dispose(); _cancellation = null; SetBusy(false); }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        _cancellation = new();
        try { StatusText.Text = await _session.RemoveSpawnedClothingShopAsync(_cancellation.Token); }
        catch (OperationCanceledException) { StatusText.Text = "Removal cancelled."; }
        catch (Exception ex)
        {
            StatusText.Text = _session.ReportError("WDL-CLOTHING-002", "The spawned clothing shop was not removed", ex);
            MessageBox.Show(this, StatusText.Text, "Clothing shop not removed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _cancellation?.Dispose(); _cancellation = null; SetBusy(false); }
    }

    private async void UnlockAll_Click(object sender, RoutedEventArgs e)
    {
        int total = _session.ClothingRewardCount;
        MessageBoxResult answer = MessageBox.Show(this,
            $"This requests {total} individual clothing records through the game's reward system:\n\n• 456 retail-shop items\n• 148 owned DLC/ULC items\n• 41 DedSec vending-machine items\n• 111 activity and progression rewards\n• 23 clothing bundles\n\nThe game does not publish a reliable ownership result for readable clothing records, so the companion reports completed game-thread submissions without claiming ownership was verified. Bulk ownership changes remain SUPER RISKY. Back up your save, stand in active gameplay, and do not close the game during the operation. Continue?",
            "Confirm unlock all clothing", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        SetBusy(true);
        UnlockProgress.Visibility = Visibility.Visible;
        UnlockProgress.Maximum = total;
        UnlockProgress.Value = 0;
        CancelButton.Visibility = Visibility.Visible;
        _cancellation = new();
        var progress = new Progress<(int Done, int Total, string Group)>(value =>
        {
            UnlockProgress.Maximum = value.Total;
            UnlockProgress.Value = value.Done;
            StatusText.Text = $"{value.Group}: checked {value.Done} of {value.Total} clothing records…";
        });
        try
        {
            ClothingUnlockResult result = await _session.UnlockAllClothingAsync(progress, _cancellation.Token);
            StatusText.Text = $"Submitted {result.Processed} readable clothing records. Switch operatives, then close and reopen the wardrobe to refresh ownership.";
            MessageBox.Show(this, StatusText.Text, "Clothing reward pass complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { StatusText.Text = $"Unlock cancelled after {(int)UnlockProgress.Value} of {total}. Already granted clothing remains owned."; }
        catch (Exception ex)
        {
            StatusText.Text = _session.ReportError("WDL-CLOTHING-003", "Unlock all clothing stopped", ex);
            MessageBox.Show(this, StatusText.Text, "Clothing unlock stopped", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _cancellation?.Dispose(); _cancellation = null;
            CancelButton.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void SetBusy(bool busy)
    {
        ShopList.IsEnabled = !busy;
        SpawnButton.IsEnabled = !busy;
        RemoveButton.IsEnabled = !busy;
        UnlockAllButton.IsEnabled = !busy;
    }
}
