using System.Diagnostics;
using System.Runtime.InteropServices;
using static FlowScribe.NativeMethods;

namespace FlowScribe;

/// <summary>
/// Watches the Right Alt key system wide with a low-level keyboard hook.
///
/// Windows removes a low-level hook whose callback is slow, so the callback
/// only records what happened and posts it to the UI thread; recording starts
/// there.
///
/// Pressing and releasing Alt on its own makes many apps (Notepad, Explorer,
/// Office) focus their menu bar, and the paste would then go to the menu. So
/// when Right Alt is released with no other key pressed in between, the
/// release is held back and replayed after an unassigned key, as AutoHotkey
/// does. Right Alt shortcuts are untouched: a key pressed while holding it
/// passes through normally.
/// </summary>
sealed class KeyboardHook : IDisposable
{
    public event Action<DateTime>? RightAltDown;
    public event Action<DateTime>? RightAltUp;
    public event Action<bool>? OtherKeyDown;  // true for Escape

    readonly Control _ui;
    readonly LowLevelKeyboardProc _proc;  // kept alive: the hook holds only a pointer
    IntPtr _hook;
    bool _rightAltDown;
    bool _otherKeyWhileDown;

    public KeyboardHook(Control ui)
    {
        _ui = ui;
        _proc = Callback;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"Keyboard hook failed (error {Marshal.GetLastWin32Error()}).");
    }

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);
        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        // Our own Ctrl+V and masking keys come back through the hook; ignore them.
        if ((info.flags & LLKHF_INJECTED) != 0) return CallNextHookEx(_hook, nCode, wParam, lParam);

        var message = (int)wParam;
        var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        var now = DateTime.UtcNow;

        if (info.vkCode == VK_RMENU)
        {
            if (isDown && !_rightAltDown)
            {
                _rightAltDown = true;
                _otherKeyWhileDown = false;
                Post(() => RightAltDown?.Invoke(now));
            }
            else if (!isDown && _rightAltDown)
            {
                _rightAltDown = false;
                Post(() => RightAltUp?.Invoke(now));
                if (!_otherKeyWhileDown)
                {
                    // Hold back this release and replay it after a masking key.
                    var inputs = new[] { Key(VK_MASK, up: false), Key(VK_MASK, up: true), Key(VK_RMENU, up: true, extended: true) };
                    SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
                    return 1;
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // On AltGr keyboard layouts Windows sends a fake Left Ctrl with Right
        // Alt (scan code 0x21D). It is part of the Right Alt press, not a shortcut.
        var fakeAltGrCtrl = info.vkCode == VK_LCONTROL && info.scanCode == 0x21D;
        if (isDown && !fakeAltGrCtrl)
        {
            if (_rightAltDown) _otherKeyWhileDown = true;
            var isEscape = info.vkCode == VK_ESCAPE;
            Post(() => OtherKeyDown?.Invoke(isEscape));
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    void Post(Action action)
    {
        if (_ui.IsHandleCreated) _ui.BeginInvoke(action);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
