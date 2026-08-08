using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using WDLConsoleCompanion.Models;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SaveLocator _locator;
    private readonly SaveValidator _validator;
    private readonly BackupService _backups;
    private readonly DialogService _dialogs;
    private readonly GameReadinessService _readiness;
    private string? _savePath;
    private string _commandText = "";
    private string _saveStatus = "Game not detected - NOT INJECTED";
    private string _editorMessage = "NOT INJECTED: live roster reading and writing are unavailable in this standalone build.";
    private bool _isOperativeVisible;
    private Operative? _selectedOperative;

    public MainViewModel(SaveLocator locator, SaveValidator validator, BackupService backups, DialogService dialogs, GameReadinessService readiness)
    {
        _locator = locator; _validator = validator; _backups = backups; _dialogs = dialogs; _readiness = readiness;
        RemoveOperativeCommand = new RelayCommand(_ => AddLine("NOT INJECTED: live retirement is unavailable."), _ => false);
        SaveOperativesCommand = new RelayCommand(_ => AddLine("NOT INJECTED: live writes are unavailable."), _ => false);
        AddLine("WDL Console Companion initialized - attachment status: NOT INJECTED.");
        AddLine("Type 'help' for commands. 'op' is an alias for 'operative'.");
    }

    public ObservableCollection<string> TerminalLines { get; } = [];
    public ObservableCollection<Operative> Operatives { get; } = [];
    public IReadOnlyList<NameOption> NameOptions { get; } = [];
    public IReadOnlyList<NameOption> SurnameOptions { get; } = [];
    public RelayCommand RemoveOperativeCommand { get; }
    public RelayCommand SaveOperativesCommand { get; }
    public string CommandText { get => _commandText; set => SetProperty(ref _commandText, value); }
    public string SaveStatus { get => _saveStatus; set => SetProperty(ref _saveStatus, value); }
    public string EditorMessage { get => _editorMessage; set => SetProperty(ref _editorMessage, value); }
    public Operative? SelectedOperative { get => _selectedOperative; set => SetProperty(ref _selectedOperative, value); }
    public bool IsOperativeVisible { get => _isOperativeVisible; set { SetProperty(ref _isOperativeVisible, value); OnPropertyChanged(nameof(IsWelcomeVisible)); } }
    public bool IsWelcomeVisible => !IsOperativeVisible;
    public bool CanWriteOperatives => false;

    public async Task InitializeAsync()
    {
        _savePath = await _locator.FindMostRecentAsync();
        if (_savePath is not null) AddLine($"Save detected for backup/restore: {_savePath}");
        _ = MonitorGameAsync();
    }

    public async Task ExecuteCommandAsync()
    {
        var command = CommandText.Trim().ToLowerInvariant(); CommandText = "";
        if (command.Length == 0) return;
        AddLine($"> {command}");
        try
        {
            switch (command)
            {
                case "help":
                    AddLine("op / operative  Open the operative panel");
                    AddLine("status          Show game detection and injection status");
                    AddLine("backup          Create a checksummed save backup");
                    AddLine("restore         Restore a verified backup (game closed)");
                    AddLine("exit            Close the companion");
                    break;
                case "op": case "operative": IsOperativeVisible = true; break;
                case "status": ReportStatus(); break;
                case "backup": await BackupAsync(); break;
                case "restore": await RestoreAsync(); break;
                case "exit": Application.Current.Shutdown(); break;
                default: AddLine($"Unknown command: {command}. Type 'help'."); break;
            }
        }
        catch (Exception ex) { AddLine($"ERROR: {ex.Message}"); }
    }

    private void ReportStatus()
    {
        var readiness = _readiness.Detect();
        AddLine($"Game process: {(readiness.ProcessDetected ? "detected" : "not detected")}");
        AddLine($"Engine ready: {(readiness.EngineReady ? "yes" : "no")}");
        AddLine(readiness.Message);
        AddLine("Injection status: NOT INJECTED");
        AddLine("The companion and game are independent processes. Closing one does not close the other.");
    }

    private async Task MonitorGameAsync()
    {
        while (true)
        {
            SaveStatus = _readiness.Detect().Message;
            await Task.Delay(1500);
        }
    }

    private async Task BackupAsync()
    {
        if (_savePath is null) { AddLine("No save was detected."); return; }
        var folder = _dialogs.ChooseFolder("Choose backup destination");
        if (folder is null) { AddLine("Backup cancelled."); return; }
        AddLine($"Backup created: {await _backups.CreateBackupAsync(_savePath, folder)}");
    }

    private async Task RestoreAsync()
    {
        if (_savePath is null) { AddLine("No target save was detected."); return; }
        if (_validator.IsGameRunning()) { AddLine("Restore blocked: close Watch Dogs: Legion first."); return; }
        var path = _dialogs.ChooseBackup();
        if (path is null) { AddLine("Restore cancelled."); return; }
        var safetyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WDL Console Companion", "Automatic Backups");
        await _backups.CreateBackupAsync(_savePath, safetyFolder);
        await _backups.RestoreAsync(path, _savePath);
        AddLine("Backup restored. The previous save was backed up automatically.");
    }

    private void AddLine(string line) => TerminalLines.Add(line);
}
