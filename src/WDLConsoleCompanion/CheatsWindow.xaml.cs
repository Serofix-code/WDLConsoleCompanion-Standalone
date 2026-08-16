using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WDLConsoleCompanion.Services;

namespace WDLConsoleCompanion;

public partial class CheatsWindow : Window
{
    private readonly TrainerSession _session;
    private readonly ObservableCollection<CheatRow> _rows =
    [
        new("godmode", "God Mode", "Infinite player health"), new("notrace", "No Trace", "No wanted level and stealth"),
        new("infammo", "Infinite Ammo", "Keeps ammunition at 999"), new("noreload", "No Reload", "Skips the reload requirement"),
        new("norecoil", "No Recoil", "Suppresses weapon recoil"), new("fastsearch", "Fast Search", "Ends pursuit searches faster"),
        new("hackcooldown", "Instant Hacker Cooldowns", "Zeros the identified hacker-skill cooldown field", "SUPER RISKY"),
        new("freezehack", "Freeze Hack Timer", "Stops the active hack countdown at the verified instruction", "SUPER RISKY"),
        new("dronerange", "Maximum Drone Range", "Sets the identified drone range to 300 metres", "SUPER RISKY"),
        new("dronehealth", "Infinite Drone Health", "Keeps the currently controlled drone at full health", "SUPER RISKY"),
        new("onehitkill", "One Hit Kill", "Forces non-player damage targets to zero health", "SUPER RISKY"),
        new("immortal", "Immortal Mode", "Uses the game death-immunity function", "SUPER RISKY"),
        new("disablefelony", "Disable Felony System", "Disables the felony system independently", "SUPER RISKY"),
        new("disabledetection", "Disable Detection", "Makes the local player undetectable independently", "SUPER RISKY"),
        new("endchase", "End Felony Chase", "Ends the current chase once", "SUPER RISKY", true, true),
        new("eto", "Add 1000 ETO", "Queues the confirmed RuleSmith currency reward", "SUPER RISKY", true, true),
        new("techpoints", "Add 10 Tech Points", "Queues the confirmed RuleSmith tech reward", "SUPER RISKY", true, true),
        new("spawnracecar", "Spawn Racecar", "Spawns a racecar at the aimed location", "SUPER RISKY", true, true),
        new("spawnshop", "Spawn DedSec Shop", "Spawns a temporary shop at the aimed location", "SUPER RISKY", true, true),
        new("distractall", "Distract Everyone", "Triggers Distract on nearby human agents", "SUPER RISKY", true, true),
        new("disruptall", "Disrupt Everyone", "Triggers communications disruption on nearby human agents", "SUPER RISKY", true, true),
        new("shockall", "Shock Everyone", "Game action found, but its exact hostility filter is not yet validated", "SUPER RISKY", false),
        new("influence", "Add Influence", "Online-only account currency; unavailable in single-player/offline mode", "ONLINE ONLY", false),
        new("xp", "Add Online XP", "Online seasonal account progression; unavailable in single-player/offline mode", "ONLINE ONLY", false),
        new("freecam", "Freecam Lab", "Guided camera-memory calibration; movement remains locked until a transform is validated", "VERY EXPERIMENTAL"),
        new("teleport", "Teleport / Waypoint / Forward", "Player, coordinate, waypoint, and movement-forward tools", "SUPER RISKY"),
        new("noclip", "Noclip / Fly", "No verified collision/physics signature outside Legion ScriptHook", "SUPER RISKY", false),
        new("recruit", "Recruit Any NPC", "Requires verified game-thread recruitment and ownership calls", "SUPER RISKY", false),
        new("clothes", "Clothing Unlock + Shop Access", "Very experimental bulk reward pass, plus 34 working temporary shop spawns", "VERY EXPERIMENTAL"),
        new("range", "Infinite Mind-Control Range", "No separate verified mind-control range signature in the local build", "SUPER RISKY", false),
        new("trapcooldown", "Trap / Missile Drone Cooldown", "No verified DX11 patch contract", "SUPER RISKY", false),
        new("fistcooldown", "Electro Fist Cooldown", "No verified DX11 patch contract", "SUPER RISKY", false),
        new("watchcooldown", "Pocketwatch Cooldown", "No verified DX11 patch contract", "SUPER RISKY", false),
        new("cloakcooldown", "AR Cloak Cooldown", "No verified DX11 patch contract", "SUPER RISKY", false),
        new("cloakduration", "Infinite AR Cloak Duration", "No verified DX11 patch contract", "SUPER RISKY", false),
        new("turretheat", "Turret No Overheat", "No verified DX11 patch contract", "SUPER RISKY", false),
        new("ammopouch", "Infinite Ammo Pouch", "No verified DX11 patch contract", "SUPER RISKY", false),
        new("gamespeed", "Game Speed", "No verified timing contract", "SUPER RISKY", false),
        new("vehicleteleport", "Teleport With Vehicle", "No verified vehicle transform ownership contract", "SUPER RISKY", false),
        new("inventory", "Inventory / Item Editor", "Requires validated inventory ownership calls", "SUPER RISKY", false),
        new("vehicle", "Personal Vehicle Editor", "Requires validated vehicle ownership calls", "SUPER RISKY", false),
        new("permadeath", "Permadeath Controls", "Requires validated campaign-state calls", "SUPER RISKY", false)
    ];
    internal CheatsWindow(TrainerSession session) { InitializeComponent(); _session = session; CheatItems.ItemsSource = _rows; Refresh(); }
    internal void Refresh() { foreach (CheatRow row in _rows) { if (row.Name == "noreload") { row.IsAvailable = _session.IsCheatAvailable(row.Name); row.Description = row.IsAvailable ? "Skips the reload requirement" : "Unavailable: the known patch targets DX12-plus, but the current engine is a different build"; } row.IsOn = _session.IsCheatActive(row.Name); } Footer.Text = _session.IsAttached ? $"Attached to PID {_session.ProcessId}" : "Not attached — working toggles require attachment; experimental Details remain readable"; CheatItems.IsEnabled = true; }
    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        CheatRow? row = _rows.FirstOrDefault(candidate => candidate.Name == name);
        if (name == "teleport") { TeleportWindow.OpenFor(_session, this); return; }
        if (name == "clothes") { ClothingWindow.OpenFor(_session, this); return; }
        if (name == "freecam") { FreecamWindow.OpenFor(_session, this); return; }
        if (row is { IsAction: true })
        {
            CheatItems.IsEnabled = false;
            try { Footer.Text = await Task.Run(() => _session.RunGameAction(name)); }
            catch (Exception ex) { Footer.Text = _session.ReportError("WDL-REWARD-001", $"{row.DisplayName} failed", ex); MessageBox.Show(Footer.Text, "Reward not added", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { CheatItems.IsEnabled = true; }
            return;
        }
        if (row is { IsAvailable: false })
        {
            Footer.Text = $"{row.DisplayName}: {row.Description}";
            MessageBox.Show($"{row.DisplayName} is UNDER DEVELOPMENT and is not executable yet.\n\n{row.Description}\n\nI'm working on making this feature functional. It will remain unavailable until its game functions and object layout can be validated safely.", "Feature under development", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        CheatItems.IsEnabled = false;
        try { Footer.Text = await Task.Run(() => _session.ToggleCheat(name, null)); }
        catch (Exception ex) { Footer.Text = _session.ReportError("WDL-OPTION-001", $"{row?.DisplayName ?? name} was not changed", ex); MessageBox.Show(Footer.Text, "Option not changed", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { Refresh(); }
    }
    internal sealed class CheatRow(string name, string displayName, string description, string risk = "STANDARD", bool isAvailable = true, bool isAction = false) : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isOn; private bool _isAvailable = isAvailable; private string _description = description;
        public string Name { get; } = name; public string DisplayName { get; } = displayName; public string Description { get => _description; set { if (_description == value) return; _description = value; PropertyChanged?.Invoke(this, new(nameof(Description))); } } public string Risk { get; } = risk; public bool IsAction { get; } = isAction; public bool IsAvailable { get => _isAvailable; set { if (_isAvailable == value) return; _isAvailable = value; PropertyChanged?.Invoke(this, new(nameof(IsAvailable))); } }
        public bool IsOn { get => _isOn; set { if (_isOn == value) return; _isOn = value; PropertyChanged?.Invoke(this, new(nameof(IsOn))); } }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
