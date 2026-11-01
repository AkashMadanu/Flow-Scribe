using System.Runtime.InteropServices;
using static FlowScribe.NativeMethods;

namespace FlowScribe;

/// <summary>
/// Puts text into whatever field has focus, in any app, by copying it and
/// sending Ctrl+V. The ways this can silently fail on Windows, and the fixes:
/// 1. The hotkey's Alt is still down, so the app sees Ctrl+Alt+V: wait until
///    every modifier key is released first.
/// 2. Another app holds the clipboard open: retry for a moment.
/// 3. The target window runs as administrator: Windows drops input sent to it
///    from a normal app without any error, so detect that and say so.
/// 4. The "V" key is elsewhere on non-QWERTY layouts: look it up per layout.
/// The text is always left on the clipboard, so it can be pasted again, or
/// pasted by hand when no text field had focus. It is saved to history first.
/// </summary>
static class TextInserter
{
    public enum Outcome { Pasted, CopiedElevatedWindow, CopiedPasteFailed, ClipboardBusy }

    public static async Task<Outcome> InsertAsync(string text)
    {
        if (!await CopyAsync(text)) return Outcome.ClipboardBusy;
        if (ForegroundWindowIsElevated()) return Outcome.CopiedElevatedWindow;

        await WaitForModifiersReleasedAsync();
        var v = (ushort)(VkKeyScan('v') & 0xFF);
        if (v == 0xFF) v = 0x56;  // no "v" on this layout: fall back to the V key position
        var inputs = new[] { Key(VK_CONTROL, false), Key(v, false), Key(v, true), Key(VK_CONTROL, true) };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length ? Outcome.Pasted : Outcome.CopiedPasteFailed;
    }

    /// Copies text to the clipboard, retrying while another app has it open.
    public static async Task<bool> CopyAsync(string text)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException)
            {
                await Task.Delay(50);
            }
        }
        return false;
    }

    static async Task WaitForModifiersReleasedAsync()
    {
        ushort[] modifiers = [VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN];
        for (var i = 0; i < 100; i++)  // at most ~1 s
        {
            if (modifiers.All(k => (GetAsyncKeyState(k) & 0x8000) == 0)) return;
            await Task.Delay(10);
        }
    }

    /// True when the focused window belongs to an elevated process and this one
    /// is not. A process we may not even query is treated as elevated.
    static bool ForegroundWindowIsElevated()
    {
        if (IsElevated(GetCurrentProcess())) return false;
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        if (pid == 0) return false;
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return true;
        try { return IsElevated(process); }
        finally { CloseHandle(process); }
    }

    static bool IsElevated(IntPtr process)
    {
        if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return true;
        try { return GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _) && elevated != 0; }
        finally { CloseHandle(token); }
    }
}
