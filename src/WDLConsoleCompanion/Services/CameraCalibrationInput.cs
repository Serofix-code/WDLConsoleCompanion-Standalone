using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WDLConsoleCompanion.Services;

/// <summary>Rejects physical mouse messages while permitting this process's injected camera motion.</summary>
internal sealed class CameraCalibrationInput : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WhKeyboardLl = 13;
    private const uint LlMhfInjected = 0x00000001;
    private const uint MouseEventMove = 0x0001;
    private const int VkEscape = 0x1B;
    private const int VkF8 = 0x77;
    private readonly HookProc _callback;
    private readonly HookProc _keyboardCallback;
    private readonly Action _cancel;
    private CancellationTokenSource? _movementCancellation;
    private Task? _movement;
    private nint _hook;
    private nint _keyboardHook;

    internal CameraCalibrationInput(Action cancel)
    {
        _cancel = cancel;
        _callback = MouseHook;
        _keyboardCallback = KeyboardHook;
        _hook = SetWindowsHookEx(WhMouseLl, _callback, GetModuleHandle(null), 0);
        if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Physical-mouse protection could not be enabled.");
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardCallback, GetModuleHandle(null), 0);
        if (_keyboardHook == 0)
        {
            int error = Marshal.GetLastWin32Error();
            UnhookWindowsHookEx(_hook); _hook = 0;
            throw new Win32Exception(error, "Keyboard protection could not be enabled.");
        }
    }

    internal static bool EscapePressed => (GetAsyncKeyState(VkEscape) & 0x8000) != 0;
    internal static bool CalibrationHotkeyPressed => (GetAsyncKeyState(VkF8) & 0x8000) != 0;

    internal void StartMovement(string axis, int direction, CancellationToken token)
    {
        StopMovement();
        _movementCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _movement = MoveCameraAsync(axis, Math.Sign(direction) == 0 ? 1 : Math.Sign(direction), _movementCancellation.Token);
    }

    internal void StopMovement()
    {
        CancellationTokenSource? cancellation = _movementCancellation;
        Task? movement = _movement;
        cancellation?.Cancel();
        try { movement?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        cancellation?.Dispose();
        _movementCancellation = null;
        _movement = null;
    }

    private async Task MoveCameraAsync(string axis, int direction, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (EscapePressed) { _cancel(); return; }
            mouse_event(MouseEventMove, axis == "horizontal" ? 7 * direction : 0, axis == "vertical" ? 7 * direction : 0, 0, 0);
            await Task.Delay(18, token).ConfigureAwait(false);
        }
    }

    private nint MouseHook(int code, nuint message, nint data)
    {
        if (code >= 0)
        {
            MouseHookData details = Marshal.PtrToStructure<MouseHookData>(data);
            if ((details.Flags & LlMhfInjected) == 0) return 1;
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    private nint KeyboardHook(int code, nuint message, nint data)
    {
        if (code >= 0)
        {
            KeyboardHookData details = Marshal.PtrToStructure<KeyboardHookData>(data);
            if (details.VirtualKey != VkEscape) return 1;
            if (EscapePressed) _cancel();
        }
        return CallNextHookEx(_keyboardHook, code, message, data);
    }

    public void Dispose()
    {
        StopMovement();
        if (_keyboardHook != 0) { UnhookWindowsHookEx(_keyboardHook); _keyboardHook = 0; }
        if (_hook == 0) return;
        UnhookWindowsHookEx(_hook);
        _hook = 0;
        GC.SuppressFinalize(this);
    }

    private delegate nint HookProc(int code, nuint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X; internal int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseHookData { internal Point Point; internal uint MouseData; internal uint Flags; internal uint Time; internal nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardHookData { internal uint VirtualKey; internal uint ScanCode; internal uint Flags; internal uint Time; internal nuint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int hookId, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, nuint extraInfo);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
}
