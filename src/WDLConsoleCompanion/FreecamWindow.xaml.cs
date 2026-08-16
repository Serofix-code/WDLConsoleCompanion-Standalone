using System.Windows;
using System.Media;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class FreecamWindow : Window
{
    private static readonly Dictionary<TrainerSession, WeakReference<FreecamWindow>> OpenWindows = [];
    private readonly TrainerSession _session;
    private MemoryScannerWindow? _scanner;
    private CameraMotionScanner? _cameraScanner;
    private CancellationTokenSource? _calibrationCancellation;
    private CancellationTokenSource? _armingCancellation;
    private bool _armed;
    private bool _busy;
    private bool _lastOperationSucceeded;
    private string? _exportPath;
    private int _exportSequence;
    private bool _hasHorizontalDiscovery;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

    private const byte VkF2 = 0x71;
    private const uint KeyUpFlag = 0x0002;

    private FreecamWindow(TrainerSession session)
    {
        InitializeComponent();
        _session = session;
        StatusText.Text = session.IsAttached
            ? $"Attached to PID {session.ProcessId}. Research CSV: {session.ResearchLogPath}"
            : $"Attach to the running game before calibration. Research CSV: {session.ResearchLogPath}";
    }

    private async void OpenPhotoMode_Click(object sender, RoutedEventArgs e)
    {
        if (_session.ProcessId is not int processId)
        {
            StatusText.Text = "Attach to the running game first.";
            return;
        }

        try
        {
            using Process game = Process.GetProcessById(processId);
            nint window = game.MainWindowHandle;
            if (window == 0 || !SetForegroundWindow(window))
                throw new InvalidOperationException("The game window could not be focused. Switch to the game and press F2 manually.");

            await Task.Delay(180);
            keybd_event(VkF2, 0, 0, 0);
            keybd_event(VkF2, 0, KeyUpFlag, 0);
            StatusText.Text = "Sent F2 to Legion. Press F2 in the game manually if Photo Mode did not open.";
            _session.ReportMemoryScan("Opened Legion's built-in Photo Mode with F2; no memory scan or write was used.");
        }
        catch (Exception ex)
        {
            StatusText.Text = _session.ReportError("WDL-CAMERA-001", "Photo Mode could not be opened automatically", ex);
        }
    }

    internal static void OpenFor(TrainerSession session, Window owner)
    {
        if (OpenWindows.TryGetValue(session, out WeakReference<FreecamWindow>? reference) && reference.TryGetTarget(out FreecamWindow? existing))
        { existing.Show(); existing.Activate(); return; }
        var window = new FreecamWindow(session) { Owner = owner };
        OpenWindows[session] = new(window);
        window.Closed += (_, _) => { window._armingCancellation?.Cancel(); window._calibrationCancellation?.Cancel(); window._scanner?.Close(); OpenWindows.Remove(session); };
        window.Show();
    }

    private void OpenScanner_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.IsAttached) { StatusText.Text = "Attach to the running game first."; return; }
        if (_scanner is null)
        {
            _scanner = new MemoryScannerWindow(_session) { Owner = this, Title = "Camera Calibration Scanner — Very Experimental" };
            _scanner.Closed += (_, _) => _scanner = null;
        }
        _scanner.Show(); _scanner.Activate();
        StatusText.Text = "Scanner opened. Use Float + WritableMemory and do not write to candidates during calibration.";
        _session.ReportMemoryScan("VERY EXPERIMENTAL: Freecam camera-calibration scanner opened; movement controls remain locked.");
    }

    private void CopyChecklist_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText("Freecam calibration: Float / WritableMemory -> Unknown first scan -> rotate camera only -> Changed -> stop -> Unchanged -> repeat horizontal and vertical movement. Do not write until an adjacent camera transform is validated.");
        StatusText.Text = "Calibration checklist copied.";
    }

    private async void NewCalibration_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.IsAttached) { StatusText.Text = "Attach to the running game first."; return; }
        if (_busy) return;
        _cameraScanner = _session.CreateCameraMotionScanner();
        _hasHorizontalDiscovery = false;
        await RunAsync(async token =>
        {
            _exportSequence = 0;
            Dispatcher.Invoke(() => CandidateText.Text = "Ready. Run Horizontal discovery once; Vertical filter will unlock afterward.");
            SetStatus("Calibration reset. Run Full-memory horizontal discovery next.", 100);
            await Task.CompletedTask;
        });
    }

    private async void HorizontalPass_Click(object sender, RoutedEventArgs e)
    {
        await RunMotionPassAsync("horizontal", "rotate left or right continuously");
        if (_cameraScanner?.Count > 0) _hasHorizontalDiscovery = true;
        SetButtons(_cameraScanner is not null);
    }

    private async void FullAutomatic_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.IsAttached) { StatusText.Text = "Attach to the running game first."; return; }
        if (_busy) return;
        if (_armed) { _armingCancellation?.Cancel(); return; }

        _armed = true;
        _armingCancellation = new();
        SetButtons(_cameraScanner is not null);
        FullAutomaticButton.Content = "ARMED — press F8 in active gameplay";
        CandidateText.Text = "Calibration armed. Return to Legion, unpause and get ready. Press F8 to start; Escape disarms. Input is not blocked yet.";
        _session.ReportMemoryScan("VERY EXPERIMENTAL: automatic freecam calibration armed. Press F8 in active gameplay; Escape disarms.");
        _session.WriteResearchLog("FreecamCalibration", "Calibration armed; input remains available until F8.", "Armed");
        try
        {
            CancellationToken token = _armingCancellation.Token;
            while (!CameraCalibrationInput.CalibrationHotkeyPressed)
            {
                token.ThrowIfCancellationRequested();
                if (CameraCalibrationInput.EscapePressed) throw new OperationCanceledException(token);
                await Task.Delay(40, token);
            }
            while (CameraCalibrationInput.CalibrationHotkeyPressed) await Task.Delay(20, token);
            await Task.Delay(150, token);
            _session.WriteResearchLog("FreecamCalibration", "F8 trigger received and released.", "Triggered");
        }
        catch (OperationCanceledException)
        {
            CandidateText.Text = "Calibration disarmed. No input was blocked.";
            _session.WriteResearchLog("FreecamCalibration", "Calibration disarmed before scanning.", "Disarmed");
            return;
        }
        finally
        {
            _armed = false;
            _armingCancellation.Dispose();
            _armingCancellation = null;
            FullAutomaticButton.Content = "Arm automatic calibration — start with F8";
            SetButtons(_cameraScanner is not null);
        }

        _cameraScanner = _session.CreateCameraMotionScanner();
        _hasHorizontalDiscovery = false;
        _exportSequence = 0;
        CandidateText.Text = "F8 received. Continuous automatic calibration is starting; Escape cancels.";
        try
        {
            using var fullCancellation = new CancellationTokenSource();
            using var fullProtection = new CameraCalibrationInput(() =>
            {
                fullCancellation.Cancel();
                _calibrationCancellation?.Cancel();
            });
            FocusGameWindow();
            int pass = 0;
            while (!fullCancellation.IsCancellationRequested && _cameraScanner.Count != 1)
            {
                string axis = pass % 2 == 0 ? "horizontal" : "vertical";
                CandidateText.Text = $"Automatic pass {pass + 1}: {axis}. {_cameraScanner.Count:N0} candidates before this pass.";
                await RunMotionPassAsync(axis, $"{axis} camera movement", countdown: false);
                if (!_lastOperationSucceeded) break;
                if (_cameraScanner.Count == 0)
                {
                    SetStatus("Automatic calibration reached zero candidates. Arm a fresh run from a neutral camera position.", 0);
                    _session.WriteResearchLog("FreecamCalibration", "Automatic calibration reached zero candidates.", "Stopped", axis: axis, candidates: 0);
                    break;
                }
                pass++;
            }
            if (_cameraScanner.Count == 1)
            {
                CandidateText.Text = $"Calibration complete: exactly one float remains. Export: {_exportPath}";
                SetStatus("AUTOMATIC CALIBRATION COMPLETE — one candidate remains.", 100);
                _session.ReportMemoryScan($"VERY EXPERIMENTAL: continuous automatic freecam calibration completed after {pass + 1:N0} passes with one candidate: {_exportPath}.");
                _session.WriteResearchLog("FreecamCalibration", "Exactly one candidate remains.", "Complete", candidates: 1, exportPath: _exportPath ?? "");
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Continuous automatic calibration cancelled. Keyboard and mouse control restored.", 0);
            _session.WriteResearchLog("FreecamCalibration", "Continuous calibration cancelled; input restored.", "Cancelled", candidates: _cameraScanner?.Count);
        }
        catch (Exception ex)
        {
            SetStatus(_session.ReportError("WDL-FREECAM-002", "Continuous automatic calibration failed", ex), 0);
            _session.WriteResearchLog("FreecamCalibration", ex.GetBaseException().Message, "Error", candidates: _cameraScanner?.Count);
        }
        finally { SetButtons(_cameraScanner is not null); }
    }

    private async void VerticalPass_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasHorizontalDiscovery) { StatusText.Text = "Run Horizontal discovery first."; return; }
        await RunMotionPassAsync("vertical", "tilt upward or downward continuously");
    }

    private async Task RunMotionPassAsync(string label, string instruction, bool countdown = true)
    {
        if (_cameraScanner is null || _busy) return;
        await RunAsync(async token =>
        {
            using var inputProtection = new CameraCalibrationInput(() => _calibrationCancellation?.Cancel());
            for (int remaining = countdown ? 5 : 0; remaining > 0; remaining--)
            {
                SetStatus($"Return to the game. {label} pass begins in {remaining}…", (5 - remaining) * 5);
                for (int tick = 0; tick < 10; tick++)
                {
                    if (CameraCalibrationInput.EscapePressed) throw new OperationCanceledException(token);
                    await Task.Delay(100, token);
                }
            }
            FocusGameWindow();
            SystemSounds.Exclamation.Play();
            inputProtection.StartMovement(label, +1, token);
            SetStatus($"{label.ToUpperInvariant()} DIRECTION 1 — Changed scan. Keep hands off the mouse; Escape cancels.", 25);
            CameraMotionScanSummary summary;
            if (_cameraScanner.Count == 0)
            {
                SetStatus($"{label} direction 1 — full-memory Changed discovery is running.", 30);
                summary = await Task.Run(() => _cameraScanner.DiscoverMotion(token, value => SetStatus(value, 45)), token);
                _session.WriteResearchLog("FreecamCalibration", "Direction 1 full-memory Changed discovery completed.", "Movement", label, +1, "ChangedDiscovery", summary.Results, summary.BytesScanned, summary.Elapsed.TotalMilliseconds);
            }
            else
            {
                await Task.Delay(1200, token);
                summary = await Task.Run(() => _cameraScanner.Filter(changed: true, token), token);
                _session.WriteResearchLog("FreecamCalibration", "Direction 1 Changed filter completed.", "Movement", label, +1, "Changed", summary.Results, summary.BytesScanned, summary.Elapsed.TotalMilliseconds);
                SetStatus($"{label} direction 1 Changed scan: {summary.Results:N0} candidates remain.", 40);
            }
            inputProtection.StopMovement();
            SystemSounds.Hand.Play(); await Task.Delay(180, token); SystemSounds.Hand.Play();
            SetStatus($"{label} STOP 1 — Unchanged scan. The camera is stationary.", 50);
            await Task.Delay(1800, token);
            if (summary.Results > 0)
            {
                summary = await Task.Run(() => _cameraScanner.Filter(changed: false, token), token);
                _session.WriteResearchLog("FreecamCalibration", "Stop 1 Unchanged filter completed.", "Stationary", label, 0, "Unchanged", summary.Results, summary.BytesScanned, summary.Elapsed.TotalMilliseconds);
            }

            if (summary.Results > 0)
            {
                SystemSounds.Exclamation.Play();
                inputProtection.StartMovement(label, -1, token);
                SetStatus($"{label.ToUpperInvariant()} DIRECTION 2 — opposite-direction Changed scan.", 65);
                await Task.Delay(1400, token);
                summary = await Task.Run(() => _cameraScanner.Filter(changed: true, token), token);
                _session.WriteResearchLog("FreecamCalibration", "Direction 2 Changed filter completed.", "Movement", label, -1, "Changed", summary.Results, summary.BytesScanned, summary.Elapsed.TotalMilliseconds);
                inputProtection.StopMovement();
                SystemSounds.Hand.Play(); await Task.Delay(180, token); SystemSounds.Hand.Play();
                SetStatus($"{label} STOP 2 — final Unchanged scan. The camera is stationary.", 82);
                await Task.Delay(1800, token);
                if (summary.Results > 0)
                {
                    summary = await Task.Run(() => _cameraScanner.Filter(changed: false, token), token);
                    _session.WriteResearchLog("FreecamCalibration", "Stop 2 Unchanged filter completed.", "Stationary", label, 0, "Unchanged", summary.Results, summary.BytesScanned, summary.Elapsed.TotalMilliseconds);
                }
            }
            ExportCandidates(label);
            _session.WriteResearchLog("FreecamCalibration", "Candidate stage exported.", "Export", label, candidates: summary.Results, exportPath: _exportPath ?? "");
            Dispatcher.Invoke(() => CandidateText.Text = $"{summary.Results:N0} candidates remain. Export: {_exportPath}");
            SetStatus($"{label} two-direction Changed/Unchanged sequence complete.", 100);
            _session.ReportMemoryScan($"VERY EXPERIMENTAL: automatic freecam {label} direction-1/stop/direction-2/stop sequence left {summary.Results:N0} candidates; exported to {_exportPath}.");
        });
    }

    private void FocusGameWindow()
    {
        if (_session.ProcessId is not int processId) throw new InvalidOperationException("The game is no longer attached.");
        using Process game = Process.GetProcessById(processId);
        if (game.MainWindowHandle == 0 || !SetForegroundWindow(game.MainWindowHandle))
            throw new InvalidOperationException("The game window could not be focused for automatic calibration.");
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        _busy = true;
        _lastOperationSucceeded = false;
        _calibrationCancellation = new();
        SetButtons(false);
        try { await operation(_calibrationCancellation.Token); _lastOperationSucceeded = true; }
        catch (OperationCanceledException) { SetStatus("Automated calibration cancelled. The previous candidate set remains available.", 0); }
        catch (Exception ex) { SetStatus(_session.ReportError("WDL-FREECAM-001", "Camera calibration failed", ex), 0); }
        finally
        {
            _calibrationCancellation.Dispose(); _calibrationCancellation = null; _busy = false;
            SetButtons(_cameraScanner is not null);
        }
    }

    private void SetButtons(bool hasCalibration)
    {
        Dispatcher.Invoke(() =>
        {
            NewCalibrationButton.IsEnabled = !_busy && !_armed;
            FullAutomaticButton.IsEnabled = !_busy;
            HorizontalButton.IsEnabled = !_busy && !_armed && hasCalibration;
            VerticalButton.IsEnabled = !_busy && !_armed && hasCalibration && _hasHorizontalDiscovery;
            CancelCalibrationButton.IsEnabled = _busy;
        });
    }

    private void SetStatus(string message, double progress) => Dispatcher.Invoke(() => { StatusText.Text = message; CalibrationProgress.Value = progress; });
    private void CancelCalibration_Click(object sender, RoutedEventArgs e) => _calibrationCancellation?.Cancel();

    private void ExportCandidates(string stage)
    {
        if (_cameraScanner is null) return;
        int sequence = ++_exportSequence;
        _exportPath = Path.Combine(Path.GetTempPath(), $"WDL-freecam-{_session.ProcessId}-{sequence:D2}-{stage}.csv");
        IEnumerable<string> lines = new[] { "Address,CurrentValue,PreviousValue" }.Concat(_cameraScanner.Preview(int.MaxValue)
            .Select(item => $"{item.AddressText},{item.CurrentValue},{item.PreviousValue}"));
        File.WriteAllLines(_exportPath, lines);
    }
}
