using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class MainWindow : Window
{
    private readonly TrainerSession _session;
    private readonly DispatcherTimer _detectorTimer;
    private readonly string _configDirectory;
    private readonly string _hotkeyPath;
    private readonly HotkeySettings _hotkeySettings;
    private readonly GlobalHotkeyManager _hotkeys;
    private OperativesWindow? _operatives;
    private CheatsWindow? _cheatsWindow;
    private MemoryScannerWindow? _scannerWindow;
    private SettingsWindow? _settingsWindow;
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
        _configDirectory = Path.Combine(AppContext.BaseDirectory, "config");
        _hotkeyPath = Path.Combine(_configDirectory, "hotkeys.json");
        try { _session = new TrainerSession(_configDirectory); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Configuration error", MessageBoxButton.OK, MessageBoxImage.Error); throw; }
        _session.Activity += message => Dispatcher.BeginInvoke(() => Log(message));
        _hotkeySettings = HotkeySettings.Load(_hotkeyPath);
        _hotkeys = new GlobalHotkeyManager(this, command => _ = HandleHotkeyAsync(command), Log);
        _detectorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _detectorTimer.Tick += async (_, _) => await DetectAndInjectAsync();
        Log("WDL Console Companion ready. Single-player/offline use only.");
        Log($"TEMPORARY RESEARCH CSV logging enabled: {_session.ResearchLogPath}. Remove this after the camera is identified.");
        ApplyRuntimeSettings();
        Log(((App)Application.Current).Settings.AutoInject ? "Auto-inject is active. Commands: inject, operative, detach, status, clear, help, exit" : "Auto-inject is disabled in Settings; Manual Inject remains available.");
        Loaded += async (_, _) =>
        {
            CommandInput.Focus();
            _hotkeys.Register(_hotkeySettings);
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
                    await OpenOperativesAsync(); break;
                case "detach":
                    _suppressAutoInjectUntilGameExit = true;
                    _operatives?.Close();
                    Log(await Task.Run(_session.Detach));
                    UpdateStatus();
                    Log("Auto-inject paused until the game exits; Manual Inject remains available.");
                    break;
                case "status": Log(_session.IsAttached ? $"Attached to PID {_session.ProcessId}." : "Detached."); break;
                case "cheats": OpenCheatsWindow(); break;
                case "scanner":
                case "scan": await OpenScannerAsync(); break;
                case "teleport":
                    if (!_session.IsAttached) await TryAttachAsync(manual: true);
                    if (_session.IsAttached) TeleportWindow.OpenFor(_session, this);
                    break;
                case "clothes":
                case "clothing":
                    if (!_session.IsAttached) await TryAttachAsync(manual: true);
                    if (_session.IsAttached) ClothingWindow.OpenFor(_session, this);
                    break;
                case "freecam":
                case "camera":
                    if (!_session.IsAttached) await TryAttachAsync(manual: true);
                    FreecamWindow.OpenFor(_session, this);
                    break;
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
                case "hackcooldown":
                case "freezehack":
                case "dronerange":
                case "dronehealth":
                case "onehitkill":
                case "ohk":
                case "immortal":
                case "disablefelony":
                case "disabledetection":
                    if (!_session.IsAttached) await TryAttachAsync(manual: true);
                    if (_session.IsAttached) Log(await Task.Run(() => _session.ToggleCheat(verb, parts.ElementAtOrDefault(1))));
                    break;
                case "clear": ClearConsole(); break;
                case "copyconsole": CopyConsole(); break;
                case "saveconsole": SaveConsole(); break;
                case "shortcuts": OpenShortcuts(); break;
                case "settings": OpenSettings(); break;
                case "eto": if (!_session.IsAttached) await TryAttachAsync(true); if (_session.IsAttached) Log(await Task.Run(_session.AddEto)); break;
                case "techpoints":
                case "tech": if (!_session.IsAttached) await TryAttachAsync(true); if (_session.IsAttached) Log(await Task.Run(_session.AddTechPoints)); break;
                case "endchase":
                case "spawnracecar":
                case "spawnshop":
                case "distractall":
                case "disruptall":
                    if (!_session.IsAttached) await TryAttachAsync(true);
                    if (_session.IsAttached) Log(await Task.Run(() => _session.RunGameAction(verb)));
                    break;
                case "theme":
                    string requestedTheme = parts.ElementAtOrDefault(1)?.ToLowerInvariant() switch { "dark" => "Dark", "light" => "Light", "system" => "System", _ => throw new InvalidOperationException("Use: theme dark, theme light, or theme system.") };
                    ((App)Application.Current).SetTheme(requestedTheme);
                    Log($"Theme changed to {requestedTheme}.");
                    break;
                case "help": Log("inject / attach       manually scan and install the hook\nop / operative          open Operative Studio\ncheats                   open the visual cheats panel\nclothes / clothing       shops + very experimental bulk clothing\nfreecam / camera         open the very experimental Freecam Lab\nscan / scanner           exact and change-filter memory scanner (super risky)\nteleport                 live coordinates, safety history and emergency return\nshortcuts                configure global F1-F12 bindings\nsettings                 theme, attachment, cleanup and performance\ntheme dark|light|system  change the application theme\neto                      add 1000 ETO (super risky)\ntech / techpoints        add 10 tech points (super risky)\ncheatstatus              print cheat status\ngodmode [on|off]         infinite player health\nimmortal [on|off]        game-thread death immunity (super risky)\nnotrace [on|off]         no wanted level + stealth\ndisablefelony [on|off]   disable the felony system (super risky)\ndisabledetection [on|off] make the player undetectable (super risky)\ninfammo [on|off]         infinite ammunition\nnoreload [on|off]        skip reload requirement\nnorecoil [on|off]        suppress weapon recoil\nfastsearch [on|off]      end pursuit searches faster\nhackcooldown [on|off]    instant hacker-skill cooldowns (super risky)\nfreezehack [on|off]      freeze active hack timer (super risky)\ndronerange [on|off]      maximum drone range (super risky)\ndronehealth [on|off]     infinite controlled-drone health (super risky)\nonehitkill [on|off]      one-hit non-player targets (super risky)\nendchase                 end the current felony chase\nspawnracecar             spawn a racecar at the reticle\nspawnshop                spawn a DedSec shop at the reticle\ndistractall / disruptall affect nearby human agents\ncopyconsole              copy the complete event console\nsaveconsole              export the console as a text file\nclear                    clear the console\ndetach                   restore all patches and pause auto-inject\nstatus, exit"); break;
                case "exit": Close(); break;
                case "": break;
                default: Log($"Unknown command '{command}'. Type help."); break;
            }
        }
        catch (Exception ex) { LogError("WDL-COMMAND-001", "Command failed", ex); UpdateStatus(); }
    }

    private async void ManualInjectButton_Click(object sender, RoutedEventArgs e) => await TryAttachAsync(manual: true);
    private async void OperativesButton_Click(object sender, RoutedEventArgs e) => await OpenOperativesAsync();
    private void CheatsButton_Click(object sender, RoutedEventArgs e) => OpenCheatsWindow();
    private async void ScannerButton_Click(object sender, RoutedEventArgs e) => await OpenScannerAsync();
    private void ShortcutsButton_Click(object sender, RoutedEventArgs e) => OpenShortcuts();
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void CopyConsole_Click(object sender, RoutedEventArgs e) => CopyConsole();
    private void ClearConsole_Click(object sender, RoutedEventArgs e) => ClearConsole();
    private void SaveConsole_Click(object sender, RoutedEventArgs e) => SaveConsole();

    private async Task OpenOperativesAsync()
    {
        if (!_session.IsAttached) await TryAttachAsync(manual: true);
        if (!_session.IsAttached) return;
        if (_operatives is null)
        {
            _operatives = new OperativesWindow(_session) { Owner = this };
            _operatives.Closed += (_, _) => _operatives = null;
        }
        _operatives.Show(); _operatives.Activate(); await _operatives.RefreshAsync();
        Log("Operative Studio opened and roster refreshed.");
    }

    private void OpenShortcuts() { Log("Shortcut editor opened."); new HotkeysWindow(_hotkeySettings, _hotkeyPath, () => _hotkeys.Register(_hotkeySettings)) { Owner = this }.Show(); }

    private void OpenSettings()
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(((App)Application.Current).Settings) { Owner = this };
        _settingsWindow.SettingsSaved += () => { ApplyRuntimeSettings(); Log("Settings saved and applied."); };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(); Log("Settings opened.");
    }

    private void ApplyRuntimeSettings()
    {
        AppSettings settings = ((App)Application.Current).Settings;
        AutoInjectStatus.Text = settings.AutoInject ? "AUTO-INJECT ON" : "AUTO-INJECT OFF";
        if (settings.CompanionMemoryTrimMb > 0 && Environment.WorkingSet / 1024 / 1024 > settings.CompanionMemoryTrimMb)
            NativeMethods.EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
    }

    private async Task HandleHotkeyAsync(string command)
    {
        Log($"Shortcut pressed: {CheatManager.Display(command)}.");
        try
        {
            if (!_session.IsAttached) await TryAttachAsync(manual: true);
            if (_session.IsAttached) Log(await Task.Run(() => _session.ToggleCheat(command, null)));
            _cheatsWindow?.Refresh();
        }
        catch (Exception ex) { LogError("WDL-SHORTCUT-001", "Shortcut failed", ex); }
    }

    private void CopyConsole()
    {
        if (string.IsNullOrEmpty(LogText.Text)) return;
        Clipboard.SetText(LogText.Text); Log("Console copied to the clipboard.");
    }

    private void ClearConsole() => LogText.Clear();

    private void SaveConsole()
    {
        var dialog = new SaveFileDialog { Title = "Export WDL event console", Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*", FileName = $"WDLConsole-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, LogText.Text); Log($"Console exported to {dialog.FileName}");
    }
    private void OpenCheatsWindow()
    {
        if (_cheatsWindow is null)
        {
            _cheatsWindow = new CheatsWindow(_session) { Owner = this };
            _cheatsWindow.Closed += (_, _) => _cheatsWindow = null;
        }
        _cheatsWindow.Refresh(); _cheatsWindow.Show(); _cheatsWindow.Activate();
        Log("Gameplay Cheats panel opened.");
    }

    private async Task OpenScannerAsync()
    {
        if (!_session.IsAttached) await TryAttachAsync(manual: true);
        if (!_session.IsAttached) return;
        if (_scannerWindow is null)
        {
            _scannerWindow = new MemoryScannerWindow(_session) { Owner = this };
            _scannerWindow.Closed += (_, _) => _scannerWindow = null;
        }
        _scannerWindow.Show();
        _scannerWindow.Activate();
        Log("Memory Scanner opened. Candidate writes are SUPER RISKY.");
    }

    private async Task DetectAndInjectAsync()
    {
        if (_attachBusy) return;
        ApplyRuntimeSettings();
        if (_session.IsAttached)
        {
            if (!_session.IsAttachedProcessAlive)
            {
                Log("Game process closed; releasing the local trainer session.");
                try { await Task.Run(_session.Detach); } catch (Exception ex) { LogError("WDL-CLEANUP-001", "Cleanup after game exit failed", ex); }
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
            int delay = ((App)Application.Current).Settings.AutoInjectDelaySeconds;
            _automaticInjectionReadyUtc = DateTime.UtcNow.AddSeconds(delay);
            _gameDetected = true;
            Log(((App)Application.Current).Settings.AutoInject ? $"Main game process and Dunia engine ready (PID {readyPid}); automatic injection in {delay} seconds…" : $"Main game process and Dunia engine ready (PID {readyPid}); auto-inject is disabled.");
            return;
        }
        if (!((App)Application.Current).Settings.AutoInject) return;
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
            if (manual) LogError("WDL-ATTACH-001", "Manual attachment failed", ex);
            else if (!string.Equals(_lastAutoError, ex.Message, StringComparison.Ordinal))
            {
                LogError("WDL-ATTACH-002", "Automatic attachment is waiting", ex);
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
        LogText.ScrollToEnd();
        try { _session.WriteResearchLog("Console", message); } catch { }
    }
    private void LogError(string code, string operation, Exception error)
    {
        Exception root = error.GetBaseException();
        Log($"ERROR [{code}] {operation}: {root.Message} | {root.GetType().Name} | HRESULT 0x{root.HResult:X8}");
    }
    private void UpdateStatus()
    {
        StatusText.Text = _session.IsAttached ? $"ATTACHED · PID {_session.ProcessId}" : "DETACHED";
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_session.IsAttached ? "#39D98A" : "#657083"));
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _detectorTimer.Stop();
        try
        {
            _hotkeys.Dispose(); _operatives?.Close(); _cheatsWindow?.Close(); _settingsWindow?.Close();
            if (((App)Application.Current).Settings.DisableCheatsOnExit) _session.Dispose();
            else _session.AbandonWithoutCleanup();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Cleanup warning", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
