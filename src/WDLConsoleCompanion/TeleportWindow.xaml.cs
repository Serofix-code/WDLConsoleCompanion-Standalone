using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class TeleportWindow : Window
{
    private static readonly Dictionary<TrainerSession, WeakReference<TeleportWindow>> OpenWindows = [];
    private readonly TrainerSession _session;
    private readonly DispatcherTimer _liveTimer;
    private readonly SemaphoreSlim _actionGate = new(1, 1);
    private bool _polling;

    internal TeleportWindow(TrainerSession session)
    {
        InitializeComponent();
        _session = session;
        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(((App)Application.Current).Settings.CoordinateRefreshMilliseconds) };
        _liveTimer.Tick += async (_, _) => await PollCurrentAsync();
        Loaded += async (_, _) => { await RefreshAsync(); _liveTimer.Start(); };
        Closed += (_, _) => _liveTimer.Stop();
    }

    internal static void OpenFor(TrainerSession session, Window owner)
    {
        if (OpenWindows.TryGetValue(session, out WeakReference<TeleportWindow>? reference) && reference.TryGetTarget(out TeleportWindow? existing))
        {
            existing.Activate();
            return;
        }
        var window = new TeleportWindow(session) { Owner = owner };
        OpenWindows[session] = new WeakReference<TeleportWindow>(window);
        window.Closed += (_, _) => OpenWindows.Remove(session);
        window.Show();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var values = await Task.Run(_session.ReadTeleportPositions);
            CurrentBox.Text = values.Current.ToString();
            WaypointBox.Text = values.Waypoint?.ToString() ?? "Not captured — place a waypoint";
            SafetyBox.Text = values.Safety is GamePosition safety ? $"{safety} ({values.SafetyCount} retained)" : "No teleport performed yet";
            XBox.Text = values.Current.X.ToString(CultureInfo.InvariantCulture);
            YBox.Text = values.Current.Y.ToString(CultureInfo.InvariantCulture);
            ZBox.Text = values.Current.Z.ToString(CultureInfo.InvariantCulture);
            Footer.Text = "Coordinate hooks active. Every teleport captures a safety point before writing.";
        }
        catch (Exception ex)
        {
            Footer.Text = ex.Message.StartsWith("Player coordinates are waiting", StringComparison.Ordinal)
                ? ex.Message
                : _session.ReportError("WDL-TELEPORT-001", "Teleport coordinates could not be captured", ex);
        }
    }

    private async Task PollCurrentAsync()
    {
        if (_polling || !_session.IsAttached) return;
        _polling = true;
        try { GamePosition current = await Task.Run(_session.ReadCurrentTeleportPosition); CurrentBox.Text = $"{current} • LIVE {DateTime.Now:HH:mm:ss}"; }
        catch { }
        finally { _polling = false; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Save_Click(object sender, RoutedEventArgs e) { try { _session.SaveTeleportPosition(); Footer.Text = "Current position saved for this session."; } catch (Exception ex) { Footer.Text = _session.ReportError("WDL-TELEPORT-002", "Position could not be saved", ex); } }
    private async void Load_Click(object sender, RoutedEventArgs e) => await RunAsync(_session.LoadTeleportPosition);
    private async void Waypoint_Click(object sender, RoutedEventArgs e) => await RunAsync(_session.TeleportToWaypoint);
    private async void Forward_Click(object sender, RoutedEventArgs e) { if (!float.TryParse(ForwardDistanceBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float distance)) { Footer.Text = "Enter a valid forward distance."; return; } await RunAsync(() => _session.TeleportForward(distance)); }
    private async void Undo_Click(object sender, RoutedEventArgs e) => await RunAsync(_session.UndoTeleport, true);
    private async void EmergencyReturn_Click(object sender, RoutedEventArgs e) => await RunAsync(_session.ReturnToSafeTeleportPosition, true);
    private async void Manual_Click(object sender, RoutedEventArgs e) { if (!float.TryParse(XBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(YBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float y) || !float.TryParse(ZBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) { Footer.Text = "Enter valid invariant-culture X, Y and Z numbers."; return; } await RunAsync(() => _session.TeleportTo(new GamePosition(x, y, z))); }

    private async Task RunAsync(Func<string> action, bool isRecovery = false)
    {
        if (!await _actionGate.WaitAsync(0)) { Footer.Text = "A teleport is already in progress; the duplicate click was ignored so recovery remains correct."; return; }
        try
        {
            string warning = isRecovery
                ? "Return to retained pre-teleport coordinates? This recovery action will not overwrite the safety history."
                : "The current location will be captured for Undo and Emergency Return before any coordinate is written. Entering unloaded geometry can still crash the game. Continue?";
            if (MessageBox.Show(warning, isRecovery ? "Teleport recovery" : "SUPER RISKY teleport", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            Footer.Text = await Task.Run(action);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Footer.Text = _session.ReportError("WDL-TELEPORT-003", "Teleport stopped", ex);
            MessageBox.Show(Footer.Text, "Teleport stopped", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _actionGate.Release(); }
    }
}
