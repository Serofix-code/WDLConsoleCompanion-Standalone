using Microsoft.Win32;

namespace WDLConsoleCompanion.Services;

public sealed class DialogService
{
    public string? ChooseFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? ChooseBackup()
    {
        var dialog = new OpenFileDialog { Title = "Choose a WDL backup", Filter = "WDL backups (*.wdlbackup)|*.wdlbackup" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
