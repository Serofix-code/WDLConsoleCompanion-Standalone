using System.Windows;
using System.Media;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using System.Windows.Input;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class FreecamWindow : Window
{
    private static readonly Dictionary<TrainerSession, WeakReference<FreecamWindow>> OpenWindows = [];
    private readonly TrainerSession _session;
    private readonly DispatcherTimer _candidateMonitor;
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
    private int _candidateMonitorSamples;
    private readonly DispatcherTimer _phaseTimer;
    private bool _phaseEnabled;
    private bool _phaseBusy;

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
        _candidateMonitor = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _candidateMonitor.Tick += CandidateMonitor_Tick;
        _phaseTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(140) };
        _phaseTimer.Tick += PhaseTimer_Tick;
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
        window.Closed += (_, _) => { window._phaseEnabled = false; window._phaseTimer.Stop(); window._armingCancellation?.Cancel(); window._calibrationCancellation?.Cancel(); window.StopCandidateMonitor(updateUi: false); window._scanner?.Close(); OpenWindows.Remove(session); };
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

    private void PhaseSafe_Click(object sender, RoutedEventArgs e)
    {
        try { _session.SaveTeleportPosition(); PhaseStatusText.Text = "Safe location saved. You can use Emergency return if movement goes wrong."; }
        catch (Exception ex) { PhaseStatusText.Text = _session.ReportError("WDL-PHASE-001", "Safe location could not be saved", ex); }
    }

    private void PhaseReturn_Click(object sender, RoutedEventArgs e)
    {
        try { _phaseEnabled = false; _phaseTimer.Stop(); _session.ReturnToSafeTeleportPosition(); PhaseStatusText.Text = "Returned to the last safe location."; }
        catch (Exception ex) { PhaseStatusText.Text = _session.ReportError("WDL-PHASE-002", "Emergency return failed", ex); }
    }

    private void PhaseFly_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.IsAttached) { PhaseStatusText.Text = "Attach to the game first."; return; }
        try
        {
            if (!_phaseEnabled)
            {
                _session.SaveTeleportPosition();
                _session.PrepareFreecamRuntime();
                _phaseEnabled = true;
                _phaseTimer.Start();
                PhaseFlyButton.Content = "Disable phase fly";
                PhaseStatusText.Text = "Phase fly enabled. W/A/S/D, Space/Ctrl; Shift uses the configured multiplier. Escape disables.";
                _session.ReportMemoryScan("VERY EXPERIMENTAL: phase-fly movement enabled; operative coordinates are being teleported, not a detached camera.");
            }
            else
            {
                _phaseEnabled = false; _phaseTimer.Stop(); PhaseFlyButton.Content = "Enable phase fly";
                PhaseStatusText.Text = "Phase fly disabled; normal gameplay camera restored.";
            }
        }
        catch (Exception ex) { PhaseStatusText.Text = _session.ReportError("WDL-PHASE-003", "Phase fly could not start", ex); }
    }

    private void PhaseProbe_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var rows = _session.ProbeFreecamApis();
            PhaseStatusText.Text = string.Join("  |  ", rows.Select(r => $"{r.Name}={r.LuaType}"));
        }
        catch (Exception ex) { PhaseStatusText.Text = _session.ReportError("WDL-PHASE-004", "Freecam API probe failed", ex); }
    }

    private async void PhaseTimer_Tick(object? sender, EventArgs e)
    {
        if (!_phaseEnabled || _phaseBusy || !_session.IsAttached) return;
        if (Keyboard.IsKeyDown(Key.Escape)) { _phaseEnabled = false; _phaseTimer.Stop(); PhaseFlyButton.Content = "Enable phase fly"; PhaseStatusText.Text = "Phase fly disabled by Escape."; return; }
        float step = ParsePhaseValue(PhaseStepBox.Text, 2f, 0.05f, 25f);
        float multiplier = ParsePhaseValue(PhaseShiftBox.Text, 2f, 1f, 10f);
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) step *= multiplier;
        try
        {
            _phaseBusy = true;
            string? result = null;
            if (Keyboard.IsKeyDown(Key.W)) result = await Task.Run(() => TeleportFeatureBridge.MoveForward(_session, step));
            else if (Keyboard.IsKeyDown(Key.S)) result = await Task.Run(() => TeleportFeatureBridge.MoveForward(_session, -step));
            else if (Keyboard.IsKeyDown(Key.A)) result = await Task.Run(() => TeleportFeatureBridge.MoveSideways(_session, -step));
            else if (Keyboard.IsKeyDown(Key.D)) result = await Task.Run(() => TeleportFeatureBridge.MoveSideways(_session, step));
            else if (Keyboard.IsKeyDown(Key.Space)) result = await Task.Run(() => TeleportFeatureBridge.MoveVertical(_session, step));
            else if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) result = await Task.Run(() => TeleportFeatureBridge.MoveVertical(_session, -step));
            if (result is not null) PhaseStatusText.Text = result;
        }
        catch (Exception ex) { PhaseStatusText.Text = _session.ReportError("WDL-PHASE-005", "Phase fly movement stopped", ex); }
        finally { _phaseBusy = false; }
    }

    private static float ParsePhaseValue(string text, float fallback, float min, float max)
        => float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value) && value >= min && value <= max ? value : fallback;

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
        StopCandidateMonitor();
        await RunAsync(async token =>
        {
            _exportSequence = 0;
            Dispatcher.Invoke(() => CandidateText.Text = "Ready. Run Horizontal discovery once; Vertical filter will unlock afterward.");
            CandidateInspectorText.Text = "No candidates inspected yet.";
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

    private void InspectCandidates_Click(object sender, RoutedEventArgs e)
    {
        if (_cameraScanner is null || _cameraScanner.Count == 0)
        {
            CandidateInspectorText.Text = "No surviving candidates. Run a gameplay-camera scan first.";
            return;
        }
        try
        {
            RenderCandidateInspection(liveSample: false);
            _session.WriteResearchLog("FreecamValidation", "Inspected nearby fields around surviving camera candidates; no memory was written.", "Inspect", candidates: _cameraScanner.Count);
            _session.ReportMemoryScan($"Read-only gameplay-camera candidate inspection completed for {_cameraScanner.Count:N0} surviving fields.");
        }
        catch (Exception ex) { CandidateInspectorText.Text = _session.ReportError("WDL-FREECAM-003", "Candidate inspection failed", ex); }
    }

    private void StartLiveMonitor_Click(object sender, RoutedEventArgs e)
    {
        CameraMotionScanner? cameraScanner = _cameraScanner;
        if (cameraScanner is null || cameraScanner.Count == 0)
        {
            CandidateInspectorText.Text = "No surviving candidates. Run a gameplay-camera scan first.";
            return;
        }

        _candidateMonitorSamples = 0;
        try
        {
            _candidateMonitorSamples = 1;
            RenderCandidateInspection(liveSample: true);
        }
        catch (Exception ex)
        {
            CandidateInspectorText.Text = _session.ReportError("WDL-FREECAM-004", "Live candidate monitor could not start", ex);
            return;
        }
        _candidateMonitor.Start();
        SetButtons(_cameraScanner is not null);
        StatusText.Text = "Live candidate monitor started (read-only, four samples per second).";
        _session.WriteResearchLog("FreecamValidation", "Started live read-only monitoring of surviving camera candidates.", "MonitorStart", candidates: cameraScanner.Count);
    }

    private void StopLiveMonitor_Click(object sender, RoutedEventArgs e) => StopCandidateMonitor(message: "Live candidate monitor stopped.");

    private void CandidateMonitor_Tick(object? sender, EventArgs e)
    {
        if (_busy || _cameraScanner is null || _cameraScanner.Count == 0 || !_session.IsAttached)
        {
            StopCandidateMonitor(message: "Live candidate monitor stopped because the scan or game connection is no longer available.");
            return;
        }

        try
        {
            _candidateMonitorSamples++;
            RenderCandidateInspection(liveSample: true);
        }
        catch (Exception ex)
        {
            _candidateMonitor.Stop();
            CandidateInspectorText.Text = _session.ReportError("WDL-FREECAM-004", "Live candidate monitor stopped", ex);
            SetButtons(_cameraScanner is not null);
        }
    }

    private void RenderCandidateInspection(bool liveSample)
    {
        if (_cameraScanner is null) return;
        string prefix = liveSample
            ? $"Live read-only sample {_candidateMonitorSamples:N0} — no game memory is written.\n\n"
            : "Read-only snapshot — no game memory is written.\n\n";
        CandidateInspectorText.Text = prefix + _cameraScanner.DescribeCandidateClusters();
    }

    private void StopCandidateMonitor(bool updateUi = true, string? message = null)
    {
        bool wasRunning = _candidateMonitor.IsEnabled;
        _candidateMonitor.Stop();
        if (message is not null) StatusText.Text = message;
        if (wasRunning) _session.WriteResearchLog("FreecamValidation", "Stopped live read-only monitoring of camera candidates.", "MonitorStop", candidates: _cameraScanner?.Count);
        if (updateUi && IsLoaded) SetButtons(_cameraScanner is not null);
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
            InspectCandidatesButton.IsEnabled = !_busy && hasCalibration && _cameraScanner?.Count > 0;
            StartLiveMonitorButton.IsEnabled = !_busy && hasCalibration && _cameraScanner?.Count > 0 && !_candidateMonitor.IsEnabled;
            StopLiveMonitorButton.IsEnabled = _candidateMonitor.IsEnabled;
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
