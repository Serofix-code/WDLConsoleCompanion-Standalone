using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class MainWindow : Window
{
    private readonly TrainerSession _session;
    private readonly DispatcherTimer _detectorTimer;
    private OperativesWindow? _operatives;
    private CheatsWindow? _cheatsWindow;
    private bool _attachBusy;
    private bool _suppressAutoInjectUntilGameExit;
    private bool _gameDetected;
    private string? _lastAutoError;
    private DateTime _nextAutoAttemptUtc;
    private DateTime _automaticInjectionReadyUtc;
    private int? _automaticInjectionPid;

    public MainWindow()
    {
        InitializeComponent();
        string config = Path.Combine(AppContext.BaseDirectory, "config");
        try { _session = new TrainerSession(config); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Configuration error", MessageBoxButton.OK, MessageBoxImage.Error); throw; }
        _detectorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _detectorTimer.Tick += async (_, _) => await DetectAndInjectAsync();
        Log("WDL Console Companion ready. Single-player/offline use only.");
        Log("Auto-inject is active. Commands: inject, operative, detach, status, clear, help, exit");
        Loaded += async (_, _) =>
        {
            CommandInput.Focus();
            _detectorTimer.Start();
            await DetectAndInjectAsync();
        };
    }

    private async void CommandInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        string command = CommandInput.Text.Trim().ToLowerInvariant();
        string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string verb = parts.FirstOrDefault() ?? "";
        CommandInput.Clear();
        Log($"> {command}");
        try
        {
            switch (verb)
            {
                case "attach":
                case "inject": await TryAttachAsync(manual: true); break;
                case "operative":
                case "op":
                    if (!_session.IsAttached) await TryAttachAsync(manual: true);
                    if (!_session.IsAttached) break;
                    _operatives ??= new OperativesWindow(_session) { Owner = this };
                    _operatives.Closed += (_, _) => _operatives = null;
                    _operatives.Show(); _operatives.Activate(); await _operatives.RefreshAsync(); break;
                case "detach":
                    _suppressAutoInjectUntilGameExit = true;
                    _operatives?.Close();
                    Log(await Task.Run(_session.Detach));
                    UpdateStatus();
                    Log("Auto-inject paused until the game exits; Manual Inject remains available.");
                    break;
                case "status": Log(_session.IsAttached ? $"Attached to PID {_session.ProcessId}." : "Detached."); break;
                case "cheats": OpenCheatsWindow(); break;
                case "cheatstatus": Log("Cheat status:\n" + _session.CheatStatus()); break;
                case "god":
                case "godmode":
                case "gfodmode":
                case "notrace":
                case "nowanted":
                case "stealth":
                case "ammo":
                case "infammo":
                case "noreload":
                case "norecoil":
                case "fastsearch":
                    if (!_session.IsAttached) await TryAttachAsync(manual: true);
                    if (_session.IsAttached) Log(await Task.Run(() => _session.ToggleCheat(verb, parts.ElementAtOrDefault(1))));
                    break;
                case "clear": LogText.Text = ""; break;
                case "help": Log("inject / attach       manually scan and install the hook\nop / operative          open operative and experimental perk editors\ncheats                   open the visual cheats panel\ncheatstatus              print cheat status\ngodmode [on|off]         infinite player health\nnotrace [on|off]         no wanted level + stealth\ninfammo [on|off]         infinite ammunition\nnoreload [on|off]        skip reload requirement\nnorecoil [on|off]        suppress weapon recoil\nfastsearch [on|off]      end pursuit searches faster\ndetach                   restore all patches and pause auto-inject\nstatus, clear, exit"); break;
                case "exit": Close(); break;
                case "": break;
                default: Log($"Unknown command '{command}'. Type help."); break;
            }
        }
        catch (Exception ex) { Log("ERROR: " + ex.Message); UpdateStatus(); }
    }

    private async void ManualInjectButton_Click(object sender, RoutedEventArgs e) => await TryAttachAsync(manual: true);
    private void CheatsButton_Click(object sender, RoutedEventArgs e) => OpenCheatsWindow();
    private void OpenCheatsWindow()
    {
        _cheatsWindow ??= new CheatsWindow(_session) { Owner = this };
        _cheatsWindow.Closed += (_, _) => _cheatsWindow = null;
        _cheatsWindow.Refresh(); _cheatsWindow.Show(); _cheatsWindow.Activate();
    }

    private async Task DetectAndInjectAsync()
    {
        if (_attachBusy) return;
        if (_session.IsAttached)
        {
            if (!_session.IsAttachedProcessAlive)
            {
                Log("Game process closed; releasing the local trainer session.");
                try { await Task.Run(_session.Detach); } catch (Exception ex) { Log("Cleanup after game exit: " + ex.Message); }
                UpdateStatus();
                _gameDetected = false;
                _suppressAutoInjectUntilGameExit = false;
            }
            return;
        }

        bool running = _session.TargetProcessIsRunning();
        if (!running)
        {
            _gameDetected = false;
            _suppressAutoInjectUntilGameExit = false;
            _lastAutoError = null;
            _automaticInjectionPid = null;
            return;
        }
        int? readyPid = _session.ReadyTargetProcessId();
        if (readyPid is null)
        {
            if (!_gameDetected) { _gameDetected = true; Log("WatchDogsLegion.exe detected; waiting for the main engine DLL…"); }
            return;
        }
        if (_automaticInjectionPid != readyPid)
        {
            _automaticInjectionPid = readyPid;
            _automaticInjectionReadyUtc = DateTime.UtcNow.AddSeconds(12);
            _gameDetected = true;
            Log($"Main game process and Dunia engine ready (PID {readyPid}); automatic injection in 12 seconds…");
            return;
        }
        if (_suppressAutoInjectUntilGameExit || DateTime.UtcNow < _nextAutoAttemptUtc || DateTime.UtcNow < _automaticInjectionReadyUtc) return;
        if (!_gameDetected)
        {
            _gameDetected = true;
            Log("WatchDogsLegion.exe detected; attempting automatic injection…");
        }
        await TryAttachAsync(manual: false);
    }

    private async Task TryAttachAsync(bool manual)
    {
        if (_attachBusy)
        {
            if (manual) Log("An injection attempt is already running.");
            return;
        }
        if (_session.IsAttached)
        {
            if (manual) Log($"Already attached to PID {_session.ProcessId}.");
            return;
        }

        _attachBusy = true;
        ManualInjectButton.IsEnabled = false;
        try
        {
            if (manual) Log("Manual injection requested…");
            string result = await Task.Run(_session.Attach);
            Log((manual ? "Manual injection successful. " : "Automatic injection successful. ") + result);
            _suppressAutoInjectUntilGameExit = false;
            _lastAutoError = null;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            if (manual) Log("MANUAL INJECT ERROR: " + ex.Message);
            else if (!string.Equals(_lastAutoError, ex.Message, StringComparison.Ordinal))
            {
                Log("Auto-inject waiting: " + ex.Message);
                _lastAutoError = ex.Message;
            }
            _nextAutoAttemptUtc = DateTime.UtcNow.AddSeconds(ex.Message.Contains("Signature not found", StringComparison.OrdinalIgnoreCase) ? 60 : 10);
            UpdateStatus();
        }
        finally
        {
            _attachBusy = false;
            ManualInjectButton.IsEnabled = true;
        }
    }

    private void Log(string message)
    {
        LogText.Text += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        LogScroller.ScrollToEnd();
    }
    private void UpdateStatus()
    {
        StatusText.Text = _session.IsAttached ? $"ATTACHED · PID {_session.ProcessId}" : "DETACHED";
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_session.IsAttached ? "#39D98A" : "#657083"));
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _detectorTimer.Stop();
        try { _operatives?.Close(); _cheatsWindow?.Close(); _session.Dispose(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Cleanup warning", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
