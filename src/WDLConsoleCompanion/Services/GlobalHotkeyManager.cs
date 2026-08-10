using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace WDLConsoleCompanion.Services;

internal sealed class GlobalHotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private readonly Window _window;
    private readonly Action<string> _activated;
    private readonly Action<string> _log;
    private readonly Dictionary<int, string> _commands = [];
    private HwndSource? _source;
    private nint _handle;

    internal GlobalHotkeyManager(Window window, Action<string> activated, Action<string> log)
    {
        _window = window; _activated = activated; _log = log;
        if (PresentationSource.FromVisual(window) is HwndSource) Attach(); else window.SourceInitialized += (_, _) => Attach();
    }

    internal void Register(HotkeySettings settings)
    {
        UnregisterAll();
        if (_handle == 0) return;
        int id = 0x5100;
        foreach (HotkeyBinding binding in settings.Bindings.Where(binding => !binding.Key.Equals("None", StringComparison.OrdinalIgnoreCase)))
        {
            if (!Enum.TryParse(binding.Key, true, out Key key)) { _log($"Shortcut ignored: {binding.Key} is invalid."); continue; }
            uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            if (RegisterHotKey(_handle, id, ModNoRepeat, virtualKey)) { _commands[id] = binding.Command; _log($"Shortcut registered: {binding.Key} toggles {CheatManager.Display(binding.Command)}."); id++; }
            else _log($"Shortcut unavailable: Windows or another app already owns {binding.Key}.");
        }
    }

    private void Attach()
    {
        _handle = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey && _commands.TryGetValue(wParam.ToInt32(), out string? command)) { handled = true; _activated(command); }
        return 0;
    }

    private void UnregisterAll()
    {
        if (_handle != 0) foreach (int id in _commands.Keys.ToArray()) UnregisterHotKey(_handle, id);
        _commands.Clear();
    }

    public void Dispose() { UnregisterAll(); _source?.RemoveHook(WndProc); _source = null; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint hWnd, int id);
}
