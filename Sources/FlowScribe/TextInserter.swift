import AppKit
import ApplicationServices
import Carbon.HIToolbox

/// Puts text into whatever field has focus, in any app.
///
/// Why other open-source dictation apps "go silent" on release, and what this
/// does about each cause:
/// 1. No Accessibility permission (or it was silently reset because the app
///    was rebuilt with a new ad-hoc signature): macOS drops the synthetic
///    Cmd+V with no error. We check `AXIsProcessTrusted()` every time and,
///    if it is off, leave the text on the clipboard and say so.
/// 2. The hotkey's own modifier is still physically down when Cmd+V is sent,
///    so the target app sees Option+Cmd+V (a different command, or nothing).
///    We wait until every modifier key is released before pasting.
/// 3. The clipboard is restored too early, so slow apps (Electron, browsers)
///    paste the OLD clipboard. We never restore it: the dictation stays on
///    the clipboard, so it can be pasted again, or pasted by hand when no
///    text field had focus.
/// 4. A password field has Secure Input on, which blocks synthetic keys.
///    We detect it and leave the text on the clipboard instead.
/// Whatever happens, the text is already saved in the last-5 history first.
@MainActor
enum TextInserter {
    enum Outcome {
        case pasted
        case copiedNoPermission
        case copiedSecureInput
    }

    static func hasAccessibility(prompt: Bool) -> Bool {
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        return AXIsProcessTrustedWithOptions([key: prompt] as CFDictionary)
    }

    static func insert(_ text: String) async -> Outcome {
        let pasteboard = NSPasteboard.general

        guard hasAccessibility(prompt: false) else {
            copy(text, to: pasteboard)
            return .copiedNoPermission
        }
        if IsSecureEventInputEnabled() {
            copy(text, to: pasteboard)
            return .copiedSecureInput
        }

        await waitForModifiersReleased()

        copy(text, to: pasteboard)
        postCommandV()
        return .pasted
    }

    static func copy(_ text: String, to pasteboard: NSPasteboard = .general) {
        pasteboard.clearContents()
        pasteboard.setString(text, forType: .string)
    }

    // MARK: - Private

    private static func waitForModifiersReleased() async {
        let modifiers: CGEventFlags = [.maskCommand, .maskAlternate, .maskControl, .maskShift, .maskSecondaryFn]
        for _ in 0..<100 {  // at most ~1 s
            let flags = CGEventSource.flagsState(.combinedSessionState)
            if flags.intersection(modifiers).isEmpty { return }
            try? await Task.sleep(for: .milliseconds(10))
        }
    }

    private static func postCommandV() {
        let source = CGEventSource(stateID: .combinedSessionState)
        let v = CGKeyCode(kVK_ANSI_V)
        let down = CGEvent(keyboardEventSource: source, virtualKey: v, keyDown: true)
        let up = CGEvent(keyboardEventSource: source, virtualKey: v, keyDown: false)
        // Set the flags explicitly so no leftover modifier rides along.
        down?.flags = .maskCommand
        up?.flags = .maskCommand
        down?.post(tap: .cghidEventTap)
        up?.post(tap: .cghidEventTap)
    }
}
